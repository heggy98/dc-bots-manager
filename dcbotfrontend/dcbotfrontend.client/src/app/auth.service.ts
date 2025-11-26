import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private isLoggedInSubject = new BehaviorSubject<boolean>(false);
  public isLoggedIn$: Observable<boolean> = this.isLoggedInSubject.asObservable();

  constructor() {
    // Při inicializaci služby můžeme zkontrolovat, zda je uživatel přihlášený
    // (například podle uloženého tokenu v localStorage).
    const token = localStorage.getItem('authToken');
    this.isLoggedInSubject.next(!!token); // !! převede null/undefined na false a string na true
  }

  login(username: string, password: string): Promise<boolean> {
    // Zde by probíhala komunikace se serverem pro ověření přihlašovacích údajů.
    // Pro tento příklad simulujeme úspěšné přihlášení po 1 sekundě.
    return new Promise((resolve) => {
      setTimeout(() => {
        if (username === 'uzivatel' && password === 'heslo') {
          localStorage.setItem('authToken', 'mockToken'); // Uložíme mock token
          this.isLoggedInSubject.next(true);
          resolve(true);
        } else {
          resolve(false);
        }
      }, 1000);
    });
  }

  logout() {
    localStorage.removeItem('authToken');
    this.isLoggedInSubject.next(false);
  }

  isAuthenticated(): Observable<boolean> {
    return this.isLoggedIn$;
  }
}