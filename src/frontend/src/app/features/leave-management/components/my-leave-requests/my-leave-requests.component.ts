import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { LeaveRequestService } from '../../services/leave-request.service';
import {
  ILeaveRequest,
  ICancelEligibility,
  STATUS_BADGE_CLASSES,
  evaluateCancelEligibility,
} from '../../models/leave-request.models';

/**
 * US-LV-003 (§8 success state): "My Leaves" list.
 *
 * Minimal list of the employee's own leave requests with type, dates, days,
 * and a status badge. The apply form navigates here on successful submission.
 *
 * US-LV-010 adds the Cancel action (§8):
 *  - A "Cancel" button on each row/card, ENABLED only for eligible requests
 *    (Pending, or Approved with a future start date — `evaluateCancelEligibility`).
 *    Ineligible rows (started/past, rejected, already-cancelled) show the button
 *    DISABLED with an explanatory tooltip (no payroll-lock detection client-side —
 *    that is surfaced as a 400 on submit).
 *  - Clicking Cancel opens a confirmation dialog with a reason textarea: the reason
 *    is REQUIRED for approved requests (BR-5, AC-2) and OPTIONAL for pending (AC-1).
 *  - On success the row updates to a gray "Cancelled" badge with strikethrough styling
 *    (§8) + a success toast; the list is refreshed so the dashboard balance reflects
 *    the change on its next load.
 *  - 400 ineligible (already started AC-3 / payroll-locked AC-4) -> toast the API
 *    message verbatim. 409 concurrency conflict -> toast + refresh (mirrors US-LV-005).
 */
@Component({
  selector: 'app-my-leave-requests',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [style({ opacity: 0 }), animate('200ms ease-out', style({ opacity: 1 }))]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
    trigger('dialogPop', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(12px) scale(0.98)' }),
        animate('220ms cubic-bezier(0.4, 0, 0.2, 1)', style({ opacity: 1, transform: 'translateY(0) scale(1)' })),
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
                <th class="text-right py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (req of requests(); track req.leaveRequestId) {
                <tr class="border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors"
                  [class.opacity-60]="req.status === 'Cancelled'">
                  <td class="py-3 px-3">
                    <span class="type-badge"
                      [style.background-color]="req.leaveTypeColor"
                      [style.color]="'#ffffff'"
                      [class.line-through]="req.status === 'Cancelled'">
                      {{ req.leaveTypeName }}
                    </span>
                  </td>
                  <td class="py-3 px-3 text-neutral-600" [class.line-through]="req.status === 'Cancelled'">
                    {{ req.startDate | date:'mediumDate' }} – {{ req.endDate | date:'mediumDate' }}
                    @if (req.isHalfDay) { <span class="text-xs text-neutral-400">({{ req.halfDaySession }})</span> }
                  </td>
                  <td class="py-3 px-3 text-center font-medium text-neutral-900"
                    [class.line-through]="req.status === 'Cancelled'">{{ req.totalDays }}</td>
                  <td class="py-3 px-3 text-neutral-500 text-xs">{{ req.requestedAt | date:'short' }}</td>
                  <td class="py-3 px-3 text-center">
                    <span class="status-badge" [class]="badgeClass(req)">{{ req.status }}</span>
                  </td>
                  <td class="py-3 px-3 text-right">
                    @if (cancelEligibility(req).eligible) {
                      <button type="button" class="btn-cancel" (click)="openCancel(req)"
                        [attr.aria-label]="'Cancel ' + req.leaveTypeName + ' request'">
                        Cancel
                      </button>
                    } @else if (req.status !== 'Cancelled' && req.status !== 'Rejected') {
                      <button type="button" class="btn-cancel" disabled
                        [title]="cancelEligibility(req).reason"
                        [attr.aria-label]="cancelEligibility(req).reason">
                        Cancel
                      </button>
                    } @else {
                      <span class="text-xs text-neutral-300">—</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (req of requests(); track req.leaveRequestId) {
            <div class="card-notion" [class.opacity-60]="req.status === 'Cancelled'">
              <div class="flex items-center justify-between mb-2">
                <span class="type-badge"
                  [style.background-color]="req.leaveTypeColor" [style.color]="'#ffffff'"
                  [class.line-through]="req.status === 'Cancelled'">
                  {{ req.leaveTypeName }}
                </span>
                <span class="status-badge" [class]="badgeClass(req)">{{ req.status }}</span>
              </div>
              <p class="text-sm text-neutral-700" [class.line-through]="req.status === 'Cancelled'">
                {{ req.startDate | date:'mediumDate' }} – {{ req.endDate | date:'mediumDate' }}
              </p>
              <p class="text-xs text-neutral-500 mt-1">
                {{ req.totalDays }} day(s) · requested {{ req.requestedAt | date:'short' }}
              </p>
              @if (cancelEligibility(req).eligible) {
                <button type="button" class="btn-cancel mt-3 w-full" (click)="openCancel(req)"
                  [attr.aria-label]="'Cancel ' + req.leaveTypeName + ' request'">
                  Cancel request
                </button>
              } @else if (req.status !== 'Cancelled' && req.status !== 'Rejected') {
                <p class="text-xs text-neutral-400 mt-3">{{ cancelEligibility(req).reason }}</p>
              }
            </div>
          }
        </div>
      }
    </div>

    <!-- ─── Cancel confirmation dialog (§8) ─────────────────── -->
    @if (cancelTarget(); as target) {
      <div class="dialog-backdrop" @backdrop (click)="closeCancel()" aria-hidden="true"></div>
      <div class="dialog-wrap" role="dialog" aria-modal="true" aria-labelledby="cancel-dialog-title">
        <div class="dialog-panel" @dialogPop>
          <h2 id="cancel-dialog-title" class="text-lg font-semibold text-neutral-900 mb-1">
            Cancel leave request?
          </h2>
          <p class="text-sm text-neutral-500 mb-4">
            {{ target.leaveTypeName }},
            {{ target.startDate | date:'mediumDate' }} – {{ target.endDate | date:'mediumDate' }}
            ({{ target.totalDays }} day(s)).
          </p>

          <label for="cancel-reason" class="block text-sm font-medium text-neutral-700 mb-1">
            Cancellation reason
            @if (reasonRequired()) {
              <span class="text-red-500">*</span>
            } @else {
              <span class="text-neutral-400 font-normal">(optional)</span>
            }
          </label>
          <textarea
            id="cancel-reason"
            rows="3"
            class="reason-input"
            [class.error-ring]="reasonRequired() && !cancelReason().trim()"
            [ngModel]="cancelReason()"
            (ngModelChange)="cancelReason.set($event)"
            [attr.aria-required]="reasonRequired()"
            placeholder="Why are you cancelling this leave?"></textarea>
          @if (reasonRequired() && !cancelReason().trim()) {
            <p class="text-xs text-red-500 mt-1">A reason is required to cancel an approved leave.</p>
          }

          <div class="flex flex-col-reverse sm:flex-row sm:justify-end gap-2 mt-6">
            <button type="button" class="btn-secondary" (click)="closeCancel()" [disabled]="isCancelling()">
              Keep request
            </button>
            <button type="button" class="btn-danger"
              (click)="confirmCancel()"
              [disabled]="isCancelling() || (reasonRequired() && !cancelReason().trim())">
              {{ isCancelling() ? 'Cancelling…' : 'Cancel leave' }}
            </button>
          </div>
        </div>
      </div>
    }
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
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white border border-neutral-200
        px-5 py-2.5 text-sm font-medium text-neutral-700 shadow-sm transition-all duration-200
        hover:bg-neutral-50 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-danger {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-red-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-cancel {
      @apply inline-flex items-center justify-center rounded-md border border-neutral-200 px-3 py-1.5
        text-xs font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        hover:border-red-200 hover:text-red-600
        disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent
        disabled:hover:border-neutral-200 disabled:hover:text-neutral-700;
    }

    .dialog-backdrop { @apply fixed inset-0 z-40 bg-neutral-900/30 backdrop-blur-[1px]; }
    .dialog-wrap { @apply fixed inset-0 z-50 flex items-end sm:items-center justify-center p-0 sm:p-4; }
    .dialog-panel {
      @apply w-full sm:max-w-md bg-white rounded-t-2xl sm:rounded-xl shadow-lg p-6 border border-neutral-100;
    }
    .reason-input {
      @apply w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-800
        shadow-sm transition-all duration-200 focus:outline-none focus:ring-2 focus:ring-brand-500/40
        focus:border-brand-400 resize-none;
    }
    .error-ring { @apply ring-2 ring-red-300 border-red-300; }
  `],
})
export class MyLeaveRequestsComponent implements OnInit, OnDestroy {
  private readonly leaveRequestService = inject(LeaveRequestService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly requests = signal<ILeaveRequest[]>([]);
  readonly isLoading = signal(true);

  // --- Cancel dialog state (US-LV-010) -----------------------
  readonly cancelTarget = signal<ILeaveRequest | null>(null);
  readonly cancelReason = signal('');
  readonly isCancelling = signal(false);

  /** Reason is mandatory only when cancelling an APPROVED request (BR-5, AC-2). */
  readonly reasonRequired = computed(() => this.cancelTarget()?.status === 'Approved');

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

  /** Whether a request is cancellable + the tooltip reason when it is not (§8). */
  cancelEligibility(req: ILeaveRequest): ICancelEligibility {
    return evaluateCancelEligibility(req);
  }

  // --- Cancel flow (US-LV-010) -------------------------------

  openCancel(req: ILeaveRequest): void {
    this.cancelTarget.set(req);
    this.cancelReason.set('');
  }

  closeCancel(): void {
    if (this.isCancelling()) {
      return;
    }
    this.cancelTarget.set(null);
    this.cancelReason.set('');
  }

  confirmCancel(): void {
    const target = this.cancelTarget();
    if (!target || this.isCancelling()) {
      return;
    }
    // Guard: reason mandatory for approved requests (BR-5, AC-2).
    if (this.reasonRequired() && !this.cancelReason().trim()) {
      return;
    }

    this.isCancelling.set(true);
    this.leaveRequestService
      .cancelLeaveRequest(target.leaveRequestId, { reason: this.cancelReason().trim() })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => this.onCancelSuccess(),
        error: (err: HttpErrorResponse) => this.onCancelError(err),
      });
  }

  private onCancelSuccess(): void {
    this.isCancelling.set(false);
    this.cancelTarget.set(null);
    this.cancelReason.set('');
    this.toastr.success('Leave request cancelled successfully.');
    // Refresh so the row shows the Cancelled badge and the dashboard balance
    // reflects the restoration on its next load.
    this.load();
  }

  private onCancelError(err: HttpErrorResponse): void {
    this.isCancelling.set(false);
    const parsed = LeaveRequestService.parseCancelError(err);
    const message = parsed?.message;

    if (err.status === 409) {
      // Concurrency conflict — a manager actioned the request first (mirror US-LV-005).
      this.toastr.error(message ?? 'This request has already been actioned. Refreshing…');
      this.cancelTarget.set(null);
      this.cancelReason.set('');
      this.load();
      return;
    }

    // 400 ineligible (already started AC-3 / payroll-locked AC-4) or any other error:
    // surface the API message verbatim, keep the dialog open so the user sees context.
    this.toastr.error(message ?? 'Unable to cancel this leave request.');
  }
}
