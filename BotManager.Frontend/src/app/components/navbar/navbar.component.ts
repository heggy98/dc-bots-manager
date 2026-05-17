import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../services/auth.service';
import { ThemeService } from '../../services/theme.service';
import { I18nService } from '../../services/i18n.service';

@Component({
  selector: 'app-navbar',
  standalone: true,
  imports: [RouterLink, CommonModule],
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.css']
})
export class NavbarComponent {
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
  toggleTheme(): void { this.themeService.toggleTheme(); }

  /**
   * Changes active UI language.
   */
  setLang(lang: 'cs' | 'en'): void { this.i18n.setLang(lang); }
}
