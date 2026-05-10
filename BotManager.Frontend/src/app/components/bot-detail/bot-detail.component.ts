import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { BotService, AdminBotDetailDto, BotTeamsDto, TeamDto } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { I18nService } from '../../services/i18n.service';
import { TeamsEditModalComponent } from './teams-edit-modal.component';
import { JsonEditorModalComponent } from './json-editor-modal.component';

@Component({
  selector: 'app-bot-detail',
  imports: [CommonModule, FormsModule, RouterLink, TeamsEditModalComponent, JsonEditorModalComponent],
  templateUrl: './bot-detail.component.html',
  styleUrl: './bot-detail.component.css'
})
export class BotDetailComponent implements OnInit {
  botId!: number;
  bot: AdminBotDetailDto | null = null;
  loading = true;
  error = '';  // Change from Singleton to allow multiple connections
  configLoading = false;
  configMessage = '';

  // Teams and Emojis
  teamsData: BotTeamsDto | null = null;
  teamsLoading = false;
  showTeamsModal = false;
  showJsonEditor = false;
  jsonEditorType: 'teams' | 'emojis' = 'teams';
  jsonEditorData: any = null;

  constructor(private route: ActivatedRoute, private botService: BotService, public i18n: I18nService) { }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) { this.botId = +id; this.loadBotDetails(); }
    else { this.error = 'Invalid bot ID.'; this.loading = false; }
  }

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
        // Load teams and emojis if this is discord-bot-aliance
        if (this.isDiscordBotAliance()) {
          this.loadTeamsAndEmojis();
        }
      },
      error: () => { this.error = 'Failed to load bot details.'; this.loading = false; }
    });
  }

  loadTeamsAndEmojis(): void {
    this.teamsLoading = true;
    this.botService.getTeams(this.botId).subscribe({
      next: (data) => { this.teamsData = data; this.teamsLoading = false; },
      error: (err) => { console.error('Failed to load teams', err); this.teamsLoading = false; }
    });
  }

  isDiscordBotAliance(): boolean {
    return this.bot?.botId === 1 || false;
  }

  openTeamsModal(): void {
    this.showTeamsModal = true;
  }

  closeTeamsModal(): void {
    this.showTeamsModal = false;
  }

  saveTeamsAndEmojis(data: BotTeamsDto): void {
    this.botService.saveTeams(this.botId, data).subscribe({
      next: () => {
        this.loadTeamsAndEmojis(); // Reload to confirm
      },
      error: (err) => console.error('Error saving teams', err)
    });
  }

  openJsonEditor(event: { type: 'teams' | 'emojis', data: any }): void {
    this.jsonEditorType = event.type;
    this.jsonEditorData = event.data;
    this.showJsonEditor = true;
  }

  closeJsonEditor(): void {
    this.showJsonEditor = false;
  }

  saveJsonData(data: any): void {
    if (this.jsonEditorType === 'teams') {
      const normalized = this.normalizeTeamsPayload(data);
      this.botService.saveTeams(this.botId, normalized).subscribe({
        next: () => this.loadTeamsAndEmojis(),
        error: (err) => console.error('Error saving teams', err)
      });
    }
  }

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

  private pickString(source: any, keys: string[]): string {
    for (const key of keys) {
      const value = source?.[key];
      if (typeof value === 'string') {
        return value;
      }
    }

    return '';
  }

  private toOptionalNumber(value: any): number | undefined {
    if (value === null || value === undefined || value === '') {
      return undefined;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : undefined;
  }

  saveConfig(): void {
    if (!this.bot) return;
    this.configLoading = true;
    this.botService.updateBotConfig(this.botId, this.bot.configuration).subscribe({
      next: () => { this.configMessage = '✓'; this.configLoading = false; setTimeout(() => this.configMessage = '', 3000); },
      error: () => { this.configMessage = '✗'; this.configLoading = false; }
    });
  }

  startBot(): void { if (confirm(this.i18n.t('bot.confirm_start'))) this.botService.startBot(this.botId).subscribe(() => this.loadBotDetails()); }
  stopBot(): void { if (confirm(this.i18n.t('bot.confirm_stop'))) this.botService.stopBot(this.botId).subscribe(() => this.loadBotDetails()); }
  restartBot(): void { if (confirm(this.i18n.t('bot.confirm_restart'))) this.botService.restartBot(this.botId).subscribe(() => this.loadBotDetails()); }

  getStatusLabelKey(): string {
    return this.isOnline() ? 'bot.running' : 'bot.last_online';
  }

  getStatusTimestamp(): Date | null {
    if (!this.bot) {
      return null;
    }

    if (this.isOnline()) {
      return this.toDate(this.bot.lastStartedAt);
    }

    return this.toDate(this.bot.lastStoppedAt ?? this.bot.lastStartedAt);
  }

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

  private isOnline(): boolean {
    return this.bot?.status?.toLowerCase() === 'online';
  }

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
}
