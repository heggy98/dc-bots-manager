import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastrService } from 'ngx-toastr';
import { toDataURL } from 'qrcode';
import { I18nService } from '../../services/i18n.service';
import { TwoFactorService, TwoFactorSetup, TwoFactorStatus } from '../../services/two-factor.service';

/**
 * Security settings of the admin account: TOTP two-factor authentication setup and removal.
 */
@Component({
  selector: 'app-admin-security',
  standalone: true,
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './admin-security.component.html',
  styleUrl: './admin-security.component.css'
})
export class AdminSecurityComponent implements OnInit {
  readonly status = signal<TwoFactorStatus | null>(null);
  readonly setup = signal<TwoFactorSetup | null>(null);
  readonly qrDataUrl = signal<string | null>(null);
  readonly recoveryCodes = signal<string[] | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');

  enableCode = '';
  disablePassword = '';
  disableCode = '';
  useRecoveryForDisable = false;

  constructor(
    private twoFactor: TwoFactorService,
    private toastr: ToastrService,
    public i18n: I18nService
  ) { }

  ngOnInit(): void {
    this.loadStatus();
  }

  /** Reloads the 2FA status from the server. */
  loadStatus(): void {
    this.twoFactor.getStatus().subscribe({
      next: status => this.status.set(status),
      error: () => this.error.set(this.i18n.t('security.load_error'))
    });
  }

  /** Starts a new setup and renders the QR code client-side. */
  startSetup(): void {
    this.busy.set(true);
    this.error.set('');
    this.recoveryCodes.set(null);
    this.twoFactor.setup().subscribe({
      next: async setup => {
        this.setup.set(setup);
        this.enableCode = '';
        try {
          this.qrDataUrl.set(await toDataURL(setup.otpAuthUri, { errorCorrectionLevel: 'M', margin: 2, width: 220 }));
        } catch {
          this.qrDataUrl.set(null);
        }
        this.busy.set(false);
      },
      error: () => {
        this.busy.set(false);
        this.error.set(this.i18n.t('security.setup_error'));
      }
    });
  }

  /** Cancels the setup in the UI (the unconfirmed secret is simply never enabled). */
  cancelSetup(): void {
    this.setup.set(null);
    this.qrDataUrl.set(null);
    this.enableCode = '';
  }

  /** Confirms the setup with a code from the authenticator app. */
  enable(): void {
    const code = this.enableCode.replace(/\s+/g, '');
    if (!/^\d{6}$/.test(code)) {
      this.error.set(this.i18n.t('security.code_format'));
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.twoFactor.enable(code).subscribe({
      next: result => {
        this.busy.set(false);
        this.recoveryCodes.set(result.recoveryCodes);
        this.cancelSetup();
        this.toastr.success(this.i18n.t('security.enabled_toast'));
        this.loadStatus();
      },
      error: err => {
        this.busy.set(false);
        this.error.set(err?.status === 429 ? this.i18n.t('login.error_locked') : this.i18n.t('security.invalid_code'));
      }
    });
  }

  /** Disables 2FA after confirming the password and a code. */
  disable(): void {
    const code = this.disableCode.trim();
    if (!this.disablePassword || !code) {
      this.error.set(this.i18n.t('security.disable_fill'));
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.twoFactor.disable({
      password: this.disablePassword,
      code: this.useRecoveryForDisable ? undefined : code.replace(/\s+/g, ''),
      recoveryCode: this.useRecoveryForDisable ? code : undefined
    }).subscribe({
      next: () => {
        this.busy.set(false);
        this.disablePassword = '';
        this.disableCode = '';
        this.recoveryCodes.set(null);
        this.toastr.success(this.i18n.t('security.disabled_toast'));
        this.loadStatus();
      },
      error: err => {
        this.busy.set(false);
        this.error.set(err?.status === 429 ? this.i18n.t('login.error_locked') : this.i18n.t('security.disable_error'));
      }
    });
  }

  /** Copies the recovery codes to the clipboard. */
  copyRecoveryCodes(): void {
    const codes = this.recoveryCodes();
    if (!codes) {
      return;
    }
    navigator.clipboard?.writeText(codes.join('\n')).then(
      () => this.toastr.success(this.i18n.t('security.copied')),
      () => undefined
    );
  }

  /** Hides the recovery codes after the admin saved them. */
  dismissRecoveryCodes(): void {
    this.recoveryCodes.set(null);
  }
}
