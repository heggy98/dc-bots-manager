import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';

import { LiveDurationComponent, formatCompactDuration, parseApiDate } from './live-duration.component';
import { ClockService } from '../../services/clock.service';

describe('LiveDurationComponent', () => {
  const base = Date.parse('2026-01-01T10:00:00Z');
  let now: ReturnType<typeof signal<number>>;
  let fixture: ComponentFixture<LiveDurationComponent>;

  const text = () => (fixture.nativeElement as HTMLElement).textContent?.trim() ?? '';

  beforeEach(async () => {
    now = signal(base);
    await TestBed.configureTestingModule({
      imports: [LiveDurationComponent],
      providers: [{ provide: ClockService, useValue: { now: now.asReadonly() } }]
    }).compileComponents();
    fixture = TestBed.createComponent(LiveDurationComponent);
  });

  it('shows running duration since start and ticks with the clock', () => {
    fixture.componentRef.setInput('online', true);
    fixture.componentRef.setInput('startedAt', '2026-01-01T09:58:55');
    fixture.detectChanges();
    expect(text()).toBe('(1m 5s)');

    now.set(base + 3600_000);
    fixture.detectChanges();
    expect(text()).toBe('(1h 1m 5s)');
  });

  it('shows time since stop when offline', () => {
    fixture.componentRef.setInput('online', false);
    fixture.componentRef.setInput('startedAt', '2025-12-01T00:00:00Z');
    fixture.componentRef.setInput('stoppedAt', '2025-12-30T08:00:00Z');
    fixture.detectChanges();
    expect(text()).toBe('(2d 2h 0m 0s)');
  });

  it('renders nothing without a reference timestamp or for future stop times', () => {
    fixture.componentRef.setInput('online', false);
    fixture.componentRef.setInput('startedAt', '2025-12-01T00:00:00Z');
    fixture.detectChanges();
    expect(text()).toBe('');
    expect(fixture.nativeElement.querySelector('span')).toBeNull();

    fixture.componentRef.setInput('stoppedAt', '2026-01-01T11:00:00Z');
    fixture.detectChanges();
    expect(text()).toBe('');
  });

  it('parses API dates as UTC when no zone is given', () => {
    expect(parseApiDate('2026-01-01T10:00:00')?.getTime()).toBe(base);
    expect(parseApiDate('2026-01-01T11:00:00+01:00')?.getTime()).toBe(base);
    expect(parseApiDate('garbage')).toBeNull();
    expect(parseApiDate(undefined)).toBeNull();
  });

  it('formats compact durations', () => {
    expect(formatCompactDuration(0)).toBe('—');
    expect(formatCompactDuration(59)).toBe('59s');
    expect(formatCompactDuration(90061)).toBe('1d 1h 1m 1s');
  });
});
