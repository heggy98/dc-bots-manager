import { ChangeDetectionStrategy, Component, OnInit, computed, signal } from '@angular/core';

import { SystemService } from '../../services/system.service';
import { I18nService } from '../../services/i18n.service';
import { ClockService } from '../../services/clock.service';

@Component({
    selector: 'app-footer',
    standalone: true,
    imports: [],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <footer class="app-footer">
      <span class="uptime-dot"></span>
      <span>{{ i18n.t('footer.uptime') }}: {{ uptimeText() }}</span>
    </footer>
  `
})
export class FooterComponent implements OnInit {
    /** Server uptime baseline and the local time it was fetched at; null until loaded. */
    private readonly baseline = signal<{ uptimeSeconds: number; fetchedAt: number } | null>(null);
    private readonly failed = signal(false);

    /** Formatted uptime, recomputed from the baseline on every clock tick. */
    readonly uptimeText = computed(() => {
        if (this.failed()) return '—';
        const baseline = this.baseline();
        if (!baseline) return '...';

        const elapsed = Math.max(0, Math.floor((this.clock.now() - baseline.fetchedAt) / 1000));
        const total = baseline.uptimeSeconds + elapsed;

        const days = Math.floor(total / 86400);
        const hours = Math.floor((total % 86400) / 3600);
        const minutes = Math.floor((total % 3600) / 60);

        if (days > 0) return `${days}d ${hours}h ${minutes}m`;
        if (hours > 0) return `${hours}h ${minutes}m`;
        return `${minutes}m`;
    });

    /**
     * Creates a new footer component.
     */
    constructor(
        public i18n: I18nService,
        private systemService: SystemService,
        private clock: ClockService
    ) { }

    /**
     * Loads server uptime baseline; the shared clock drives local ticking.
     */
    ngOnInit(): void {
        this.systemService.getUptime().subscribe({
            next: (data) => {
                this.baseline.set({ uptimeSeconds: data.uptimeSeconds, fetchedAt: Date.now() });
            },
            error: () => { this.failed.set(true); }
        });
    }
}
