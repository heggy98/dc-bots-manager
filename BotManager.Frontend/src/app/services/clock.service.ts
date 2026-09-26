import { DestroyRef, Injectable, NgZone, inject, signal } from '@angular/core';

/**
 * App-wide 1-second clock exposed as a signal.
 *
 * Components rendering "time since X" text read `now()` from their templates so the
 * text keeps ticking under OnPush. The interval runs outside the Angular zone, so a
 * tick does not trigger an app-wide change detection pass; only views that read
 * `now()` (directly or through a computed whose value changed) are refreshed.
 */
@Injectable({ providedIn: 'root' })
export class ClockService {
  private readonly nowSignal = signal(Date.now());
  readonly now = this.nowSignal.asReadonly();

  constructor() {
    const id = inject(NgZone).runOutsideAngular(() =>
      setInterval(() => this.nowSignal.set(Date.now()), 1000)
    );
    inject(DestroyRef).onDestroy(() => clearInterval(id));
  }
}
