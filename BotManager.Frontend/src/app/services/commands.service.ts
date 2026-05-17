import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface GlobalCommandDto {
  commandName: string;
  subCommandName?: string;
  description?: string;
  minimumPermissionLevel: number;
  isEnabled: boolean;
  userHint?: string;
  successMessage?: string;
  permissionMessage?: string;
  errorMessage?: string;
  adminOnlyMessage?: string;
  invalidArgumentsMessage?: string;
  botCount: number;
  hasDifferencesAcrossBots: boolean;
}

export interface UpdateGlobalCommandDto {
  description?: string;
  minimumPermissionLevel: number;
  isEnabled: boolean;
  userHint?: string;
  successMessage?: string;
  permissionMessage?: string;
  errorMessage?: string;
  adminOnlyMessage?: string;
  invalidArgumentsMessage?: string;
}

@Injectable({
  providedIn: 'root'
})
export class CommandsService {
  /**
   * Creates a new commands API service.
   */
  constructor(private http: HttpClient) { }

  /**
   * Returns globally grouped command settings for the current admin.
   */
  getGlobalCommands(): Observable<GlobalCommandDto[]> {
    return this.http.get<GlobalCommandDto[]>('/api/commands/global');
  }

  /**
   * Updates a command definition across all owned bots.
   */
  updateGlobalCommand(commandName: string, subCommandName: string | undefined, request: UpdateGlobalCommandDto): Observable<{ updated: number }> {
    const subCommandQuery = subCommandName ? `?subCommandName=${encodeURIComponent(subCommandName)}` : '';
    return this.http.put<{ updated: number }>(`/api/commands/global/${encodeURIComponent(commandName)}${subCommandQuery}`, request);
  }
}
