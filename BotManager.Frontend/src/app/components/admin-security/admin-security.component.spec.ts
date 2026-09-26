import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';

import { AdminSecurityComponent } from './admin-security.component';

describe('AdminSecurityComponent', () => {
  let fixture: ComponentFixture<AdminSecurityComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdminSecurityComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    }).compileComponents();

    fixture = TestBed.createComponent(AdminSecurityComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  it('shows the setup button when 2FA is disabled', () => {
    http.expectOne('/api/auth/2fa/status').flush({ enabled: false, pendingSetup: false, recoveryCodesRemaining: 0 });
    fixture.detectChanges();

    const button: HTMLButtonElement | null = fixture.nativeElement.querySelector('button.btn-primary');
    expect(button).toBeTruthy();
    expect(fixture.componentInstance.status()?.enabled).toBeFalse();
  });

  it('renders the secret after starting the setup', async () => {
    http.expectOne('/api/auth/2fa/status').flush({ enabled: false, pendingSetup: false, recoveryCodesRemaining: 0 });
    fixture.componentInstance.startSetup();
    http.expectOne('/api/auth/2fa/setup').flush({
      secret: 'JBSWY3DPEHPK3PXP',
      otpAuthUri: 'otpauth://totp/BotManager:admin%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=BotManager'
    });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('JBSWY3DPEHPK3PXP');
  });

  it('shows remaining recovery codes when enabled', () => {
    http.expectOne('/api/auth/2fa/status').flush({ enabled: true, pendingSetup: false, recoveryCodesRemaining: 5 });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('#disablePassword')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('5');
  });
});
