import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface TwoFactorStatus {
  enabled: boolean;
  pendingSetup: boolean;
  recoveryCodesRemaining: number;
}

export interface TwoFactorSetup {
  secret: string;
  otpAuthUri: string;
}

export interface TwoFactorDisableRequest {
  password: string;
  code?: string;
  recoveryCode?: string;
}

/**
 * API client for the admin account's TOTP two-factor authentication settings.
 */
@Injectable({ providedIn: 'root' })
export class TwoFactorService {
  constructor(private http: HttpClient) { }

  /** Returns whether 2FA is enabled and how many recovery codes remain. */
  getStatus(): Observable<TwoFactorStatus> {
    return this.http.get<TwoFactorStatus>('/api/auth/2fa/status');
  }

  /** Starts a setup: returns a new secret and otpauth:// URI (not enforced until enabled). */
  setup(): Observable<TwoFactorSetup> {
    return this.http.post<TwoFactorSetup>('/api/auth/2fa/setup', {});
  }

  /** Confirms the setup with a code; returns the recovery codes (shown only once). */
  enable(code: string): Observable<{ recoveryCodes: string[] }> {
    return this.http.post<{ recoveryCodes: string[] }>('/api/auth/2fa/enable', { code });
  }

  /** Disables 2FA (password plus TOTP or recovery code). */
  disable(request: TwoFactorDisableRequest): Observable<void> {
    return this.http.post<void>('/api/auth/2fa/disable', request);
  }
}
