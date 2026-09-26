import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideToastr } from 'ngx-toastr';
import { Subject } from 'rxjs';

import { BotDetailComponent } from './bot-detail.component';
import { BotEventsService } from '../../services/bot-events.service';
import { ClockService } from '../../services/clock.service';
import { signal } from '@angular/core';

describe('BotDetailComponent', () => {
  let component: BotDetailComponent;
  let fixture: ComponentFixture<BotDetailComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BotDetailComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideToastr()]
    })
    .compileComponents();

    fixture = TestBed.createComponent(BotDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});

describe('BotDetailComponent (OnPush updates)', () => {
  let fixture: ComponentFixture<BotDetailComponent>;
  let httpMock: HttpTestingController;
  const clockNow = signal(Date.parse('2026-01-01T10:00:00Z'));
  const events = {
    statusChanged$: new Subject<any>(),
    newLog$: new Subject<any>(),
    statsUpdated$: new Subject<any>(),
    historyUpdated$: new Subject<any>(),
    connectAndJoin: () => Promise.resolve(),
    disconnect: () => Promise.resolve(),
    isConnected: () => true
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BotDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideToastr(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '5' }) } } },
        { provide: BotEventsService, useValue: events },
        { provide: ClockService, useValue: { now: clockNow.asReadonly() } }
      ]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(BotDetailComponent);
    fixture.detectChanges();

    httpMock.expectOne('/api/bot/admin/5').flush({
      botId: 5,
      name: 'Test bot',
      status: 'Offline',
      lastStartedAt: '2026-01-01T08:00:00Z',
      lastStoppedAt: '2026-01-01T09:59:00Z',
      requests24h: 1,
      errors24h: 0,
      isPublic: false,
      isTokenAuthorized: true,
      configuration: {},
      guilds: [],
      logs: [],
      histories: []
    });
    httpMock.expectOne('/api/bot/admin/5/teams').flush({ teams: [] });
    httpMock.expectOne('/api/bot/admin/5/boards').flush([]);
    fixture.detectChanges();
  });

  const badge = () => fixture.nativeElement.querySelector('.bot-meta .status-badge') as HTMLElement;

  it('renders HTTP-loaded bot details', () => {
    expect(fixture.nativeElement.querySelector('h2')?.textContent).toContain('Test bot');
    expect(badge().classList).toContain('offline');
  });

  it('re-renders on SignalR status and log events', () => {
    events.statusChanged$.next({ botId: 5, status: 1, statusText: 'Online', timestamp: new Date().toISOString() });
    events.newLog$.next({ botId: 5, timestamp: new Date().toISOString(), level: 'Info', message: 'hello from hub' });
    fixture.detectChanges();

    expect(badge().classList).toContain('online');
    expect(fixture.nativeElement.querySelector('.logger-box')?.textContent).toContain('hello from hub');
  });

  it('ignores SignalR events for other bots', () => {
    events.statusChanged$.next({ botId: 99, status: 1, statusText: 'Online', timestamp: new Date().toISOString() });
    fixture.detectChanges();

    expect(badge().classList).toContain('offline');
  });

  it('ticks the status duration without re-checking the bot-detail view', () => {
    const duration = () => (fixture.nativeElement.querySelector('.bot-meta app-live-duration') as HTMLElement).textContent?.trim();
    expect(duration()).toBe('(1m 0s)');

    const parentRead = spyOn(fixture.componentInstance, 'getStatusTimestamp').and.callThrough();
    clockNow.set(clockNow() + 5000);
    fixture.detectChanges();

    expect(duration()).toBe('(1m 5s)');
    expect(parentRead).not.toHaveBeenCalled();
  });
});
