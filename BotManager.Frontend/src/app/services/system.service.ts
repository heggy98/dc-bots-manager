import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface UptimeDto {
    startedAt: string;
    uptimeSeconds: number;
}

export interface SystemConfigDto {
    id: number;
    key: string;
    value: string;
    description?: string;
}

@Injectable({
    providedIn: 'root'
})
export class SystemService {
    /**
     * Creates a new system API service.
     */
    constructor(private http: HttpClient) { }

    /**
     * Gets backend startup and uptime information.
     */
    getUptime(): Observable<UptimeDto> {
        return this.http.get<UptimeDto>('/api/system/uptime');
    }

    /**
     * Gets all system configuration rows.
     */
    getConfigs(): Observable<SystemConfigDto[]> {
        return this.http.get<SystemConfigDto[]>('/api/systemconfig');
    }

    /**
     * Updates a single system configuration value.
     */
    updateConfig(key: string, value: string): Observable<void> {
        return this.http.put<void>('/api/systemconfig', { key, value });
    }
}
