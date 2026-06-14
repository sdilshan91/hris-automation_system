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
  IOvertimeQueueItem,
  OVERTIME_STATUS_CLASSES,
  overtimeTypeLabel,
  formatMultiplier,
  formatWorkMinutes,
} from '../../models/attendance.models';

/** Which inline action panel is expanded for a given row (§8). */
type RowAction = 'none' | 'approve' | 'reject';

/**
 * US-ATT-006: Manager approval queue for overtime records.
 *
 * Mirrors the ATT-004 regularization-approvals interaction model (Notion-style table /
 * mobile cards, inline expand, per-row Approve / Reject with a slide-down panel) so the
 * two queues feel like one unified approval hub (§8). Columns: Employee, Date, Overtime,
 * Reason, Submitted On, Status.
 *
 *  - Approve (AC-4, FR-6): optional comment + an optional "adjust" field to award fewer
 *    minutes than requested (`approvedMinutes`). Empty adjust -> full requested amount.
 *  - Reject: mandatory reason, min 10 chars, with a live char counter (mirrors ATT-004).
 *  - Self-approval (BR-8) / not-team-member (403) and already-actioned (409) errors show
 *    the server `{ message }` verbatim.
 *  - On a successful action the row leaves the pending list.
 *
 * Role-gated to Manager / HR Officer / Tenant Admin via the route child guard.
 */
@Component({
  selector: 'app-overtime-approvals',
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
    trigger('rowOut', [
      transition(':leave', [
        animate(
          '300ms cubic-bezier(0.4, 0, 1, 1)',
          style({ opacity: 0, transform: 'translateX(24px)', height: 0 }),
        ),
      ]),
    ]),
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
            <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Overtime Approvals</h1>
            @if (pendingCount() > 0) {
              <span class="count-badge" data-test="pending-count"
                [attr.aria-label]="pendingCount() + ' pending overtime requests'">
                {{ pendingCount() }}
              </span>
            }
          </div>
          <p class="text-sm text-neutral-500 mt-1">
            Review your team's overtime — approve (adjust the awarded hours) or reject.
          </p>
        </div>
        <button type="button" class="btn-secondary text-sm" (click)="refresh()"
          [disabled]="isLoading()" aria-label="Refresh queue">Refresh</button>
      </div>

      @if (isLoading()) {
        <div class="card-notion" aria-busy="true" data-test="skeleton">
          <div class="space-y-3">
            @for (_ of [1,2,3,4]; track $index) {
              <div class="skeleton-line w-full h-12"></div>
            }
          </div>
        </div>
      } @else if (requests().length === 0) {
        <div @fadeIn class="card-notion text-center py-16" data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No pending overtime</h3>
          <p class="text-sm text-neutral-500">Your team has no overtime awaiting your review.</p>
        </div>
      } @else {
        <!-- Desktop table -->
        <div class="hidden md:block card-notion !p-0 overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="Pending overtime requests">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th">Employee</th>
                <th class="th">Date</th>
                <th class="th">Overtime</th>
                <th class="th">Multiplier</th>
                <th class="th">Reason</th>
                <th class="th">Submitted On</th>
                <th class="th text-center">Status</th>
                <th class="th text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (req of requests(); track req.id) {
                <tr class="row" @rowOut data-test="queue-row">
                  <td class="td">
                    <button type="button" class="flex items-center gap-2.5 text-left"
                      (click)="toggleExpand(req.id)"
                      [attr.aria-expanded]="expandedId() === req.id"
                      [attr.data-test]="'expand-' + req.id">
                      <span class="avatar">{{ initials(req.employeeName) }}</span>
                      <span class="font-medium text-neutral-900">{{ req.employeeName }}</span>
                    </button>
                  </td>
                  <td class="td text-neutral-600 whitespace-nowrap">{{ req.date | date:'mediumDate' }}</td>
                  <td class="td text-neutral-600 whitespace-nowrap">{{ minutes(req.overtimeMinutes) }}</td>
                  <td class="td text-neutral-600">{{ multiplier(req.multiplier) }}</td>
                  <td class="td text-neutral-500 max-w-[14rem] truncate" [title]="req.reason">{{ req.reason }}</td>
                  <td class="td text-neutral-500 text-xs whitespace-nowrap">{{ req.submittedOn | date:'short' }}</td>
                  <td class="td text-center">
                    <span class="status-badge" [class]="badgeClass()">Pending</span>
                  </td>
                  <td class="td text-right whitespace-nowrap">
                    <div class="inline-flex gap-1.5">
                      <button type="button" class="btn-approve !py-1.5 !px-3 text-xs"
                        [attr.data-test]="'approve-' + req.id" [disabled]="isActioning()"
                        (click)="startAction(req.id, 'approve')">Approve</button>
                      <button type="button" class="btn-reject !py-1.5 !px-3 text-xs"
                        [attr.data-test]="'reject-' + req.id" [disabled]="isActioning()"
                        (click)="startAction(req.id, 'reject')">Reject</button>
                    </div>
                  </td>
                </tr>
                @if (expandedId() === req.id || activeRowId() === req.id) {
                  <tr class="bg-neutral-50/60" @expand [attr.data-test]="'detail-' + req.id">
                    <td colspan="8" class="px-4 py-4">
                      <ng-container *ngTemplateOutlet="detailBlock; context: { $implicit: req }"></ng-container>
                    </td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (req of requests(); track req.id) {
            <div class="card-notion" @rowOut data-test="queue-card">
              <div class="flex items-start justify-between gap-3 mb-2">
                <div class="flex items-center gap-2.5">
                  <span class="avatar">{{ initials(req.employeeName) }}</span>
                  <span class="font-medium text-neutral-900">{{ req.employeeName }}</span>
                </div>
                <span class="status-badge" [class]="badgeClass()">Pending</span>
              </div>
              <dl class="grid grid-cols-2 gap-x-3 gap-y-1.5 text-xs mb-2">
                <div><dt class="dl">Date</dt><dd class="text-neutral-700">{{ req.date | date:'mediumDate' }}</dd></div>
                <div><dt class="dl">Overtime</dt><dd class="text-neutral-700">{{ minutes(req.overtimeMinutes) }}</dd></div>
                <div><dt class="dl">Multiplier</dt><dd class="text-neutral-700">{{ multiplier(req.multiplier) }}</dd></div>
                <div><dt class="dl">Submitted</dt><dd class="text-neutral-700">{{ req.submittedOn | date:'short' }}</dd></div>
              </dl>
              <p class="text-xs text-neutral-500 mb-3 line-clamp-2">{{ req.reason }}</p>
              <div class="flex gap-2">
                <button type="button" class="btn-approve flex-1 !py-2 text-sm"
                  [disabled]="isActioning()" (click)="startAction(req.id, 'approve')">Approve</button>
                <button type="button" class="btn-reject flex-1 !py-2 text-sm"
                  [disabled]="isActioning()" (click)="startAction(req.id, 'reject')">Reject</button>
              </div>
              @if (activeRowId() === req.id) {
                <div class="mt-3" @expand>
                  <ng-container *ngTemplateOutlet="actionPanel; context: { $implicit: req }"></ng-container>
                </div>
              }
            </div>
          }
        </div>
      }
    </div>

    <!-- ─── Detail block (desktop expand) ─── -->
    <ng-template #detailBlock let-req>
      <dl class="grid grid-cols-2 sm:grid-cols-4 gap-x-6 gap-y-3 text-sm mb-3">
        <div><dt class="dl">Employee</dt><dd class="text-neutral-800">{{ req.employeeName }}</dd></div>
        <div><dt class="dl">Date</dt><dd class="text-neutral-800">{{ req.date | date:'mediumDate' }}</dd></div>
        <div><dt class="dl">Overtime</dt><dd class="text-neutral-800">{{ minutes(req.overtimeMinutes) }}</dd></div>
        <div><dt class="dl">Type</dt><dd class="text-neutral-800">{{ typeLabel(req) }}</dd></div>
      </dl>
      <div class="mb-1">
        <dt class="dl">Reason</dt>
        <dd class="text-sm text-neutral-700 whitespace-pre-line">{{ req.reason }}</dd>
      </div>
      @if (activeRowId() === req.id) {
        <div class="mt-3">
          <ng-container *ngTemplateOutlet="actionPanel; context: { $implicit: req }"></ng-container>
        </div>
      }
    </ng-template>

    <!-- ─── Shared action panel ─── -->
    <ng-template #actionPanel let-req>
      @if (rowAction() === 'approve') {
        <div data-test="approve-panel" class="space-y-3">
          <div>
            <label class="dl mb-1 block" [attr.for]="'adj-' + req.id">
              Adjust awarded minutes <span class="text-neutral-300 normal-case">(optional, FR-6)</span>
            </label>
            <input type="number" min="0" [max]="req.overtimeMinutes" step="1"
              [id]="'adj-' + req.id" class="action-input w-40"
              [ngModel]="adjustedMinutes()" (ngModelChange)="adjustedMinutes.set($event)"
              [placeholder]="'Default ' + req.overtimeMinutes"
              data-test="approve-minutes" />
            <p class="text-xs text-neutral-400 mt-1">
              Leave blank to approve the full {{ minutes(req.overtimeMinutes) }}.
            </p>
          </div>
          <div>
            <label class="dl mb-1 block" [attr.for]="'cmt-' + req.id">
              Comment <span class="text-neutral-300 normal-case">(optional)</span>
            </label>
            <textarea [id]="'cmt-' + req.id" rows="2" class="action-textarea"
              [ngModel]="comment()" (ngModelChange)="comment.set($event)"
              placeholder="Add an optional note for the employee…"
              data-test="approve-comment"></textarea>
          </div>
        </div>
      }
      @if (rowAction() === 'reject') {
        <div data-test="reject-panel">
          <div class="flex items-center justify-between mb-1">
            <label class="dl block" [attr.for]="'rsn-' + req.id">
              Reason <span class="text-red-400 normal-case">(required)</span>
            </label>
            <span class="text-xs" [class.text-red-500]="reasonBelowMin()"
              [class.text-neutral-400]="!reasonBelowMin()">
              {{ reasonLength() }}/{{ minReason }}
            </span>
          </div>
          <textarea [id]="'rsn-' + req.id" rows="2" class="action-textarea"
            [class.textarea-error]="reasonBelowMin()"
            [ngModel]="rejectReason()" (ngModelChange)="rejectReason.set($event)"
            placeholder="Explain why this overtime is being rejected (min 10 characters)…"
            aria-required="true" data-test="reject-reason"></textarea>
          @if (reasonBelowMin()) {
            <p class="text-xs text-red-500 mt-1">Reason must be at least {{ minReason }} characters.</p>
          }
        </div>
      }

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
          (click)="cancelAction()" [disabled]="isActioning()" data-test="action-cancel">Cancel</button>
        @if (rowAction() === 'approve') {
          <button type="button" class="btn-approve !py-1.5 !px-3 text-xs"
            (click)="confirmAction(req)" [disabled]="isActioning()" data-test="confirm-approve">
            {{ isActioning() ? 'Approving…' : 'Confirm approval' }}
          </button>
        } @else {
          <button type="button" class="btn-reject-solid !py-1.5 !px-3 text-xs"
            (click)="confirmAction(req)" [disabled]="isActioning() || reasonBelowMin()"
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
    .status-badge { @apply inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset; }
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
    .action-input {
      @apply block rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .textarea-error { @apply border-red-300 focus:border-red-400 focus:ring-red-400; }
    .error-banner { @apply bg-red-50 border border-red-100 rounded-lg p-3 flex items-start gap-2.5; }
  `],
})
export class OvertimeApprovalsComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** Reject reason minimum length. */
  readonly minReason = 10;

  // ─── State ──────────────────────────────────────────────────
  readonly requests = signal<IOvertimeQueueItem[]>([]);
  readonly isLoading = signal(true);
  readonly isActioning = signal(false);

  readonly expandedId = signal<string | null>(null);
  readonly activeRowId = signal<string | null>(null);
  readonly rowAction = signal<RowAction>('none');

  readonly comment = signal('');
  readonly adjustedMinutes = signal<number | null>(null);
  readonly rejectReason = signal('');
  readonly actionError = signal<string | null>(null);

  // ─── Computed ───────────────────────────────────────────────
  readonly pendingCount = computed(() => this.requests().length);
  readonly reasonLength = computed(() => this.rejectReason().trim().length);
  readonly reasonBelowMin = computed(() => this.reasonLength() < this.minReason);

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getPendingOvertime()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (items) => {
          this.requests.set(items);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load the overtime approvals queue.');
        },
      });
  }

  refresh(): void {
    this.resetActionState();
    this.load();
  }

  toggleExpand(id: string): void {
    this.expandedId.update((cur) => (cur === id ? null : id));
  }

  startAction(id: string, action: 'approve' | 'reject'): void {
    this.activeRowId.set(id);
    this.rowAction.set(action);
    this.comment.set('');
    this.adjustedMinutes.set(null);
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
    this.adjustedMinutes.set(null);
    this.rejectReason.set('');
    this.actionError.set(null);
  }

  confirmAction(req: IOvertimeQueueItem): void {
    if (this.isActioning()) {
      return;
    }
    const mode = this.rowAction();
    if (mode === 'none') {
      return;
    }

    this.actionError.set(null);
    this.isActioning.set(true);

    if (mode === 'approve') {
      const adj = this.adjustedMinutes();
      const approvedMinutes = adj != null && adj >= 0 ? Number(adj) : undefined;
      const c = this.comment().trim();
      this.attendanceService
        .approveOvertime(req.id, approvedMinutes, c.length > 0 ? c : undefined)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: () => this.onActionSuccess(req, 'approve'),
          error: (err: HttpErrorResponse) => this.onActionError(err),
        });
    } else {
      if (this.reasonBelowMin()) {
        this.isActioning.set(false);
        return;
      }
      this.attendanceService
        .rejectOvertime(req.id, this.rejectReason().trim())
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: () => this.onActionSuccess(req, 'reject'),
          error: (err: HttpErrorResponse) => this.onActionError(err),
        });
    }
  }

  private onActionSuccess(req: IOvertimeQueueItem, mode: 'approve' | 'reject'): void {
    this.isActioning.set(false);
    this.removeFromQueue(req.id);
    this.resetActionState();
    const who = req.employeeName ?? 'the employee';
    if (mode === 'approve') {
      this.toastr.success(`Overtime approved for ${who}.`);
    } else {
      this.toastr.success(`Overtime rejected for ${who}.`);
    }
  }

  /**
   * BR-8 self-approval / not-team-member (403): show the server `message` verbatim
   * inline. 409 already-actioned: toast + re-sync the queue.
   */
  private onActionError(err: HttpErrorResponse): void {
    this.isActioning.set(false);
    const parsed = AttendanceService.parseOvertimeActionError(err);
    const message = parsed?.message ?? 'Failed to process this overtime. Please try again.';
    if (err.status === 409) {
      this.toastr.error(message);
      this.resetActionState();
      this.load();
      return;
    }
    this.actionError.set(message);
  }

  private removeFromQueue(id: string): void {
    this.requests.update((list) => list.filter((r) => r.id !== id));
  }

  // ─── View helpers ───────────────────────────────────────────
  typeLabel(req: IOvertimeQueueItem): string {
    return overtimeTypeLabel(req.type);
  }

  minutes(m: number): string {
    return formatWorkMinutes(m);
  }

  multiplier(m: number): string {
    return formatMultiplier(m);
  }

  badgeClass(): string {
    return OVERTIME_STATUS_CLASSES.PENDING;
  }

  initials(name: string): string {
    const parts = (name ?? '').trim().split(/\s+/);
    const first = parts[0]?.[0] ?? '';
    const last = parts.length > 1 ? parts[parts.length - 1][0] : '';
    return (first + last).toUpperCase() || '?';
  }
}
