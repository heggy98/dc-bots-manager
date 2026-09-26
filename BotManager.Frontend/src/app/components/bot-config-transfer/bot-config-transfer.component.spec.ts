import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';

import { BotConfigTransferComponent } from './bot-config-transfer.component';
import { BotConfigImportSummary } from '../../services/bot-config-transfer.service';

const summary = (dryRun: boolean): BotConfigImportSummary => ({
  dryRun,
  mode: 'replace',
  appliedDiscordIds: true,
  botName: 'Imported bot',
  boardsRemoved: 1,
  boardsImported: 2,
  teamsImported: 5,
  commandsUpdated: 3,
  commandsCreated: 0,
  warnings: ['The bot is running; restart it to apply the imported configuration.']
});

describe('BotConfigTransferComponent', () => {
  let fixture: ComponentFixture<BotConfigTransferComponent>;
  let component: BotConfigTransferComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BotConfigTransferComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideToastr()]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BotConfigTransferComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('botId', 7);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.match(r => r.url.startsWith('/i18n/'));
    httpMock.verify();
  });

  function selectFile(content: string, name = 'config.json'): Promise<void> {
    const file = new File([content], name, { type: 'application/json' });
    const input = { files: [file], value: 'C:\\fakepath\\config.json' } as unknown as HTMLInputElement;
    return component.onFileSelected({ target: input } as unknown as Event);
  }

  it('downloads the export as a JSON file', () => {
    const click = spyOn(HTMLAnchorElement.prototype, 'click');
    component.exportConfig();
    httpMock.expectOne('/api/bot/admin/7/export').flush({ schemaVersion: 1, boards: [] });
    expect(click).toHaveBeenCalled();
  });

  it('rejects files that are not JSON without calling the API', async () => {
    await selectFile('{ nope');
    fixture.detectChanges();
    expect(component.errors().length).toBe(1);
    expect(fixture.nativeElement.querySelector('.transfer-errors')).not.toBeNull();
  });

  it('previews via a dry run, then imports on confirm and notifies the parent', async () => {
    const importedSpy = jasmine.createSpy('imported');
    component.imported.subscribe(importedSpy);

    await selectFile(JSON.stringify({ schemaVersion: 1, boards: [] }));
    const dry = httpMock.expectOne(r => r.url === '/api/bot/admin/7/import');
    expect(dry.request.method).toBe('POST');
    expect(dry.request.params.get('dryRun')).toBe('true');
    expect(dry.request.params.get('mode')).toBe('replace');
    dry.flush(summary(true));
    fixture.detectChanges();

    const preview: HTMLElement = fixture.nativeElement.querySelector('.preview');
    expect(preview.textContent).toContain('Imported bot');
    expect(preview.textContent).toContain('5');
    expect(importedSpy).not.toHaveBeenCalled();

    (preview.querySelector('.btn-primary') as HTMLButtonElement).click();
    const real = httpMock.expectOne(r => r.url === '/api/bot/admin/7/import');
    expect(real.request.params.get('dryRun')).toBe('false');
    expect(real.request.body).toEqual({ schemaVersion: 1, boards: [] });
    real.flush(summary(false));
    fixture.detectChanges();

    expect(importedSpy).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('.preview')).toBeNull();
  });

  it('re-runs the dry run without Discord ids when the option is unchecked', async () => {
    await selectFile(JSON.stringify({ schemaVersion: 1 }));
    httpMock.expectOne(r => r.params.get('applyDiscordIds') === 'true').flush(summary(true));

    component.setApplyDiscordIds(false);
    httpMock.expectOne(r => r.params.get('applyDiscordIds') === 'false' && r.params.get('dryRun') === 'true')
      .flush({ ...summary(true), appliedDiscordIds: false });
    expect(component.preview()?.appliedDiscordIds).toBeFalse();
  });

  it('lists server validation errors', async () => {
    await selectFile(JSON.stringify({ schemaVersion: 9 }));
    httpMock.expectOne(r => r.url === '/api/bot/admin/7/import')
      .flush({ message: 'Import document is invalid.', errors: ['schemaVersion: unsupported value 9 (expected 1).'] },
        { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();

    expect(component.preview()).toBeNull();
    expect(fixture.nativeElement.querySelector('.transfer-errors')?.textContent).toContain('schemaVersion');
  });
});
