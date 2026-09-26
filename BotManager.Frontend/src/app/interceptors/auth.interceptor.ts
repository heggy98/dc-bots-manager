import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/** Header the backend requires on cookie-authenticated state-changing requests (CSRF guard). */
const CSRF_HEADER = 'X-Requested-With';
const CSRF_VALUE = 'BotManager';

/**
 * Returns true for same-origin relative API URLs.
 */
function isOwnApiUrl(url: string): boolean {
  return url.startsWith('/api/');
}

/**
 * Auth endpoints whose 401 is a real answer (bad credentials, missing 2FA code) and must not trigger a refresh.
 */
function isSessionEndpoint(url: string): boolean {
  return ['/api/auth/login', '/api/auth/google-login', '/api/auth/refresh', '/api/auth/logout']
    .some(path => url === path || url.startsWith(path + '?'));
}

/**
 * Adds the CSRF header to own API requests. Authentication itself uses HttpOnly cookies sent by
 * the browser. On 401 the session is refreshed once and the request retried; if that fails the
 * user is sent to the login page.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!isOwnApiUrl(req.url)) {
    return next(req);
  }

  const authService = inject(AuthService);
  const router = inject(Router);
  const request = req.clone({ setHeaders: { [CSRF_HEADER]: CSRF_VALUE } });

  const redirectToLogin = () => {
    authService.clearSession();
    const currentUrl = router.url;
    if (!currentUrl.startsWith('/login')) {
      router.navigate(['/login'], { queryParams: { returnUrl: currentUrl } });
    }
  };

  return next(request).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 401 || isSessionEndpoint(req.url)) {
        return throwError(() => err);
      }

      if (!authService.isLoggedIn()) {
        redirectToLogin();
        return throwError(() => err);
      }

      return authService.refresh().pipe(
        switchMap(() => next(request)),
        catchError((retryErr: unknown) => {
          if (retryErr instanceof HttpErrorResponse && retryErr.status === 401) {
            redirectToLogin();
          }
          return throwError(() => retryErr);
        })
      );
    })
  );
};
