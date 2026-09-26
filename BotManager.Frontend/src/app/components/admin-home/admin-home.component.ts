import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { AdminBotDto, BotService } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router, RouterLink } from '@angular/router';
import { I18nService } from '../../services/i18n.service';
import { ToastrService } from 'ngx-toastr';
import { LiveDurationComponent, parseApiDate } from '../live-duration/live-duration.component';
import { UsageStatsComponent } from '../usage-stats/usage-stats.component';

@Component({
  selector: 'app-admin-home',
  imports: [CommonModule, FormsModule, RouterLink, LiveDurationComponent, UsageStatsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-home.component.html',
  styleUrl: './admin-home.component.css'
})
export class AdminHomeComponent implements OnInit {
  readonly bots = signal<AdminBotDto[]>([]);
  readonly loading = signal(true);
  readonly showForm = signal(false);
  readonly newBotName = signal('');
  readonly newBotToken = signal('');
  readonly newBotIsPublic = signal(false);
  readonly formError = signal('');
  readonly formLoading = signal(false);
  readonly actionLoadingBotId = signal<number | null>(null);

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
    this.loading.set(true);
    this.botService.getMyBots().subscribe({
      next: (data) => { this.bots.set(data); this.loading.set(false); },
      error: (err) => {
        if (err.status === 401) { this.authService.logout(); this.router.navigate(['/login']); }
        this.loading.set(false);
      }
    });
  }

  /**
   * Logs out and navigates to the public page.
   */
  logout(): void { this.authService.logout(); this.router.navigate(['/']); }

  /**
   * Ends all sessions on every device (after confirmation) and navigates to the login page.
   */
  logoutAll(): void {
    if (!confirm(this.i18n.t('nav.logout_all_confirm'))) return;
    this.authService.logoutAll().subscribe({
      next: () => this.router.navigate(['/login']),
      error: () => this.router.navigate(['/login'])
    });
  }

  /**
   * Toggles the create-bot form visibility.
   */
  toggleAddBot(): void { this.showForm.update(v => !v); this.formError.set(''); }

  /**
   * Submits a new bot creation request.
   */
  createBot(): void {
    if (!this.newBotName() || !this.newBotToken()) { this.formError.set(this.i18n.t('admin.fill_fields')); return; }
    this.formLoading.set(true);
    this.botService.createBot({ name: this.newBotName(), botToken: this.newBotToken(), isPublic: this.newBotIsPublic() }).subscribe({
      next: () => {
        this.newBotName.set('');
        this.newBotToken.set('');
        this.newBotIsPublic.set(false);
        this.showForm.set(false);
        this.formLoading.set(false);
        this.toastr.success(this.i18n.t('admin.create_success'), this.i18n.t('admin.register'));
        this.loadBots();
      },
      error: (err) => {
        this.formError.set(err?.error ?? this.i18n.t('admin.create_error'));
        this.formLoading.set(false);
        this.toastr.error(this.formError(), this.i18n.t('admin.register'));
      }
    });
  }

  /**
   * Starts a bot from dashboard card actions.
   */
  startBot(botId: number): void {
    this.actionLoadingBotId.set(botId);
    this.botService.startBot(botId).subscribe({
      next: () => {
        this.actionLoadingBotId.set(null);
        this.toastr.success(this.i18n.t('bot.start_success'), this.i18n.t('bot.start'));
        this.loadBots();
      },
      error: () => {
        this.actionLoadingBotId.set(null);
        this.toastr.error(this.i18n.t('bot.start_error'), this.i18n.t('bot.start'));
      }
    });
  }

  /**
   * Stops a bot from dashboard card actions.
   */
  stopBot(botId: number): void {
    this.actionLoadingBotId.set(botId);
    this.botService.stopBot(botId).subscribe({
      next: () => {
        this.actionLoadingBotId.set(null);
        this.toastr.success(this.i18n.t('bot.stop_success'), this.i18n.t('bot.stop'));
        this.loadBots();
      },
      error: () => {
        this.actionLoadingBotId.set(null);
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
      ? parseApiDate(bot.lastStartedAt)
      : parseApiDate(bot.lastStoppedAt ?? bot.lastStartedAt);
  }

  /**
   * Stable identity for bot cards.
   */
  trackByBot(_index: number, bot: AdminBotDto): number {
    return bot.botId;
  }
}
