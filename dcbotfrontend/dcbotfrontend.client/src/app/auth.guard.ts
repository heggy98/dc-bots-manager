import { Injectable } from '@angular/core';
import { CanActivate, ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree, Router } from '@angular/router';
import { Observable } from 'rxjs';
import { AuthService } from './auth.service';
import { map, take } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {

  constructor(private authService: AuthService, private router: Router) {}

  canActivate(
    route: ActivatedRouteSnapshot,
    state: RouterStateSnapshot): Observable<boolean | UrlTree> | Promise<boolean | UrlTree> | boolean | UrlTree {
    return this.authService.isAuthenticated().pipe(
      take(1), // Zajistíme, že se Observable dokončí po první emisi
      map((isAuthenticated) => {
        if (isAuthenticated) {
          return true; // Uživatel je přihlášený, může aktivovat cestu
        } else {
          // Uživatel není přihlášený, přesměrujeme ho na přihlašovací stránku
          return this.router.parseUrl('/login');
        }
      })
    );
  }
}