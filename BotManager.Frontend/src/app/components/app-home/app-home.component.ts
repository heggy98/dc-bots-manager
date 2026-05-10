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

  constructor(private botService: BotService, public i18n: I18nService) { }

  ngOnInit(): void {
    this.botService.getPublicBots().subscribe({
      next: (data) => { this.bots = data; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  toDate(value?: string): Date | null {
    if (!value) return null;
    const hasZone = /[zZ]|[+-]\d\d:\d\d$/.test(value);
    const normalized = hasZone ? value : `${value}Z`;
    const parsed = new Date(normalized);
    return Number.isNaN(parsed.getTime()) ? null : parsed;
  }

  isOnline(bot: BotPublicDto): boolean {
    return bot.status?.toLowerCase() === 'online';
  }

  getStatusTimestamp(bot: BotPublicDto): Date | null {
    return this.isOnline(bot)
      ? this.toDate(bot.lastStartedAt)
      : this.toDate(bot.lastStoppedAt ?? bot.lastStartedAt);
  }
}
