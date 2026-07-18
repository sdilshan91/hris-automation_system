import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { UserManagementService } from '../../services/user-management.service';
import { IUserDetail } from '../../models/user-management.models';

/**
 * US-ADM-005 FR-6: User detail view.
 * Shows profile, roles, linked employee, recent audit, active sessions and
 * invitation history. Sections render gracefully when the backend omits them.
 */
@Component({
  selector: 'app-user-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-container max-w-4xl">
      <a
        routerLink="/admin/users"
        class="inline-flex items-center gap-1.5 text-sm text-neutral-500 hover:text-neutral-800 transition-colors mb-5"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
          <path fill-rule="evenodd" d="M12.78 5.22a.75.75 0 0 1 0 1.06L9.06 10l3.72 3.72a.75.75 0 1 1-1.06 1.06l-4.25-4.25a.75.75 0 0 1 0-1.06l4.25-4.25a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd" />
        </svg>
        {{ 'userManagement.detail.back' | translate }}
      </a>

      @if (loading()) {
        <div class="space-y-4">
          <div class="card-notion animate-pulse h-28"></div>
          <div class="card-notion animate-pulse h-40"></div>
        </div>
      } @else if (error()) {
        <div class="card-notion text-center py-12">
          <p class="text-sm text-neutral-600">{{ error() }}</p>
          <button class="btn-secondary mt-4" (click)="load()">
            {{ 'common.retry' | translate }}
          </button>
        </div>
      } @else if (user(); as u) {
        <!-- Profile -->
        <div class="card-notion mb-5">
          <div class="flex items-center gap-4">
            <div class="w-14 h-14 rounded-full bg-brand-100 text-brand-700 text-lg font-semibold flex items-center justify-center flex-shrink-0">
              {{ initials(u.displayName) }}
            </div>
            <div class="min-w-0">
              <h1 class="text-xl font-semibold text-neutral-900 tracking-tight truncate">{{ u.displayName }}</h1>
              <p class="text-sm text-neutral-500 truncate">{{ u.email }}</p>
              <span
                class="inline-block mt-1.5 px-2 py-0.5 rounded-full text-xs font-medium"
                [ngClass]="statusClass(u.status)"
              >
                {{ ('userManagement.status.' + (u.status | lowercase)) | translate }}
              </span>
            </div>
          </div>
        </div>

        <!-- Roles + linked employee -->
        <div class="grid grid-cols-1 md:grid-cols-2 gap-5 mb-5">
          <div class="card-notion">
            <h2 class="section-title">{{ 'userManagement.detail.roles' | translate }}</h2>
            @if (u.roles.length) {
              <div class="flex flex-wrap gap-1.5">
                @for (r of u.roles; track r.id) {
                  <span class="px-2 py-0.5 rounded-md bg-neutral-100 text-neutral-700 text-xs font-medium">{{ r.name }}</span>
                }
              </div>
            } @else {
              <p class="empty-line">{{ 'userManagement.detail.noRoles' | translate }}</p>
            }
          </div>

          <div class="card-notion">
            <h2 class="section-title">{{ 'userManagement.detail.linkedEmployee' | translate }}</h2>
            @if (u.linkedEmployee; as emp) {
              <p class="text-sm font-medium text-neutral-800">{{ emp.fullName }}</p>
              <p class="text-xs text-neutral-500">
                {{ emp.jobTitle || '—' }}<span *ngIf="emp.department"> · {{ emp.department }}</span>
              </p>
            } @else {
              <p class="empty-line">{{ 'userManagement.detail.noEmployee' | translate }}</p>
            }
          </div>
        </div>

        <!-- Active sessions -->
        <div class="card-notion mb-5">
          <h2 class="section-title">{{ 'userManagement.detail.sessions' | translate }}</h2>
          @if (u.activeSessions?.length) {
            <ul class="divide-y divide-neutral-50">
              @for (s of u.activeSessions; track s.id) {
                <li class="flex items-center justify-between py-2 text-sm">
                  <span class="text-neutral-700">{{ s.device }}</span>
                  <span class="text-xs text-neutral-400">{{ s.ipAddress }} · {{ s.lastActiveAt | date: 'short' }}</span>
                </li>
              }
            </ul>
          } @else {
            <p class="empty-line">{{ 'userManagement.detail.noSessions' | translate }}</p>
          }
        </div>

        <!-- Recent audit -->
        <div class="card-notion mb-5">
          <h2 class="section-title">{{ 'userManagement.detail.audit' | translate }}</h2>
          @if (u.recentAudit?.length) {
            <ul class="divide-y divide-neutral-50">
              @for (a of u.recentAudit; track a.id) {
                <li class="py-2 text-sm">
                  <span class="text-neutral-800 font-medium">{{ a.action }}</span>
                  <span class="text-neutral-400 text-xs"> · {{ a.actor }} · {{ a.at | date: 'short' }}</span>
                  @if (a.detail) {
                    <p class="text-xs text-neutral-500 mt-0.5">{{ a.detail }}</p>
                  }
                </li>
              }
            </ul>
          } @else {
            <p class="empty-line">{{ 'userManagement.detail.noAudit' | translate }}</p>
          }
        </div>

        <!-- Invitation history -->
        <div class="card-notion">
          <h2 class="section-title">{{ 'userManagement.detail.invitations' | translate }}</h2>
          @if (u.invitationHistory?.length) {
            <ul class="divide-y divide-neutral-50">
              @for (inv of u.invitationHistory; track inv.id) {
                <li class="flex items-center justify-between py-2 text-sm">
                  <span class="text-neutral-700">{{ inv.email }}</span>
                  <span class="text-xs text-neutral-400">{{ inv.status }} · {{ inv.invitedAt | date: 'short' }}</span>
                </li>
              }
            </ul>
          } @else {
            <p class="empty-line">{{ 'userManagement.detail.noInvitations' | translate }}</p>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .section-title { @apply text-sm font-semibold text-neutral-700 mb-3; }
    .empty-line { @apply text-sm text-neutral-400; }
  `],
})
export class UserDetailComponent implements OnInit {
  private readonly service = inject(UserManagementService);
  private readonly route = inject(ActivatedRoute);

  readonly user = signal<IUserDetail | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('userTenantId');
    if (!id) {
      this.error.set('User not found.');
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.error.set('');
    this.service.getUserDetail(id).subscribe({
      next: (detail) => {
        this.user.set(detail);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load user. Please try again.');
        this.loading.set(false);
      },
    });
  }

  initials(name: string): string {
    const parts = (name || '').trim().split(/\s+/);
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return (name || '?').substring(0, 2).toUpperCase();
  }

  statusClass(status: string): string {
    // BE may serialize the status PascalCase ("Active"); normalize so the badge
    // colour resolves regardless of casing (ISSUE-211).
    switch ((status || '').toLowerCase()) {
      case 'active':
        return 'bg-emerald-50 text-emerald-700';
      case 'invited':
        return 'bg-amber-50 text-amber-700';
      default:
        return 'bg-neutral-100 text-neutral-600';
    }
  }
}
