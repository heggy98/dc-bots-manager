import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, finalize, firstValueFrom, shareReplay, tap } from 'rxjs';

export interface LoginRequest {
  email: string;
  password?: string;
  recaptchaToken?: string;
  googleIdToken?: string;
}

/**
 * Session info returned by the backend. The tokens themselves live in HttpOnly cookies
 * and are never readable from JavaScript.
 */
export interface SessionInfo {
  email: string;
  accessTokenExpiresAt: string;
  refreshTokenExpiresAt: string;
}

export interface AttemptStatus {
  locked: boolean;
  attempts: number;
  captchaRequired: boolean;
  captchaSiteKey: string | null;
}

const SESSION_KEY = 'auth_session';
/** Refresh the access token this long before it expires. */
const REFRESH_MARGIN_MS = 60_000;

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private refreshInFlight$: Observable<SessionInfo> | null = null;

  /**
   * Creates a new authentication API service.
   */
  constructor(private http: HttpClient) {
    // Tokens from older versions were stored in localStorage; they are no longer used.
    localStorage.removeItem('auth_token');
  }

  /**
   * Submits email/password credentials; the backend sets the session cookies.
   */
  login(request: LoginRequest): Observable<SessionInfo> {
    return this.http.post<SessionInfo>('/api/auth/login', request).pipe(tap(s => this.setSession(s)));
  }

  /**
   * Submits a Google login token.
   */
  googleLogin(request: LoginRequest): Observable<SessionInfo> {
    return this.http.post<SessionInfo>('/api/auth/google-login', request).pipe(tap(s => this.setSession(s)));
  }

  /**
   * Gets lock/attempt counters and captcha requirements for the current caller.
   */
  getAttemptStatus(): Observable<AttemptStatus> {
    return this.http.get<AttemptStatus>('/api/auth/attempt-status');
  }

  /**
   * Rotates the refresh cookie and obtains a new access cookie. Concurrent callers share one request.
   */
  refresh(): Observable<SessionInfo> {
    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = this.http.post<SessionInfo>('/api/auth/refresh', {}).pipe(
        tap({
          next: s => this.setSession(s),
          error: () => this.clearSession()
        }),
        finalize(() => { this.refreshInFlight$ = null; }),
        shareReplay(1)
      );
    }
    return this.refreshInFlight$;
  }

  /**
   * Refreshes the session when the access token is about to expire. Resolves false when the session is gone.
   */
  async ensureFreshSession(): Promise<boolean> {
    const session = this.getSession();
    if (!session || !this.isLoggedIn()) {
      return false;
    }

    if (Date.parse(session.accessTokenExpiresAt) - Date.now() > REFRESH_MARGIN_MS) {
      return true;
    }

    try {
      await firstValueFrom(this.refresh());
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Returns true while a (refreshable) session exists. The server remains the source of truth:
   * a rejected request triggers refresh or logout in the interceptor.
   */
  isLoggedIn(): boolean {
    const session = this.getSession();
    if (!session) {
      return false;
    }

    const refreshExpiry = Date.parse(session.refreshTokenExpiresAt);
    if (Number.isNaN(refreshExpiry) || refreshExpiry <= Date.now()) {
      this.clearSession();
      return false;
    }

    return true;
  }

  /**
   * Email of the logged-in admin, if any.
   */
  getEmail(): string | null {
    return this.getSession()?.email ?? null;
  }

  /**
   * Ends the session on the server (revokes the refresh token, clears cookies) and locally.
   */
  logout(): void {
    this.http.post('/api/auth/logout', {}).subscribe({ error: () => undefined });
    this.clearSession();
  }

  /**
   * Ends all sessions on every device.
   */
  logoutAll(): Observable<void> {
    return this.http.post<void>('/api/auth/logout-all', {}).pipe(finalize(() => this.clearSession()));
  }

  /**
   * Forgets the local session hint (used when the server rejects the session).
   */
  clearSession(): void {
    localStorage.removeItem(SESSION_KEY);
  }

  private setSession(session: SessionInfo): void {
    localStorage.setItem(SESSION_KEY, JSON.stringify({
      email: session.email,
      accessTokenExpiresAt: session.accessTokenExpiresAt,
      refreshTokenExpiresAt: session.refreshTokenExpiresAt
    }));
  }

  private getSession(): SessionInfo | null {
    try {
      const raw = localStorage.getItem(SESSION_KEY);
      return raw ? JSON.parse(raw) as SessionInfo : null;
    } catch {
      return null;
    }
  }
}
