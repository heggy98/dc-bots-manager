import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SystemService, SystemConfigDto } from '../../services/system.service';
import { I18nService } from '../../services/i18n.service';
import { ToastrService } from 'ngx-toastr';

@Component({
    selector: 'app-admin-config',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './admin-config.component.html',
    styleUrl: './admin-config.component.css'
})
export class AdminConfigComponent implements OnInit {
    configs: SystemConfigDto[] = [];
    editValues: { [key: string]: string } = {};
    savedKey = '';

    /**
     * Creates a new admin config component.
     */
    constructor(
        private systemService: SystemService,
        private toastr: ToastrService,
        public i18n: I18nService
    ) { }

    /**
     * Loads editable system configuration values on initialization.
     */
    ngOnInit(): void {
        this.systemService.getConfigs().subscribe({
            next: (data) => {
                this.configs = data;
                data.forEach(c => this.editValues[c.key] = c.value);
            }
        });
    }

    /**
     * Saves a single configuration row.
     */
    save(config: SystemConfigDto): void {
        this.systemService.updateConfig(config.key, this.editValues[config.key]).subscribe({
            next: () => {
                config.value = this.editValues[config.key];
                this.savedKey = config.key;
                this.toastr.success(this.i18n.t('config.saved'), this.i18n.t('config.save'));
                setTimeout(() => this.savedKey = '', 2000);
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
}
