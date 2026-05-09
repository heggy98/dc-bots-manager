import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { SystemService } from '../../services/system.service';
import { I18nService } from '../../services/i18n.service';

@Component({
    selector: 'app-footer',
    standalone: true,
    imports: [CommonModule],
    template: `
    <footer class="app-footer">
      <span class="uptime-dot"></span>
      <span>{{ i18n.t('footer.uptime') }}: {{ uptimeText }}</span>
    </footer>
  `
})
export class FooterComponent implements OnInit, OnDestroy {
    uptimeText = '...';
    private intervalId: any;
    private serverUptimeSeconds = 0;
    private lastFetch = Date.now();

    constructor(public i18n: I18nService, private systemService: SystemService) { }

    ngOnInit(): void {
        this.systemService.getUptime().subscribe({
            next: (data) => {
                this.serverUptimeSeconds = data.uptimeSeconds;
                this.lastFetch = Date.now();
                this.updateText();
            },
            error: () => { this.uptimeText = '—'; }
        });

        this.intervalId = setInterval(() => this.updateText(), 1000);
    }

    ngOnDestroy(): void {
        clearInterval(this.intervalId);
    }

    private updateText(): void {
        const elapsed = Math.floor((Date.now() - this.lastFetch) / 1000);
        const total = this.serverUptimeSeconds + elapsed;

        const days = Math.floor(total / 86400);
        const hours = Math.floor((total % 86400) / 3600);
        const minutes = Math.floor((total % 3600) / 60);

        if (days > 0) {
            this.uptimeText = `${days}d ${hours}h ${minutes}m`;
        } else if (hours > 0) {
            this.uptimeText = `${hours}h ${minutes}m`;
        } else {
            this.uptimeText = `${minutes}m`;
        }
    }
}
