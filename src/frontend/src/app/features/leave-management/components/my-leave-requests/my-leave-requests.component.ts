import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { LeaveRequestService } from '../../services/leave-request.service';
import {
  ILeaveRequest,
  STATUS_BADGE_CLASSES,
} from '../../models/leave-request.models';

/**
 * US-LV-003 (§8 success state): "My Leaves" list.
 *
 * Minimal list of the employee's own leave requests with type, dates, days,
 * and a status badge. The apply form navigates here on successful submission.
 */
@Component({
  selector: 'app-my-leave-requests',
  standalone: true,
  imports: [CommonModule, RouterLink],
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
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">My Leave Requests</h1>
          <p class="text-sm text-neutral-500 mt-1">Track the status of your time-off requests.</p>
        </div>
        <a routerLink="/leave/apply" class="btn-primary text-sm">+ Apply for Leave</a>
      </div>

      @if (isLoading()) {
        <div class="card-notion" aria-live="polite" aria-busy="true">
          <div class="space-y-3">
            @for (_ of [1,2,3]; track $index) {
              <div class="skeleton-line w-full h-12"></div>
            }
          </div>
        </div>
      } @else if (requests().length === 0) {
        <div @fadeIn class="card-notion text-center py-16">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No leave requests yet</h3>
          <p class="text-sm text-neutral-500 mb-4">When you apply for leave, your requests appear here.</p>
          <a routerLink="/leave/apply" class="btn-primary">+ Apply for Leave</a>
        </div>
      } @else {
        <!-- Desktop table -->
        <div class="hidden md:block card-notion overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="My leave requests">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Type</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Dates</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Days</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Requested</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (req of requests(); track req.leaveRequestId) {
                <tr class="border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors">
                  <td class="py-3 px-3">
                    <span class="type-badge"
                      [style.background-color]="req.leaveTypeColor"
                      [style.color]="'#ffffff'">
                      {{ req.leaveTypeName }}
                    </span>
                  </td>
                  <td class="py-3 px-3 text-neutral-600">
                    {{ req.startDate | date:'mediumDate' }} – {{ req.endDate | date:'mediumDate' }}
                    @if (req.isHalfDay) { <span class="text-xs text-neutral-400">({{ req.halfDaySession }})</span> }
                  </td>
                  <td class="py-3 px-3 text-center font-medium text-neutral-900">{{ req.totalDays }}</td>
                  <td class="py-3 px-3 text-neutral-500 text-xs">{{ req.requestedAt | date:'short' }}</td>
                  <td class="py-3 px-3 text-center">
                    <span class="status-badge" [class]="badgeClass(req)">{{ req.status }}</span>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (req of requests(); track req.leaveRequestId) {
            <div class="card-notion">
              <div class="flex items-center justify-between mb-2">
                <span class="type-badge"
                  [style.background-color]="req.leaveTypeColor" [style.color]="'#ffffff'">
                  {{ req.leaveTypeName }}
                </span>
                <span class="status-badge" [class]="badgeClass(req)">{{ req.status }}</span>
              </div>
              <p class="text-sm text-neutral-700">
                {{ req.startDate | date:'mediumDate' }} – {{ req.endDate | date:'mediumDate' }}
              </p>
              <p class="text-xs text-neutral-500 mt-1">
                {{ req.totalDays }} day(s) · requested {{ req.requestedAt | date:'short' }}
              </p>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-4xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .type-badge {
      @apply inline-flex items-center px-2.5 py-1 rounded-md text-xs font-semibold;
    }
    .status-badge {
      @apply inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset;
    }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-brand-700;
    }
  `],
})
export class MyLeaveRequestsComponent implements OnInit, OnDestroy {
  private readonly leaveRequestService = inject(LeaveRequestService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly requests = signal<ILeaveRequest[]>([]);
  readonly isLoading = signal(true);

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.isLoading.set(true);
    this.leaveRequestService
      .getMyLeaveRequests()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (requests) => {
          this.requests.set(requests);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load your leave requests.');
        },
      });
  }

  badgeClass(req: ILeaveRequest): string {
    return STATUS_BADGE_CLASSES[req.status] ?? STATUS_BADGE_CLASSES.Pending;
  }
}
