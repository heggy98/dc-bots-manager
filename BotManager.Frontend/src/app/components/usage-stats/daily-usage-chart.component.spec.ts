import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';

import { DailyUsageChartComponent, columnPath, niceTicks } from './daily-usage-chart.component';
import { DailyUsageDto } from '../../services/stats.service';

function day(date: string, commands: number, errors: number): DailyUsageDto {
  return { date, commands, errors, joins: 0, leaves: 0 };
}

describe('niceTicks', () => {
  it('rounds the maximum up to a clean step', () => {
    expect(niceTicks(0)).toEqual([0, 1]);
    expect(niceTicks(3)).toEqual([0, 1, 2, 3]);
    expect(niceTicks(97)).toEqual([0, 25, 50, 75, 100]);
    expect(niceTicks(1234)).toEqual([0, 500, 1000, 1500]);
  });
});

describe('columnPath', () => {
  it('draws nothing for empty heights and a rounded top otherwise', () => {
    expect(columnPath(0, 0, 10, 0, true)).toBe('');
    expect(columnPath(0, 0, 10, 20, false)).toBe('M0,0h10v20h-10Z');
    expect(columnPath(0, 0, 10, 20, true)).toContain('Q');
  });
});

describe('DailyUsageChartComponent', () => {
  let fixture: ComponentFixture<DailyUsageChartComponent>;
  const data = [day('2026-09-01', 4, 1), day('2026-09-02', 0, 0), day('2026-09-03', 10, 0)];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DailyUsageChartComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();

    fixture = TestBed.createComponent(DailyUsageChartComponent);
    fixture.componentRef.setInput('data', data);
    fixture.detectChanges();
  });

  it('renders stacked segments only for non-empty values and labels the peak day', () => {
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('path.bar.s1').length).toBe(2);
    expect(el.querySelectorAll('path.bar.s2').length).toBe(1);
    expect(el.querySelector('text.peak')?.textContent?.trim()).toBe('10');
    expect(el.querySelectorAll('rect.hit').length).toBe(3);
  });

  it('shows a tooltip on hover and moves it with the arrow keys', () => {
    const el: HTMLElement = fixture.nativeElement;
    (el.querySelectorAll('rect.hit')[0] as SVGRectElement).dispatchEvent(new Event('pointerenter'));
    fixture.detectChanges();
    expect(el.querySelector('.tooltip')?.textContent).toContain('3');

    el.querySelector('svg')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight' }));
    fixture.detectChanges();
    expect(fixture.componentInstance.active()).toBe(1);

    el.querySelector('svg')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'End' }));
    fixture.detectChanges();
    expect(fixture.componentInstance.active()).toBe(2);
    expect(el.querySelector('.sr-only')?.textContent).toContain('10');

    el.querySelector('svg')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el.querySelector('.tooltip')).toBeNull();
  });

  it('offers a table view with every value', () => {
    const el: HTMLElement = fixture.nativeElement;
    (el.querySelector('.view-toggle') as HTMLButtonElement).click();
    fixture.detectChanges();

    const rows = el.querySelectorAll('tbody tr');
    expect(rows.length).toBe(3);
    expect(rows[0].textContent).toContain('4');
    expect(el.querySelector('svg')).toBeNull();
  });

  it('keeps totals consistent with the data', () => {
    expect(fixture.componentInstance.totals()).toEqual({ commands: 14, errors: 1, success: 13 });
  });
});
