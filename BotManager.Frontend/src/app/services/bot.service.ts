import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface BotPublicDto {
  botId: number;
  name: string;
  discordBotName?: string;
  discordBotAvatarUrl?: string;
  serverCount?: number;
  status: string;
  lastStartedAt?: string;
  lastStoppedAt?: string;
}

export interface AdminBotDto {
  botId: number;
  name: string;
  botToken: string;
  discordBotName?: string;
  discordBotAvatarUrl?: string;
  serverCount?: number;
  status: string;
  requests24h: number;
  errors24h: number;
  lastStartedAt?: string;
  lastStoppedAt?: string;
}

export interface BotConfigurationDto {
  boardChannelId?: string;
  boardMessageId?: string;
}

export interface BotLogDto {
  timestamp: string;
  level: string;
  message: string;
}

export interface BotHistoryDto {
  id: number;
  startedAt: string;
  stoppedAt?: string;
  durationSeconds?: number;
  stopReason?: string;
  errorDetails?: string;
}

export interface TeamDto {
  teamId?: number;
  name: string;
  leaderName: string;
  contact: string;
  emoji: string;
}

export interface BotTeamsDto {
  teams: TeamDto[];
}

export interface AdminBotDetailDto extends AdminBotDto {
  configuration: BotConfigurationDto;
  guilds: string[];
  logs: BotLogDto[];
  histories: BotHistoryDto[];
}

export interface CreateBotDto {
  name: string;
  botToken: string;
}

@Injectable({
  providedIn: 'root'
})
export class BotService {
  constructor(private http: HttpClient) { }

  getPublicBots(): Observable<BotPublicDto[]> {
    return this.http.get<BotPublicDto[]>('/api/bot/public');
  }

  getAdminBots(): Observable<AdminBotDto[]> {
    return this.http.get<AdminBotDto[]>('/api/bot/admin');
  }

  getBotDetail(id: number): Observable<AdminBotDetailDto> {
    return this.http.get<AdminBotDetailDto>(`/api/bot/admin/${id}`);
  }

  createBot(bot: CreateBotDto): Observable<number> {
    return this.http.post<number>('/api/bot', bot);
  }

  updateBotConfig(id: number, config: BotConfigurationDto): Observable<void> {
    return this.http.put<void>(`/api/bot/admin/${id}/config`, config);
  }

  startBot(id: number): Observable<any> {
    return this.http.post(`/api/bot/admin/${id}/start`, {});
  }

  stopBot(id: number): Observable<any> {
    return this.http.post(`/api/bot/admin/${id}/stop`, {});
  }

  restartBot(id: number): Observable<any> {
    return this.http.post(`/api/bot/admin/${id}/restart`, {});
  }

  getTeams(id: number): Observable<BotTeamsDto> {
    return this.http.get<BotTeamsDto>(`/api/bot/admin/${id}/teams`);
  }

  saveTeams(id: number, teamsData: BotTeamsDto): Observable<any> {
    return this.http.post(`/api/bot/admin/${id}/teams`, teamsData);
  }
}
