import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';

import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    })
    .compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('switches to the code step when the backend requires two-factor authentication', () => {
    component.email = 'admin@example.com';
    component.password = 'secret';
    component.onSubmit();

    http.expectOne('/api/auth/login').flush({ twoFactorRequired: true }, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    expect(component.twoFactorStep).toBeTrue();
    expect(component.errorMessage).toBe('');
    expect(fixture.nativeElement.querySelector('#totpCode')).toBeTruthy();

    component.totpCode = '123 456';
    component.onSubmit();
    const retry = http.match(r => r.url === '/api/auth/login' && r.body?.totpCode === '123456');
    expect(retry.length).toBe(1);
    expect(retry[0].request.body.password).toBe('secret');
  });

  it('sends a recovery code instead of a TOTP code when chosen', () => {
    component.email = 'admin@example.com';
    component.password = 'secret';
    component.twoFactorStep = true;
    component.toggleRecoveryCode();
    component.totpCode = 'ABCDE-FGHJK';
    component.onSubmit();

    const req = http.match(r => r.url === '/api/auth/login');
    expect(req[0].request.body.recoveryCode).toBe('ABCDE-FGHJK');
    expect(req[0].request.body.totpCode).toBeUndefined();
  });
});
