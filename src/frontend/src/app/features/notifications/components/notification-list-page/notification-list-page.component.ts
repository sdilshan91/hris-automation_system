import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { NotificationService } from '../../services/notification.service';
import {
  INotification,
  notificationRoute,
  notificationVisual,
  relativeTime,
} from '../../models/notification.models';

/**
 * US-NTF-001: full "View All" notifications page (§8 — the panel footer links
 * here). Same data + actions as the panel (mark read, mark-all, navigate) in a
 * wider, scrollable layout with a "Load more" affordance for pagination (FR-6).
 */
@Component({
  selector: 'app-notification-list-page',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page">
      <header class="page-header">
        <h1 class="page-title">{{ 'notifications.page.title' | translate }}</h1>
        @if (svc.unreadCount() > 0) {
          <button type="button" class="mark-all-btn" (click)="markAllRead()">
            {{ 'notifications.panel.markAllRead' | translate }}
          </button>
        }
      </header>

      <ul class="list" role="list">
        @for (n of svc.notifications(); track n.id) {
          <li class="row-wrap" role="listitem">
            <button
              type="button"
              class="row"
              [class.unread]="!n.isRead"
              (click)="open(n)"
            >
              <span class="icon-chip" [attr.data-tone]="visual(n).tone" aria-hidden="true">
                {{ visual(n).icon }}
              </span>
              <span class="row-body">
                <span class="row-title" [class.font-semibold]="!n.isRead">
                  {{ n.title }}
                  @if (!n.isRead) {
                    <span class="unread-dot" aria-hidden="true"></span>
                  }
                </span>
                <span class="row-message">{{ n.message }}</span>
              </span>
              <span class="row-time">{{ time(n) }}</span>
            </button>
          </li>
        } @empty {
          @if (!svc.loading()) {
            <li class="empty" role="listitem">
              {{ 'notifications.panel.empty' | translate }}
            </li>
          }
        }
      </ul>

      @if (svc.hasMore()) {
        <div class="more">
          <button
            type="button"
            class="more-btn"
            [disabled]="svc.loading()"
            (click)="svc.loadNextPage()"
          >
            @if (svc.loading()) {
              {{ 'notifications.page.loading' | translate }}
            } @else {
              {{ 'notifications.page.loadMore' | translate }}
            }
          </button>
        </div>
      }
    </section>
  `,
  styles: [
    `
      .page {
        @apply mx-auto max-w-2xl px-4 py-6 sm:px-6;
      }

      .page-header {
        @apply mb-4 flex items-center justify-between;
      }

      .page-title {
        @apply m-0 text-xl font-semibold text-neutral-900;
      }

      .mark-all-btn {
        @apply rounded-md px-2.5 py-1.5 text-sm font-medium text-brand-600
          transition-colors hover:bg-brand-50 focus:outline-none focus:ring-2 focus:ring-brand-500;
      }

      .list {
        @apply m-0 list-none overflow-hidden rounded-xl border border-neutral-100 bg-white p-0 shadow-sm;
      }

      .row-wrap {
        @apply border-b border-neutral-50 last:border-b-0;
      }

      .row {
        @apply flex w-full items-start gap-3 px-4 py-3.5 text-left
          transition-colors duration-200 hover:bg-neutral-50 focus:bg-neutral-50 focus:outline-none;
      }

      .row.unread {
        @apply bg-brand-50/40;
      }

      .icon-chip {
        @apply flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full
          bg-neutral-100 text-[11px] font-semibold text-neutral-600;
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

      .row-body {
        @apply flex min-w-0 flex-1 flex-col gap-0.5;
      }

      .row-title {
        @apply flex items-center gap-1.5 text-sm text-neutral-900;
      }

      .unread-dot {
        @apply inline-block h-2 w-2 flex-shrink-0 rounded-full bg-brand-600;
      }

      .row-message {
        @apply text-xs text-neutral-500;
      }

      .row-time {
        @apply flex-shrink-0 whitespace-nowrap text-xs text-neutral-400;
      }

      .empty {
        @apply px-4 py-12 text-center text-sm text-neutral-400;
      }

      .more {
        @apply mt-4 flex justify-center;
      }

      .more-btn {
        @apply rounded-lg border border-neutral-200 bg-white px-4 py-2 text-sm
          font-medium text-neutral-700 transition-colors hover:bg-neutral-50
          focus:outline-none focus:ring-2 focus:ring-brand-500
          disabled:cursor-not-allowed disabled:opacity-60;
      }
    `,
  ],
})
export class NotificationListPageComponent implements OnInit {
  readonly svc = inject(NotificationService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.svc.loadFirstPage();
  }

  visual(n: INotification) {
    return notificationVisual(n.type);
  }

  time(n: INotification): string {
    return relativeTime(n.createdAt);
  }

  open(n: INotification): void {
    if (!n.isRead) {
      this.svc.markRead(n.id).subscribe();
    }
    const route = notificationRoute(n.resourceType, n.resourceId);
    if (route && route[0] !== '/notifications') {
      this.router.navigate(route);
    }
  }

  markAllRead(): void {
    this.svc.markAllRead().subscribe();
  }
}
