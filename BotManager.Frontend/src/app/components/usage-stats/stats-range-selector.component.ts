import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { I18nService } from '../../services/i18n.service';
import { STATS_RANGES, StatsRange } from '../../services/stats.service';

/**
 * Segmented 7 / 30 / 90 day range control (radio group semantics, arrow-key navigation).
 */
@Component({
  selector: 'app-stats-range-selector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="range" role="radiogroup" [attr.aria-label]="i18n.t('stats.range')" (keydown)="onKey($event)">
      @for (r of ranges; track r) {
        <button type="button" role="radio"
          [class.selected]="r === value()"
          [attr.aria-checked]="r === value()"
          [attr.tabindex]="r === value() ? 0 : -1"
          (click)="select(r)">
          {{ i18n.t('stats.last_days').replace('{n}', '' + r) }}
        </button>
      }
    </div>
  `,
  styles: [`
    .range { display: inline-flex; border: 1px solid var(--border-color); border-radius: 10px; padding: 3px; gap: 2px; background: var(--bg-glass); }
    button { border: 0; background: transparent; color: var(--text-secondary); font: inherit; font-size: 0.8rem; font-weight: 600;
      padding: 6px 12px; border-radius: 8px; cursor: pointer; min-height: 32px; }
    button:hover { background: var(--primary-soft); }
    button.selected { background: var(--primary); color: #fff; }
    button:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
  `]
})
export class StatsRangeSelectorComponent {
  readonly i18n = inject(I18nService);
  readonly ranges = STATS_RANGES;
  readonly value = input<StatsRange>(30);
  readonly valueChange = output<StatsRange>();

  select(range: StatsRange): void {
    if (range !== this.value()) this.valueChange.emit(range);
  }

  onKey(event: KeyboardEvent): void {
    const index = this.ranges.indexOf(this.value());
    let next = -1;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = Math.min(this.ranges.length - 1, index + 1);
    if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = Math.max(0, index - 1);
    if (next < 0) return;
    event.preventDefault();
    this.select(this.ranges[next]);
    const buttons = (event.currentTarget as HTMLElement).querySelectorAll('button');
    queueMicrotask(() => (buttons[next] as HTMLButtonElement | undefined)?.focus());
  }
}
