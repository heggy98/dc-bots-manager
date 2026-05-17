import { Component, OnInit } from '@angular/core';
import { AdminBotDto, BotService } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router, RouterLink } from '@angular/router';
import { I18nService } from '../../services/i18n.service';
import { ToastrService } from 'ngx-toastr';

@Component({
  selector: 'app-admin-home',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './admin-home.component.html',
  styleUrl: './admin-home.component.css'
})
export class AdminHomeComponent implements OnInit {
  bots: AdminBotDto[] = [];
  loading = true;
  showForm = false;
  newBotName = '';
  newBotToken = '';
  newBotIsPublic = false;
  formError = '';
  formLoading = false;
  actionLoadingBotId: number | null = null;

  /**
   * Creates a new admin home component.
   */
  constructor(
    private botService: BotService,
    public authService: AuthService,
    private router: Router,
    private toastr: ToastrService,
    public i18n: I18nService
  ) { }

  /**
   * Initializes page data.
   */
  ngOnInit(): void { this.loadBots(); }

  /**
   * Loads bots owned by the current user.
   */
  loadBots(): void {
    this.loading = true;
    this.botService.getMyBots().subscribe({
      next: (data) => { this.bots = data; this.loading = false; },
      error: (err) => {
        if (err.status === 401) { this.authService.logout(); this.router.navigate(['/login']); }
        this.loading = false;
      }
    });
  }

  /**
   * Logs out and navigates to the public page.
   */
  logout(): void { this.authService.logout(); this.router.navigate(['/']); }

  /**
   * Toggles the create-bot form visibility.
   */
  toggleAddBot(): void { this.showForm = !this.showForm; this.formError = ''; }

  /**
   * Submits a new bot creation request.
   */
  createBot(): void {
    if (!this.newBotName || !this.newBotToken) { this.formError = this.i18n.t('admin.fill_fields'); return; }
    this.formLoading = true;
    this.botService.createBot({ name: this.newBotName, botToken: this.newBotToken, isPublic: this.newBotIsPublic }).subscribe({
      next: () => {
        this.newBotName = '';
        this.newBotToken = '';
        this.newBotIsPublic = false;
        this.showForm = false;
        this.formLoading = false;
        this.toastr.success(this.i18n.t('admin.create_success'), this.i18n.t('admin.register'));
        this.loadBots();
      },
      error: () => {
        this.formError = this.i18n.t('admin.create_error');
        this.formLoading = false;
        this.toastr.error(this.formError, this.i18n.t('admin.register'));
      }
    });
  }

  /**
   * Formats current bot uptime as a short human-readable string.
   */
  getBotUptime(bot: AdminBotDto): string {
    return this.getStatusDurationText(bot);
  }

  /**
   * Starts a bot from dashboard card actions.
   */
  startBot(botId: number): void {
    this.actionLoadingBotId = botId;
    this.botService.startBot(botId).subscribe({
      next: () => {
        this.actionLoadingBotId = null;
        this.toastr.success(this.i18n.t('bot.start_success'), this.i18n.t('bot.start'));
        this.loadBots();
      },
      error: () => {
        this.actionLoadingBotId = null;
        this.toastr.error(this.i18n.t('bot.start_error'), this.i18n.t('bot.start'));
      }
    });
  }

  /**
   * Stops a bot from dashboard card actions.
   */
  stopBot(botId: number): void {
    this.actionLoadingBotId = botId;
    this.botService.stopBot(botId).subscribe({
      next: () => {
        this.actionLoadingBotId = null;
        this.toastr.success(this.i18n.t('bot.stop_success'), this.i18n.t('bot.stop'));
        this.loadBots();
      },
      error: () => {
        this.actionLoadingBotId = null;
        this.toastr.error(this.i18n.t('bot.stop_error'), this.i18n.t('bot.stop'));
      }
    });
  }

  /**
   * Returns whether the bot is currently online.
   */
  isOnline(bot: AdminBotDto): boolean {
    return bot.status?.toLowerCase() === 'online';
  }

  /**
   * Returns status timestamp according to online/offline state.
   */
  getStatusTimestamp(bot: AdminBotDto): Date | null {
    return this.isOnline(bot)
      ? this.toDate(bot.lastStartedAt)
      : this.toDate(bot.lastStoppedAt ?? bot.lastStartedAt);
  }

  /**
   * Returns relative duration text for current status timestamp.
   */
  getStatusDurationText(bot: AdminBotDto): string {
    if (this.isOnline(bot)) {
      const startedAt = this.toDate(bot.lastStartedAt);
      if (!startedAt) return '—';
      const seconds = Math.floor((Date.now() - startedAt.getTime()) / 1000);
      return this.formatDuration(seconds);
    }

    const stoppedAt = this.toDate(bot.lastStoppedAt);
    if (!stoppedAt) return '—';
    const seconds = Math.floor((Date.now() - stoppedAt.getTime()) / 1000);
    if (seconds < 0) return '—';
    return this.formatDuration(seconds);
  }

  /**
   * Parses API date strings as UTC when timezone suffix is omitted.
   */
  private toDate(value?: string): Date | null {
    if (!value) return null;
    const hasZone = /[zZ]|[+-]\d\d:\d\d$/.test(value);
    const normalized = hasZone ? value : `${value}Z`;
    const parsed = new Date(normalized);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  }

  /**
   * Formats duration seconds to compact day/hour/minute/second text.
   */
  private formatDuration(seconds?: number): string {
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
