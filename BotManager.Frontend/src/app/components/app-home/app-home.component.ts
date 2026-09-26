import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { BotService, BotPublicDto } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { I18nService } from '../../services/i18n.service';
import { LiveDurationComponent, parseApiDate } from '../live-duration/live-duration.component';

@Component({
  selector: 'app-app-home',
  standalone: true,
  imports: [CommonModule, LiveDurationComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app-home.component.html',
  styleUrls: ['./app-home.component.css']
})
export class AppHomeComponent implements OnInit {
  readonly bots = signal<BotPublicDto[]>([]);
  readonly loading = signal(true);

  /**
   * Creates a new public home component.
   */
  constructor(private botService: BotService, public i18n: I18nService) { }

  /**
   * Loads public bot cards on startup.
   */
  ngOnInit(): void {
    this.botService.getPublicBots().subscribe({
      next: (data) => { this.bots.set(data); this.loading.set(false); },
      error: () => { this.loading.set(false); }
    });
  }

  /**
   * Returns whether a bot is online.
   */
  isOnline(bot: BotPublicDto): boolean {
    return bot.status?.toLowerCase() === 'online';
  }

  /**
   * Returns the most relevant status timestamp for display.
   */
  getStatusTimestamp(bot: BotPublicDto): Date | null {
    return this.isOnline(bot)
      ? parseApiDate(bot.lastStartedAt)
      : parseApiDate(bot.lastStoppedAt ?? bot.lastStartedAt);
  }

  /**
   * Stable identity for bot cards.
   */
  trackByBot(_index: number, bot: BotPublicDto): number {
    return bot.botId;
  }
}
