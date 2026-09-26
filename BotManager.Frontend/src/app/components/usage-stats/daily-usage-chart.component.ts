import {
  AfterViewInit, ChangeDetectionStrategy, Component, ElementRef, OnDestroy, computed, inject, input, signal
} from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { DailyUsageDto } from '../../services/stats.service';
import { I18nService } from '../../services/i18n.service';

/** Column geometry for one day. */
export interface DayColumn {
  index: number;
  day: DailyUsageDto;
  success: number;
  x: number;
  bandX: number;
  successPath: string | null;
  errorPath: string | null;
  top: number;
}

const HEIGHT = 220;
const MARGIN = { top: 22, right: 8, bottom: 28, left: 44 };
const MAX_BAR = 24;
const GAP = 2;
const RADIUS = 4;
let nextChartId = 0;

/**
 * Rounds a maximum up to a "nice" axis bound (1, 2, 2.5, 5 x 10^n) and returns ticks from 0.
 */
export function niceTicks(max: number, count = 4): number[] {
  if (!Number.isFinite(max) || max <= 0) return [0, 1];
  const rawStep = max / count;
  const magnitude = Math.pow(10, Math.floor(Math.log10(rawStep)));
  const step = [1, 2, 2.5, 5, 10].map(m => m * magnitude).find(s => s >= rawStep) ?? 10 * magnitude;
  const niceStep = step < 1 ? 1 : step;
  const top = Math.ceil(max / niceStep) * niceStep;
  const ticks: number[] = [];
  for (let v = 0; v <= top + 1e-9; v += niceStep) ticks.push(Math.round(v * 100) / 100);
  return ticks;
}

/**
 * SVG path of a column with a rounded data-end (top) and a square base.
 */
export function columnPath(x: number, y: number, width: number, height: number, roundTop: boolean): string {
  if (height <= 0 || width <= 0) return '';
  const r = roundTop ? Math.min(RADIUS, width / 2, height) : 0;
  if (r === 0) return `M${x},${y}h${width}v${height}h${-width}Z`;
  return `M${x},${y + height}V${y + r}Q${x},${y} ${x + r},${y}H${x + width - r}Q${x + width},${y} ${x + width},${y + r}V${y + height}Z`;
}

/**
 * Daily commands chart: stacked columns (successful + errors) on one axis, with a hover/focus
 * tooltip, keyboard navigation (arrow keys), a selective peak label and a table view.
 */
@Component({
  selector: 'app-daily-usage-chart',
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="chart-head">
      <ul class="legend" [attr.aria-label]="i18n.t('stats.legend')">
        <li><span class="swatch s1" aria-hidden="true"></span>{{ i18n.t('stats.successful') }} <strong>{{ totals().success | number }}</strong></li>
        <li><span class="swatch s2" aria-hidden="true"></span>{{ i18n.t('stats.errors') }} <strong>{{ totals().errors | number }}</strong></li>
      </ul>
      <button type="button" class="btn btn-outline btn-sm view-toggle" (click)="showTable.set(!showTable())" [attr.aria-pressed]="showTable()">
        {{ showTable() ? i18n.t('stats.show_chart') : i18n.t('stats.show_table') }}
      </button>
    </div>

    @if (!showTable()) {
      <div class="plot" [style.height.px]="height">
        <svg
          [attr.width]="width()"
          [attr.height]="height"
          [attr.viewBox]="'0 0 ' + width() + ' ' + height"
          role="group"
          tabindex="0"
          [attr.aria-label]="ariaSummary()"
          [attr.aria-describedby]="liveId"
          (keydown)="onKey($event)"
          (focus)="onFocus()"
          (blur)="active.set(null)"
          (pointerleave)="active.set(null)">
          @for (tick of ticks(); track tick) {
            <line class="grid" [attr.x1]="margin.left" [attr.x2]="width() - margin.right" [attr.y1]="y(tick)" [attr.y2]="y(tick)"
              [class.baseline]="tick === 0" />
            <text class="tick" [attr.x]="margin.left - 8" [attr.y]="y(tick)" text-anchor="end" dominant-baseline="middle">{{ tick | number }}</text>
          }
          @for (col of columns(); track col.day.date) {
            <g [class.dim]="active() !== null && active() !== col.index">
              @if (col.successPath) { <path class="bar s1" [attr.d]="col.successPath" /> }
              @if (col.errorPath) { <path class="bar s2" [attr.d]="col.errorPath" /> }
            </g>
          }
          @if (peak(); as p) {
            <text class="peak" [attr.x]="p.x" [attr.y]="p.top - 6" text-anchor="middle">{{ p.day.commands | number }}</text>
          }
          @for (col of columns(); track col.day.date) {
            @if (xLabelIndexes().has(col.index)) {
              <text class="tick" [attr.x]="col.x" [attr.y]="height - 8" text-anchor="middle">{{ shortDate(col.day.date) }}</text>
            }
          }
          @for (col of columns(); track col.day.date) {
            <rect class="hit" [attr.x]="col.bandX" [attr.y]="margin.top" [attr.width]="band()" [attr.height]="plotHeight"
              (pointerenter)="active.set(col.index)" (pointerdown)="active.set(col.index)" />
          }
        </svg>
        @if (activeColumn(); as col) {
          <div class="tooltip" [style.left.px]="tooltipLeft()" role="presentation">
            <div class="tt-date">{{ longDate(col.day.date) }}</div>
            <div class="tt-row"><span class="key s1"></span><strong>{{ col.success | number }}</strong> {{ i18n.t('stats.successful') }}</div>
            <div class="tt-row"><span class="key s2"></span><strong>{{ col.day.errors | number }}</strong> {{ i18n.t('stats.errors') }}</div>
            <div class="tt-row total"><strong>{{ col.day.commands | number }}</strong> {{ i18n.t('stats.commands') }}</div>
          </div>
        }
        <div [id]="liveId" class="sr-only" aria-live="polite">{{ liveText() }}</div>
      </div>
    } @else {
      <div class="table-wrap">
        <table>
          <caption class="sr-only">{{ i18n.t('stats.daily_title') }}</caption>
          <thead>
            <tr>
              <th scope="col">{{ i18n.t('stats.date') }}</th>
              <th scope="col" class="num">{{ i18n.t('stats.commands') }}</th>
              <th scope="col" class="num">{{ i18n.t('stats.errors') }}</th>
              <th scope="col" class="num">{{ i18n.t('stats.joins') }}</th>
              <th scope="col" class="num">{{ i18n.t('stats.leaves') }}</th>
            </tr>
          </thead>
          <tbody>
            @for (d of data(); track d.date) {
              <tr>
                <td>{{ longDate(d.date) }}</td>
                <td class="num">{{ d.commands | number }}</td>
                <td class="num">{{ d.errors | number }}</td>
                <td class="num">{{ d.joins | number }}</td>
                <td class="num">{{ d.leaves | number }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
  styleUrls: ['./viz-tokens.css', './daily-usage-chart.component.css']
})
export class DailyUsageChartComponent implements AfterViewInit, OnDestroy {
  readonly i18n = inject(I18nService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private resizeObserver: ResizeObserver | null = null;

  /** One row per day, oldest first. */
  readonly data = input<DailyUsageDto[]>([]);

  readonly liveId = `daily-chart-live-${++nextChartId}`;
  readonly height = HEIGHT;
  readonly margin = MARGIN;
  readonly plotHeight = HEIGHT - MARGIN.top - MARGIN.bottom;

  readonly width = signal(640);
  readonly active = signal<number | null>(null);
  readonly showTable = signal(false);

  readonly totals = computed(() => {
    const rows = this.data();
    const commands = rows.reduce((s, d) => s + d.commands, 0);
    const errors = rows.reduce((s, d) => s + d.errors, 0);
    return { commands, errors, success: commands - errors };
  });

  readonly ticks = computed(() => niceTicks(Math.max(0, ...this.data().map(d => d.commands))));
  private readonly yMax = computed(() => this.ticks()[this.ticks().length - 1] || 1);
  readonly band = computed(() => (this.width() - MARGIN.left - MARGIN.right) / Math.max(1, this.data().length));

  readonly columns = computed<DayColumn[]>(() => {
    const band = this.band();
    const barWidth = Math.max(2, Math.min(MAX_BAR, band * 0.66));
    const baseline = this.y(0);
    return this.data().map((day, index) => {
      const errors = Math.min(day.errors, day.commands);
      const success = Math.max(0, day.commands - errors);
      const bandX = MARGIN.left + index * band;
      const x = bandX + band / 2;
      const left = x - barWidth / 2;
      const successHeight = baseline - this.y(success);
      const errorHeightRaw = baseline - this.y(errors);
      const bothVisible = successHeight > 0 && errorHeightRaw > 0;
      // 2px surface gap between the stacked segments, taken from the upper segment.
      const errorHeight = bothVisible ? Math.max(1, errorHeightRaw - GAP) : errorHeightRaw;
      const successTop = baseline - successHeight;
      const errorTop = successTop - (bothVisible ? GAP : 0) - errorHeight;
      return {
        index,
        day,
        success,
        x,
        bandX,
        successPath: successHeight > 0 ? columnPath(left, successTop, barWidth, successHeight, errorHeightRaw <= 0) : null,
        errorPath: errorHeightRaw > 0 ? columnPath(left, errorTop, barWidth, errorHeight, true) : null,
        top: errorHeightRaw > 0 ? errorTop : successTop
      };
    });
  });

  /** The single busiest day gets a direct label (selective labeling). */
  readonly peak = computed(() => {
    const cols = this.columns();
    let best: DayColumn | null = null;
    for (const c of cols) if (c.day.commands > 0 && (!best || c.day.commands > best.day.commands)) best = c;
    return best;
  });

  readonly xLabelIndexes = computed(() => {
    const n = this.data().length;
    const set = new Set<number>();
    if (n === 0) return set;
    const maxLabels = Math.max(2, Math.floor((this.width() - MARGIN.left - MARGIN.right) / 64));
    const step = Math.max(1, Math.ceil(n / maxLabels));
    for (let i = n - 1; i >= 0; i -= step) set.add(i);
    return set;
  });

  readonly activeColumn = computed(() => {
    const i = this.active();
    return i === null ? null : this.columns()[i] ?? null;
  });

  readonly tooltipLeft = computed(() => {
    const col = this.activeColumn();
    if (!col) return 0;
    const tooltipWidth = 170;
    return Math.min(Math.max(0, col.x - tooltipWidth / 2), Math.max(0, this.width() - tooltipWidth));
  });

  readonly liveText = computed(() => {
    const col = this.activeColumn();
    if (!col) return '';
    return `${this.longDate(col.day.date)}: ${col.day.commands} ${this.i18n.t('stats.commands')}, ${col.day.errors} ${this.i18n.t('stats.errors')}`;
  });

  readonly ariaSummary = computed(() => {
    const t = this.totals();
    return `${this.i18n.t('stats.daily_title')}: ${t.commands} ${this.i18n.t('stats.commands')}, ${t.errors} ${this.i18n.t('stats.errors')}. ${this.i18n.t('stats.keyboard_hint')}`;
  });

  ngAfterViewInit(): void {
    if (typeof ResizeObserver === 'undefined') return;
    this.resizeObserver = new ResizeObserver(entries => {
      const w = Math.floor(entries[0]?.contentRect.width ?? 0);
      if (w > 0 && w !== this.width()) this.width.set(Math.max(280, w));
    });
    this.resizeObserver.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.resizeObserver?.disconnect();
  }

  /** Maps a value to the SVG y coordinate. */
  y(value: number): number {
    return MARGIN.top + this.plotHeight * (1 - value / (this.yMax() || 1));
  }

  onFocus(): void {
    if (this.active() === null && this.data().length > 0) this.active.set(this.data().length - 1);
  }

  onKey(event: KeyboardEvent): void {
    const n = this.data().length;
    if (n === 0) return;
    const current = this.active() ?? n - 1;
    let next: number | null = null;
    if (event.key === 'ArrowLeft') next = Math.max(0, current - 1);
    else if (event.key === 'ArrowRight') next = Math.min(n - 1, current + 1);
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = n - 1;
    else if (event.key === 'Escape') { this.active.set(null); return; }
    if (next !== null) {
      event.preventDefault();
      this.active.set(next);
    }
  }

  shortDate(date: string): string {
    return this.format(date, { day: 'numeric', month: 'numeric' });
  }

  longDate(date: string): string {
    return this.format(date, { weekday: 'short', day: 'numeric', month: 'numeric', year: 'numeric' });
  }

  private format(date: string, options: Intl.DateTimeFormatOptions): string {
    const parsed = new Date(`${date}T00:00:00Z`);
    if (Number.isNaN(parsed.getTime())) return date;
    return parsed.toLocaleDateString(this.i18n.lang() === 'cs' ? 'cs-CZ' : 'en-GB', { ...options, timeZone: 'UTC' });
  }
}
