import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { ReconciliationService } from '../../services/reconciliation.service';
import { LeaveEncashmentFormComponent } from '../leave-encashment-form/leave-encashment-form.component';
import {
  IReconciliationReport,
  IReconciliationRow,
  RECON_PAY_MONTHS,
  RECON_STATE_BADGE,
  RECON_STATE_LABELS,
  ReconciliationState,
  hasMismatch,
  leaveBreakdown,
  reconciliationState,
} from '../../models/reconciliation.models';

/**
 * US-PAY-010 (FR-7, §8): Pre-payroll reconciliation page.
 *
 * A Notion-style table showing each employee's attendance + leave summary for the
 * selected period — working days, present, absent, leave (by type), overtime hours and
 * calculated LOP — with COLOR-CODED cells (red absences, amber LOP, green full
 * attendance) and a per-row state badge. A "Show mismatches only" filter surfaces the
 * §8 "Reconcile" case: an absence not covered by approved leave (absent day with no
 * leave record).
 *
 * AC-4: when the period's attendance is NOT finalized the page shows an amber warning
 * banner with a link to the attendance finalization page, and the "Initiate payroll"
 * action is DISABLED (blocked) until attendance is finalized — the reconciliation
 * payload's `attendanceFinalized` flag drives both.
 *
 * Leave encashment (AC-3/FR-5) is triggered from a right slide-over drawer launched
 * from the header. Mobile (§8): the summary stat cards + the per-employee state are
 * viewable; the dense daily drill-down is desktop-only (a placeholder note replaces it
 * on small screens).
 */
@Component({
  selector: 'app-payroll-reconciliation',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, LeaveEncashmentFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div
        class="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between"
      >
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Pre-payroll reconciliation
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Review attendance &amp; leave per employee before initiating the run.
          </p>
        </div>
        <div class="flex flex-wrap items-end gap-3">
          <label class="block">
            <span class="mb-1 block text-xs font-medium text-neutral-600">Month</span>
            <select
              class="rounded-lg border border-neutral-300 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
              aria-label="Pay month"
              [ngModel]="payMonth()"
              (ngModelChange)="onMonthChange($event)"
            >
              @for (m of months; track m.value) {
                <option [ngValue]="m.value">{{ m.label }}</option>
              }
            </select>
          </label>
          <label class="block">
            <span class="mb-1 block text-xs font-medium text-neutral-600">Year</span>
            <select
              class="rounded-lg border border-neutral-300 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
              aria-label="Pay year"
              [ngModel]="payYear()"
              (ngModelChange)="onYearChange($event)"
            >
              @for (y of years; track y) {
                <option [ngValue]="y">{{ y }}</option>
              }
            </select>
          </label>
          <button
            type="button"
            class="inline-flex items-center gap-2 rounded-lg border border-neutral-300 bg-white px-4 py-2 text-sm font-medium text-neutral-700 shadow-sm transition hover:bg-neutral-50"
            (click)="openEncashment()"
          >
            Leave encashment
          </button>
        </div>
      </div>

      <!-- Attendance-not-finalized warning banner (AC-4) -->
      @if (report(); as r) {
        @if (!r.attendanceFinalized) {
          <div
            class="mt-6 flex items-start gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3"
            role="alert"
          >
            <svg
              class="mt-0.5 h-5 w-5 shrink-0 text-amber-500"
              viewBox="0 0 20 20"
              fill="currentColor"
              aria-hidden="true"
            >
              <path
                fill-rule="evenodd"
                d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z"
                clip-rule="evenodd"
              />
            </svg>
            <div class="text-sm">
              <p class="font-medium text-amber-800">
                Attendance data for {{ periodLabel() }} is not yet finalized.
              </p>
              <p class="mt-0.5 text-amber-700">
                LOP and overtime cannot be reliably calculated until attendance is
                finalized. Payroll initiation is blocked for this period.
                <a
                  routerLink="/attendance/payroll-integration"
                  class="font-medium underline hover:text-amber-900"
                  >Go to attendance finalization</a
                >.
              </p>
            </div>
          </div>
        } @else {
          <div
            class="mt-6 flex items-center gap-2 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-2.5 text-sm text-emerald-700"
            role="status"
          >
            <svg
              class="h-4 w-4 shrink-0"
              viewBox="0 0 20 20"
              fill="currentColor"
              aria-hidden="true"
            >
              <path
                fill-rule="evenodd"
                d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z"
                clip-rule="evenodd"
              />
            </svg>
            Attendance for {{ periodLabel() }} is finalized — ready to initiate payroll.
          </div>
        }
      }

      <!-- Summary stat cards (mobile-friendly, §8) -->
      @if (report(); as r) {
        <div class="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <p class="text-xs font-medium uppercase tracking-wide text-neutral-500">
              Employees
            </p>
            <p class="mt-1 text-2xl font-semibold text-neutral-900">
              {{ r.rows.length }}
            </p>
          </div>
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <p class="text-xs font-medium uppercase tracking-wide text-neutral-500">
              Full attendance
            </p>
            <p class="mt-1 text-2xl font-semibold text-emerald-600">
              {{ fullCount() }}
            </p>
          </div>
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <p class="text-xs font-medium uppercase tracking-wide text-neutral-500">
              With LOP
            </p>
            <p class="mt-1 text-2xl font-semibold text-amber-600">
              {{ lopCount() }}
            </p>
          </div>
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <p class="text-xs font-medium uppercase tracking-wide text-neutral-500">
              Unreconciled
            </p>
            <p class="mt-1 text-2xl font-semibold text-rose-600">
              {{ mismatchCount() }}
            </p>
          </div>
        </div>
      }

      <!-- Toolbar: mismatch filter -->
      <div class="mt-6 flex items-center justify-between gap-3">
        <label class="flex items-center gap-2 text-sm text-neutral-700">
          <input
            type="checkbox"
            class="h-4 w-4 rounded border-neutral-300"
            [ngModel]="mismatchesOnly()"
            (ngModelChange)="mismatchesOnly.set($event)"
          />
          Show mismatches only
          @if (mismatchCount() > 0) {
            <span
              class="inline-flex items-center rounded-full bg-rose-50 px-2 py-0.5 text-xs font-medium text-rose-700 ring-1 ring-inset ring-rose-600/20"
              >{{ mismatchCount() }}</span
            >
          }
        </label>
      </div>

      <!-- States -->
      @if (loading()) {
        <div class="mt-4 space-y-2">
          @for (i of skeletonRows; track i) {
            <div class="h-12 animate-pulse rounded-lg bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-6 text-center text-sm text-rose-700"
          role="alert"
        >
          Could not load the reconciliation report.
          <button
            type="button"
            class="ml-1 font-medium underline"
            (click)="load()"
          >
            Retry
          </button>
        </div>
      } @else if (visibleRows().length === 0) {
        <div
          class="mt-4 rounded-xl border border-neutral-200 bg-white px-4 py-10 text-center text-sm text-neutral-500"
        >
          @if (mismatchesOnly()) {
            No mismatches — every absence is covered by approved leave.
          } @else {
            No employees to reconcile for this period.
          }
        </div>
      } @else {
        <!-- Mobile: per-employee summary cards (desktop drill-down deferred, §8) -->
        <div class="mt-4 space-y-3 md:hidden">
          @for (row of visibleRows(); track row.employeeId) {
            <div
              class="rounded-xl border bg-white p-4 shadow-sm"
              [class.border-rose-200]="stateOf(row) === 'mismatch'"
              [class.border-neutral-200]="stateOf(row) !== 'mismatch'"
            >
              <div class="flex items-start justify-between">
                <div>
                  <p class="text-sm font-medium text-neutral-900">
                    {{ row.employeeName }}
                  </p>
                  <p class="text-xs text-neutral-400">{{ row.employeeNo }}</p>
                </div>
                <span
                  class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="stateBadge(row)"
                  >{{ stateLabel(row) }}</span
                >
              </div>
              <dl class="mt-3 grid grid-cols-3 gap-2 text-center text-xs">
                <div>
                  <dt class="text-neutral-400">Present</dt>
                  <dd class="font-medium text-neutral-800">{{ row.presentDays }}</dd>
                </div>
                <div>
                  <dt class="text-neutral-400">Absent</dt>
                  <dd
                    class="font-medium"
                    [class.text-rose-600]="row.absentDays > 0"
                    [class.text-neutral-800]="row.absentDays === 0"
                  >
                    {{ row.absentDays }}
                  </dd>
                </div>
                <div>
                  <dt class="text-neutral-400">LOP</dt>
                  <dd
                    class="font-medium"
                    [class.text-amber-600]="row.calculatedLopDays > 0"
                    [class.text-neutral-800]="row.calculatedLopDays === 0"
                  >
                    {{ row.calculatedLopDays }}
                  </dd>
                </div>
              </dl>
            </div>
          }
          <p class="px-1 pt-1 text-xs text-neutral-400">
            The daily attendance drill-down is available on a larger screen.
          </p>
        </div>

        <!-- Desktop: Notion-style reconciliation table -->
        <div
          class="mt-4 hidden overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm md:block"
        >
          <div class="overflow-x-auto">
            <table class="min-w-full divide-y divide-neutral-200 text-sm">
              <thead class="bg-neutral-50">
                <tr class="text-left text-xs font-medium uppercase tracking-wide text-neutral-500">
                  <th class="px-4 py-3">Employee</th>
                  <th class="px-4 py-3 text-right">Working</th>
                  <th class="px-4 py-3 text-right">Present</th>
                  <th class="px-4 py-3 text-right">Absent</th>
                  <th class="px-4 py-3 text-right">Leave</th>
                  <th class="px-4 py-3 text-right">OT hrs</th>
                  <th class="px-4 py-3 text-right">LOP</th>
                  <th class="px-4 py-3">Status</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-neutral-100">
                @for (row of visibleRows(); track row.employeeId) {
                  <tr
                    class="transition hover:bg-neutral-50/60"
                    [class.bg-rose-50/40]="stateOf(row) === 'mismatch'"
                  >
                    <td class="px-4 py-3">
                      <p class="font-medium text-neutral-900">{{ row.employeeName }}</p>
                      <p class="text-xs text-neutral-400">{{ row.employeeNo }}</p>
                    </td>
                    <td class="px-4 py-3 text-right tabular-nums text-neutral-700">
                      {{ row.workingDays }}
                    </td>
                    <td
                      class="px-4 py-3 text-right tabular-nums"
                      [class.text-emerald-700]="row.absentDays === 0 && row.calculatedLopDays === 0"
                      [class.font-medium]="row.absentDays === 0 && row.calculatedLopDays === 0"
                      [class.text-neutral-700]="!(row.absentDays === 0 && row.calculatedLopDays === 0)"
                    >
                      {{ row.presentDays }}
                    </td>
                    <td
                      class="px-4 py-3 text-right tabular-nums"
                      [class.bg-rose-50]="row.absentDays > 0"
                      [class.text-rose-700]="row.absentDays > 0"
                      [class.font-medium]="row.absentDays > 0"
                      [class.text-neutral-400]="row.absentDays === 0"
                    >
                      {{ row.absentDays }}
                    </td>
                    <td class="px-4 py-3 text-right tabular-nums text-neutral-700">
                      @if (row.totalLeaveDays > 0) {
                        <span
                          [title]="leaveTooltip(row)"
                          class="cursor-help underline decoration-dotted underline-offset-2"
                          >{{ row.totalLeaveDays }}</span
                        >
                      } @else {
                        <span class="text-neutral-400">0</span>
                      }
                    </td>
                    <td
                      class="px-4 py-3 text-right tabular-nums"
                      [class.text-neutral-700]="row.overtimeHours > 0"
                      [class.text-neutral-400]="row.overtimeHours === 0"
                    >
                      {{ row.overtimeHours }}
                    </td>
                    <td
                      class="px-4 py-3 text-right tabular-nums"
                      [class.bg-amber-50]="row.calculatedLopDays > 0"
                      [class.text-amber-700]="row.calculatedLopDays > 0"
                      [class.font-medium]="row.calculatedLopDays > 0"
                      [class.text-neutral-400]="row.calculatedLopDays === 0"
                    >
                      {{ row.calculatedLopDays }}
                    </td>
                    <td class="px-4 py-3">
                      <span
                        class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                        [class]="stateBadge(row)"
                        >{{ stateLabel(row) }}</span
                      >
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </div>

    <!-- Leave encashment drawer (AC-3/FR-5) -->
    @if (encashmentOpen()) {
      <app-leave-encashment-form
        (close)="encashmentOpen.set(false)"
        (saved)="onEncashed()"
      />
    }
  `,
})
export class PayrollReconciliationComponent implements OnInit {
  private readonly reconciliation = inject(ReconciliationService);

  readonly months = RECON_PAY_MONTHS;
  readonly years = this.buildYears();
  readonly skeletonRows = Array.from({ length: 6 }, (_, i) => i);

  readonly payMonth = signal(this.defaultMonth());
  readonly payYear = signal(this.defaultYear());

  readonly report = signal<IReconciliationReport | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal(false);
  readonly mismatchesOnly = signal(false);
  readonly encashmentOpen = signal(false);

  /** Rows after the mismatch filter (the §8 Reconcile surface). */
  readonly visibleRows = computed(() => {
    const rows = this.report()?.rows ?? [];
    return this.mismatchesOnly() ? rows.filter((r) => hasMismatch(r)) : rows;
  });

  readonly fullCount = computed(() => this.countState('full'));
  readonly lopCount = computed(() => this.countState('lop'));
  readonly mismatchCount = computed(() => this.countState('mismatch'));

  /** "May 2026" label for the selected period (banner + cards). */
  readonly periodLabel = computed(() => {
    const m = this.months.find((x) => x.value === this.payMonth());
    return `${m?.label ?? this.payMonth()} ${this.payYear()}`;
  });

  ngOnInit(): void {
    this.load();
  }

  onMonthChange(value: number): void {
    this.payMonth.set(value);
    this.load();
  }

  onYearChange(value: number): void {
    this.payYear.set(value);
    this.load();
  }

  /** Fetch the reconciliation report for the selected period (FR-7). */
  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.reconciliation
      .getReconciliation(this.payYear(), this.payMonth())
      .subscribe({
        next: (report) => {
          this.report.set(report);
          this.loading.set(false);
        },
        error: () => {
          this.report.set(null);
          this.loadError.set(true);
          this.loading.set(false);
        },
      });
  }

  // ─── Leave encashment drawer ─────────────────────────────────

  openEncashment(): void {
    this.encashmentOpen.set(true);
  }

  onEncashed(): void {
    this.encashmentOpen.set(false);
    // Reload so any LOP/leave figures reflect the new encashment context.
    this.load();
  }

  // ─── Row view helpers ────────────────────────────────────────

  stateOf(row: IReconciliationRow): ReconciliationState {
    return reconciliationState(row);
  }

  stateBadge(row: IReconciliationRow): string {
    return RECON_STATE_BADGE[reconciliationState(row)];
  }

  stateLabel(row: IReconciliationRow): string {
    return RECON_STATE_LABELS[reconciliationState(row)];
  }

  leaveTooltip(row: IReconciliationRow): string {
    const parts = leaveBreakdown(row).map((b) => `${b.type}: ${b.days}`);
    return parts.length ? parts.join(', ') : 'No approved leave';
  }

  // ─── Internals ───────────────────────────────────────────────

  private countState(state: ReconciliationState): number {
    return (this.report()?.rows ?? []).filter(
      (r) => reconciliationState(r) === state,
    ).length;
  }

  private defaultMonth(): number {
    const now = new Date();
    const m = now.getMonth(); // 0-based → previous month (payroll runs in arrears)
    return m === 0 ? 12 : m;
  }

  private defaultYear(): number {
    const now = new Date();
    return now.getMonth() === 0 ? now.getFullYear() - 1 : now.getFullYear();
  }

  private buildYears(): number[] {
    const current = new Date().getFullYear();
    return [current - 1, current, current + 1];
  }
}
