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
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  IPendingRegularization,
  RegularizationType,
  RegularizationAction,
  REGULARIZATION_STATUS_CLASSES,
  regularizationTypeLabel,
  formatRequestedTimes,
  IBulkApproveResult,
} from '../../models/attendance.models';

/** Which inline action panel is expanded for a given row (US-ATT-004 §8). */
type RowAction = 'none' | 'approve' | 'reject';

/**
 * US-ATT-004: Manager approval queue for attendance regularization requests.
 *
 * Extends the ATT-003 employee-side feature with the MANAGER side: a Notion-style
 * table (desktop) / card list (mobile) of the manager's team's PENDING regularizations
 * (AC-3), with columns Employee, Date, Type, Requested Times, Reason, Submitted On.
 *
 *  - Rows expand in place to show full detail (§8) without navigating away.
 *  - Inline Approve / Reject per row with a slide-down comment area (§8):
 *      Approve -> optional comment (BR-2).
 *      Reject  -> mandatory reason, min 10 chars, with a live char counter (BR-1, FR-3)
 *                 mirroring the ATT-003 drawer pattern.
 *  - Bulk approve (BR-7): row checkboxes + a "Bulk Approve" toolbar action; partial
 *    failures (AC-5 / BR-5) are reported per item and only succeeded rows leave.
 *  - On a successful action the row animates out of the pending list (§8).
 *  - A pending-count badge is surfaced in the header (the nav could consume
 *    `pendingCount`); wiring it into the global nav is deferred.
 *  - AC-5 (authorization denial) and BR-5 (payroll-locked period) errors display the
 *    server `{ message }` verbatim.
 *
 * Role-gated to Manager / HR Officer / Tenant Admin via the route child guard.
 */
@Component({
  selector: 'app-regularization-approvals',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    // The actioned row slides out of the queue on success (§8).
    trigger('rowOut', [
      transition(':leave', [
        animate(
          '300ms cubic-bezier(0.4, 0, 1, 1)',
          style({ opacity: 0, transform: 'translateX(24px)', height: 0 }),
        ),
      ]),
    ]),
    // Detail + comment/reason area slide-down reveal (§8).
    trigger('expand', [
      transition(':enter', [
        style({ opacity: 0, height: 0 }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0, height: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <div class="flex items-center gap-2.5">
            <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Regularization Approvals</h1>
            @if (pendingCount() > 0) {
              <span class="count-badge" data-test="pending-count"
                [attr.aria-label]="pendingCount() + ' pending requests'">
                {{ pendingCount() }}
              </span>
            }
          </div>
          <p class="text-sm text-neutral-500 mt-1">
            Review attendance corrections from your team — approve or reject with a comment.
          </p>
        </div>
        <button
          type="button"
          class="btn-secondary text-sm"
          (click)="refresh()"
          [disabled]="isLoading()"
          aria-label="Refresh queue"
        >
          Refresh
        </button>
      </div>

      <!-- Bulk toolbar (BR-7) -->
      @if (!isLoading() && requests().length > 0) {
        <div class="card-notion !p-3 mb-4 flex flex-wrap items-center justify-between gap-3" @fadeIn>
          <label class="flex items-center gap-2 text-sm text-neutral-600 cursor-pointer">
            <input type="checkbox" class="checkbox" data-test="select-all"
              [checked]="allSelected()" [indeterminate]="someSelected()"
              (change)="toggleSelectAll($event)" aria-label="Select all requests" />
            @if (selectedIds().size > 0) {
              {{ selectedIds().size }} selected
            } @else {
              Select all
            }
          </label>
          <button type="button" class="btn-approve !py-2 !px-4" data-test="bulk-approve"
            [disabled]="selectedIds().size === 0 || isActioning()"
            (click)="bulkApprove()">
            @if (isActioning()) {
              <span class="btn-spinner"></span> Approving…
            } @else {
              Bulk Approve ({{ selectedIds().size }})
            }
          </button>
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion" aria-live="polite" aria-busy="true" data-test="skeleton">
          <div class="space-y-3">
            @for (_ of [1,2,3,4]; track $index) {
              <div class="skeleton-line w-full h-12"></div>
            }
          </div>
        </div>
      } @else if (requests().length === 0) {
        <!-- Empty state -->
        <div @fadeIn class="card-notion text-center py-16" data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No pending approvals</h3>
          <p class="text-sm text-neutral-500">
            Your team has no attendance regularizations awaiting your review.
          </p>
        </div>
      } @else {
        <!-- Desktop table (Notion database view) -->
        <div class="hidden md:block card-notion !p-0 overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="Pending regularization requests">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th w-10"></th>
                <th class="th">Employee</th>
                <th class="th">Date</th>
                <th class="th">Type</th>
                <th class="th">Requested Times</th>
                <th class="th">Reason</th>
                <th class="th">Submitted On</th>
                <th class="th text-center">Status</th>
                <th class="th text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (req of requests(); track req.regularizationId) {
                <tr class="row" @rowOut data-test="queue-row">
                  <td class="td">
                    <input type="checkbox" class="checkbox"
                      [attr.data-test]="'select-' + req.regularizationId"
                      [checked]="selectedIds().has(req.regularizationId)"
                      (change)="toggleSelect(req.regularizationId)"
                      [attr.aria-label]="'Select request from ' + req.employeeName" />
                  </td>
                  <td class="td">
                    <button type="button" class="flex items-center gap-2.5 text-left"
                      (click)="toggleExpand(req.regularizationId)"
                      [attr.aria-expanded]="expandedId() === req.regularizationId"
                      [attr.data-test]="'expand-' + req.regularizationId">
                      <span class="avatar">{{ initials(req.employeeName) }}</span>
                      <span class="font-medium text-neutral-900">{{ req.employeeName }}</span>
                    </button>
                  </td>
                  <td class="td text-neutral-600 whitespace-nowrap">{{ req.date | date:'mediumDate' }}</td>
                  <td class="td text-neutral-600">{{ typeLabel(req.regularizationType) }}</td>
                  <td class="td text-neutral-600 whitespace-nowrap">{{ requestedTimes(req) }}</td>
                  <td class="td text-neutral-500 max-w-[14rem] truncate" [title]="req.reason">{{ req.reason }}</td>
                  <td class="td text-neutral-500 text-xs whitespace-nowrap">{{ submittedOn(req) | date:'short' }}</td>
                  <td class="td text-center">
                    <span class="status-badge" [class]="badgeClass()">Pending</span>
                  </td>
                  <td class="td text-right whitespace-nowrap">
                    <div class="inline-flex gap-1.5">
                      <button type="button" class="btn-approve !py-1.5 !px-3 text-xs"
                        [attr.data-test]="'approve-' + req.regularizationId"
                        [disabled]="isActioning()"
                        (click)="startAction(req.regularizationId, 'approve')">Approve</button>
                      <button type="button" class="btn-reject !py-1.5 !px-3 text-xs"
                        [attr.data-test]="'reject-' + req.regularizationId"
                        [disabled]="isActioning()"
                        (click)="startAction(req.regularizationId, 'reject')">Reject</button>
                    </div>
                  </td>
                </tr>
                <!-- Expanded detail / action area -->
                @if (expandedId() === req.regularizationId || activeRowId() === req.regularizationId) {
                  <tr class="bg-neutral-50/60" @expand
                    [attr.data-test]="'detail-' + req.regularizationId">
                    <td></td>
                    <td colspan="8" class="px-4 py-4">
                      <ng-container
                        *ngTemplateOutlet="detailBlock; context: { $implicit: req }">
                      </ng-container>
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (req of requests(); track req.regularizationId) {
            <div class="card-notion" @rowOut data-test="queue-card">
              <div class="flex items-start justify-between gap-3 mb-2">
                <label class="flex items-center gap-2.5 cursor-pointer">
                  <input type="checkbox" class="checkbox"
                    [checked]="selectedIds().has(req.regularizationId)"
                    (change)="toggleSelect(req.regularizationId)"
                    [attr.aria-label]="'Select request from ' + req.employeeName" />
                  <span class="avatar">{{ initials(req.employeeName) }}</span>
                  <span class="font-medium text-neutral-900">{{ req.employeeName }}</span>
                </label>
                <span class="status-badge" [class]="badgeClass()">Pending</span>
              </div>
              <dl class="grid grid-cols-2 gap-x-3 gap-y-1.5 text-xs mb-2">
                <div><dt class="dl">Date</dt><dd class="text-neutral-700">{{ req.date | date:'mediumDate' }}</dd></div>
                <div><dt class="dl">Type</dt><dd class="text-neutral-700">{{ typeLabel(req.regularizationType) }}</dd></div>
                <div><dt class="dl">Requested</dt><dd class="text-neutral-700">{{ requestedTimes(req) }}</dd></div>
                <div><dt class="dl">Submitted</dt><dd class="text-neutral-700">{{ submittedOn(req) | date:'short' }}</dd></div>
              </dl>
              <p class="text-xs text-neutral-500 mb-3 line-clamp-2">{{ req.reason }}</p>
              <div class="flex gap-2">
                <button type="button" class="btn-approve flex-1 !py-2 text-sm"
                  [disabled]="isActioning()"
                  (click)="startAction(req.regularizationId, 'approve')">Approve</button>
                <button type="button" class="btn-reject flex-1 !py-2 text-sm"
                  [disabled]="isActioning()"
                  (click)="startAction(req.regularizationId, 'reject')">Reject</button>
              </div>
              @if (activeRowId() === req.regularizationId) {
                <div class="mt-3" @expand>
                  <ng-container *ngTemplateOutlet="actionPanel; context: { $implicit: req }"></ng-container>
                </div>
              }
            </div>
          }
        </div>
      }
    </div>

    <!-- ─── Detail block (desktop expand): full detail + inline action panel ─── -->
    <ng-template #detailBlock let-req>
      <dl class="grid grid-cols-2 sm:grid-cols-4 gap-x-6 gap-y-3 text-sm mb-3">
        <div><dt class="dl">Employee</dt><dd class="text-neutral-800">{{ req.employeeName }}</dd></div>
        <div><dt class="dl">Date</dt><dd class="text-neutral-800">{{ req.date | date:'mediumDate' }}</dd></div>
        <div><dt class="dl">Type</dt><dd class="text-neutral-800">{{ typeLabel(req.regularizationType) }}</dd></div>
        <div><dt class="dl">Requested times</dt><dd class="text-neutral-800">{{ requestedTimes(req) }}</dd></div>
      </dl>
      <div class="mb-1">
        <dt class="dl">Reason</dt>
        <dd class="text-sm text-neutral-700 whitespace-pre-line">{{ req.reason }}</dd>
      </div>
      @if (activeRowId() === req.regularizationId) {
        <div class="mt-3">
          <ng-container *ngTemplateOutlet="actionPanel; context: { $implicit: req }"></ng-container>
        </div>
      }
    </ng-template>

    <!-- ─── Shared action panel (approve comment / reject reason) ─── -->
    <ng-template #actionPanel let-req>
      @if (rowAction() === 'approve') {
        <div data-test="approve-panel">
          <label class="dl mb-1 block" [attr.for]="'cmt-' + req.regularizationId">
            Comment <span class="text-neutral-300 normal-case">(optional)</span>
          </label>
          <textarea [id]="'cmt-' + req.regularizationId" rows="2" class="action-textarea"
            [ngModel]="comment()" (ngModelChange)="comment.set($event)"
            placeholder="Add an optional note for the employee…"
            data-test="approve-comment"></textarea>
        </div>
      }
      @if (rowAction() === 'reject') {
        <div data-test="reject-panel">
          <div class="flex items-center justify-between mb-1">
            <label class="dl block" [attr.for]="'rsn-' + req.regularizationId">
              Reason <span class="text-red-400 normal-case">(required)</span>
            </label>
            <span class="text-xs" [class.text-red-500]="reasonBelowMin()"
              [class.text-neutral-400]="!reasonBelowMin()">
              {{ reasonLength() }}/{{ minReason }}
            </span>
          </div>
          <textarea [id]="'rsn-' + req.regularizationId" rows="2" class="action-textarea"
            [class.textarea-error]="reasonBelowMin()"
            [ngModel]="rejectReason()" (ngModelChange)="rejectReason.set($event)"
            placeholder="Explain why this request is being rejected (min 10 characters)…"
            aria-required="true" data-test="reject-reason"></textarea>
          @if (reasonBelowMin()) {
            <p class="text-xs text-red-500 mt-1">Reason must be at least {{ minReason }} characters.</p>
          }
        </div>
      }

      <!-- AC-5 authorization / BR-5 payroll-lock error, shown verbatim -->
      @if (actionError(); as msg) {
        <div class="error-banner mt-3" role="alert" data-test="action-error">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
            class="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" aria-hidden="true">
            <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-7 4a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm-1-9a.75.75 0 0 0-.75.75v3.5a.75.75 0 0 0 1.5 0v-3.5A.75.75 0 0 0 10 5Z" clip-rule="evenodd"/>
          </svg>
          <p class="text-sm text-red-700 flex-1">{{ msg }}</p>
        </div>
      }

      <div class="flex justify-end gap-2 mt-3">
        <button type="button" class="btn-secondary !py-1.5 !px-3 text-xs"
          (click)="cancelAction()" [disabled]="isActioning()" data-test="action-cancel">
          Cancel
        </button>
        @if (rowAction() === 'approve') {
          <button type="button" class="btn-approve !py-1.5 !px-3 text-xs"
            (click)="confirmAction(req.regularizationId)" [disabled]="isActioning()"
            data-test="confirm-approve">
            {{ isActioning() ? 'Approving…' : 'Confirm approval' }}
          </button>
        } @else {
          <button type="button" class="btn-reject-solid !py-1.5 !px-3 text-xs"
            (click)="confirmAction(req.regularizationId)"
            [disabled]="isActioning() || reasonBelowMin()"
            data-test="confirm-reject">
            {{ isActioning() ? 'Rejecting…' : 'Confirm rejection' }}
          </button>
        }
      </div>
    </ng-template>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-6xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .count-badge {
      @apply inline-flex items-center justify-center min-w-[1.5rem] h-6 px-2 rounded-full
        bg-amber-100 text-amber-700 text-xs font-semibold ring-1 ring-inset ring-amber-200;
    }

    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .td { @apply py-3 px-4 align-middle; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60; }
    .dl { @apply text-[11px] font-medium text-neutral-400 uppercase tracking-wider; }

    .avatar {
      @apply w-8 h-8 rounded-full bg-brand-100 text-brand-700 text-xs font-semibold
        flex items-center justify-center overflow-hidden flex-shrink-0;
    }
    .status-badge {
      @apply inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset;
    }
    .checkbox {
      @apply w-4 h-4 rounded border-neutral-300 text-brand-600
        focus:ring-2 focus:ring-brand-500/30 cursor-pointer;
    }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-approve {
      @apply inline-flex items-center justify-center rounded-lg bg-green-600 px-4 py-2.5
        text-sm font-medium text-white transition-colors hover:bg-green-700
        disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .btn-reject {
      @apply inline-flex items-center justify-center rounded-lg border border-red-200 bg-white px-4 py-2.5
        text-sm font-medium text-red-600 transition-colors hover:bg-red-50
        disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .btn-reject-solid {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2.5
        text-sm font-medium text-white transition-colors hover:bg-red-700
        disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .action-textarea {
      @apply block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors resize-none
        focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .textarea-error { @apply border-red-300 focus:border-red-400 focus:ring-red-400; }
    .error-banner {
      @apply bg-red-50 border border-red-100 rounded-lg p-3 flex items-start gap-2.5;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class RegularizationApprovalsComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** Reject reason minimum length (BR-1, FR-3). */
  readonly minReason = 10;

  // ─── State ──────────────────────────────────────────────────
  readonly requests = signal<IPendingRegularization[]>([]);
  readonly isLoading = signal(true);
  readonly isActioning = signal(false);

  /** Selected ids for bulk approve (BR-7). */
  readonly selectedIds = signal<Set<string>>(new Set());

  /** Which row's detail is expanded (read-only view, §8). */
  readonly expandedId = signal<string | null>(null);
  /** Which row has its action panel open + the chosen action. */
  readonly activeRowId = signal<string | null>(null);
  readonly rowAction = signal<RowAction>('none');

  /** Optional approve comment (BR-2). */
  readonly comment = signal('');
  /** Mandatory reject reason (BR-1). */
  readonly rejectReason = signal('');
  /** Inline server error for the active action (AC-5 / BR-5), shown verbatim. */
  readonly actionError = signal<string | null>(null);

  // ─── Computed ───────────────────────────────────────────────

  /** Pending count badge — a nav could consume this (§8). */
  readonly pendingCount = computed(() => this.requests().length);

  readonly reasonLength = computed(() => this.rejectReason().trim().length);
  readonly reasonBelowMin = computed(() => this.reasonLength() < this.minReason);

  readonly allSelected = computed(() => {
    const list = this.requests();
    return list.length > 0 && this.selectedIds().size === list.length;
  });

  readonly someSelected = computed(() => {
    const n = this.selectedIds().size;
    return n > 0 && n < this.requests().length;
  });

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ───────────────────────────────────────────
  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getPendingApprovals()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (items) => {
          this.requests.set(items);
          this.selectedIds.set(new Set());
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load the pending approvals queue.');
        },
      });
  }

  refresh(): void {
    this.resetActionState();
    this.load();
  }

  // ─── Selection (BR-7) ───────────────────────────────────────
  toggleSelect(id: string): void {
    this.selectedIds.update((set) => {
      const next = new Set(set);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  toggleSelectAll(event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) {
      this.selectedIds.set(new Set(this.requests().map((r) => r.regularizationId)));
    } else {
      this.selectedIds.set(new Set());
    }
  }

  // ─── Expand / action panel ──────────────────────────────────
  toggleExpand(id: string): void {
    this.expandedId.update((cur) => (cur === id ? null : id));
  }

  /** Open the inline approve/reject panel for a row (§8). */
  startAction(id: string, action: 'approve' | 'reject'): void {
    this.activeRowId.set(id);
    this.rowAction.set(action);
    this.comment.set('');
    this.rejectReason.set('');
    this.actionError.set(null);
  }

  cancelAction(): void {
    if (this.isActioning()) {
      return;
    }
    this.resetActionState();
  }

  private resetActionState(): void {
    this.activeRowId.set(null);
    this.rowAction.set('none');
    this.comment.set('');
    this.rejectReason.set('');
    this.actionError.set(null);
  }

  // ─── Approve / Reject (AC-1, AC-2) ──────────────────────────
  confirmAction(id: string): void {
    if (this.isActioning()) {
      return;
    }
    const mode = this.rowAction();
    if (mode === 'none') {
      return;
    }
    const action: RegularizationAction = mode === 'approve' ? 'APPROVE' : 'REJECT';
    let comment: string | undefined;
    if (mode === 'reject') {
      if (this.reasonBelowMin()) {
        return; // submit is disabled, but guard the call too (BR-1).
      }
      comment = this.rejectReason().trim();
    } else {
      const c = this.comment().trim();
      comment = c.length > 0 ? c : undefined;
    }

    this.actionError.set(null);
    this.isActioning.set(true);
    this.attendanceService
      .processRegularization(id, action, comment)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => this.onActionSuccess(id, mode),
        error: (err: HttpErrorResponse) => this.onActionError(err),
      });
  }

  private onActionSuccess(id: string, mode: RowAction): void {
    this.isActioning.set(false);
    const req = this.requests().find((r) => r.regularizationId === id);
    this.removeFromQueue(id);
    this.resetActionState();
    const who = req?.employeeName ?? 'the employee';
    if (mode === 'approve') {
      this.toastr.success(`Regularization approved for ${who}.`);
    } else {
      this.toastr.success(`Regularization rejected for ${who}.`);
    }
  }

  /**
   * AC-5 (not authorized) / BR-5 (payroll locked): show the server `message`
   * verbatim inline, keeping the panel open. 409 -> already actioned, refresh.
   */
  private onActionError(err: HttpErrorResponse): void {
    this.isActioning.set(false);
    const parsed = AttendanceService.parseActionError(err);
    const message = parsed?.message ?? 'Failed to process this request. Please try again.';

    if (err.status === 409) {
      this.toastr.error(message);
      this.resetActionState();
      this.load(); // already handled elsewhere — re-sync the queue.
      return;
    }
    this.actionError.set(message);
  }

  // ─── Bulk approve (BR-7) ────────────────────────────────────
  bulkApprove(): void {
    const ids = Array.from(this.selectedIds());
    if (ids.length === 0 || this.isActioning()) {
      return;
    }
    this.isActioning.set(true);
    this.attendanceService
      .bulkApprove(ids)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => this.onBulkSuccess(res),
        error: (err: HttpErrorResponse) => this.onBulkError(err),
      });
  }

  /**
   * Bulk-approve completion: remove every succeeded item from the queue and surface
   * any per-item failures (AC-5 / BR-5) verbatim via toast so a partial failure does
   * not silently drop requests.
   */
  private onBulkSuccess(res: IBulkApproveResult): void {
    this.isActioning.set(false);
    const results = res?.items ?? [];
    const succeeded = results.filter((r) => r.succeeded).map((r) => r.regularizationId);
    const failed = results.filter((r) => !r.succeeded);

    for (const id of succeeded) {
      this.removeFromQueue(id);
    }
    this.selectedIds.set(new Set());

    if (succeeded.length > 0) {
      this.toastr.success(`${succeeded.length} request(s) approved.`);
    }
    for (const f of failed) {
      this.toastr.error(f.error ?? 'A request could not be approved.');
    }
  }

  private onBulkError(err: HttpErrorResponse): void {
    this.isActioning.set(false);
    const parsed = AttendanceService.parseActionError(err);
    this.toastr.error(parsed?.message ?? 'Bulk approval failed. Please try again.');
  }

  private removeFromQueue(id: string): void {
    this.requests.update((list) => list.filter((r) => r.regularizationId !== id));
    this.selectedIds.update((set) => {
      const next = new Set(set);
      next.delete(id);
      return next;
    });
  }

  // ─── View helpers ───────────────────────────────────────────
  typeLabel(type: RegularizationType): string {
    return regularizationTypeLabel(type);
  }

  requestedTimes(req: IPendingRegularization): string {
    return formatRequestedTimes(req);
  }

  submittedOn(req: IPendingRegularization): string {
    return req.submittedOn;
  }

  badgeClass(): string {
    return REGULARIZATION_STATUS_CLASSES.PENDING;
  }

  initials(name: string): string {
    const parts = (name ?? '').trim().split(/\s+/);
    const first = parts[0]?.[0] ?? '';
    const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
    return (first + last).toUpperCase() || '?';
  }
}
