import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { SystemService, SystemConfigDto } from '../../services/system.service';
import { I18nService } from '../../services/i18n.service';

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

    constructor(private systemService: SystemService, public i18n: I18nService) { }

    ngOnInit(): void {
        this.systemService.getConfigs().subscribe({
            next: (data) => {
                this.configs = data;
                data.forEach(c => this.editValues[c.key] = c.value);
            }
        });
    }

    save(config: SystemConfigDto): void {
        this.systemService.updateConfig(config.key, this.editValues[config.key]).subscribe({
            next: () => {
                config.value = this.editValues[config.key];
                this.savedKey = config.key;
                setTimeout(() => this.savedKey = '', 2000);
            }
        });
    }
}
