import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr, ToastrService } from 'ngx-toastr';

import { AdminCommandsComponent } from './admin-commands.component';
import { GlobalCommandDto } from '../../services/commands.service';
import { I18nService } from '../../services/i18n.service';

describe('AdminCommandsComponent (OnPush)', () => {
  let fixture: ComponentFixture<AdminCommandsComponent>;
  let httpMock: HttpTestingController;
  let toastr: ToastrService;
  let savedLang: string | null;

  const cmd = (commandName: string, subCommandName?: string, extra: Partial<GlobalCommandDto> = {}): GlobalCommandDto => ({
    commandName,
    subCommandName,
    description: `${commandName} ${subCommandName ?? ''}`.trim(),
    minimumPermissionLevel: 0,
    isEnabled: true,
    botCount: 1,
    hasDifferencesAcrossBots: false,
    ...extra
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const failI18nLoads = () =>
    httpMock.match(r => r.url.startsWith('/i18n/')).forEach(r => r.error(new ProgressEvent('offline')));

  beforeEach(async () => {
    savedLang = localStorage.getItem('lang');
    localStorage.setItem('lang', 'en');
    await TestBed.configureTestingModule({
      imports: [AdminCommandsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideToastr()]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    toastr = TestBed.inject(ToastrService);
    fixture = TestBed.createComponent(AdminCommandsComponent);
    fixture.detectChanges();
    failI18nLoads();
  });

  afterEach(() => {
    if (savedLang === null) localStorage.removeItem('lang');
    else localStorage.setItem('lang', savedLang);
  });

  const loadWith = (data: GlobalCommandDto[]) => {
    httpMock.expectOne('/api/commands/global').flush(data);
    fixture.detectChanges();
  };

  it('renders grouped commands after the HTTP response', () => {
    expect(el().querySelector('.loading-spinner')?.textContent).toContain('Loading');
    loadWith([cmd('zeta'), cmd('alpha', 'b'), cmd('alpha')]);

    const headers = Array.from(el().querySelectorAll('.cmd-header h3')).map(h => h.textContent?.trim());
    expect(headers).toEqual(['/alpha', '/zeta']);
    const firstGroupRows = el().querySelectorAll('.command-group')[0].querySelectorAll('tbody tr');
    expect(firstGroupRows[0].textContent).toContain('(root)');
    expect(firstGroupRows[1].textContent).toContain('b');
  });

  it('shows the empty state when there are no commands', () => {
    loadWith([]);
    expect(el().textContent).toContain('No');
    expect(el().querySelector('.command-group')).toBeNull();
  });

  it('opens and closes the edit modal', () => {
    loadWith([cmd('alpha')]);
    (el().querySelector('.actions-cell button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('.modal-overlay')).not.toBeNull();
    expect((el().querySelector('.modal-body input[name="description"]') as HTMLInputElement)).not.toBeNull();

    (el().querySelector('.modal-header .close-btn') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el().querySelector('.modal-overlay')).toBeNull();
  });

  it('saves, toasts, shows the message and reloads', fakeAsync(() => {
    const success = spyOn(toastr, 'success');
    loadWith([cmd('alpha')]);
    (el().querySelector('.actions-cell button') as HTMLButtonElement).click();
    fixture.detectChanges();

    (el().querySelector('.modal-body form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect((el().querySelector('.modal-body button[type="submit"]') as HTMLButtonElement).disabled).toBeTrue();

    const put = httpMock.expectOne('/api/commands/global/alpha');
    expect(put.request.method).toBe('PUT');
    expect(put.request.body.description).toBe('alpha');
    put.flush({ updated: 3 });
    fixture.detectChanges();

    expect(success).toHaveBeenCalled();
    expect(el().querySelector('.success-alert')?.textContent).toContain(': 3');
    httpMock.expectOne('/api/commands/global').flush([cmd('alpha', undefined, { isEnabled: false })]);
    fixture.detectChanges();
    expect(el().querySelector('.status-badge')?.classList).toContain('offline');

    tick(300);
    fixture.detectChanges();
    expect(el().querySelector('.modal-overlay')).toBeNull();
  }));

  it('shows an error message and toast when save fails', () => {
    const error = spyOn(toastr, 'error');
    loadWith([cmd('alpha', 'sub')]);
    fixture.componentInstance.openEditModal(cmd('alpha', 'sub'));
    fixture.componentInstance.saveCommand({ minimumPermissionLevel: 0, isEnabled: true });
    httpMock.expectOne('/api/commands/global/alpha?subCommandName=sub').flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();

    expect(error).toHaveBeenCalled();
    expect(el().querySelector('.success-alert')).not.toBeNull();
  });

  it('re-renders on language switch', () => {
    loadWith([cmd('alpha')]);
    const title = () => el().querySelector('.page-header h2')?.textContent?.trim();
    expect(title()).toBe('Global Commands Management');

    TestBed.inject(I18nService).setLang('cs');
    failI18nLoads();
    fixture.detectChanges();
    expect(title()).toBe('Globální správa příkazů');
  });
});
