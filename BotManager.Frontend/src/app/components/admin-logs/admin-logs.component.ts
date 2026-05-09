import { Component, OnInit } from '@angular/core';
import { LogService, SystemLogDto, LoginAuditDto } from '../../services/log.service';
import { CommonModule } from '@angular/common';
import { I18nService } from '../../services/i18n.service';

@Component({
  selector: 'app-admin-logs',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './admin-logs.component.html',
  styleUrl: './admin-logs.component.css'
})
export class AdminLogsComponent implements OnInit {
  systemLogs: SystemLogDto[] = [];
  loginLogs: LoginAuditDto[] = [];
  activeTab: 'system' | 'login' = 'system';
  loading = true;

  constructor(private logService: LogService, public i18n: I18nService) { }

  ngOnInit(): void { this.loadLogs(); }

  loadLogs(): void {
    this.loading = true;
    this.logService.getSystemLogs().subscribe({
      next: (data) => {
        this.systemLogs = data;
        // Convert ISO date strings to Date objects for local timezone display
        this.systemLogs.forEach(log => {
          if (log.timestamp && typeof log.timestamp === 'string') {
            log.timestamp = new Date(log.timestamp);
          }
        });
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
    this.logService.getLoginAuditLogs().subscribe({
      next: (data) => {
        this.loginLogs = data;
        // Convert ISO date strings to Date objects for local timezone display
        this.loginLogs.forEach(log => {
          if (log.timestamp && typeof log.timestamp === 'string') {
            log.timestamp = new Date(log.timestamp);
          }
        });
      }
    });
  }

  setTab(tab: 'system' | 'login'): void { this.activeTab = tab; }
}
