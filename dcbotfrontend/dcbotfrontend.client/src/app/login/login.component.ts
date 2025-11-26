import { Component, Input } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../auth.service'; // Předpokládám, že AuthService je v této cestě

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent {
  username = '';
  password = '';
  errorMessage = '';
  loginSuccess = false;

  constructor(private router: Router, private authService: AuthService) { }

  login() {
    // Zde by měla být logika pro ověření uživatele pomocí AuthService
    this.authService.login(this.username, this.password)
      .then((success) => {
        if (success) {
          this.loginSuccess = true;
          this.errorMessage = '';
          console.log('Přihlášeno!');
          this.router.navigate(['/dashboard']); // Přesměrování na /dashboard
        } else {
          this.errorMessage = 'Neplatné jméno nebo heslo.';
          this.loginSuccess = false;
        }
      })
      .catch((error) => {
        this.errorMessage = 'Chyba při přihlašování.';
        console.error('Chyba přihlášení:', error);
        this.loginSuccess = false;
      });
  }
}