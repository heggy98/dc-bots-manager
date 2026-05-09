import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

export type ThemeMode = 'dark' | 'light';

@Injectable({
    providedIn: 'root'
})
export class ThemeService {
    private themeSubject = new BehaviorSubject<ThemeMode>(this.getSavedTheme());
    theme$ = this.themeSubject.asObservable();

    private getSavedTheme(): ThemeMode {
        return (localStorage.getItem('theme') as ThemeMode) || 'dark';
    }

    get currentTheme(): ThemeMode {
        return this.themeSubject.value;
    }

    toggleTheme(): void {
        const next = this.currentTheme === 'dark' ? 'light' : 'dark';
        localStorage.setItem('theme', next);
        this.themeSubject.next(next);
        this.applyTheme(next);
    }

    applyTheme(theme: ThemeMode): void {
        document.body.classList.remove('theme-dark', 'theme-light');
        document.body.classList.add(`theme-${theme}`);
    }

    init(): void {
        this.applyTheme(this.currentTheme);
    }
}
