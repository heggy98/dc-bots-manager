import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface SystemLogDto {
    id: number;
    timestamp: string;
    level: string;
    category: string;
    message: string;
    exception?: string;
}

export interface LoginAuditDto {
    id: number;
    timestamp: string;
    email: string;
    ipAddress: string;
    success: boolean;
    failReason?: string;
    isBruteforceBlock: boolean;
}

export interface AdminAuditDto {
    id: number;
    timestamp: string;
    actorEmail: string;
    action: string;
    targetType?: string | null;
    targetId?: string | null;
    details?: string | null;
    ipAddress?: string | null;
}

@Injectable({
    providedIn: 'root'
})
export class LogService {
    /**
     * Creates a new logging API service.
     */
    constructor(private http: HttpClient) { }

    /**
     * Gets recent system log rows.
     */
    getSystemLogs(take: number = 100): Observable<SystemLogDto[]> {
        return this.http.get<SystemLogDto[]>(`/api/systemlogs?take=${take}`);
    }

    /**
     * Gets recent login audit log rows.
     */
    getLoginAuditLogs(take: number = 100): Observable<LoginAuditDto[]> {
        return this.http.get<LoginAuditDto[]>(`/api/systemlogs/login-audit?take=${take}`);
    }

    /**
     * Gets recent admin audit rows (who changed what).
     */
    getAdminAuditLogs(take: number = 200): Observable<AdminAuditDto[]> {
        return this.http.get<AdminAuditDto[]>(`/api/systemlogs/admin-audit?take=${take}`);
    }
}
