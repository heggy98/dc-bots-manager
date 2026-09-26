import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { By } from '@angular/platform-browser';
import { UsageStatsComponent } from './usage-stats.component';
import { BarListComponent } from './bar-list.component';
import { StatsRangeSelectorComponent } from './stats-range-selector.component';
import { UsageStatsDto } from '../../services/stats.service';

function stats(days: number, botId: number | null = null): UsageStatsDto {
  return {
    days,
    from: '2026-09-01T00:00:00',
    to: '2026-09-07T12:00:00',
    botId,
    totals: { commands: 12, errors: 3, joins: 2, leaves: 1 },
    daily: Array.from({ length: days }, (_, i) => ({ date: `2026-09-${String(i + 1).padStart(2, '0')}`, commands: i, errors: 0, joins: 0, leaves: 0 })),
    topCommands: [{ command: '/board add-team', commandName: 'board', subCommandName: 'add-team', count: 9, errors: 1 }],
    uptime: [{ botId: 1, name: 'Alpha', status: 'Online', uptimeSeconds: 50, windowSeconds: 100, uptimePercent: 50 }]
  };
}

describe('UsageStatsComponent', () => {
  let fixture: ComponentFixture<UsageStatsComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    localStorage.removeItem('stats.range');
    await TestBed.configureTestingModule({
      imports: [UsageStatsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UsageStatsComponent);
  });

  afterEach(() => localStorage.removeItem('stats.range'));

  it('loads dashboard stats for 30 days and renders tiles, chart, top commands and uptime', () => {
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === '/api/bot/admin/stats' && r.params.get('days') === '30').flush(stats(30));
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.tile-value')?.textContent).toContain('12');
    expect(el.querySelector('app-daily-usage-chart')).not.toBeNull();
    expect(el.textContent).toContain('/board add-team');
    expect(el.textContent).toContain('Alpha');
    expect(el.textContent).toContain('50 %');
  });

  it('reloads with the selected range and remembers it', () => {
    fixture.detectChanges();
    httpMock.expectOne(r => r.params.get('days') === '30').flush(stats(30));
    fixture.detectChanges();

    fixture.debugElement.query(By.directive(StatsRangeSelectorComponent))
      .componentInstance.valueChange.emit(7);
    fixture.detectChanges();

    httpMock.expectOne(r => r.url === '/api/bot/admin/stats' && r.params.get('days') === '7').flush(stats(7));
    expect(localStorage.getItem('stats.range')).toBe('7');
  });

  it('uses the per-bot endpoint when a bot id is set and hides the per-bot uptime list', () => {
    fixture.componentRef.setInput('botId', 5);
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === '/api/bot/admin/5/stats').flush(stats(30, 5));
    fixture.detectChanges();

    const lists = fixture.debugElement.queryAll(By.directive(BarListComponent));
    expect(lists.length).toBe(1);
    expect(fixture.nativeElement.textContent).toContain('2 / 1');
  });

  it('shows a retry action when loading fails', () => {
    fixture.detectChanges();
    httpMock.expectOne(r => r.url === '/api/bot/admin/stats').flush('x', { status: 500, statusText: 'err' });
    fixture.detectChanges();

    const retry = fixture.nativeElement.querySelector('.stats-error button') as HTMLButtonElement;
    expect(retry).not.toBeNull();
    retry.click();
    httpMock.expectOne(r => r.url === '/api/bot/admin/stats');
  });
});

describe('BarListComponent', () => {
  it('scales bars to the largest value and meters to the fixed maximum', () => {
    const fixture = TestBed.createComponent(BarListComponent);
    fixture.componentRef.setInput('items', [
      { key: 'a', label: 'a', value: 10, valueText: '10' },
      { key: 'b', label: 'b', value: 5, valueText: '5' }
    ]);
    fixture.detectChanges();
    const fills = fixture.nativeElement.querySelectorAll('.fill') as NodeListOf<HTMLElement>;
    expect(fills[0].style.width).toBe('100%');
    expect(fills[1].style.width).toBe('50%');

    fixture.componentRef.setInput('variant', 'meter');
    fixture.detectChanges();
    expect(fills[0].style.width).toBe('10%');
  });
});
