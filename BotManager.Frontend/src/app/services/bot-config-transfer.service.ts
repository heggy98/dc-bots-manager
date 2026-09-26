import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Portable bot configuration document (never contains the bot token). */
export interface BotConfigExportDocument {
  schemaVersion: number;
  exportedAt?: string;
  bot?: { name?: string; isPublic?: boolean; autoStart?: boolean } | null;
  boards?: Array<{
    boardType?: string;
    guildId?: string | null;
    boardChannelId?: string | null;
    boardTitle?: string | null;
    boardDescriptionTemplate?: string | null;
    subtitleLabel?: string | null;
    contactLabel?: string | null;
    isActive?: boolean;
    teams?: Array<{ name?: string; leaderName?: string; contact?: string; emoji?: string }> | null;
  }> | null;
  commands?: Array<Record<string, unknown>> | null;
}

export interface BotConfigImportSummary {
  dryRun: boolean;
  mode: string;
  appliedDiscordIds: boolean;
  botName?: string | null;
  boardsRemoved: number;
  boardsImported: number;
  teamsImported: number;
  commandsUpdated: number;
  commandsCreated: number;
  warnings: string[];
}

/** Maximum import file size accepted by the API. */
export const MAX_IMPORT_BYTES = 1024 * 1024;

/**
 * Bot configuration export/import API.
 */
@Injectable({ providedIn: 'root' })
export class BotConfigTransferService {
  private readonly http = inject(HttpClient);

  /** Downloads the bot's configuration document. */
  exportConfig(botId: number): Observable<BotConfigExportDocument> {
    return this.http.get<BotConfigExportDocument>(`/api/bot/admin/${botId}/export`);
  }

  /** Imports (replace mode) a configuration document; with dryRun only validates and summarizes. */
  importConfig(botId: number, document: unknown, options: { dryRun: boolean; applyDiscordIds: boolean }): Observable<BotConfigImportSummary> {
    const params = new HttpParams()
      .set('mode', 'replace')
      .set('dryRun', options.dryRun)
      .set('applyDiscordIds', options.applyDiscordIds);
    return this.http.post<BotConfigImportSummary>(`/api/bot/admin/${botId}/import`, document, { params });
  }
}
