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
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { finalize, takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  IPeriodLock,
  IReconciliationRow,
  IReconciliationResult,
  IAttendancePayrollRow,
  IAttendancePayrollResult,
  PAYROLL_PROCESS_STEPS,
  formatPeriodLabel,
  periodDateRange,
  formatWorkMinutes,
} from '../../models/attendance.models';

/**
 * US-ATT-009: Attendance integration with Payroll (HR view).
 *
 * The attendance side of the payroll prep flow. Combines:
 *  - a visual payroll-process stepper (Lock Attendance → Generate → Review → Finalize →
 *    Publish) where only "Lock Attendance" is actionable (FR-3, §8) — the rest are
 *    disabled placeholders pending the payroll module.
 *  - an Attendance-Lock panel: month selector + current lock status, a "Lock Attendance"
 *    action with a confirmation modal (AC-4, POST period-lock) and an "Unlock" action
 *    (AC-5, POST unlock). A prominent banner surfaces the locked state (§8).
 *  - a reconciliation view (FR-5): a Notion-style table of the attendance summary per
 *    employee with the payroll-input columns shown as a clearly-labelled "pending
 *    payroll module" placeholder (that module is not yet built). Stacks on mobile.
 *  - an optional read-only payroll-data preview (FR-1/FR-2) HR can expand.
 *
 * HR-only via the route guard. Tenant-scoped via the tenantInterceptor.
 */
@Component({
  selector: 'app-payroll-integration',
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
    trigger('modal', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.96)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'scale(1)' })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0, transform: 'scale(0.96)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header + month picker -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Payroll Integration
          </h1>
          <p class="text-sm text-neutral-500 mt-1">
            Lock attendance and reconcile the feed to payroll for the selected month.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <button type="button" class="arrow-btn" (click)="stepMonth(-1)"
            aria-label="Previous month" data-test="prev-month">‹</button>
          <label class="sr-only" for="payroll-month">Month</label>
          <input id="payroll-month" type="month" class="field" [ngModel]="month()"
            (ngModelChange)="onMonthChange($event)" data-test="month-selector" />
          <button type="button" class="arrow-btn" (click)="stepMonth(1)"
            aria-label="Next month" data-test="next-month">›</button>
        </div>
      </div>

      <!-- Locked banner (§8) -->
      @if (isLocked()) {
        <div class="locked-banner" role="status" data-test="locked-banner" @fadeIn>
          <span class="lock-glyph" aria-hidden="true">🔒</span>
          <div class="flex-1">
            <p class="font-semibold text-amber-900">
              Attendance locked for {{ monthLabel() }} payroll
            </p>
            <p class="text-xs text-amber-800/80">
              No clock-in/out, regularization, or modifications are allowed for this period.
              @if (lock()?.lockedAt) {
                Locked {{ dateTime(lock()!.lockedAt) }}.
              }
            </p>
          </div>
        </div>
      }

      <!-- Payroll-process stepper (§8) -->
      <div class="card-notion mb-5" @fadeIn>
        <ol class="flex flex-col sm:flex-row sm:items-center gap-3 sm:gap-0" data-test="stepper">
          @for (step of steps; track step.key; let i = $index) {
            <li class="step" [class.step-active]="step.key === 'lock'"
              [class.step-disabled]="!step.actionable" data-test="step">
              <span class="step-index"
                [class.step-index-active]="step.key === 'lock' && isLocked()">
                @if (step.key === 'lock' && isLocked()) { ✓ } @else { {{ i + 1 }} }
              </span>
              <span class="step-label">{{ step.label }}</span>
              @if (!step.actionable) {
                <span class="step-pending">Payroll module pending</span>
              }
              @if (i < steps.length - 1) {
                <span class="step-sep" aria-hidden="true">›</span>
              }
            </li>
          }
        </ol>
      </div>

      <!-- Attendance Lock panel (FR-3, AC-4, AC-5) -->
      <div class="card-notion mb-6" @fadeIn>
        <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
          <div>
            <h2 class="text-base font-semibold text-neutral-900">Attendance Lock</h2>
            <p class="text-sm text-neutral-500 mt-0.5">
              {{ monthLabel() }} ·
              @if (lockLoading()) {
                <span data-test="lock-loading">checking status…</span>
              } @else if (isLocked()) {
                <span class="text-amber-700 font-medium" data-test="lock-status">Locked</span>
              } @else {
                <span class="text-green-700 font-medium" data-test="lock-status">Open</span>
              }
            </p>
          </div>
          <div class="flex items-center gap-2">
            @if (!isLocked()) {
              <button type="button" class="btn-primary" (click)="openLockConfirm()"
                [disabled]="lockLoading() || acting()" data-test="lock-btn">
                Lock Attendance
              </button>
            } @else {
              <button type="button" class="btn-secondary" (click)="openUnlockConfirm()"
                [disabled]="acting()" data-test="unlock-btn">
                Unlock
              </button>
            }
          </div>
        </div>
      </div>

      <!-- Reconciliation view (FR-5) -->
      <div class="flex items-center justify-between mb-3">
        <h2 class="text-base font-semibold text-neutral-900">
          Reconciliation
          <span class="text-xs font-normal text-neutral-400">attendance vs payroll</span>
        </h2>
        <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700 underline"
          (click)="loadReconciliation()" data-test="refresh-recon">Refresh from attendance</button>
      </div>

      @if (reconLoading()) {
        <div class="card-notion space-y-3" aria-busy="true" data-test="recon-skeleton">
          @for (_ of [1,2,3,4]; track $index) {
            <div class="skeleton-line h-10 w-full"></div>
          }
        </div>
      } @else if (reconRows().length === 0) {
        <div class="card-notion text-center py-12" @fadeIn data-test="recon-empty">
          <h3 class="text-base font-semibold text-neutral-900 mb-1">No reconciliation rows</h3>
          <p class="text-sm text-neutral-500">No attendance data for {{ monthLabel() }}.</p>
        </div>
      } @else {
        <!-- Desktop side-by-side table -->
        <div class="card-notion !p-0 overflow-x-auto hidden md:block" @fadeIn>
          <table class="w-full text-sm" aria-label="Attendance vs payroll reconciliation">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th" rowspan="2">Employee</th>
                <th class="th text-center th-group" colspan="4">Attendance</th>
                <th class="th text-center th-group th-pending" colspan="2">
                  Payroll <span class="pending-tag">pending module</span>
                </th>
              </tr>
              <tr class="border-b border-neutral-100">
                <th class="th text-right">Present</th>
                <th class="th text-right">LOP</th>
                <th class="th text-right">Approved OT</th>
                <th class="th text-right">Work hrs</th>
                <th class="th text-right th-pending">LOP deduction</th>
                <th class="th text-right th-pending">OT pay</th>
              </tr>
            </thead>
            <tbody>
              @for (row of reconRows(); track row.employeeId) {
                <tr class="row" data-test="recon-row">
                  <td class="td font-medium text-neutral-900">{{ row.employeeName }}</td>
                  <td class="td text-right text-green-700">{{ row.presentDays }}</td>
                  <td class="td text-right" [class.text-red-700]="row.lopDays > 0"
                    [class.font-semibold]="row.lopDays > 0" data-test="recon-lop">{{ row.lopDays }}</td>
                  <td class="td text-right text-neutral-700">{{ minutes(row.approvedOvertimeMinutes) }}</td>
                  <td class="td text-right text-neutral-700">{{ minutes(row.totalWorkMinutes) }}</td>
                  <td class="td text-right td-pending" data-test="recon-pending">—</td>
                  <td class="td text-right td-pending">—</td>
                </tr>
              }
            </tbody>
          </table>
          <p class="px-4 py-3 text-xs text-neutral-400 border-t border-neutral-50">
            Payroll columns will populate once the Payroll module is built; they show the
            attendance feed only for now.
          </p>
        </div>

        <!-- Mobile stacked cards (§8: attendance on top, payroll below) -->
        <div class="space-y-3 md:hidden" @fadeIn>
          @for (row of reconRows(); track row.employeeId) {
            <div class="mobile-card" data-test="recon-card">
              <span class="font-medium text-neutral-900 block mb-2">{{ row.employeeName }}</span>
              <p class="text-[11px] font-medium text-neutral-400 uppercase tracking-wider mb-1">Attendance</p>
              <div class="grid grid-cols-2 gap-y-1 text-xs text-neutral-500 mb-3">
                <span>Present <b class="text-green-700">{{ row.presentDays }}</b></span>
                <span>LOP <b [class.text-red-700]="row.lopDays > 0">{{ row.lopDays }}</b></span>
                <span>Approved OT <b class="text-neutral-700">{{ minutes(row.approvedOvertimeMinutes) }}</b></span>
                <span>Work <b class="text-neutral-700">{{ minutes(row.totalWorkMinutes) }}</b></span>
              </div>
              <p class="text-[11px] font-medium text-neutral-400 uppercase tracking-wider mb-1">
                Payroll <span class="pending-tag">pending module</span>
              </p>
              <div class="grid grid-cols-2 gap-y-1 text-xs text-neutral-400">
                <span>LOP deduction <b>—</b></span>
                <span>OT pay <b>—</b></span>
              </div>
            </div>
          }
        </div>
      }

      <!-- Payroll-data preview (FR-1/FR-2, optional read-only) -->
      <div class="mt-6">
        <button type="button" class="text-sm text-neutral-600 hover:text-neutral-900 underline"
          (click)="togglePreview()" data-test="toggle-preview">
          {{ showPreview() ? 'Hide' : 'Show' }} payroll-data feed
        </button>
        @if (showPreview()) {
          <div class="mt-3" @fadeIn>
            @if (previewLoading()) {
              <div class="card-notion space-y-3" aria-busy="true" data-test="preview-skeleton">
                @for (_ of [1,2,3]; track $index) {
                  <div class="skeleton-line h-9 w-full"></div>
                }
              </div>
            } @else if (previewRows().length === 0) {
              <div class="card-notion text-center py-8 text-sm text-neutral-500" data-test="preview-empty">
                No payroll-data rows for {{ monthLabel() }}.
              </div>
            } @else {
              <div class="card-notion !p-0 overflow-x-auto" data-test="preview-table">
                <table class="w-full text-sm" aria-label="Attendance payroll-data feed">
                  <thead>
                    <tr class="border-b border-neutral-100">
                      <th class="th">Employee</th>
                      <th class="th text-right">Working</th>
                      <th class="th text-right">Present</th>
                      <th class="th text-right">Absent</th>
                      <th class="th text-right">LOP</th>
                      <th class="th text-right">Late ded.</th>
                      <th class="th text-right">Approved OT</th>
                      <th class="th text-right">Work hrs</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of previewRows(); track row.employeeId) {
                      <tr class="row" data-test="preview-row">
                        <td class="td text-neutral-700">{{ row.employeeId }}</td>
                        <td class="td text-right text-neutral-700">{{ row.totalWorkingDays }}</td>
                        <td class="td text-right text-green-700">{{ row.totalPresentDays }}</td>
                        <td class="td text-right text-neutral-700">{{ row.totalAbsentDays }}</td>
                        <td class="td text-right" [class.text-red-700]="row.lopDays > 0">{{ row.lopDays }}</td>
                        <td class="td text-right text-neutral-700">{{ row.lateDeductionDays }}</td>
                        <td class="td text-right text-neutral-700">{{ minutes(row.approvedOvertimeMinutes) }}</td>
                        <td class="td text-right text-neutral-700">{{ minutes(row.totalWorkMinutes) }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        }
      </div>
    </div>

    <!-- Lock / Unlock confirmation modal (AC-4, AC-5) -->
    @if (confirmMode(); as mode) {
      <div class="fixed inset-0 z-40 bg-neutral-900/40" (click)="closeConfirm()"
        data-test="confirm-backdrop"></div>
      <div class="fixed inset-0 z-50 flex items-center justify-center p-4 pointer-events-none">
        <div class="confirm-panel pointer-events-auto" @modal role="dialog" aria-modal="true"
          [attr.aria-label]="mode === 'lock' ? 'Confirm lock attendance' : 'Confirm unlock attendance'"
          data-test="confirm-modal">
          @if (mode === 'lock') {
            <h3 class="text-lg font-semibold text-neutral-900 mb-1">Lock attendance?</h3>
            <p class="text-sm text-neutral-600 mb-5">
              This freezes all attendance for <b>{{ monthLabel() }}</b>. No clock-in/out,
              regularization, or modifications will be allowed until you unlock it. Payroll
              can then proceed for the period.
            </p>
          } @else {
            <h3 class="text-lg font-semibold text-neutral-900 mb-1">Unlock attendance?</h3>
            <p class="text-sm text-neutral-600 mb-5">
              This re-opens <b>{{ monthLabel() }}</b> for corrections. Any payroll already
              started for this period must be re-pulled and re-locked once corrected.
            </p>
          }
          <div class="flex justify-end gap-2">
            <button type="button" class="btn-secondary" (click)="closeConfirm()"
              [disabled]="acting()" data-test="confirm-cancel">Cancel</button>
            @if (mode === 'lock') {
              <button type="button" class="btn-primary" (click)="confirmLock()"
                [disabled]="acting()" data-test="confirm-lock">
                {{ acting() ? 'Locking…' : 'Lock attendance' }}
              </button>
            } @else {
              <button type="button" class="btn-danger" (click)="confirmUnlock()"
                [disabled]="acting()" data-test="confirm-unlock">
                {{ acting() ? 'Unlocking…' : 'Unlock attendance' }}
              </button>
            }
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-6xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider whitespace-nowrap; }
    .th-group { @apply border-l border-neutral-100; }
    .th-pending { @apply text-neutral-300; }
    .td { @apply py-3 px-4 align-middle whitespace-nowrap; }
    .td-pending { @apply text-neutral-300; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60; }
    .pending-tag {
      @apply inline-block rounded-full bg-neutral-100 px-2 py-0.5 text-[10px] font-medium
        text-neutral-400 normal-case tracking-normal ml-1;
    }
    .field {
      @apply block rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .arrow-btn {
      @apply inline-flex items-center justify-center h-9 w-9 rounded-lg border border-neutral-200 bg-white
        text-neutral-600 transition-colors hover:bg-neutral-50;
    }
    .locked-banner {
      @apply flex items-center gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 mb-5 shadow-sm;
    }
    .lock-glyph { @apply text-xl; }
    .step {
      @apply flex items-center gap-2 sm:flex-1 text-sm text-neutral-500;
    }
    .step-active { @apply text-neutral-900 font-medium; }
    .step-disabled { @apply opacity-60; }
    .step-index {
      @apply inline-flex items-center justify-center h-6 w-6 rounded-full bg-neutral-100 text-xs
        font-semibold text-neutral-500;
    }
    .step-index-active { @apply bg-green-100 text-green-700; }
    .step-label { @apply whitespace-nowrap; }
    .step-pending {
      @apply hidden lg:inline rounded-full bg-neutral-100 px-2 py-0.5 text-[10px] font-medium text-neutral-400;
    }
    .step-sep { @apply hidden sm:inline text-neutral-300 mx-2 ml-auto; }
    .mobile-card { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-4; }
    .confirm-panel { @apply w-full max-w-md rounded-xl bg-white shadow-xl p-6; }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5 text-sm font-medium
        text-white transition-all duration-200 hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-3.5 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-danger {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-5 py-2.5 text-sm font-medium
        text-white transition-all duration-200 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class PayrollIntegrationComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly steps = PAYROLL_PROCESS_STEPS;

  // ─── State ──────────────────────────────────────────────────
  readonly month = signal(currentMonthIso());

  readonly lock = signal<IPeriodLock | null>(null);
  readonly lockLoading = signal(true);
  readonly acting = signal(false);
  /** null | 'lock' | 'unlock' — which confirmation modal is open. */
  readonly confirmMode = signal<'lock' | 'unlock' | null>(null);

  readonly reconResult = signal<IReconciliationResult | null>(null);
  readonly reconLoading = signal(true);

  readonly showPreview = signal(false);
  readonly previewResult = signal<IAttendancePayrollResult | null>(null);
  readonly previewLoading = signal(false);
  private previewLoaded = false;

  // ─── Computed ───────────────────────────────────────────────
  readonly isLocked = computed(() => this.lock()?.isLocked === true);
  readonly monthLabel = computed(() => formatPeriodLabel(this.month()));
  readonly reconRows = computed<IReconciliationRow[]>(
    () => this.reconResult()?.rows ?? [],
  );
  readonly previewRows = computed<IAttendancePayrollRow[]>(
    () => this.previewResult()?.rows ?? [],
  );

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.loadLock();
    this.loadReconciliation();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Month navigation ───────────────────────────────────────
  onMonthChange(value: string): void {
    if (!value) {
      return;
    }
    this.month.set(value);
    this.reloadAll();
  }

  stepMonth(delta: number): void {
    const [y, m] = this.month().split('-').map(Number);
    const d = new Date(y, m - 1 + delta, 1);
    this.month.set(
      `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}`,
    );
    this.reloadAll();
  }

  private reloadAll(): void {
    this.loadLock();
    this.loadReconciliation();
    this.previewLoaded = false;
    if (this.showPreview()) {
      this.loadPreview();
    }
  }

  // ─── Lock status (FR-3, AC-4) ───────────────────────────────
  loadLock(): void {
    this.lockLoading.set(true);
    this.attendanceService
      .getPeriodLock(this.month())
      .pipe(takeUntil(this.destroy$), finalize(() => this.lockLoading.set(false)))
      .subscribe({
        next: (l) => this.lock.set(l),
        error: () => {
          this.lock.set(null);
          this.toastr.error('Failed to load the lock status.');
        },
      });
  }

  // ─── Reconciliation (FR-5) ──────────────────────────────────
  loadReconciliation(): void {
    this.reconLoading.set(true);
    this.attendanceService
      .getReconciliation(this.month())
      .pipe(takeUntil(this.destroy$), finalize(() => this.reconLoading.set(false)))
      .subscribe({
        next: (res) => this.reconResult.set(res),
        error: () => {
          this.reconResult.set(null);
          this.toastr.error('Failed to load the reconciliation.');
        },
      });
  }

  // ─── Lock / Unlock actions (AC-4, AC-5) ─────────────────────
  openLockConfirm(): void {
    this.confirmMode.set('lock');
  }

  openUnlockConfirm(): void {
    this.confirmMode.set('unlock');
  }

  closeConfirm(): void {
    if (this.acting()) {
      return;
    }
    this.confirmMode.set(null);
  }

  confirmLock(): void {
    if (this.acting()) {
      return;
    }
    const { start, end } = periodDateRange(this.month());
    this.acting.set(true);
    this.attendanceService
      .lockPeriod(start, end)
      .pipe(takeUntil(this.destroy$), finalize(() => this.acting.set(false)))
      .subscribe({
        next: (l) => {
          this.lock.set(l);
          this.confirmMode.set(null);
          this.toastr.success(`Attendance locked for ${this.monthLabel()} payroll.`);
        },
        error: () => this.toastr.error('Failed to lock the period.'),
      });
  }

  confirmUnlock(): void {
    if (this.acting()) {
      return;
    }
    const id = this.lock()?.id;
    if (!id) {
      this.confirmMode.set(null);
      return;
    }
    this.acting.set(true);
    this.attendanceService
      .unlockPeriod(id)
      .pipe(takeUntil(this.destroy$), finalize(() => this.acting.set(false)))
      .subscribe({
        next: (l) => {
          this.lock.set(l);
          this.confirmMode.set(null);
          this.toastr.success(`Attendance unlocked for ${this.monthLabel()}.`);
        },
        error: () => this.toastr.error('Failed to unlock the period.'),
      });
  }

  // ─── Payroll-data preview (FR-1/FR-2) ───────────────────────
  togglePreview(): void {
    const next = !this.showPreview();
    this.showPreview.set(next);
    if (next && !this.previewLoaded) {
      this.loadPreview();
    }
  }

  loadPreview(): void {
    this.previewLoading.set(true);
    this.attendanceService
      .getPayrollData(this.month())
      .pipe(takeUntil(this.destroy$), finalize(() => this.previewLoading.set(false)))
      .subscribe({
        next: (res) => {
          this.previewResult.set(res);
          this.previewLoaded = true;
        },
        error: () => {
          this.previewResult.set(null);
          this.toastr.error('Failed to load the payroll-data feed.');
        },
      });
  }

  // ─── Formatting ─────────────────────────────────────────────
  minutes(m: number): string {
    return formatWorkMinutes(m);
  }

  /** Format a UTC timestamptz to a local date+time; em-dash for null. */
  dateTime(iso: string | null | undefined): string {
    if (!iso) {
      return '—';
    }
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return '—';
    }
    return d.toLocaleString();
  }
}

/** Current month as `yyyy-MM` in the browser's local timezone. */
function currentMonthIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  return `${y}-${m}`;
}
