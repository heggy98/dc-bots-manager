import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BotService, AdminBotDetailDto, BotTeamsDto, TeamDto, BotConfigurationDto, BoardConfigListItemDto, UpdateBoardConfigRequest } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { I18nService } from '../../services/i18n.service';
import { TeamsEditModalComponent } from './teams-edit-modal.component';
import { JsonEditorModalComponent } from './json-editor-modal.component';
import { ConfigEditModalComponent } from './config-edit-modal.component';
import { BoardConfigEditModalComponent } from './board-config-edit-modal.component';
import { ToastrService } from 'ngx-toastr';
import { BotEventsService } from '../../services/bot-events.service';
import { forkJoin, Subscription } from 'rxjs';

@Component({
  selector: 'app-bot-detail',
  imports: [CommonModule, FormsModule, RouterLink, TeamsEditModalComponent, JsonEditorModalComponent, ConfigEditModalComponent, BoardConfigEditModalComponent],
  templateUrl: './bot-detail.component.html',
  styleUrl: './bot-detail.component.css'
})
export class BotDetailComponent implements OnInit, OnDestroy {
  botId!: number;
  bot: AdminBotDetailDto | null = null;
  loading = true;
  error = '';  // Change from Singleton to allow multiple connections
  configLoading = false;
  configMessage = '';
  visibilityLoading = false;
  private botEventsSubscription = new Subscription();
  private fallbackSyncIntervalId: ReturnType<typeof setInterval> | null = null;

  // Config Modal
  showConfigModal = false;

  // Boards
  boards: BoardConfigListItemDto[] = [];
  boardsLoading = false;
  selectedBoardForConfig: BoardConfigListItemDto | null = null;
  showBoardConfigModal = false;
  activeBoardIdForTeams: number | null = null;

  // Teams and Emojis
  teamsData: BotTeamsDto | null = null;
  teamsLoading = false;
  showTeamsModal = false;
  teamsSaveInProgress = false;
  teamsSavePhase: 'idle' | 'saving' | 'syncing' = 'idle';
  private teamsSavePhaseTimer: ReturnType<typeof setTimeout> | null = null;
  showJsonEditor = false;
  jsonEditorType: 'teams' | 'emojis' = 'teams';
  jsonEditorData: any = null;

  /**
   * Creates a new bot detail component.
   */
  constructor(
    private route: ActivatedRoute,
    private botService: BotService,
    private botEventsService: BotEventsService,
    private toastr: ToastrService,
    public i18n: I18nService
  ) { }

  /**
   * Initializes bot id and triggers initial detail load.
   */
  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.botId = +id;
      this.loadBotDetails();
      this.connectRealtime();
    }
    else { this.error = 'Invalid bot ID.'; this.loading = false; }
  }

  /**
   * Disposes periodic polling resources.
   */
  ngOnDestroy(): void {
    this.botEventsSubscription.unsubscribe();
    this.botEventsService.disconnect();
    this.stopFallbackRuntimeSync();
    this.clearTeamsSavePhaseTimer();
  }

  /**
   * Loads bot details and normalizes loaded timestamps.
   */
  loadBotDetails(): void {
    this.loading = true;
    this.botService.getBotDetail(this.botId).subscribe({
      next: (data) => {
        this.bot = data;
        if (!this.bot.configuration) this.bot.configuration = {};
        if (this.bot.histories) {
          this.bot.histories.forEach(history => {
            if (history.startedAt && typeof history.startedAt === 'string') {
              history.startedAt = new Date(history.startedAt).toString();
            }
            if (history.stoppedAt && typeof history.stoppedAt === 'string') {
              history.stoppedAt = new Date(history.stoppedAt).toString();;
            }
          });
        }
        this.loading = false;

        // Parallel follow-up requests for board related data.
        if (this.isBoardEnabledBot()) {
          forkJoin({
            teams: this.botService.getTeams(this.botId),
            boards: this.botService.getBoards(this.botId)
          }).subscribe({
            next: (res) => {
              this.teamsData = res.teams;
              this.boards = res.boards;
              this.teamsLoading = false;
              this.boardsLoading = false;
            },
            error: () => {
              this.toastr.error('Failed to load board data.', 'Error');
              this.teamsLoading = false;
              this.boardsLoading = false;
            }
          });
        }
      },
      error: () => { this.error = 'Failed to load bot details.'; this.loading = false; }
    });
  }

  /**
   * Loads teams and emoji data for the current bot, optionally scoped to a board.
   */
  loadTeamsAndEmojis(boardConfigurationId?: number): void {
    this.teamsLoading = true;
    this.botService.getTeams(this.botId, boardConfigurationId).subscribe({
      next: (data) => { this.teamsData = data; this.teamsLoading = false; },
      error: () => {
        this.toastr.error('Failed to load teams.', 'Error');
        this.teamsLoading = false;
      }
    });
  }

  /**
   * Loads board configurations for the current bot.
   */
  loadBoards(): void {
    this.boardsLoading = true;
    this.botService.getBoards(this.botId).subscribe({
      next: (data) => { this.boards = data; this.boardsLoading = false; },
      error: () => {
        this.toastr.error('Failed to load boards.', 'Error');
        this.boardsLoading = false;
      }
    });
  }

  /**
   * Opens the board config edit modal for a specific board.
   */
  openBoardConfigModal(board: BoardConfigListItemDto): void {
    this.selectedBoardForConfig = board;
    this.showBoardConfigModal = true;
  }

  /**
   * Closes the board config edit modal.
   */
  closeBoardConfigModal(): void {
    this.showBoardConfigModal = false;
    this.selectedBoardForConfig = null;
  }

  /**
   * Saves board configuration and refreshes board list.
   */
  saveBoardConfig(request: UpdateBoardConfigRequest): void {
    if (!this.selectedBoardForConfig) return;
    const boardId = this.selectedBoardForConfig.boardConfigurationId;
    this.botService.updateBoard(this.botId, boardId, request).subscribe({
      next: () => {
        this.closeBoardConfigModal();
        this.loadBoards();
        this.toastr.success('Board configuration saved.', 'Saved');
      },
      error: () => this.toastr.error('Failed to save board configuration.', 'Save Failed')
    });
  }

  /**
   * Opens teams edit modal scoped to the given board.
   */
  openTeamsForBoard(board: BoardConfigListItemDto): void {
    this.activeBoardIdForTeams = board.boardConfigurationId;
    this.loadTeamsAndEmojis(board.boardConfigurationId);
    this.showTeamsModal = true;
  }

  /**
   * Sets the active board for the current bot.
   */
  setActiveBoardForBot(boardConfigurationId: number): void {
    this.botService.setActiveBoard(this.botId, boardConfigurationId).subscribe({
      next: () => {
        this.loadBoards();
        this.toastr.success('Active board updated.', 'Updated');
      },
      error: () => this.toastr.error('Failed to set active board.', 'Update Failed')
    });
  }

  /**
   * Creates a new board configuration and refreshes board list.
   */
  addBoard(): void {
    this.botService.createBoard(this.botId, { boardType: 'teams' }).subscribe({
      next: () => {
        this.loadBoards();
        this.toastr.success('Board created.', 'Created');
      },
      error: () => this.toastr.error('Failed to create board.', 'Create Failed')
    });
  }

  /**
   * Deletes a board configuration after confirmation.
   */
  deleteBoard(boardConfigurationId: number): void {
    if (!confirm('Delete this board configuration? All teams scoped to it will also be removed.')) return;
    this.botService.deleteBoard(this.botId, boardConfigurationId).subscribe({
      next: () => {
        this.loadBoards();
        this.toastr.success('Board deleted.', 'Deleted');
      },
      error: () => this.toastr.error('Failed to delete board.', 'Delete Failed')
    });
  }

  /**
   * Returns whether board-team management should be enabled for this bot.
   */
  isBoardEnabledBot(): boolean {
    return true;
  }

  /**
   * Opens the teams edit modal.
   */
  openTeamsModal(): void {
    this.activeBoardIdForTeams = null;
    this.showTeamsModal = true;
  }

  /**
   * Closes the teams edit modal.
   */
  closeTeamsModal(): void {
    if (this.teamsSaveInProgress) return;
    this.showTeamsModal = false;
    this.activeBoardIdForTeams = null;
  }

  /**
   * Saves teams and refreshes displayed teams after save.
   */
  saveTeamsAndEmojis(data: BotTeamsDto): void {
    this.teamsSaveInProgress = true;
    this.teamsSavePhase = 'saving';
    this.clearTeamsSavePhaseTimer();

    // This remains one HTTP call; phase switch is UX-only while backend processes reaction sync.
    this.teamsSavePhaseTimer = setTimeout(() => {
      this.teamsSavePhase = 'syncing';
    }, 700);

    this.botService.saveTeams(this.botId, data, this.activeBoardIdForTeams ?? undefined).subscribe({
      next: () => {
        this.clearTeamsSavePhaseTimer();
        this.teamsSaveInProgress = false;
        this.teamsSavePhase = 'idle';
        this.loadTeamsAndEmojis(this.activeBoardIdForTeams ?? undefined);
        this.closeTeamsModal();
        this.toastr.success('Teams saved.', 'Saved');
      },
      error: () => {
        this.clearTeamsSavePhaseTimer();
        this.teamsSaveInProgress = false;
        this.teamsSavePhase = 'idle';
        this.toastr.error('Failed to save teams.', 'Save Failed');
      }
    });
  }

  private clearTeamsSavePhaseTimer(): void {
    if (this.teamsSavePhaseTimer) {
      clearTimeout(this.teamsSavePhaseTimer);
      this.teamsSavePhaseTimer = null;
    }
  }

  /**
   * Opens the JSON editor modal with selected payload.
   */
  openJsonEditor(event: { type: 'teams' | 'emojis', data: any }): void {
    this.jsonEditorType = event.type;
    this.jsonEditorData = event.data;
    this.showJsonEditor = true;
  }

  /**
   * Closes the JSON editor modal.
   */
  closeJsonEditor(): void {
    this.showJsonEditor = false;
  }

  /**
   * Saves data from JSON editor when editing teams payload.
   */
  saveJsonData(data: any): void {
    if (this.jsonEditorType === 'teams') {
      const normalized = this.normalizeTeamsPayload(data);
      this.botService.saveTeams(this.botId, normalized, this.activeBoardIdForTeams ?? undefined).subscribe({
        next: () => {
          this.loadTeamsAndEmojis(this.activeBoardIdForTeams ?? undefined);
          this.toastr.success('Teams JSON saved.', 'Saved');
        },
        error: () => this.toastr.error('Failed to save teams JSON.', 'Save Failed')
      });
    }
  }

  /**
   * Normalizes arbitrary JSON input to the expected teams DTO shape.
   */
  private normalizeTeamsPayload(data: any): BotTeamsDto {
    const sourceTeams = Array.isArray(data)
      ? data
      : (Array.isArray(data?.teams) ? data.teams : []);

    const teams: TeamDto[] = sourceTeams.map((team: any) => ({
      teamId: this.toOptionalNumber(team?.teamId ?? team?.TeamId),
      name: this.pickString(team, ['name', 'Name']),
      leaderName: this.pickString(team, ['leaderName', 'LeaderName', 'leader', 'Leader']),
      contact: this.pickString(team, ['contact', 'Contact']),
      emoji: this.pickString(team, ['emoji', 'Emoji'])
    }));

    return { teams };
  }

  /**
   * Returns the first string value from a list of candidate keys.
   */
  private pickString(source: any, keys: string[]): string {
    for (const key of keys) {
      const value = source?.[key];
      if (typeof value === 'string') {
        return value;
      }
    }

    return '';
  }

  /**
   * Converts input to optional numeric value, returning undefined for empty or invalid values.
   */
  private toOptionalNumber(value: any): number | undefined {
    if (value === null || value === undefined || value === '') {
      return undefined;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : undefined;
  }

  /**
   * Opens the configuration edit modal.
   */
  openConfigModal(): void {
    this.showConfigModal = true;
  }

  /**
   * Closes the configuration edit modal.
   */
  closeConfigModal(): void {
    this.showConfigModal = false;
  }

  /**
   * Saves bot configuration and updates UI state.
   */
  saveConfig(config: BotConfigurationDto): void {
    this.configLoading = true;
    this.botService.updateBotConfig(this.botId, config).subscribe({
      next: () => {
        if (this.bot) {
          this.bot.configuration = config;
        }
        this.configMessage = '✓';
        this.configLoading = false;
        this.toastr.success('Bot configuration saved.', 'Saved');
        setTimeout(() => this.configMessage = '', 3000);
      },
      error: () => {
        this.configMessage = '✗';
        this.configLoading = false;
        this.toastr.error('Failed to save bot configuration.', 'Save Failed');
      }
    });
  }

  /**
   * Updates whether the bot is publicly listed.
   */
  setPublicVisibility(isPublic: boolean): void {
    if (!this.bot || this.visibilityLoading || this.bot.isPublic === isPublic) {
      return;
    }

    const previous = this.bot.isPublic;
    this.bot.isPublic = isPublic;
    this.visibilityLoading = true;

    this.botService.updateBotVisibility(this.botId, { isPublic }).subscribe({
      next: () => {
        this.visibilityLoading = false;
        this.toastr.success('Bot visibility updated.', 'Updated');
      },
      error: () => {
        this.visibilityLoading = false;
        this.bot!.isPublic = previous;
        this.toastr.error('Failed to update bot visibility.', 'Update Failed');
      }
    });
  }

  /**
   * Clears bot logs after user confirmation.
   */
  clearBotLogs(): void {
    if (!confirm(this.i18n.t('bot.confirm_clear_logs'))) {
      return;
    }

    this.botService.clearLogs(this.botId).subscribe({
      next: () => this.loadBotDetails(),
      error: () => {
        this.error = this.i18n.t('bot.clear_logs_error');
      }
    });
  }

  /**
   * Clears bot run history after user confirmation.
   */
  clearBotHistory(): void {
    if (!confirm(this.i18n.t('bot.confirm_clear_history'))) {
      return;
    }

    this.botService.clearHistory(this.botId).subscribe({
      next: () => this.loadBotDetails(),
      error: () => {
        this.error = this.i18n.t('bot.clear_history_error');
      }
    });
  }

  /**
   * Starts the current bot after confirmation.
   */
  startBot(): void { if (confirm(this.i18n.t('bot.confirm_start'))) this.botService.startBot(this.botId).subscribe(() => this.loadBotDetails()); }

  /**
   * Stops the current bot after confirmation.
   */
  stopBot(): void { if (confirm(this.i18n.t('bot.confirm_stop'))) this.botService.stopBot(this.botId).subscribe(() => this.loadBotDetails()); }

  /**
   * Restarts the current bot after confirmation.
   */
  restartBot(): void { if (confirm(this.i18n.t('bot.confirm_restart'))) this.botService.restartBot(this.botId).subscribe(() => this.loadBotDetails()); }

  /**
   * Gets translation key for the status timestamp label.
   */
  getStatusLabelKey(): string {
    return this.isOnline() ? 'bot.running' : 'bot.last_online';
  }

  /**
   * Returns status timestamp for current bot based on online/offline state.
   */
  getStatusTimestamp(): Date | null {
    if (!this.bot) {
      return null;
    }

    if (this.isOnline()) {
      return this.toDate(this.bot.lastStartedAt);
    }

    return this.toDate(this.bot.lastStoppedAt ?? this.bot.lastStartedAt);
  }

  /**
   * Returns elapsed time text for current status reference point.
   */
  getStatusDurationText(): string {
    if (!this.bot) {
      return '—';
    }

    if (this.isOnline()) {
      const startedAt = this.toDate(this.bot.lastStartedAt);
      if (!startedAt) {
        return '—';
      }

      const seconds = Math.floor((Date.now() - startedAt.getTime()) / 1000);
      return this.formatDuration(seconds);
    }

    const stoppedAt = this.toDate(this.bot.lastStoppedAt);
    if (!stoppedAt) {
      return '—';
    }

    const seconds = Math.floor((Date.now() - stoppedAt.getTime()) / 1000);
    if (seconds < 0) {
      return '—';
    }

    return this.formatDuration(seconds);
  }

  /**
   * Returns whether current bot is online.
   */
  private isOnline(): boolean {
    return this.isRunningState(this.bot?.status);
  }

  isRunningState(status?: string | null): boolean {
    if (!status) return false;
    const normalized = status.toLowerCase();
    return normalized === 'online' || normalized === 'working' || normalized === 'connecting' || normalized === 'reconnecting';
  }

  /**
   * Parses a date string as UTC when timezone suffix is missing.
   */
  private toDate(value?: string): Date | null {
    if (!value) {
      return null;
    }

    // Backend stores UTC in SQL datetime2; when timezone suffix is missing,
    // force UTC parsing so the browser converts correctly to local time.
    const hasZone = /[zZ]|[+-]\d\d:\d\d$/.test(value);
    const normalized = hasZone ? value : `${value}Z`;
    const parsed = new Date(normalized);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  }

  /**
   * Formats seconds into compact day/hour/minute/second text.
   */
  formatDuration(seconds?: number): string {
    if (!seconds) return '—';
    const d = Math.floor(seconds / 86400);
    const h = Math.floor((seconds % 86400) / 3600);
    const m = Math.floor((seconds % 3600) / 60);
    const s = seconds % 60;
    if (d > 0) return `${d}d ${h}h ${m}m ${s}s`;
    if (h > 0) return `${h}h ${m}m ${s}s`;
    if (m > 0) return `${m}m ${s}s`;
    return `${s}s`;
  }

  isReconnectHistory(stopReason?: string | null, errorDetails?: string | null): boolean {
    const reason = `${stopReason ?? ''} ${errorDetails ?? ''}`.toLowerCase();
    return reason.includes('reconnect')
      || reason.includes('resumed')
      || reason.includes('gateway reconnect')
      || reason.includes('znovu')
      || reason.includes('připoj');
  }

  getHistoryReasonText(stopReason?: string | null, errorDetails?: string | null): string {
    if (this.isReconnectHistory(stopReason, errorDetails)) {
      return 'Gateway reconnect/resume';
    }

    return stopReason || '—';
  }

  private connectRealtime(): void {
    this.botEventsService.connectAndJoin(this.botId).catch(() => {
      this.toastr.warning('Realtime updates unavailable. Using manual refresh.', 'SignalR');
    });

    this.botEventsSubscription.add(
      this.botEventsService.statusChanged$.subscribe((event) => {
        if (!this.bot || event.botId !== this.botId) return;
        this.bot.status = event.statusText;
      })
    );

    this.botEventsSubscription.add(
      this.botEventsService.newLog$.subscribe((event) => {
        if (!this.bot || event.botId !== this.botId) return;
        this.bot.logs = [{
          timestamp: event.timestamp,
          level: event.level,
          message: event.message
        }, ...(this.bot.logs ?? [])].slice(0, 300);
      })
    );

    this.botEventsSubscription.add(
      this.botEventsService.statsUpdated$.subscribe((event) => {
        if (!this.bot || event.botId !== this.botId) return;
        this.bot.requests24h = event.requests24h;
        this.bot.errors24h = event.errors24h;
      })
    );

    this.botEventsSubscription.add(
      this.botEventsService.historyUpdated$.subscribe((event) => {
        if (event.botId !== this.botId) return;
        this.refreshRuntimeSnapshot();
      })
    );

    this.startFallbackRuntimeSync();
  }

  private startFallbackRuntimeSync(): void {
    this.stopFallbackRuntimeSync();
    this.fallbackSyncIntervalId = setInterval(() => {
      this.refreshRuntimeSnapshot();
    }, 45000);
  }

  private stopFallbackRuntimeSync(): void {
    if (!this.fallbackSyncIntervalId) {
      return;
    }

    clearInterval(this.fallbackSyncIntervalId);
    this.fallbackSyncIntervalId = null;
  }

  private refreshRuntimeSnapshot(): void {
    if (!this.botId || !this.bot) {
      return;
    }

    this.botService.getBotDetail(this.botId).subscribe({
      next: (data) => {
        if (!this.bot) {
          return;
        }

        this.bot.status = data.status;
        this.bot.requests24h = data.requests24h;
        this.bot.errors24h = data.errors24h;
        this.bot.histories = data.histories;
        this.bot.lastStartedAt = data.lastStartedAt;
        this.bot.lastStoppedAt = data.lastStoppedAt;
      }
    });
  }
}
