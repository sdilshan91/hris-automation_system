import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  output,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { animate, style, transition, trigger } from '@angular/animations';
import { TranslateModule } from '@ngx-translate/core';
import { NotificationService } from '../../services/notification.service';
import {
  INotification,
  notificationRoute,
  notificationVisual,
  relativeTime,
} from '../../models/notification.models';

/**
 * US-NTF-001: notification dropdown/panel (AC-3/AC-4/AC-5).
 *
 * Presentational shell over NotificationService: lists notifications
 * (most-recent first), supports infinite scroll for the next 20 (FR-6), a
 * "Mark All as Read" link at the top and "View All" at the bottom. Clicking an
 * item marks it read, decrements the badge and navigates to the linked resource
 * (FR-8). Responsive: 380px dropdown on desktop, full-width bottom sheet < 768px.
 */
@Component({
  selector: 'app-notification-panel',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      class="panel"
      role="dialog"
      aria-modal="false"
      [attr.aria-label]="'notifications.panel.title' | translate"
    >
      <header class="panel-header">
        <h2 class="panel-title">{{ 'notifications.panel.title' | translate }}</h2>
        @if (svc.unreadCount() > 0) {
          <button
            type="button"
            class="mark-all-btn"
            (click)="markAllRead()"
          >
            {{ 'notifications.panel.markAllRead' | translate }}
          </button>
        }
      </header>

      <ul
        #scroller
        class="panel-list"
        role="list"
        tabindex="0"
        (scroll)="onScroll()"
        [attr.aria-busy]="svc.loading()"
      >
        @for (n of svc.notifications(); track n.id) {
          <li class="item-wrap" role="listitem">
            <button
              type="button"
              class="item"
              [class.unread]="!n.isRead"
              [@slideIn]
              (click)="open(n)"
            >
              <span class="icon-chip" [attr.data-tone]="visual(n).tone" aria-hidden="true">
                <span class="dot" [class.dot-on]="!n.isRead"></span>
                {{ visual(n).icon }}
              </span>
              <span class="item-body">
                <span class="item-title" [class.font-semibold]="!n.isRead">
                  {{ n.title }}
                  @if (!n.isRead) {
                    <span class="unread-dot" aria-hidden="true"></span>
                  }
                </span>
                <span class="item-message">{{ n.message }}</span>
              </span>
              <span class="item-time">{{ time(n) }}</span>
            </button>
          </li>
        } @empty {
          @if (!svc.loading()) {
            <li class="empty" role="listitem">
              {{ 'notifications.panel.empty' | translate }}
            </li>
          }
        }

        @if (svc.loading()) {
          <li class="loading-row" role="listitem" aria-hidden="true">
            <span class="spinner"></span>
          </li>
        }
      </ul>

      <footer class="panel-footer">
        <button type="button" class="view-all-btn" (click)="viewAll()">
          {{ 'notifications.panel.viewAll' | translate }}
        </button>
      </footer>
    </div>
  `,
  styles: [
    `
      .panel {
        @apply flex max-h-[480px] w-[380px] max-w-[92vw] flex-col overflow-hidden
          rounded-xl border border-neutral-100 bg-white shadow-lg;
      }

      @media (max-width: 767px) {
        .panel {
          @apply max-h-[80vh] w-screen max-w-none rounded-b-none rounded-t-xl;
        }
      }

      .panel-header {
        @apply flex items-center justify-between border-b border-neutral-50 px-4 py-3;
      }

      .panel-title {
        @apply m-0 text-sm font-semibold text-neutral-900;
      }

      .mark-all-btn {
        @apply rounded-md px-2 py-1 text-xs font-medium text-brand-600
          transition-colors hover:bg-brand-50 focus:outline-none
          focus:ring-2 focus:ring-brand-500;
      }

      .panel-list {
        @apply m-0 flex-1 list-none overflow-y-auto p-0
          focus:outline-none focus:ring-2 focus:ring-inset focus:ring-brand-500;
      }

      .item-wrap {
        @apply border-b border-neutral-50 last:border-b-0;
      }

      .item {
        @apply flex w-full items-start gap-3 px-4 py-3 text-left
          transition-colors duration-200 hover:bg-neutral-50
          focus:outline-none focus:bg-neutral-50;
      }

      .item.unread {
        @apply bg-brand-50/40;
      }

      .icon-chip {
        @apply relative flex h-9 w-9 flex-shrink-0 items-center justify-center
          rounded-full bg-neutral-100 text-[11px] font-semibold uppercase text-neutral-600;
      }

      .icon-chip[data-tone='green'] {
        @apply bg-green-100 text-green-700;
      }
      .icon-chip[data-tone='blue'] {
        @apply bg-blue-100 text-blue-700;
      }
      .icon-chip[data-tone='amber'] {
        @apply bg-amber-100 text-amber-700;
      }
      .icon-chip[data-tone='purple'] {
        @apply bg-purple-100 text-purple-700;
      }
      .icon-chip[data-tone='red'] {
        @apply bg-red-100 text-red-700;
      }

      .item-body {
        @apply flex min-w-0 flex-1 flex-col gap-0.5;
      }

      .item-title {
        @apply flex items-center gap-1.5 truncate text-sm text-neutral-900;
      }

      .unread-dot {
        @apply inline-block h-2 w-2 flex-shrink-0 rounded-full bg-brand-600;
      }

      .item-message {
        @apply line-clamp-2 text-xs text-neutral-500;
      }

      .item-time {
        @apply flex-shrink-0 whitespace-nowrap text-xs text-neutral-400;
      }

      .empty {
        @apply px-4 py-10 text-center text-sm text-neutral-400;
      }

      .loading-row {
        @apply flex justify-center py-4;
      }

      .spinner {
        @apply h-5 w-5 animate-spin rounded-full border-2 border-neutral-200 border-t-brand-600;
      }

      .panel-footer {
        @apply border-t border-neutral-50 p-2;
      }

      .view-all-btn {
        @apply w-full rounded-lg py-2 text-center text-sm font-medium text-brand-600
          transition-colors hover:bg-brand-50 focus:outline-none
          focus:ring-2 focus:ring-brand-500;
      }
    `,
  ],
  animations: [
    // Smooth 200ms slide-in for items appearing in the panel (§8 / AC-2).
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateY(-8px)', opacity: 0 }),
        animate(
          '200ms ease-out',
          style({ transform: 'translateY(0)', opacity: 1 }),
        ),
      ]),
    ]),
  ],
})
export class NotificationPanelComponent {
  readonly svc = inject(NotificationService);
  private readonly router = inject(Router);

  /** Emitted after an action that should close the panel (item click / view all). */
  readonly closed = output<void>();

  private readonly scroller =
    viewChild<ElementRef<HTMLUListElement>>('scroller');

  /** Re-exposed so the template's track/aria stays terse. */
  readonly hasItems = computed(() => this.svc.notifications().length > 0);

  visual(n: INotification) {
    return notificationVisual(n.type);
  }

  time(n: INotification): string {
    return relativeTime(n.createdAt);
  }

  /** Infinite scroll: load the next page when near the bottom (FR-6). */
  onScroll(): void {
    const el = this.scroller()?.nativeElement;
    if (!el) {
      return;
    }
    const nearBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 48;
    if (nearBottom) {
      this.svc.loadNextPage();
    }
  }

  /** AC-4: mark read, decrement badge, navigate to the linked resource. */
  open(n: INotification): void {
    if (!n.isRead) {
      this.svc.markRead(n.id).subscribe();
    }
    const route = notificationRoute(n.resourceType, n.resourceId);
    this.closed.emit();
    if (route) {
      this.router.navigate(route);
    }
  }

  markAllRead(): void {
    this.svc.markAllRead().subscribe();
  }

  viewAll(): void {
    this.closed.emit();
    this.router.navigate(['/notifications']);
  }
}
