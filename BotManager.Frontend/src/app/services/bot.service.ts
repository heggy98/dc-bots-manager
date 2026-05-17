import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface BotPublicDto {
  botId: number;
  name: string;
  ownerUserId: string;
  isPublic: boolean;
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
  ownerUserId: string;
  isPublic: boolean;
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
  activeBoardConfigurationId?: number;
  boardType?: string;
  boardChannelId?: string;
  boardMessageId?: string;
  boardTitle?: string;
  boardDescriptionTemplate?: string;
  subtitleLabel?: string;
  contactLabel?: string;
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
  isPublic: boolean;
}

export interface UpdateBotVisibilityDto {
  isPublic: boolean;
}

export interface BoardConfigListItemDto {
  boardConfigurationId: number;
  botId: number;
  boardType: string;
  guildId?: string;
  boardChannelId?: string;
  boardMessageId?: string;
  boardTitle?: string;
  boardDescriptionTemplate?: string;
  subtitleLabel?: string;
  contactLabel?: string;
  isActive: boolean;
}

export interface UpdateBoardConfigRequest {
  boardType?: string;
  boardChannelId?: string;
  boardMessageId?: string;
  boardTitle?: string;
  boardDescriptionTemplate?: string;
  subtitleLabel?: string;
  contactLabel?: string;
}

export interface CreateBoardConfigRequest {
  boardType?: string;
  guildId?: string;
  boardTitle?: string;
  boardDescriptionTemplate?: string;
  subtitleLabel?: string;
  contactLabel?: string;
}

@Injectable({
  providedIn: 'root'
})
export class BotService {
  /**
   * Creates a new bot API service.
   */
  constructor(private http: HttpClient) { }

  /**
   * Loads publicly visible bots.
   */
  getPublicBots(): Observable<BotPublicDto[]> {
    return this.http.get<BotPublicDto[]>('/api/bot/public');
  }

  /**
   * Loads all bots for admin users.
   */
  getAdminBots(): Observable<AdminBotDto[]> {
    return this.http.get<AdminBotDto[]>('/api/bot/admin');
  }

  /**
   * Loads bots owned by the authenticated user.
   */
  getMyBots(): Observable<AdminBotDto[]> {
    return this.http.get<AdminBotDto[]>('/api/bot/mine');
  }

  /**
   * Loads detailed bot information by id.
   */
  getBotDetail(id: number): Observable<AdminBotDetailDto> {
    return this.http.get<AdminBotDetailDto>(`/api/bot/admin/${id}`);
  }

  /**
   * Creates a new bot entry.
   */
  createBot(bot: CreateBotDto): Observable<number> {
    return this.http.post<number>('/api/bot', bot);
  }

  /**
   * Updates saved bot configuration values.
   */
  updateBotConfig(id: number, config: BotConfigurationDto): Observable<void> {
    return this.http.put<void>(`/api/bot/admin/${id}/config`, config);
  }

  /**
   * Updates whether bot is publicly listed.
   */
  updateBotVisibility(id: number, request: UpdateBotVisibilityDto): Observable<void> {
    return this.http.put<void>(`/api/bot/admin/${id}/visibility`, request);
  }

  /**
   * Starts a bot runtime instance.
   */
  startBot(id: number): Observable<any> {
    return this.http.post(`/api/bot/admin/${id}/start`, {});
  }

  /**
   * Stops a bot runtime instance.
   */
  stopBot(id: number): Observable<any> {
    return this.http.post(`/api/bot/admin/${id}/stop`, {});
  }

  /**
   * Restarts a bot runtime instance.
   */
  restartBot(id: number): Observable<any> {
    return this.http.post(`/api/bot/admin/${id}/restart`, {});
  }

  /**
   * Loads bot teams, optionally scoped to a specific board configuration.
   */
  getTeams(id: number, boardConfigurationId?: number): Observable<BotTeamsDto> {
    const params = boardConfigurationId ? `?boardConfigurationId=${boardConfigurationId}` : '';
    return this.http.get<BotTeamsDto>(`/api/bot/admin/${id}/teams${params}`);
  }

  /**
   * Saves bot teams, optionally scoped to a specific board configuration.
   */
  saveTeams(id: number, teamsData: BotTeamsDto, boardConfigurationId?: number): Observable<any> {
    const params = boardConfigurationId ? `?boardConfigurationId=${boardConfigurationId}` : '';
    return this.http.post(`/api/bot/admin/${id}/teams${params}`, teamsData);
  }

  /**
   * Lists board configurations for a bot.
   */
  getBoards(id: number): Observable<BoardConfigListItemDto[]> {
    return this.http.get<BoardConfigListItemDto[]>(`/api/bot/admin/${id}/boards`);
  }

  /**
   * Updates board configuration fields.
   */
  updateBoard(id: number, boardConfigurationId: number, request: UpdateBoardConfigRequest): Observable<void> {
    return this.http.put<void>(`/api/bot/admin/${id}/boards/${boardConfigurationId}`, request);
  }

  /**
   * Creates a new board configuration.
   */
  createBoard(id: number, request: CreateBoardConfigRequest): Observable<{ boardConfigurationId: number }> {
    return this.http.post<{ boardConfigurationId: number }>(`/api/bot/admin/${id}/boards`, request);
  }

  /**
   * Sets the active board configuration.
   */
  setActiveBoard(id: number, boardConfigurationId: number): Observable<void> {
    return this.http.put<void>(`/api/bot/admin/${id}/boards/active`, { boardConfigurationId });
  }

  /**
   * Deletes a board configuration.
   */
  deleteBoard(id: number, boardConfigurationId: number): Observable<void> {
    return this.http.delete<void>(`/api/bot/admin/${id}/boards/${boardConfigurationId}`);
  }

  /**
   * Loads a catalog of available emojis for team selection.
   */
  getEmojiCatalog(): Observable<string[]> {
    return this.http.get<string[]>('/api/bot/admin/emoji-catalog');
  }

  /**
   * Clears system and command logs for a bot.
   */
  clearLogs(id: number): Observable<{ removedSystemLogs: number; removedCommandLogs: number }> {
    return this.http.delete<{ removedSystemLogs: number; removedCommandLogs: number }>(`/api/bot/admin/${id}/logs`);
  }

  /**
   * Loads merged bot logs.
   */
  getBotLogs(id: number): Observable<BotLogDto[]> {
    return this.http.get<BotLogDto[]>(`/api/bot/admin/${id}/logs`);
  }

  /**
   * Clears run history for a bot.
   */
  clearHistory(id: number): Observable<{ removedHistories: number }> {
    return this.http.delete<{ removedHistories: number }>(`/api/bot/admin/${id}/history`);
  }
}
