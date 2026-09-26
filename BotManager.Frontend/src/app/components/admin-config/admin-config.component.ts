import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { SystemService, SystemConfigDto } from '../../services/system.service';
import { I18nService } from '../../services/i18n.service';
import { ToastrService } from 'ngx-toastr';

@Component({
    selector: 'app-admin-config',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './admin-config.component.html',
    styleUrl: './admin-config.component.css'
})
export class AdminConfigComponent implements OnInit {
    readonly configs = signal<SystemConfigDto[]>([]);
    readonly editValues = signal<Record<string, string>>({});
    readonly savedKey = signal('');

    private savedKeyTimer: ReturnType<typeof setTimeout> | null = null;

    /**
     * Creates a new admin config component.
     */
    constructor(
        private systemService: SystemService,
        private toastr: ToastrService,
        public i18n: I18nService
    ) {
        inject(DestroyRef).onDestroy(() => {
            if (this.savedKeyTimer) clearTimeout(this.savedKeyTimer);
        });
    }

    /**
     * Loads editable system configuration values on initialization.
     */
    ngOnInit(): void {
        this.systemService.getConfigs().subscribe({
            next: (data) => {
                this.configs.set(data);
                this.editValues.set(Object.fromEntries(data.map(c => [c.key, c.value])));
            }
        });
    }

    /**
     * Immutably updates the edited value of a configuration row.
     */
    setEditValue(key: string, value: string): void {
        this.editValues.update(values => ({ ...values, [key]: value }));
    }

    /**
     * Saves a single configuration row.
     */
    save(config: SystemConfigDto): void {
        const value = this.editValues()[config.key];
        this.systemService.updateConfig(config.key, value).subscribe({
            next: () => {
                this.configs.update(list => list.map(c => (c.key === config.key ? { ...c, value } : c)));
                this.savedKey.set(config.key);
                this.toastr.success(this.i18n.t('config.saved'), this.i18n.t('config.save'));
                if (this.savedKeyTimer) clearTimeout(this.savedKeyTimer);
                this.savedKeyTimer = setTimeout(() => {
                    this.savedKeyTimer = null;
                    this.savedKey.set('');
                }, 2000);
            },
            error: () => {
                this.toastr.error(this.i18n.t('config.save_error'), this.i18n.t('config.save'));
            }
        });
    }

    /**
     * Returns true for config keys whose values are long/multiline text.
     */
    isMultiline(key: string): boolean {
        return key === 'BoardGlobal.DefaultBoardDescription';
    }

    /**
     * Stable identity for config rows.
     */
    trackByConfig(_index: number, cfg: SystemConfigDto): number {
        return cfg.id;
    }
}
