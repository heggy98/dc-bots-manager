import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ClockService } from '../../services/clock.service';

/**
 * Parses API datetime strings, forcing UTC when the timezone suffix is missing
 * (backend stores UTC in SQL datetime2 without an offset).
 */
export function parseApiDate(value?: string | null): Date | null {
  if (!value) return null;
  const hasZone = /[zZ]|[+-]\d\d:\d\d$/.test(value);
  const normalized = hasZone ? value : `${value}Z`;
  const parsed = new Date(normalized);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

/**
 * Formats seconds into compact day/hour/minute/second text ("—" for empty/zero).
 */
export function formatCompactDuration(seconds?: number): string {
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

/**
 * Ticking "(running for X)" / "(offline for X)" text.
 *
 * This is the only view that reads the per-second {@link ClockService} signal, so a
 * clock tick refreshes just this tiny OnPush view instead of the whole parent page.
 * Renders nothing when there is no meaningful duration.
 */
@Component({
  selector: 'app-live-duration',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (text() !== '—') {<span>({{ text() }})</span>}`
})
export class LiveDurationComponent {
  private readonly clock = inject(ClockService);

  /** Whether the bot is currently running (duration counts from `startedAt`). */
  readonly online = input(false);
  /** API timestamp of the last start. */
  readonly startedAt = input<string | null | undefined>(undefined);
  /** API timestamp of the last stop. */
  readonly stoppedAt = input<string | null | undefined>(undefined);

  private readonly startedDate = computed(() => parseApiDate(this.startedAt()));
  private readonly stoppedDate = computed(() => parseApiDate(this.stoppedAt()));

  /** Duration text for the current status reference point, or "—". */
  readonly text = computed(() => {
    if (this.online()) {
      const started = this.startedDate();
      if (!started) return '—';
      return formatCompactDuration(Math.floor((this.clock.now() - started.getTime()) / 1000));
    }

    const stopped = this.stoppedDate();
    if (!stopped) return '—';
    const seconds = Math.floor((this.clock.now() - stopped.getTime()) / 1000);
    if (seconds < 0) return '—';
    return formatCompactDuration(seconds);
  });
}
