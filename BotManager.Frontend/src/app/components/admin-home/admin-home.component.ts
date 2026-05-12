import { Component, OnInit } from '@angular/core';
import { AdminBotDto, BotService } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { Router, RouterLink } from '@angular/router';
import { I18nService } from '../../services/i18n.service';

@Component({
  selector: 'app-admin-home',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './admin-home.component.html',
  styleUrl: './admin-home.component.css'
})
export class AdminHomeComponent implements OnInit {
  bots: AdminBotDto[] = [];
  loading = true;
  showForm = false;
  newBotName = '';
  newBotToken = '';
  newBotIsPublic = false;
  formError = '';
  formLoading = false;

  constructor(
    private botService: BotService,
    public authService: AuthService,
    private router: Router,
    public i18n: I18nService
  ) { }

  ngOnInit(): void { this.loadBots(); }

  loadBots(): void {
    this.loading = true;
    this.botService.getMyBots().subscribe({
      next: (data) => { this.bots = data; this.loading = false; },
      error: (err) => {
        if (err.status === 401) { this.authService.logout(); this.router.navigate(['/login']); }
        this.loading = false;
      }
    });
  }

  logout(): void { this.authService.logout(); this.router.navigate(['/']); }

  toggleAddBot(): void { this.showForm = !this.showForm; this.formError = ''; }

  createBot(): void {
    if (!this.newBotName || !this.newBotToken) { this.formError = this.i18n.t('admin.fill_fields'); return; }
    this.formLoading = true;
    this.botService.createBot({ name: this.newBotName, botToken: this.newBotToken, isPublic: this.newBotIsPublic }).subscribe({
      next: () => {
        this.newBotName = '';
        this.newBotToken = '';
        this.newBotIsPublic = false;
        this.showForm = false;
        this.formLoading = false;
        this.loadBots();
      },
      error: () => { this.formError = this.i18n.t('admin.create_error'); this.formLoading = false; }
    });
  }

  getBotUptime(bot: AdminBotDto): string {
    if (bot.status !== 'Online' || !bot.lastStartedAt) return '';
    const start = new Date(bot.lastStartedAt).getTime();
    const now = Date.now();
    const sec = Math.floor((now - start) / 1000);
    const h = Math.floor(sec / 3600); const m = Math.floor((sec % 3600) / 60);
    if (h > 0) return `${h}h ${m}m`;
    return `${m}m`;
  }
}
