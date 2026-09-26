import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

/**
 * Returns true for same-origin relative API/hub URLs that may carry the bearer token.
 */
function isOwnApiUrl(url: string): boolean {
  return url.startsWith('/api/') || url.startsWith('/hubs/');
}

/**
 * Adds bearer token header to outgoing API requests when available
 * and redirects to login when the backend rejects the session.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const ownApi = isOwnApiUrl(req.url);
  const token = ownApi ? authService.getValidToken() : null;
  const request = token
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(request).pipe(
    catchError((err: unknown) => {
      if (
        ownApi &&
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        !req.url.startsWith('/api/auth/')
      ) {
        authService.logout();
        const currentUrl = router.url;
        if (!currentUrl.startsWith('/login')) {
          router.navigate(['/login'], { queryParams: { returnUrl: currentUrl } });
        }
      }
      return throwError(() => err);
    })
  );
};
