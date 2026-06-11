import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { ITenantUser } from '../../../../core/auth/auth.models';

/**
 * US-AUTH-010 AC-5 / FR-6: Admin view for tenant user lockout management.
 * Displays user cards; locked accounts show a red "Locked" badge + an "Unlock"
 * button. On unlock success the badge clears and the user can immediately log in.
 *
 * Gated behind Tenant Admin/Owner role via the route guard.
 * Reuses the card/list pattern from AdminUserSessionsComponent (US-AUTH-009).
 */
@Component({
  selector: 'app-admin-user-lockout',
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
    <div class="users-container" [@fadeSlide]>
      <div class="users-card">
        <div class="users-header">
          <div class="header-top">
            <div>
              <h2 class="users-title">User Account Security</h2>
              <p class="users-subtitle">
                View user lockout status and unlock accounts that have been locked
                due to too many failed login attempts.
              </p>
            </div>
            @if (lockedCount() > 0 && !isLoading()) {
              <span class="locked-count-badge">
                {{ lockedCount() }} locked
              </span>
            }
          </div>
        </div>

        @if (isLoading()) {
          <div class="loading-section">
            <div class="spinner"></div>
            <p class="loading-text">Loading users...</p>
          </div>
        } @else if (loadError()) {
          <div class="error-section">
            <p class="error-text">{{ loadError() }}</p>
            <button class="btn-secondary mt-3" (click)="loadUsers()">
              Retry
            </button>
          </div>
        } @else {
          <div class="users-body">
            @for (user of users(); track user.userId) {
              <div class="user-card" [@cardEnter]>
                <div class="user-avatar">
                  @if (user.avatarUrl) {
                    <img
                      [src]="user.avatarUrl"
                      [alt]="user.displayName"
                      class="avatar-img"
                    />
                  } @else {
                    <span class="avatar-initials">{{ getInitials(user.displayName) }}</span>
                  }
                </div>

                <div class="user-info">
                  <div class="user-name-row">
                    <span class="user-name">{{ user.displayName }}</span>
                    @if (isLocked(user)) {
                      <span class="badge-locked">Locked</span>
                    }
                  </div>
                  <div class="user-meta">
                    <span class="meta-item">{{ user.email }}</span>
                  </div>
                  <div class="user-detail-row">
                    @if (user.roles.length > 0) {
                      <span class="user-roles">{{ user.roles.join(', ') }}</span>
                    }
                    @if (isLocked(user)) {
                      <span class="meta-separator" aria-hidden="true"></span>
                      <span class="lockout-info">
                        Failed attempts: {{ user.failedLoginCount }}
                      </span>
                    }
                  </div>
                </div>

                <div class="user-actions">
                  @if (isLocked(user)) {
                    <button
                      class="btn-unlock"
                      [disabled]="unlockingId() === user.userId"
                      [attr.aria-label]="'Unlock account for ' + user.displayName"
                      (click)="unlockUser(user)"
                    >
                      @if (unlockingId() === user.userId) {
                        <span class="btn-spinner-sm"></span>
                        Unlocking...
                      } @else {
                        <svg
                          xmlns="http://www.w3.org/2000/svg"
                          viewBox="0 0 20 20"
                          fill="currentColor"
                          class="w-4 h-4"
                          aria-hidden="true"
                        >
                          <path
                            d="M14.5 1A4.5 4.5 0 0 0 10 5.5V9H3a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-1.5V5.5a3 3 0 1 1 6 0v2.75a.75.75 0 0 0 1.5 0V5.5A4.5 4.5 0 0 0 14.5 1Z"
                          />
                        </svg>
                        Unlock
                      }
                    </button>
                  }
                </div>
              </div>
            } @empty {
              <div class="empty-state">
                <p>No users found in this workspace.</p>
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

    .users-container {
      @apply mx-auto max-w-2xl px-4 py-6 sm:px-6;
    }

    .users-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion overflow-hidden;
    }

    .users-header {
      @apply px-6 py-5 border-b border-neutral-50;
    }

    .header-top {
      @apply flex flex-col sm:flex-row sm:items-start sm:justify-between gap-3;
    }

    .users-title {
      @apply text-lg font-semibold text-neutral-900;
    }

    .users-subtitle {
      @apply mt-1 text-sm text-neutral-500;
    }

    .locked-count-badge {
      @apply inline-flex items-center px-2.5 py-1 rounded-full text-xs font-medium
        bg-red-50 text-red-700 border border-red-100 whitespace-nowrap;
    }

    .users-body {
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

    /* ─── User Card ─────────────────────────────── */

    .user-card {
      @apply flex items-start gap-3 px-6 py-4 transition-colors duration-150;
    }

    .user-card:hover {
      @apply bg-neutral-50/50;
    }

    .user-avatar {
      @apply flex-shrink-0 w-10 h-10 rounded-full bg-neutral-100 flex items-center
        justify-center text-neutral-500 overflow-hidden mt-0.5;
    }

    .avatar-img {
      @apply w-full h-full object-cover;
    }

    .avatar-initials {
      @apply text-sm font-semibold text-neutral-600;
    }

    .user-info {
      @apply flex-1 min-w-0;
    }

    .user-name-row {
      @apply flex flex-wrap items-center gap-2;
    }

    .user-name {
      @apply text-sm font-medium text-neutral-900;
    }

    .badge-locked {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium
        bg-red-50 text-red-700 border border-red-100;
    }

    .user-meta {
      @apply flex flex-wrap items-center gap-1 mt-0.5 text-xs text-neutral-500;
    }

    .meta-item {
      @apply truncate;
    }

    .meta-separator {
      @apply inline-block w-1 h-1 rounded-full bg-neutral-300;
    }

    .user-detail-row {
      @apply flex flex-wrap items-center gap-1 mt-0.5 text-xs text-neutral-400;
    }

    .user-roles {
      @apply text-xs text-neutral-400;
    }

    .lockout-info {
      @apply text-xs text-red-500;
    }

    .user-actions {
      @apply flex-shrink-0 ml-2;
    }

    .btn-unlock {
      @apply inline-flex items-center gap-1.5 justify-center rounded-lg px-3 py-1.5
        text-xs font-medium text-brand-600 bg-white ring-1 ring-inset ring-brand-200
        transition-all duration-200 hover:bg-brand-50
        disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-white;
      min-width: 80px;
    }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }

    .btn-spinner-sm {
      @apply inline-block w-3.5 h-3.5 border-2 border-brand-200 border-t-brand-600 rounded-full;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }

    @media (max-width: 480px) {
      .user-card {
        @apply flex-wrap;
      }

      .user-actions {
        @apply w-full ml-0 mt-2 pl-[3.25rem];
      }
    }
  `,
  ],
})
export class AdminUserLockoutComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly toastr = inject(ToastrService);

  readonly users = signal<ITenantUser[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly unlockingId = signal<string | null>(null);

  /** Derived count of currently locked users for the header badge */
  readonly lockedCount = computed(() =>
    this.users().filter((u) => this.isLocked(u)).length
  );

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.authService.getTenantUsers().subscribe({
      next: (users) => {
        this.users.set(users);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load users.'
        );
      },
    });
  }

  unlockUser(user: ITenantUser): void {
    if (this.unlockingId()) {
      return;
    }

    this.unlockingId.set(user.userId);

    this.authService.unlockUser(user.userId).subscribe({
      next: () => {
        // Clear lockout state in the local list so the badge disappears
        this.users.update((list) =>
          list.map((u) =>
            u.userId === user.userId
              ? { ...u, lockedUntil: null, failedLoginCount: 0 }
              : u
          )
        );
        this.unlockingId.set(null);
        this.toastr.success(
          `${user.displayName}'s account has been unlocked.`
        );
      },
      error: (err: HttpErrorResponse) => {
        this.unlockingId.set(null);
        this.toastr.error(
          err.error?.message || 'Failed to unlock account.'
        );
      },
    });
  }

  /**
   * Determines if a user is currently locked.
   * The account is locked when lockedUntil is set AND is in the future.
   */
  isLocked(user: ITenantUser): boolean {
    if (!user.lockedUntil) {
      return false;
    }
    return new Date(user.lockedUntil).getTime() > Date.now();
  }

  getInitials(name: string): string {
    if (!name) {
      return '?';
    }
    const parts = name.trim().split(/\s+/);
    if (parts.length === 1) {
      return parts[0].charAt(0).toUpperCase();
    }
    return (
      parts[0].charAt(0).toUpperCase() +
      parts[parts.length - 1].charAt(0).toUpperCase()
    );
  }
}
