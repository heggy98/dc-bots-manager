import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface LoginRequest {
  email: string;
  password?: string;
  recaptchaToken?: string;
  googleIdToken?: string;
}

export interface LoginResponse {
  token: string;
  email: string;
}

export interface AttemptStatus {
  locked: boolean;
  attempts: number;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  /**
   * Creates a new authentication API service.
   */
  constructor(private http: HttpClient) { }

  /**
   * Submits email/password credentials.
   */
  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', request);
  }

  /**
   * Submits a Google login token.
   */
  googleLogin(request: LoginRequest): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/google-login', request);
  }

  /**
   * Gets lock and attempt counters for the current caller.
   */
  getAttemptStatus(): Observable<AttemptStatus> {
    return this.http.get<AttemptStatus>('/api/auth/attempt-status');
  }

  /**
   * Persists the auth token in local storage.
   */
  saveToken(token: string): void {
    localStorage.setItem('auth_token', token);
  }

  /**
   * Reads the auth token from local storage.
   */
  getToken(): string | null {
    return localStorage.getItem('auth_token');
  }

  /**
   * Returns true when a stored token exists and has not expired.
   * Expired or malformed tokens are cleared.
   */
  isLoggedIn(): boolean {
    const token = this.getToken();
    if (!token) {
      return false;
    }

    const exp = this.getTokenExpiry(token);
    if (exp === null || exp <= Date.now()) {
      this.logout();
      return false;
    }

    return true;
  }

  /**
   * Returns a valid (non-expired) token or null.
   */
  getValidToken(): string | null {
    return this.isLoggedIn() ? this.getToken() : null;
  }

  /**
   * Clears persisted authentication state.
   */
  logout(): void {
    localStorage.removeItem('auth_token');
  }

  /**
   * Decodes the JWT payload and returns its expiry in epoch milliseconds, or null when malformed.
   */
  private getTokenExpiry(token: string): number | null {
    try {
      const parts = token.split('.');
      if (parts.length !== 3) {
        return null;
      }

      let base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
      base64 += '='.repeat((4 - (base64.length % 4)) % 4);
      const payload = JSON.parse(atob(base64));
      return typeof payload?.exp === 'number' ? payload.exp * 1000 : null;
    } catch {
      return null;
    }
  }
}
