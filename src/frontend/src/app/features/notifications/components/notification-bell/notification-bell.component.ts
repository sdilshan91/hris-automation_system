import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  OnInit,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { animate, style, transition, trigger } from '@angular/animations';
import { TranslateModule } from '@ngx-translate/core';
import { NotificationService } from '../../services/notification.service';
import { NotificationPanelComponent } from '../notification-panel/notification-panel.component';

/**
 * US-NTF-001: notification bell for the top navigation (AC-2/AC-3).
 *
 * A button with a red unread-count badge (capped at "99+", FR-5) that bounces on
 * a new arrival (§8). Clicking toggles the notification panel, anchored as a
 * dropdown on desktop and a full-width bottom sheet on mobile (< 768px). Hosts a
 * polite ARIA live region announcing newly-arrived notifications (NFR-6).
 *
 * Starts the SignalR connection on init so realtime push is active app-wide
 * (AC-1), and is keyboard navigable (Enter/Space toggle, Escape closes).
 */
@Component({
  selector: 'app-notification-bell',
  standalone: true,
  imports: [CommonModule, TranslateModule, NotificationPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="bell-root">
      <button
        #trigger
        type="button"
        class="bell-btn"
        [attr.aria-label]="'notifications.bell.ariaLabel' | translate"
        [attr.aria-expanded]="open()"
        aria-haspopup="dialog"
        (click)="toggle()"
      >
        <svg
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 20 20"
          fill="currentColor"
          class="bell-icon"
          aria-hidden="true"
        >
          <path
            fill-rule="evenodd"
            d="M10 2a6 6 0 0 0-6 6c0 1.887-.454 3.665-1.257 5.234a.75.75 0 0 0 .515 1.076 32.91 32.91 0 0 0 3.256.508 3.5 3.5 0 0 0 6.972 0 32.903 32.903 0 0 0 3.256-.508.75.75 0 0 0 .515-1.076A11.448 11.448 0 0 1 16 8a6 6 0 0 0-6-6Zm0 14.5a2 2 0 0 1-1.95-1.557 33.54 33.54 0 0 0 3.9 0A2 2 0 0 1 10 16.5Z"
            clip-rule="evenodd"
          />
        </svg>

        @if (svc.badge()) {
          <span
            class="badge"
            [@bounce]="bounceState()"
            [attr.aria-hidden]="true"
          >
            {{ svc.badge() }}
          </span>
        }
      </button>

      <!-- ARIA live region: announces new arrivals to screen readers (NFR-6). -->
      <p class="sr-only" aria-live="polite" role="status">
        {{ svc.liveAnnouncement() }}
      </p>

      @if (open()) {
        <!-- Backdrop (mobile bottom-sheet dismissal) -->
        <div class="backdrop" (click)="close()"></div>
        <div class="panel-anchor" [@dropdown]>
          <app-notification-panel (closed)="close()" />
        </div>
      }
    </div>
  `,
  styles: [
    `
      .bell-root {
        @apply relative;
      }

      .bell-btn {
        @apply relative flex h-9 w-9 items-center justify-center rounded-lg
          text-neutral-500 transition-colors hover:bg-neutral-100 hover:text-neutral-700
          focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-1;
      }

      .bell-icon {
        @apply h-5 w-5;
      }

      .badge {
        @apply absolute -right-0.5 -top-0.5 flex min-w-[18px] items-center justify-center
          rounded-full bg-red-600 px-1 text-[10px] font-semibold leading-none text-white;
        height: 18px;
      }

      .backdrop {
        @apply fixed inset-0 z-40;
      }

      @media (max-width: 767px) {
        .backdrop {
          @apply bg-black/30 backdrop-blur-sm;
        }
      }

      .panel-anchor {
        @apply absolute right-0 z-50 mt-2;
      }

      /* Mobile: dock as a bottom sheet (full width). */
      @media (max-width: 767px) {
        .panel-anchor {
          @apply fixed bottom-0 left-0 right-0 mt-0;
        }
      }

      .sr-only {
        @apply absolute m-[-1px] h-px w-px overflow-hidden whitespace-nowrap border-0 p-0;
        clip: rect(0, 0, 0, 0);
      }
    `,
  ],
  animations: [
    // Subtle bounce when the unread count changes (§8 / AC-2).
    trigger('bounce', [
      transition('* => *', [
        style({ transform: 'scale(1)' }),
        animate('150ms ease-out', style({ transform: 'scale(1.35)' })),
        animate('150ms ease-in', style({ transform: 'scale(1)' })),
      ]),
    ]),
    // Panel open transition.
    trigger('dropdown', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(-6px)' }),
        animate(
          '180ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
})
export class NotificationBellComponent implements OnInit {
  readonly svc = inject(NotificationService);

  readonly open = signal(false);
  /** Toggled each time the unread count rises so the badge re-animates. */
  readonly bounceState = signal(0);

  private readonly trigger =
    viewChild<ElementRef<HTMLButtonElement>>('trigger');
  private lastCount = 0;

  constructor() {
    // Bounce the badge whenever the unread count INCREASES (a new arrival),
    // not when it drops (mark-read). AC-2 / §8.
    effect(() => {
      const count = this.svc.unreadCount();
      if (count > this.lastCount) {
        this.bounceState.update((v) => v + 1);
      }
      this.lastCount = count;
    });
  }

  ngOnInit(): void {
    // Establish the realtime connection on app shell load (AC-1). Failures fall
    // back to polling inside the service (NFR-5); never throws to the shell.
    void this.svc.start();
  }

  toggle(): void {
    this.open.update((v) => !v);
    if (this.open()) {
      // Refresh the first page on open so the list is current.
      this.svc.loadFirstPage();
    }
  }

  close(): void {
    this.open.set(false);
  }

  /** Close on Escape and return focus to the trigger (keyboard a11y, NFR-6). */
  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.open()) {
      this.close();
      this.trigger()?.nativeElement.focus();
    }
  }

  /** Close when clicking outside the bell root. */
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.open()) {
      return;
    }
    const root = (event.target as HTMLElement)?.closest('.bell-root');
    if (!root) {
      this.close();
    }
  }
}
