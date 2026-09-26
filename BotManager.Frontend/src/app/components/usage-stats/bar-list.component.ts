import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface BarListItem {
  key: string | number;
  label: string;
  value: number;
  /** Formatted value shown at the bar tip. */
  valueText: string;
  /** Optional secondary text under the label (e.g. error count, status). */
  note?: string;
}

/**
 * Horizontal single-series bar list. Every value is labeled at the bar tip, so no tooltip is needed.
 * variant "bar": bars are scaled to the largest value (top commands).
 * variant "meter": bars are a share of a fixed maximum and drawn over a lighter track (uptime %).
 */
@Component({
  selector: 'app-bar-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (items().length === 0) {
      <p class="empty">{{ emptyText() }}</p>
    } @else {
      <ul class="bar-list" [attr.aria-label]="label()">
        @for (item of items(); track item.key) {
          <li>
            <div class="row-label">
              <span class="name" [title]="item.label">{{ item.label }}</span>
              @if (item.note) { <span class="note">{{ item.note }}</span> }
            </div>
            <div class="row-bar">
              <div class="track" [class.meter]="variant() === 'meter'" role="presentation">
                <div class="fill" [style.width.%]="percent(item.value)"></div>
              </div>
              <span class="value">{{ item.valueText }}</span>
            </div>
          </li>
        }
      </ul>
    }
  `,
  styleUrls: ['./viz-tokens.css', './bar-list.component.css']
})
export class BarListComponent {
  readonly items = input<BarListItem[]>([]);
  readonly variant = input<'bar' | 'meter'>('bar');
  /** Fixed maximum for meters (default 100). Bars scale to the largest item. */
  readonly max = input<number | null>(null);
  readonly label = input('');
  readonly emptyText = input('—');

  private readonly scaleMax = computed(() => {
    const fixed = this.max();
    if (fixed !== null && fixed > 0) return fixed;
    if (this.variant() === 'meter') return 100;
    return Math.max(1, ...this.items().map(i => i.value));
  });

  percent(value: number): number {
    const p = (value / this.scaleMax()) * 100;
    return Math.max(0, Math.min(100, p));
  }
}
