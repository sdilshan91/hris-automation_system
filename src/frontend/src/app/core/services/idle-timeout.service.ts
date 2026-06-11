import { Injectable, inject, signal, OnDestroy, NgZone } from '@angular/core';
import { Subscription } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { environment } from '../../../environments/environment';

/**
 * US-AUTH-009 BR-6: Idle timeout tracking and warning service.
 *
 * Monitors user activity (mouse, keyboard, touch, scroll) and triggers
 * a warning modal 5 minutes before idle expiry. Also provides a
 * keep-alive call that resets the idle timer on the backend.
 *
 * The idle timeout value is driven by the tenant auth settings
 * (idleTimeoutMinutes). The warning fires at (timeout - 5) minutes.
 */
@Injectable({ providedIn: 'root' })
export class IdleTimeoutService implements OnDestroy {
  private readonly authService = inject(AuthService);
  private readonly ngZone = inject(NgZone);

  /** Whether the warning modal should be shown */
  readonly showWarning = signal(false);

  /** Seconds remaining until idle expiry (countdown in warning modal) */
  readonly secondsRemaining = signal(0);

  /** Configured idle timeout in minutes (set from tenant auth settings) */
  private idleTimeoutMinutes = 60;

  /** Warning threshold in seconds (default 5 minutes = 300s) */
  private readonly warningSeconds = environment.idleWarningSeconds || 300;

  private warningTimerId: ReturnType<typeof setTimeout> | null = null;
  private countdownTimerId: ReturnType<typeof setInterval> | null = null;
  private activityListenersBound = false;
  private running = false;
  private keepAliveSub: Subscription | null = null;

  private readonly activityEvents: string[] = [
    'mousemove',
    'mousedown',
    'keydown',
    'touchstart',
    'scroll',
  ];

  private readonly activityHandler = this.onActivity.bind(this);

  /**
   * Start idle tracking with the given timeout.
   * Call this after login / settings load.
   */
  start(idleTimeoutMinutes: number): void {
    this.stop();
    this.idleTimeoutMinutes = idleTimeoutMinutes;

    if (idleTimeoutMinutes <= 0) {
      return; // Idle timeout disabled
    }

    this.running = true;
    this.bindActivityListeners();
    this.resetWarningTimer();
  }

  /** Stop idle tracking (on logout or destroy). */
  stop(): void {
    this.running = false;
    this.clearTimers();
    this.unbindActivityListeners();
    this.showWarning.set(false);
    this.secondsRemaining.set(0);
  }

  /** User clicked "Stay logged in" -- keep alive and reset timer. */
  stayLoggedIn(): void {
    this.showWarning.set(false);
    this.clearTimers();

    this.keepAliveSub?.unsubscribe();
    this.keepAliveSub = this.authService.keepAlive().subscribe({
      next: () => {
        if (this.running) {
          this.resetWarningTimer();
        }
      },
      error: () => {
        // If keep-alive fails the auth interceptor will handle 401
      },
    });
  }

  ngOnDestroy(): void {
    this.stop();
    this.keepAliveSub?.unsubscribe();
  }

  // ─── Private ──────────────────────────────────────────────

  private onActivity(): void {
    if (!this.running || this.showWarning()) {
      return; // Don't reset while warning is showing
    }
    this.resetWarningTimer();
  }

  private resetWarningTimer(): void {
    this.clearTimers();

    const totalIdleMs = this.idleTimeoutMinutes * 60 * 1000;
    const warningMs = this.warningSeconds * 1000;
    const delayMs = Math.max(totalIdleMs - warningMs, 0);

    this.ngZone.runOutsideAngular(() => {
      this.warningTimerId = setTimeout(() => {
        this.ngZone.run(() => this.triggerWarning());
      }, delayMs);
    });
  }

  private triggerWarning(): void {
    this.secondsRemaining.set(this.warningSeconds);
    this.showWarning.set(true);

    this.ngZone.runOutsideAngular(() => {
      this.countdownTimerId = setInterval(() => {
        this.ngZone.run(() => {
          const current = this.secondsRemaining();
          if (current <= 1) {
            this.clearTimers();
            this.showWarning.set(false);
            this.secondsRemaining.set(0);
            // Session will be expired on next API call (backend enforces)
            this.authService.logout();
          } else {
            this.secondsRemaining.set(current - 1);
          }
        });
      }, 1000);
    });
  }

  private clearTimers(): void {
    if (this.warningTimerId !== null) {
      clearTimeout(this.warningTimerId);
      this.warningTimerId = null;
    }
    if (this.countdownTimerId !== null) {
      clearInterval(this.countdownTimerId);
      this.countdownTimerId = null;
    }
  }

  private bindActivityListeners(): void {
    if (this.activityListenersBound) {
      return;
    }
    this.ngZone.runOutsideAngular(() => {
      this.activityEvents.forEach((event) => {
        document.addEventListener(event, this.activityHandler, {
          passive: true,
        });
      });
    });
    this.activityListenersBound = true;
  }

  private unbindActivityListeners(): void {
    if (!this.activityListenersBound) {
      return;
    }
    this.activityEvents.forEach((event) => {
      document.removeEventListener(event, this.activityHandler);
    });
    this.activityListenersBound = false;
  }
}
