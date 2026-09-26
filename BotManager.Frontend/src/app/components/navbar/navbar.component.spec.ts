import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';

import { NavbarComponent } from './navbar.component';

describe('NavbarComponent', () => {
  let component: NavbarComponent;
  let fixture: ComponentFixture<NavbarComponent>;
  let httpMock: HttpTestingController;
  let savedLang: string | null;

  beforeEach(async () => {
    savedLang = localStorage.getItem('lang');
    localStorage.setItem('lang', 'cs');

    await TestBed.configureTestingModule({
      imports: [NavbarComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    })
    .compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(NavbarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => {
    if (savedLang === null) {
      localStorage.removeItem('lang');
    } else {
      localStorage.setItem('lang', savedLang);
    }
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('re-renders translations under OnPush when translations load and the language changes', () => {
    httpMock.expectOne('/i18n/cs.json').flush({ 'nav.login': 'Prihlaseni-CS' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Prihlaseni-CS');

    component.setLang('en');
    httpMock.expectOne('/i18n/en.json').flush({ 'nav.login': 'Login-EN' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Login-EN');
  });
});
