import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Subscription } from 'rxjs';
import { I18nService } from '../../services/i18n.service';
import { StatsRange, StatsService, UsageStatsDto, STATS_RANGES } from '../../services/stats.service';
import { DailyUsageChartComponent } from './daily-usage-chart.component';
import { BarListComponent, BarListItem } from './bar-list.component';
import { StatsRangeSelectorComponent } from './stats-range-selector.component';

const RANGE_STORAGE_KEY = 'stats.range';

/**
 * Usage statistics section: range selector, headline tiles, daily commands/errors chart, top commands
 * and uptime. Without `botId` it aggregates all of the user's bots (admin dashboard); with `botId`
 * it shows a single bot (bot detail), including reaction-based team joins/leaves.
 */
@Component({
  selector: 'app-usage-stats',
  imports: [DecimalPipe, DailyUsageChartComponent, BarListComponent, StatsRangeSelectorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './usage-stats.component.html',
  styleUrl: './usage-stats.component.css'
})
export class UsageStatsComponent {
  readonly i18n = inject(I18nService);
  private readonly statsService = inject(StatsService);
  private subscription: Subscription | null = null;

  /** When set, statistics are scoped to this bot. */
  readonly botId = input<number | null>(null);

  readonly range = signal<StatsRange>(readStoredRange());
  readonly stats = signal<UsageStatsDto | null>(null);
  readonly loading = signal(false);
  readonly error = signal(false);

  readonly isBotScope = computed(() => this.botId() !== null && this.botId() !== undefined);

  readonly errorRate = computed(() => {
    const t = this.stats()?.totals;
    if (!t || t.commands === 0) return null;
    return (t.errors / t.commands) * 100;
  });

  readonly topCommandItems = computed<BarListItem[]>(() =>
    (this.stats()?.topCommands ?? []).map(c => ({
      key: c.command,
      label: c.command,
      value: c.count,
      valueText: c.count.toLocaleString(),
      note: c.errors > 0 ? `${c.errors.toLocaleString()} ${this.i18n.t('stats.errors').toLowerCase()}` : undefined
    })));

  readonly uptimeItems = computed<BarListItem[]>(() =>
    (this.stats()?.uptime ?? []).map(u => ({
      key: u.botId,
      label: u.name,
      value: u.uptimePercent,
      valueText: `${u.uptimePercent.toLocaleString(undefined, { maximumFractionDigits: 1 })} %`,
      note: this.i18n.t('status.' + u.status)
    })));

  readonly botUptime = computed(() => this.stats()?.uptime?.[0] ?? null);

  constructor() {
    // Reload whenever the scope or the range changes.
    effect(() => {
      const botId = this.botId();
      const days = this.range();
      untracked(() => this.load(botId, days));
    });
    inject(DestroyRef).onDestroy(() => this.subscription?.unsubscribe());
  }

  setRange(range: StatsRange): void {
    this.range.set(range);
    try { localStorage.setItem(RANGE_STORAGE_KEY, String(range)); } catch { /* storage unavailable */ }
  }

  reload(): void {
    this.load(this.botId(), this.range());
  }

  private load(botId: number | null, days: number): void {
    this.subscription?.unsubscribe();
    this.loading.set(true);
    this.error.set(false);
    const request$ = botId !== null && botId !== undefined
      ? this.statsService.getBotStats(botId, days)
      : this.statsService.getDashboardStats(days);
    this.subscription = request$.subscribe({
      next: stats => { this.stats.set(stats); this.loading.set(false); },
      error: () => { this.error.set(true); this.loading.set(false); }
    });
  }
}

function readStoredRange(): StatsRange {
  try {
    const stored = Number(localStorage.getItem(RANGE_STORAGE_KEY));
    return (STATS_RANGES as readonly number[]).includes(stored) ? stored as StatsRange : 30;
  } catch {
    return 30;
  }
}
