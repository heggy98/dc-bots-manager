import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr, ToastrService } from 'ngx-toastr';

import { AdminConfigComponent } from './admin-config.component';
import { I18nService } from '../../services/i18n.service';

describe('AdminConfigComponent (OnPush)', () => {
  let fixture: ComponentFixture<AdminConfigComponent>;
  let httpMock: HttpTestingController;
  let toastr: ToastrService;
  let savedLang: string | null;

  const el = () => fixture.nativeElement as HTMLElement;
  const failI18nLoads = () =>
    httpMock.match(r => r.url.startsWith('/i18n/')).forEach(r => r.error(new ProgressEvent('offline')));
  const saveButtons = () => Array.from(el().querySelectorAll('.config-control button')) as HTMLButtonElement[];

  beforeEach(fakeAsync(() => {
    savedLang = localStorage.getItem('lang');
    localStorage.setItem('lang', 'en');
    TestBed.configureTestingModule({
      imports: [AdminConfigComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideToastr()]
    });

    httpMock = TestBed.inject(HttpTestingController);
    toastr = TestBed.inject(ToastrService);
    fixture = TestBed.createComponent(AdminConfigComponent);
    fixture.detectChanges();
    failI18nLoads();

    httpMock.expectOne('/api/systemconfig').flush([
      { id: 1, key: 'Site.Name', value: 'Bots', description: 'Display name' },
      { id: 2, key: 'BoardGlobal.DefaultBoardDescription', value: 'Line1\nLine2' }
    ]);
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
  }));

  afterEach(() => {
    if (savedLang === null) localStorage.removeItem('lang');
    else localStorage.setItem('lang', savedLang);
  });

  it('renders loaded configs with their values', () => {
    expect(el().querySelectorAll('.config-item').length).toBe(2);
    expect((el().querySelector('input[type="text"]') as HTMLInputElement).value).toBe('Bots');
    expect((el().querySelector('textarea') as HTMLTextAreaElement).value).toBe('Line1\nLine2');
    expect(el().textContent).toContain('Display name');
  });

  it('saves the edited value, shows the check mark and reverts it after 2s', fakeAsync(() => {
    const success = spyOn(toastr, 'success');
    const field = el().querySelector('input[type="text"]') as HTMLInputElement;
    field.value = 'Renamed';
    field.dispatchEvent(new Event('input'));
    expect(fixture.componentInstance.editValues()['Site.Name']).toBe('Renamed');

    saveButtons()[0].click();
    const put = httpMock.expectOne('/api/systemconfig');
    expect(put.request.body).toEqual({ key: 'Site.Name', value: 'Renamed' });
    put.flush(null);
    fixture.detectChanges();

    expect(success).toHaveBeenCalled();
    expect(saveButtons()[0].textContent?.trim()).toBe('✓');
    expect(saveButtons()[1].textContent?.trim()).toBe('Save');
    expect(fixture.componentInstance.configs()[0].value).toBe('Renamed');

    tick(2000);
    fixture.detectChanges();
    expect(saveButtons()[0].textContent?.trim()).toBe('Save');
  }));

  it('toasts an error when save fails', () => {
    const error = spyOn(toastr, 'error');
    saveButtons()[1].click();
    httpMock.expectOne('/api/systemconfig').flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();
    expect(error).toHaveBeenCalled();
    expect(saveButtons()[1].textContent?.trim()).toBe('Save');
  });

  it('re-renders on language switch', () => {
    TestBed.inject(I18nService).setLang('cs');
    failI18nLoads();
    fixture.detectChanges();
    expect(el().querySelector('.page-header h2')?.textContent?.trim()).toBe('Konfigurace systému');
    expect(saveButtons()[0].textContent?.trim()).toBe('Uložit');
  });
});
