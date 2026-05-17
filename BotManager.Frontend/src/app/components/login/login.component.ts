import { Component, OnInit } from '@angular/core';
import { AuthService } from '../../services/auth.service';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { I18nService } from '../../services/i18n.service';
import { ToastrService } from 'ngx-toastr';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, CommonModule],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent implements OnInit {
  email = '';
  password = '';
  errorMessage = '';
  isLoading = false;
  showCaptcha = false;
  failedAttempts = 0;

  /**
   * Creates a new login component.
   */
  constructor(
    private authService: AuthService,
    private router: Router,
    private toastr: ToastrService,
    public i18n: I18nService
  ) { }

  /**
   * Loads current login-attempt status for adaptive login UI.
   */
  ngOnInit(): void {
    this.authService.getAttemptStatus().subscribe({
      next: (status) => {
        this.failedAttempts = status.attempts;
        this.showCaptcha = status.attempts >= 3;
      },
      error: () => { }
    });
  }

  /**
   * Submits login credentials and handles authentication errors.
   */
  onSubmit(): void {
    if (!this.email || !this.password) {
      this.errorMessage = this.i18n.t('login.fill_fields');
      return;
    }
    this.isLoading = true;
    this.errorMessage = '';

    this.authService.login({ email: this.email, password: this.password }).subscribe({
      next: (res) => {
        this.authService.saveToken(res.token);
        this.toastr.success(this.i18n.t('login.success'), this.i18n.t('login.title'));
        this.router.navigate(['/admin']);
      },
      error: (err) => {
        this.isLoading = false;
        this.failedAttempts++;
        if (this.failedAttempts >= 3) this.showCaptcha = true;
        if (err.status === 429) {
          this.errorMessage = this.i18n.t('login.error_locked');
        } else {
          this.errorMessage = this.i18n.t('login.error_credentials');
        }
        this.toastr.error(this.errorMessage, this.i18n.t('login.title'));
      }
    });
  }

  /**
   * Placeholder handler for Google login flow.
   */
  onGoogleLogin(): void {
    this.errorMessage = this.i18n.t('login.error_google') + ' (Google Sign-In SDK needed)';
  }
}
