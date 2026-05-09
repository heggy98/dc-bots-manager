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

@Injectable({
    providedIn: 'root'
})
export class LogService {
    constructor(private http: HttpClient) { }

    getSystemLogs(take: number = 100): Observable<SystemLogDto[]> {
        return this.http.get<SystemLogDto[]>(`/api/systemlogs?take=${take}`);
    }

    getLoginAuditLogs(take: number = 100): Observable<LoginAuditDto[]> {
        return this.http.get<LoginAuditDto[]>(`/api/systemlogs/login-audit?take=${take}`);
    }
}
