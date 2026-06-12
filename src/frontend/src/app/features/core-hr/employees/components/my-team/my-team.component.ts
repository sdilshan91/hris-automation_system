import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { finalize } from 'rxjs';
import { AuthService } from '@core/auth/auth.service';
import { EmployeeService } from '../../services/employee.service';
import {
  IDirectReport,
  getStatusBadgeClasses,
  getInitialsFromName,
} from '../../models/employee.models';

/**
 * US-CHR-011 AC-4 / FR-5: "My Team" direct reports view for managers.
 *
 * Displays all employees who report to the current user (or a specified manager)
 * as compact cards with avatar, name, title, department, and status badge.
 * Quick actions: View Profile (navigates), Approve Leave (placeholder/disabled).
 *
 * Lazy-loaded under /employees/my-team. Accessible to Manager, HR Officer, Tenant Admin.
 */
@Component({
  selector: 'app-my-team',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            My Team
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Direct reports who report to you.
          </p>
        </div>
      </div>

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4" aria-live="polite" aria-busy="true">
          @for (_ of skeletonCards; track $index) {
            <div class="card-notion animate-pulse">
              <div class="flex items-center gap-3 mb-3">
                <div class="w-10 h-10 rounded-full bg-neutral-200"></div>
                <div class="flex-1 space-y-2">
                  <div class="h-3.5 bg-neutral-200 rounded w-3/4"></div>
                  <div class="h-3 bg-neutral-100 rounded w-1/2"></div>
                </div>
              </div>
              <div class="space-y-2">
                <div class="h-3 bg-neutral-100 rounded w-full"></div>
                <div class="h-3 bg-neutral-100 rounded w-2/3"></div>
              </div>
            </div>
          }
        </div>
      }

      <!-- Error state -->
      @if (loadError()) {
        <div @fadeIn class="card-notion text-center py-16">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
            class="w-12 h-12 mx-auto text-red-300 mb-4" aria-hidden="true">
            <path fill-rule="evenodd" d="M9.401 3.003c1.155-2 4.043-2 5.197 0l7.355 12.748c1.154 2-.29 4.5-2.599 4.5H4.645c-2.309 0-3.752-2.5-2.598-4.5L9.4 3.003ZM12 8.25a.75.75 0 0 1 .75.75v3.75a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75Zm0 8.25a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Z" clip-rule="evenodd"/>
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">Failed to load team</h3>
          <p class="text-sm text-neutral-500 mb-4">{{ loadError() }}</p>
          <button type="button" class="btn-primary" (click)="loadDirectReports()">
            Retry
          </button>
        </div>
      }

      <!-- Empty state -->
      @if (!isLoading() && !loadError() && directReports().length === 0) {
        <div @fadeIn class="card-notion text-center py-16">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
            class="w-12 h-12 mx-auto text-neutral-300 mb-4" aria-hidden="true">
            <path d="M8.25 6.75a3.75 3.75 0 1 1 7.5 0 3.75 3.75 0 0 1-7.5 0ZM15.75 9.75a3 3 0 1 1 6 0 3 3 0 0 1-6 0ZM2.25 9.75a3 3 0 1 1 6 0 3 3 0 0 1-6 0ZM6.31 15.117A6.745 6.745 0 0 1 12 12a6.745 6.745 0 0 1 6.709 7.498.75.75 0 0 1-.372.568A12.696 12.696 0 0 1 12 21.75c-2.305 0-4.47-.612-6.337-1.684a.75.75 0 0 1-.372-.568 6.787 6.787 0 0 1 1.019-4.38Z"/>
            <path d="M5.082 14.254a8.287 8.287 0 0 0-1.308 5.135 9.687 9.687 0 0 1-1.764-.44l-.115-.04a.563.563 0 0 1-.373-.487l-.01-.121a3.75 3.75 0 0 1 3.57-4.047ZM20.226 19.389a8.287 8.287 0 0 0-1.308-5.135 3.75 3.75 0 0 1 3.57 4.047l-.01.121a.563.563 0 0 1-.373.486l-.115.04c-.567.2-1.156.349-1.764.441Z"/>
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">
            No direct reports
          </h3>
          <p class="text-sm text-neutral-500">
            You currently have no employees reporting to you.
          </p>
        </div>
      }

      <!-- Direct reports card grid -->
      @if (!isLoading() && !loadError() && directReports().length > 0) {
        <div class="mb-3">
          <span class="text-sm text-neutral-500">
            {{ directReports().length }} direct report{{ directReports().length === 1 ? '' : 's' }}
          </span>
        </div>
        <div @fadeIn class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          @for (report of directReports(); track report.employeeId) {
            <div
              class="report-card"
              role="article"
              [attr.aria-label]="report.firstName + ' ' + report.lastName"
            >
              <div class="flex items-center gap-3 mb-3">
                <div class="avatar-circle">
                  @if (report.profilePhotoUrl) {
                    <img
                      [src]="report.profilePhotoUrl"
                      [alt]="report.firstName + ' ' + report.lastName"
                      class="w-full h-full object-cover"
                    />
                  } @else {
                    <span>{{ getInitials(report.firstName, report.lastName) }}</span>
                  }
                </div>
                <div class="min-w-0 flex-1">
                  <p class="text-sm font-semibold text-neutral-900 truncate">
                    {{ report.firstName }} {{ report.lastName }}
                  </p>
                  <p class="text-xs text-neutral-500 truncate">
                    {{ report.jobTitleName || 'No title' }}
                  </p>
                </div>
                <span
                  class="status-badge"
                  [ngClass]="getStatusClasses(report.status)"
                >
                  {{ report.status }}
                </span>
              </div>

              <div class="text-xs text-neutral-500 space-y-1 mb-3">
                @if (report.departmentName) {
                  <p class="truncate">
                    <span class="inline-block bg-neutral-100 text-neutral-600 rounded px-1.5 py-0.5 text-[10px] font-medium">
                      {{ report.departmentName }}
                    </span>
                  </p>
                }
                <p class="truncate">{{ report.email }}</p>
              </div>

              <!-- Quick actions -->
              <div class="flex items-center gap-2 pt-3 border-t border-neutral-100">
                <button
                  type="button"
                  class="quick-action-btn"
                  (click)="viewProfile(report.employeeId)"
                  aria-label="View profile"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                    <path d="M10 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM3.465 14.493a1.23 1.23 0 0 0 .41 1.412A9.957 9.957 0 0 0 10 18c2.31 0 4.438-.784 6.131-2.1.43-.333.604-.903.408-1.41a7.002 7.002 0 0 0-13.074.003Z"/>
                  </svg>
                  View Profile
                </button>
                <button
                  type="button"
                  class="quick-action-btn opacity-50 cursor-not-allowed"
                  disabled
                  aria-label="Approve Leave (coming soon)"
                  title="Leave module not yet available"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                    <path fill-rule="evenodd" d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2Zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75Z" clip-rule="evenodd"/>
                  </svg>
                  Approve Leave
                </button>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .report-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-4
        transition-all duration-200 hover:shadow-md;
    }

    .avatar-circle {
      @apply w-10 h-10 rounded-full bg-brand-100 text-brand-700
        text-sm font-semibold flex items-center justify-center
        overflow-hidden flex-shrink-0;
    }

    .status-badge {
      @apply text-xs font-medium px-2 py-0.5 rounded-full capitalize;
    }

    .quick-action-btn {
      @apply inline-flex items-center gap-1 text-xs font-medium
        text-brand-600 hover:text-brand-700 transition-colors duration-150
        px-2 py-1.5 rounded-md hover:bg-brand-50;
    }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class MyTeamComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly employeeService = inject(EmployeeService);
  private readonly authService = inject(AuthService);

  readonly isLoading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly directReports = signal<IDirectReport[]>([]);
  readonly skeletonCards = Array.from({ length: 6 }, (_, i) => i);

  ngOnInit(): void {
    this.loadDirectReports();
  }

  loadDirectReports(): void {
    const user = this.authService.currentUser();
    // The current user's employeeId should be available from their user profile.
    // Assumption: the backend resolves the current user's employee record
    // and returns their direct reports via GET /employees/:managerId/direct-reports.
    // For HR/Admin, we use a known managerId from user context.
    // For now, we use 'me' as a special identifier that the backend resolves.
    const managerId = (user as any)?.employeeId || 'me';

    this.isLoading.set(true);
    this.loadError.set(null);

    this.employeeService
      .getDirectReports(managerId)
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: (reports) => {
          this.directReports.set(reports);
        },
        error: (err) => {
          if (err.status === 404) {
            this.directReports.set([]);
          } else {
            this.loadError.set('Failed to load direct reports. Please try again.');
          }
        },
      });
  }

  viewProfile(employeeId: string): void {
    this.router.navigate(['/employees', employeeId]);
  }

  getInitials(firstName: string, lastName: string): string {
    return getInitialsFromName(firstName, lastName);
  }

  getStatusClasses(status: string): string {
    return getStatusBadgeClasses(status);
  }
}
