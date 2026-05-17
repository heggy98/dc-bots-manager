import { Component, OnInit } from '@angular/core';
import { BotService, BotPublicDto } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { I18nService } from '../../services/i18n.service';

@Component({
  selector: 'app-app-home',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './app-home.component.html',
  styleUrls: ['./app-home.component.css']
})
export class AppHomeComponent implements OnInit {
  bots: BotPublicDto[] = [];
  loading = true;

  /**
   * Creates a new public home component.
   */
  constructor(private botService: BotService, public i18n: I18nService) { }

  /**
   * Loads public bot cards on startup.
   */
  ngOnInit(): void {
    this.botService.getPublicBots().subscribe({
      next: (data) => { this.bots = data; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  /**
   * Converts API datetime strings to Date values, normalizing UTC when needed.
   */
  toDate(value?: string): Date | null {
    if (!value) return null;
    const hasZone = /[zZ]|[+-]\d\d:\d\d$/.test(value);
    const normalized = hasZone ? value : `${value}Z`;
    const parsed = new Date(normalized);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
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
      ? this.toDate(bot.lastStartedAt)
      : this.toDate(bot.lastStoppedAt ?? bot.lastStartedAt);
  }

  /**
   * Returns relative duration text for current status timestamp.
   */
  getStatusDurationText(bot: BotPublicDto): string {
    if (this.isOnline(bot)) {
      const startedAt = this.toDate(bot.lastStartedAt);
      if (!startedAt) {
        return '—';
      }

      const seconds = Math.floor((Date.now() - startedAt.getTime()) / 1000);
      return this.formatDuration(seconds);
    }

    const stoppedAt = this.toDate(bot.lastStoppedAt);
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
   * Formats seconds to compact day/hour/minute/second text.
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
