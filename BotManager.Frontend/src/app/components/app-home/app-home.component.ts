import { Component, OnInit } from '@angular/core';
import { BotService, BotPublicDto } from '../../services/bot.service';
import { CommonModule } from '@angular/common';
import { I18nService } from '../../services/i18n.service';

@Component({
  selector: 'app-app-home',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './app-home.component.html',
  styleUrls: ['./app-home.component.css']
})
export class AppHomeComponent implements OnInit {
  bots: BotPublicDto[] = [];
  loading = true;

  constructor(private botService: BotService, public i18n: I18nService) { }

  ngOnInit(): void {
    this.botService.getPublicBots().subscribe({
      next: (data) => { this.bots = data; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }
}
