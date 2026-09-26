import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { LogService, SystemLogDto, LoginAuditDto } from '../../services/log.service';
import { CommonModule } from '@angular/common';
import { I18nService } from '../../services/i18n.service';

@Component({
  selector: 'app-admin-logs',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-logs.component.html',
  styleUrl: './admin-logs.component.css'
})
export class AdminLogsComponent implements OnInit {
  readonly systemLogs = signal<SystemLogDto[]>([]);
  readonly loginLogs = signal<LoginAuditDto[]>([]);
  readonly activeTab = signal<'system' | 'login'>('system');
  readonly loading = signal(true);

  /**
   * Creates a new admin logs component.
   */
  constructor(private logService: LogService, public i18n: I18nService) { }

  /**
   * Loads log data on component initialization.
   */
  ngOnInit(): void { this.loadLogs(); }

  /**
   * Loads system and login audit logs.
   */
  loadLogs(): void {
    this.loading.set(true);
    this.logService.getSystemLogs().subscribe({
      next: (data) => {
        // Convert ISO date strings to Date objects for local timezone display
        data.forEach(log => {
          if (log.timestamp && typeof log.timestamp === 'string') {
            log.timestamp = new Date(log.timestamp).toString();
          }
        });
        this.systemLogs.set(data);
        this.loading.set(false);
      },
      error: () => { this.loading.set(false); }
    });
    this.logService.getLoginAuditLogs().subscribe({
      next: (data) => {
        // Convert ISO date strings to Date objects for local timezone display
        data.forEach(log => {
          if (log.timestamp && typeof log.timestamp === 'string') {
            log.timestamp = new Date(log.timestamp).toString();
          }
        });
        this.loginLogs.set(data);
      }
    });
  }

  /**
   * Switches the active logs tab.
   */
  setTab(tab: 'system' | 'login'): void { this.activeTab.set(tab); }

  /**
   * Stable identity for log rows.
   */
  trackByLogId(_index: number, log: { id: number }): number {
    return log.id;
  }
}
