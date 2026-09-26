import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Selectable statistics windows (days). */
export const STATS_RANGES = [7, 30, 90] as const;
export type StatsRange = typeof STATS_RANGES[number];

export interface DailyUsageDto {
  /** UTC day, yyyy-MM-dd. */
  date: string;
  commands: number;
  errors: number;
  joins: number;
  leaves: number;
}

export interface TopCommandDto {
  command: string;
  commandName: string;
  subCommandName?: string | null;
  count: number;
  errors: number;
}

export interface BotUptimeDto {
  botId: number;
  name: string;
  status: string;
  uptimeSeconds: number;
  windowSeconds: number;
  uptimePercent: number;
}

export interface UsageStatsDto {
  days: number;
  from: string;
  to: string;
  botId?: number | null;
  totals: { commands: number; errors: number; joins: number; leaves: number };
  daily: DailyUsageDto[];
  topCommands: TopCommandDto[];
  uptime: BotUptimeDto[];
}

/**
 * Usage statistics API.
 */
@Injectable({ providedIn: 'root' })
export class StatsService {
  private readonly http = inject(HttpClient);

  /** Aggregated statistics across the current user's bots. */
  getDashboardStats(days: number): Observable<UsageStatsDto> {
    return this.http.get<UsageStatsDto>('/api/bot/admin/stats', { params: { days } });
  }

  /** Statistics for one bot. */
  getBotStats(botId: number, days: number): Observable<UsageStatsDto> {
    return this.http.get<UsageStatsDto>(`/api/bot/admin/${botId}/stats`, { params: { days } });
  }
}
