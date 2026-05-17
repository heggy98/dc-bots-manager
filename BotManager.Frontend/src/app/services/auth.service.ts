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
   * Returns true when a token is currently stored.
   */
  isLoggedIn(): boolean {
    return !!this.getToken();
  }

  /**
   * Clears persisted authentication state.
   */
  logout(): void {
    localStorage.removeItem('auth_token');
  }
}
