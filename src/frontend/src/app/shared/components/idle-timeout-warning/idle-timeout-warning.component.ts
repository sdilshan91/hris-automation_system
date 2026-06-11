import {
  Component,
  ChangeDetectionStrategy,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { IdleTimeoutService } from '../../../core/services/idle-timeout.service';

/**
 * US-AUTH-009 BR-6: Idle timeout warning modal.
 *
 * Displays a modal with a countdown timer 5 minutes before idle expiry.
 * The user can click "Stay logged in" to reset the idle timer via a
 * keep-alive API call. If the countdown reaches zero, the session is
 * considered expired and the user is logged out.
 *
 * On mobile (< 640px), the modal renders as a bottom sheet.
 *
 * This component is intended to be placed in the main layout so it
 * appears globally when the user is authenticated.
 */
@Component({
  selector: 'app-idle-timeout-warning',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('overlayFade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
    trigger('dialogSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(16px) scale(0.97)' }),
        animate(
          '250ms ease-out',
          style({ opacity: 1, transform: 'translateY(0) scale(1)' })
        ),
      ]),
      transition(':leave', [
        animate(
          '150ms ease-in',
          style({ opacity: 0, transform: 'translateY(8px) scale(0.97)' })
        ),
      ]),
    ]),
  ],
  template: `
    @if (idleService.showWarning()) {
      <div
        class="warning-overlay"
        [@overlayFade]
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="idle-warning-title"
        aria-describedby="idle-warning-desc"
      >
        <div class="warning-dialog" [@dialogSlide]>
          <!-- Icon -->
          <div class="warning-icon-wrapper">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="1.5"
              class="w-7 h-7 text-amber-600"
              aria-hidden="true"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                d="M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z"
              />
            </svg>
          </div>

          <h3 id="idle-warning-title" class="warning-title">
            Session about to expire
          </h3>

          <p id="idle-warning-desc" class="warning-text">
            Your session will expire due to inactivity.
          </p>

          <!-- Countdown -->
          <div class="countdown" aria-live="polite">
            <span class="countdown-value">{{ formatCountdown() }}</span>
            <span class="countdown-label">remaining</span>
          </div>

          <!-- Action -->
          <button
            class="btn-stay"
            (click)="idleService.stayLoggedIn()"
            autofocus
          >
            Stay logged in
          </button>
        </div>
      </div>
    }
  `,
  styles: [
    `
    .warning-overlay {
      @apply fixed inset-0 z-[100] flex items-center justify-center
        bg-black/30 backdrop-blur-sm px-4;
    }

    /* Bottom sheet on mobile */
    @media (max-width: 639px) {
      .warning-overlay {
        @apply items-end pb-0;
      }

      .warning-dialog {
        @apply rounded-b-none w-full max-w-none mb-0;
        padding-bottom: calc(1.5rem + env(safe-area-inset-bottom, 0px));
      }
    }

    .warning-dialog {
      @apply w-full max-w-sm rounded-xl bg-white border border-neutral-100
        p-6 shadow-notion-lg text-center;
    }

    .warning-icon-wrapper {
      @apply w-12 h-12 mx-auto mb-3 rounded-full bg-amber-50 flex items-center justify-center;
    }

    .warning-title {
      @apply text-base font-semibold text-neutral-900;
    }

    .warning-text {
      @apply mt-1.5 text-sm text-neutral-500;
    }

    .countdown {
      @apply mt-4 flex flex-col items-center;
    }

    .countdown-value {
      @apply text-3xl font-bold text-neutral-900 tabular-nums;
    }

    .countdown-label {
      @apply text-xs text-neutral-500 mt-0.5;
    }

    .btn-stay {
      @apply mt-5 w-full inline-flex items-center justify-center rounded-lg
        bg-brand-600 px-5 py-2.5 text-sm font-medium text-white shadow-sm
        transition-all duration-200 hover:bg-brand-700
        focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-2;
    }
  `,
  ],
})
export class IdleTimeoutWarningComponent {
  readonly idleService = inject(IdleTimeoutService);

  formatCountdown(): string {
    const total = this.idleService.secondsRemaining();
    const minutes = Math.floor(total / 60);
    const seconds = total % 60;
    return `${minutes}:${seconds.toString().padStart(2, '0')}`;
  }
}
