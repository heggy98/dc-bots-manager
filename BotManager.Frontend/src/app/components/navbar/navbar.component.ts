import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

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
  ) { }

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
