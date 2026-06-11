import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  input,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { ISession } from '../../../../core/auth/auth.models';

/**
 * US-AUTH-009 AC-4 / AC-5: Admin view of a user's active sessions.
 * Displays session cards with device/browser/OS, IP, timestamps, and
 * admin revoke actions. Also supports revoking all sessions at once.
 *
 * Receives the userId via route input binding.
 * Gated behind Tenant Admin/Owner role via the route guard.
 */
@Component({
  selector: 'app-admin-user-sessions',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '300ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
    trigger('cardEnter', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(4px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div class="sessions-container" [@fadeSlide]>
      <div class="sessions-card">
        <div class="sessions-header">
          <div class="header-top">
            <div>
              <h2 class="sessions-title">User Sessions</h2>
              <p class="sessions-subtitle">
                View and manage active sessions for this user.
              </p>
            </div>
            @if (sessions().length > 0 && !isLoading()) {
              <button
                class="btn-revoke-all"
                [disabled]="revokingAll()"
                (click)="revokeAllSessions()"
              >
                @if (revokingAll()) {
                  <span class="btn-spinner-sm"></span>
                  Revoking...
                } @else {
                  Revoke all
                }
              </button>
            }
          </div>
        </div>

        @if (isLoading()) {
          <div class="loading-section">
            <div class="spinner"></div>
            <p class="loading-text">Loading user sessions...</p>
          </div>
        } @else if (loadError()) {
          <div class="error-section">
            <p class="error-text">{{ loadError() }}</p>
            <button class="btn-secondary mt-3" (click)="loadSessions()">
              Retry
            </button>
          </div>
        } @else {
          <div class="sessions-body">
            @for (session of sessions(); track session.sessionId) {
              <div class="session-card" [@cardEnter]>
                <div class="session-icon">
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="1.5"
                    class="w-5 h-5"
                    aria-hidden="true"
                  >
                    @if (isDesktop(session)) {
                      <path
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        d="M9 17.25v1.007a3 3 0 0 1-.879 2.122L7.5 21h9l-.621-.621A3 3 0 0 1 15 18.257V17.25m6-12V15a2.25 2.25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 15V5.25A2.25 2.25 0 0 1 5.25 3h13.5A2.25 2.25 0 0 1 21 5.25Z"
                      />
                    } @else {
                      <path
                        stroke-linecap="round"
                        stroke-linejoin="round"
                        d="M10.5 1.5H8.25A2.25 2.25 0 0 0 6 3.75v16.5a2.25 2.25 0 0 0 2.25 2.25h7.5A2.25 2.25 0 0 0 18 20.25V3.75a2.25 2.25 0 0 0-2.25-2.25H13.5m-3 0V3h3V1.5m-3 0h3m-3 18.75h3"
                      />
                    }
                  </svg>
                </div>

                <div class="session-info">
                  <div class="session-device-row">
                    <span class="session-device">
                      {{ session.browser }} on {{ session.os }}
                    </span>
                    @if (session.isCurrent) {
                      <span class="badge-current">User's current session</span>
                    }
                  </div>
                  <div class="session-meta">
                    <span class="meta-item" [title]="session.ipAddress">
                      {{ session.ipAddress }}
                    </span>
                    <span class="meta-separator" aria-hidden="true"></span>
                    <span class="meta-item">
                      {{ session.device }}
                    </span>
                  </div>
                  <div class="session-timestamps">
                    <span>
                      Signed in {{ formatDate(session.issuedAt) }}
                    </span>
                    <span class="meta-separator" aria-hidden="true"></span>
                    <span>
                      Last active {{ formatDate(session.lastActiveAt) }}
                    </span>
                  </div>
                </div>

                <div class="session-actions">
                  <button
                    class="btn-revoke"
                    [disabled]="revokingId() === session.sessionId || revokingAll()"
                    [attr.aria-label]="'Revoke session on ' + session.browser + ' ' + session.os"
                    (click)="revokeSession(session)"
                  >
                    @if (revokingId() === session.sessionId) {
                      <span class="btn-spinner-sm"></span>
                    } @else {
                      Revoke
                    }
                  </button>
                </div>
              </div>
            } @empty {
              <div class="empty-state">
                <p>No active sessions found for this user.</p>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
  styles: [
    `
    :host {
      display: block;
    }

    .sessions-container {
      @apply mx-auto max-w-2xl px-4 py-6 sm:px-6;
    }

    .sessions-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion overflow-hidden;
    }

    .sessions-header {
      @apply px-6 py-5 border-b border-neutral-50;
    }

    .header-top {
      @apply flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3;
    }

    .sessions-title {
      @apply text-lg font-semibold text-neutral-900;
    }

    .sessions-subtitle {
      @apply mt-1 text-sm text-neutral-500;
    }

    .sessions-body {
      @apply divide-y divide-neutral-50;
    }

    .loading-section {
      @apply flex flex-col items-center justify-center py-12;
    }

    .spinner {
      @apply w-7 h-7 border-2 border-neutral-200 border-t-brand-600 rounded-full;
      animation: spin 0.7s linear infinite;
    }

    .loading-text {
      @apply mt-3 text-sm text-neutral-500;
    }

    .error-section {
      @apply px-6 py-8 text-center;
    }

    .error-text {
      @apply text-sm text-red-600;
    }

    .empty-state {
      @apply px-6 py-10 text-center text-sm text-neutral-500;
    }

    /* ─── Session Card ─────────────────────────── */

    .session-card {
      @apply flex items-start gap-3 px-6 py-4 transition-colors duration-150;
    }

    .session-card:hover {
      @apply bg-neutral-50/50;
    }

    .session-icon {
      @apply flex-shrink-0 w-10 h-10 rounded-lg bg-neutral-100 flex items-center
        justify-center text-neutral-500 mt-0.5;
    }

    .session-info {
      @apply flex-1 min-w-0;
    }

    .session-device-row {
      @apply flex flex-wrap items-center gap-2;
    }

    .session-device {
      @apply text-sm font-medium text-neutral-900;
    }

    .badge-current {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium
        bg-amber-50 text-amber-700;
    }

    .session-meta {
      @apply flex flex-wrap items-center gap-1 mt-0.5 text-xs text-neutral-500;
    }

    .meta-separator {
      @apply inline-block w-1 h-1 rounded-full bg-neutral-300;
    }

    .session-timestamps {
      @apply flex flex-wrap items-center gap-1 mt-0.5 text-xs text-neutral-400;
    }

    .session-actions {
      @apply flex-shrink-0 ml-2;
    }

    .btn-revoke {
      @apply inline-flex items-center justify-center rounded-lg px-3 py-1.5
        text-xs font-medium text-red-600 bg-white ring-1 ring-inset ring-red-200
        transition-all duration-200 hover:bg-red-50
        disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-white;
      min-width: 64px;
    }

    .btn-revoke-all {
      @apply inline-flex items-center gap-1.5 rounded-lg px-3.5 py-2
        text-xs font-medium text-red-600 bg-white ring-1 ring-inset ring-red-200
        transition-all duration-200 hover:bg-red-50 whitespace-nowrap
        disabled:opacity-40 disabled:cursor-not-allowed;
    }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }

    .btn-spinner-sm {
      @apply inline-block w-3.5 h-3.5 border-2 border-red-200 border-t-red-600 rounded-full;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }

    @media (max-width: 480px) {
      .session-card {
        @apply flex-wrap;
      }

      .session-actions {
        @apply w-full ml-0 mt-2 pl-[3.25rem];
      }
    }
  `,
  ],
})
export class AdminUserSessionsComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly toastr = inject(ToastrService);

  /** Route param: userId */
  readonly userId = input.required<string>();

  readonly sessions = signal<ISession[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly revokingId = signal<string | null>(null);
  readonly revokingAll = signal(false);

  ngOnInit(): void {
    this.loadSessions();
  }

  loadSessions(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.authService.getUserSessions(this.userId()).subscribe({
      next: (sessions) => {
        this.sessions.set(sessions);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load user sessions.'
        );
      },
    });
  }

  revokeSession(session: ISession): void {
    if (this.revokingId() || this.revokingAll()) {
      return;
    }

    this.revokingId.set(session.sessionId);

    this.authService
      .revokeUserSession(this.userId(), session.sessionId)
      .subscribe({
        next: () => {
          this.sessions.update((list) =>
            list.filter((s) => s.sessionId !== session.sessionId)
          );
          this.revokingId.set(null);
          this.toastr.success('Session revoked successfully.');
        },
        error: (err: HttpErrorResponse) => {
          this.revokingId.set(null);
          this.toastr.error(
            err.error?.message || 'Failed to revoke session.'
          );
        },
      });
  }

  revokeAllSessions(): void {
    if (this.revokingAll()) {
      return;
    }

    this.revokingAll.set(true);

    this.authService.revokeUserSession(this.userId()).subscribe({
      next: () => {
        this.sessions.set([]);
        this.revokingAll.set(false);
        this.toastr.success('All sessions revoked successfully.');
      },
      error: (err: HttpErrorResponse) => {
        this.revokingAll.set(false);
        this.toastr.error(
          err.error?.message || 'Failed to revoke sessions.'
        );
      },
    });
  }

  isDesktop(session: ISession): boolean {
    const os = (session.os || '').toLowerCase();
    return (
      os.includes('windows') ||
      os.includes('mac') ||
      os.includes('linux') ||
      os.includes('chrome os')
    );
  }

  formatDate(dateStr: string): string {
    if (!dateStr) {
      return 'Unknown';
    }
    try {
      const date = new Date(dateStr);
      const now = new Date();
      const diffMs = now.getTime() - date.getTime();
      const diffMins = Math.floor(diffMs / 60000);

      if (diffMins < 1) {
        return 'just now';
      }
      if (diffMins < 60) {
        return `${diffMins}m ago`;
      }
      const diffHours = Math.floor(diffMins / 60);
      if (diffHours < 24) {
        return `${diffHours}h ago`;
      }
      const diffDays = Math.floor(diffHours / 24);
      if (diffDays < 7) {
        return `${diffDays}d ago`;
      }
      return date.toLocaleDateString(undefined, {
        month: 'short',
        day: 'numeric',
        year: date.getFullYear() !== now.getFullYear() ? 'numeric' : undefined,
      });
    } catch {
      return dateStr;
    }
  }
}
