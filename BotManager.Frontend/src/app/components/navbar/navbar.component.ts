import { ChangeDetectionStrategy, ChangeDetectorRef, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, fromEvent } from 'rxjs';

import { AuthService } from '../../services/auth.service';
import { ThemeService } from '../../services/theme.service';
import { I18nService } from '../../services/i18n.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.css']
})
export class NavbarComponent {
  readonly mobileMenuOpen = signal(false);

  /**
   * Creates a new navbar component.
   */
  constructor(
    public authService: AuthService,
    public themeService: ThemeService,
    public i18n: I18nService
  ) {
    // authService.isLoggedIn() is not reactive (reads localStorage). Login/logout
    // are always followed by a navigation, so re-check the view after each one,
    // and when another tab changes storage.
    const cdr = inject(ChangeDetectorRef);
    const destroyRef = inject(DestroyRef);
    inject(Router).events
      .pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed(destroyRef))
      .subscribe(() => cdr.markForCheck());
    fromEvent(window, 'storage')
      .pipe(takeUntilDestroyed(destroyRef))
      .subscribe(() => cdr.markForCheck());
  }

  /**
   * Toggles application theme.
   */
  toggleTheme(): void {
    this.themeService.toggleTheme();
  }

  /**
   * Toggles mobile navigation visibility.
   */
  toggleMobileMenu(): void {
    this.mobileMenuOpen.update(open => !open);
  }

  /**
   * Closes mobile navigation after any action.
   */
  closeMobileMenu(): void {
    this.mobileMenuOpen.set(false);
  }

  /**
   * Changes active UI language.
   */
  setLang(lang: 'cs' | 'en'): void {
    this.i18n.setLang(lang);
    this.closeMobileMenu();
  }
}
