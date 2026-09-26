import { Component, ElementRef, NgZone, OnDestroy, OnInit, ViewChild } from '@angular/core';
import { AuthService } from '../../services/auth.service';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { I18nService } from '../../services/i18n.service';
import { ToastrService } from 'ngx-toastr';

/** Minimal typing of the Google reCAPTCHA v2 API used here. */
interface GreCaptcha {
  ready(callback: () => void): void;
  render(container: HTMLElement, parameters: { sitekey: string; callback: (token: string) => void; 'expired-callback': () => void }): number;
  reset(widgetId?: number): void;
}

declare global {
  interface Window { grecaptcha?: GreCaptcha; }
}

const RECAPTCHA_SCRIPT_ID = 'recaptcha-api-script';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, CommonModule],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements OnInit, OnDestroy {
  email = '';
  password = '';
  errorMessage = '';
  isLoading = false;
  showCaptcha = false;
  failedAttempts = 0;
  /** True once the backend asked for the second factor (password already accepted). */
  twoFactorStep = false;
  totpCode = '';
  useRecoveryCode = false;

  private captchaSiteKey: string | null = null;
  private captchaToken: string | null = null;
  private captchaWidgetId: number | null = null;
  private destroyed = false;

  @ViewChild('captchaContainer')
  set captchaContainer(ref: ElementRef<HTMLElement> | undefined) {
    this.captchaElement = ref?.nativeElement ?? null;
    this.renderCaptchaIfNeeded();
  }
  private captchaElement: HTMLElement | null = null;

  /**
   * Creates a new login component.
   */
  constructor(
    private authService: AuthService,
    private router: Router,
    private route: ActivatedRoute,
    private toastr: ToastrService,
    private zone: NgZone,
    public i18n: I18nService
  ) { }

  /**
   * Loads current login-attempt status (lockout / captcha requirement).
   */
  ngOnInit(): void {
    this.refreshAttemptStatus();
  }

  ngOnDestroy(): void {
    this.destroyed = true;
  }

  /**
   * Submits login credentials and handles authentication errors.
   */
  onSubmit(): void {
    if (!this.email || !this.password) {
      this.errorMessage = this.i18n.t('login.fill_fields');
      return;
    }
    if (this.showCaptcha && this.captchaSiteKey && !this.captchaToken) {
      this.errorMessage = this.i18n.t('login.captcha_required');
      return;
    }

    const secondFactor = this.twoFactorStep ? this.totpCode.trim() : '';
    if (this.twoFactorStep && !secondFactor) {
      this.errorMessage = this.i18n.t('login.totp_required');
      return;
    }

    this.isLoading = true;
    this.errorMessage = '';

    this.authService.login({
      email: this.email,
      password: this.password,
      recaptchaToken: this.captchaToken ?? undefined,
      totpCode: this.twoFactorStep && !this.useRecoveryCode ? secondFactor.replace(/\s+/g, '') : undefined,
      recoveryCode: this.twoFactorStep && this.useRecoveryCode ? secondFactor : undefined
    }).subscribe({
      next: () => {
        this.toastr.success(this.i18n.t('login.success'), this.i18n.t('login.title'));
        this.router.navigateByUrl(this.getReturnUrl());
      },
      error: (err) => {
        this.isLoading = false;
        this.resetCaptcha();
        if (err.status === 401 && err.error?.twoFactorRequired) {
          const wasInvalid = !!err.error?.invalidCode;
          this.twoFactorStep = true;
          this.totpCode = '';
          if (!wasInvalid) {
            // Password accepted; ask for the code without counting it as a failure.
            this.errorMessage = '';
            this.refreshAttemptStatus();
            return;
          }
          this.failedAttempts++;
          this.errorMessage = this.i18n.t('login.totp_invalid');
          this.toastr.error(this.errorMessage, this.i18n.t('login.title'));
          this.refreshAttemptStatus();
          return;
        }
        this.failedAttempts++;
        if (err.status === 429) {
          this.errorMessage = this.i18n.t('login.error_locked');
        } else if (err.status === 400 && err.error?.captchaRequired) {
          this.errorMessage = this.i18n.t('login.captcha_required');
        } else {
          this.errorMessage = this.i18n.t('login.error_credentials');
        }
        this.toastr.error(this.errorMessage, this.i18n.t('login.title'));
        this.refreshAttemptStatus();
      }
    });
  }

  /**
   * Leaves the second-factor step and returns to the credentials form.
   */
  backToCredentials(): void {
    this.twoFactorStep = false;
    this.totpCode = '';
    this.useRecoveryCode = false;
    this.errorMessage = '';
  }

  /**
   * Switches between authenticator code and recovery code input.
   */
  toggleRecoveryCode(): void {
    this.useRecoveryCode = !this.useRecoveryCode;
    this.totpCode = '';
    this.errorMessage = '';
  }

  /**
   * Returns a safe in-app return URL from the query string, defaulting to the admin home.
   */
  private getReturnUrl(): string {
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    if (returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') && !returnUrl.startsWith('/login')) {
      return returnUrl;
    }
    return '/admin';
  }

  /**
   * Placeholder handler for Google login flow.
   */
  onGoogleLogin(): void {
    this.errorMessage = this.i18n.t('login.error_google') + ' (Google Sign-In SDK needed)';
  }

  private refreshAttemptStatus(): void {
    this.authService.getAttemptStatus().subscribe({
      next: (status) => {
        this.failedAttempts = status.attempts;
        this.captchaSiteKey = status.captchaSiteKey;
        // The captcha is only shown when the backend has it configured and requires it.
        this.showCaptcha = !!status.captchaSiteKey && status.captchaRequired;
        if (this.showCaptcha) {
          this.loadCaptchaScript();
        }
      },
      error: () => { }
    });
  }

  private loadCaptchaScript(): void {
    if (window.grecaptcha || document.getElementById(RECAPTCHA_SCRIPT_ID)) {
      this.renderCaptchaIfNeeded();
      return;
    }

    const script = document.createElement('script');
    script.id = RECAPTCHA_SCRIPT_ID;
    script.src = 'https://www.google.com/recaptcha/api.js?render=explicit';
    script.async = true;
    script.defer = true;
    script.onload = () => this.renderCaptchaIfNeeded();
    document.head.appendChild(script);
  }

  private renderCaptchaIfNeeded(): void {
    const grecaptcha = window.grecaptcha;
    if (!grecaptcha || !this.captchaElement || !this.captchaSiteKey || this.captchaWidgetId !== null || this.destroyed) {
      return;
    }

    const element = this.captchaElement;
    const siteKey = this.captchaSiteKey;
    grecaptcha.ready(() => {
      if (this.captchaWidgetId !== null || this.destroyed) {
        return;
      }
      this.captchaWidgetId = grecaptcha.render(element, {
        sitekey: siteKey,
        callback: (token: string) => this.zone.run(() => { this.captchaToken = token; }),
        'expired-callback': () => this.zone.run(() => { this.captchaToken = null; })
      });
    });
  }

  private resetCaptcha(): void {
    this.captchaToken = null;
    if (this.captchaWidgetId !== null) {
      window.grecaptcha?.reset(this.captchaWidgetId);
    }
  }
}
