import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type ThemeMode = 'dark' | 'light';

@Injectable({
    providedIn: 'root'
})
export class ThemeService {
    private themeSubject = new BehaviorSubject<ThemeMode>(this.getSavedTheme());
    theme$ = this.themeSubject.asObservable();

    /**
     * Reads persisted theme mode from local storage.
     */
    private getSavedTheme(): ThemeMode {
        return (localStorage.getItem('theme') as ThemeMode) || 'dark';
    }

    /**
     * Returns the currently active theme mode.
     */
    get currentTheme(): ThemeMode {
        return this.themeSubject.value;
    }

    /**
     * Toggles between dark and light mode and persists the result.
     */
    toggleTheme(): void {
        const next = this.currentTheme === 'dark' ? 'light' : 'dark';
        localStorage.setItem('theme', next);
        this.themeSubject.next(next);
        this.applyTheme(next);
    }

    /**
     * Applies the specified theme class to the document body.
     */
    applyTheme(theme: ThemeMode): void {
        document.body.classList.remove('theme-dark', 'theme-light');
        document.body.classList.add(`theme-${theme}`);
    }

    /**
     * Initializes the UI with the persisted theme.
     */
    init(): void {
        this.applyTheme(this.currentTheme);
    }
}
