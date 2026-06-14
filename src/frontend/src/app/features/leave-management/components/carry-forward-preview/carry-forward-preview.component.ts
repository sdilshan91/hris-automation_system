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
import { takeUntil } from 'rxjs/operators';

import { CarryForwardPreviewService } from '../../services/carry-forward-preview.service';
import {
  ICarryForwardPreviewRow,
  buildPreviewYearOptions,
  matchesEmployeeTerm,
  distinctDepartments,
  distinctLeaveTypes,
  sumTotals,
} from '../../models/carry-forward-preview.models';

/**
 * US-LV-008 (AC-5): HR-facing Carry-Forward & Expiry preview report.
 *
 * A read-only Notion-like table (§8, §10) showing each employee's projected
 * carry-forward and forfeiture amounts for a selected closing year, BEFORE the
 * backend year-end Hangfire job runs. Layout per §8:
 *  - Year selector at the top (the closing leave year to preview).
 *  - Filters by department, employee (text), and leave type.
 *  - Color coding: carry-forward = blue, expired/forfeited = gray strikethrough.
 *  - Loading skeletons + empty state consistent with the other leave screens.
 *  - Mobile (360px+): the table is horizontally scrollable; a compact card list
 *    is used below `md:`.
 *
 * No "Run carry-forward now" button is rendered: the story's processing runs on
 * a schedule via Hangfire and the API contract exposes NO manual-trigger
 * endpoint. Per the brief, the action is omitted rather than inventing backend
 * behavior. If a trigger endpoint is added, gate it behind a confirmation dialog
 * (§10) and call a new service method.
 *
 * DEFER (backend, not built here): the two Hangfire jobs and Redis invalidation.
 */
@Component({
  selector: 'app-carry-forward-preview',
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
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header + year selector -->
      <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Carry-Forward Preview
          </h1>
          <p class="text-sm text-neutral-500 mt-1">
            Projected carry-forward and forfeiture for each employee at the close of the
            selected leave year. Read-only — nothing is committed.
          </p>
        </div>
        <div>
          <label class="label-sm" for="cf-year">Closing year</label>
          <select
            id="cf-year"
            class="input-sm select-input min-w-[8rem]"
            [ngModel]="selectedYear()"
            (ngModelChange)="selectYear(+$event)"
            aria-label="Select the closing leave year to preview"
          >
            @for (y of yearOptions; track y) {
              <option [value]="y">{{ y }}</option>
            }
          </select>
        </div>
      </div>

      <!-- Legend (color is never the sole signal: text labels accompany the swatches) -->
      <div class="flex flex-wrap items-center gap-4 mb-4 text-xs text-neutral-500">
        <span class="inline-flex items-center gap-1.5">
          <span class="h-2.5 w-2.5 rounded-full bg-blue-500" aria-hidden="true"></span>
          Carry-forward
        </span>
        <span class="inline-flex items-center gap-1.5">
          <span class="h-2.5 w-2.5 rounded-full bg-neutral-400" aria-hidden="true"></span>
          Expired / forfeited
        </span>
      </div>

      <!-- Filters -->
      <div class="card-notion mb-4">
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <div>
            <label class="label-sm" for="f-dept">Department</label>
            <select
              id="f-dept"
              class="input-sm select-input"
              [ngModel]="filterDepartment()"
              (ngModelChange)="filterDepartment.set($event)"
            >
              <option value="">All departments</option>
              @for (d of departmentOptions(); track d) {
                <option [value]="d">{{ d }}</option>
              }
            </select>
          </div>
          <div>
            <label class="label-sm" for="f-emp">Employee</label>
            <input
              id="f-emp"
              type="text"
              class="input-sm"
              placeholder="Search by name…"
              [ngModel]="filterEmployee()"
              (ngModelChange)="filterEmployee.set($event)"
            />
          </div>
          <div>
            <label class="label-sm" for="f-lt">Leave Type</label>
            <select
              id="f-lt"
              class="input-sm select-input"
              [ngModel]="filterLeaveTypeId()"
              (ngModelChange)="filterLeaveTypeId.set($event)"
            >
              <option value="">All leave types</option>
              @for (lt of leaveTypeOptions(); track lt.id) {
                <option [value]="lt.id">{{ lt.name }}</option>
              }
            </select>
          </div>
        </div>
      </div>

      <!-- Loading skeletons -->
      @if (isLoading()) {
        <div class="card-notion" aria-live="polite" aria-busy="true">
          <div class="space-y-3">
            @for (_ of [1,2,3,4,5]; track $index) {
              <div class="skeleton-line w-full h-10"></div>
            }
          </div>
        </div>
      } @else if (filteredRows().length === 0) {
        <!-- Empty state -->
        <div @fadeIn class="card-notion text-center py-16" data-testid="empty-state">
          <svg class="mx-auto mb-4 text-neutral-300" width="64" height="64" viewBox="0 0 24 24"
            fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
            <rect x="3" y="4" width="18" height="17" rx="2" />
            <path d="M16 2v4M8 2v4M3 10h18M9 14l2 2 4-4" />
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">Nothing to carry forward</h3>
          <p class="text-sm text-neutral-500 max-w-md mx-auto">
            @if (allRows().length === 0) {
              No projected carry-forward or forfeiture for {{ selectedYear() }}.
            } @else {
              No rows match the current filters. Try clearing the department, employee, or
              leave-type filters.
            }
          </p>
        </div>
      } @else {
        <!-- Summary strip -->
        <div class="grid grid-cols-3 gap-3 mb-4" data-testid="totals">
          <div class="card-notion py-3">
            <p class="text-xs text-neutral-400 uppercase tracking-wider">Employees × types</p>
            <p class="text-xl font-semibold text-neutral-900">{{ totals().rows }}</p>
          </div>
          <div class="card-notion py-3">
            <p class="text-xs text-neutral-400 uppercase tracking-wider">Carry-forward</p>
            <p class="text-xl font-semibold text-blue-600">{{ totals().carryForward }}</p>
          </div>
          <div class="card-notion py-3">
            <p class="text-xs text-neutral-400 uppercase tracking-wider">Forfeited</p>
            <p class="text-xl font-semibold text-neutral-500">{{ totals().forfeiture }}</p>
          </div>
        </div>

        <!-- Desktop table -->
        <div class="hidden md:block card-notion overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="Carry-forward preview" data-testid="preview-table">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th">Employee</th>
                <th class="th">Department</th>
                <th class="th">Leave Type</th>
                <th class="th text-right">Carry-forward</th>
                <th class="th text-right">Forfeited</th>
              </tr>
            </thead>
            <tbody>
              @for (r of filteredRows(); track trackRow(r); let odd = $odd) {
                <tr class="border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors"
                  [class.bg-neutral-50/40]="odd">
                  <td class="td font-medium text-neutral-900">{{ r.employeeName }}</td>
                  <td class="td text-neutral-600">{{ r.departmentName || '—' }}</td>
                  <td class="td text-neutral-600">{{ r.leaveTypeName }}</td>
                  <td class="td text-right">
                    @if (r.projectedCarryForward > 0) {
                      <span class="cf-amount" data-testid="cf-amount">+{{ r.projectedCarryForward }}</span>
                    } @else {
                      <span class="text-neutral-300">0</span>
                    }
                  </td>
                  <td class="td text-right">
                    @if (r.projectedForfeiture > 0) {
                      <span class="ff-amount" data-testid="ff-amount">{{ r.projectedForfeiture }}</span>
                    } @else {
                      <span class="text-neutral-300">0</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile card list (360px+) -->
        <div class="md:hidden space-y-3" @fadeIn data-testid="preview-cards">
          @for (r of filteredRows(); track trackRow(r)) {
            <div class="card-notion">
              <div class="flex items-start justify-between gap-2 mb-2">
                <div>
                  <p class="font-medium text-neutral-900">{{ r.employeeName }}</p>
                  <p class="text-xs text-neutral-500">{{ r.departmentName || '—' }}</p>
                </div>
                <span class="text-xs text-neutral-500 ring-1 ring-inset ring-neutral-200 rounded px-2 py-0.5">
                  {{ r.leaveTypeName }}
                </span>
              </div>
              <div class="flex items-center gap-6 text-sm">
                <span>
                  <span class="text-neutral-400 mr-1">Carry-forward</span>
                  @if (r.projectedCarryForward > 0) {
                    <span class="cf-amount">+{{ r.projectedCarryForward }}</span>
                  } @else { <span class="text-neutral-300">0</span> }
                </span>
                <span>
                  <span class="text-neutral-400 mr-1">Forfeited</span>
                  @if (r.projectedForfeiture > 0) {
                    <span class="ff-amount">{{ r.projectedForfeiture }}</span>
                  } @else { <span class="text-neutral-300">0</span> }
                </span>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-6xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-sm {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2
        text-sm text-neutral-900 placeholder-neutral-400 transition-all duration-150
        focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-400;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    .th { @apply text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .td { @apply py-3 px-3; }

    /* §8 color coding: carry-forward = blue; forfeited = gray strikethrough */
    .cf-amount { @apply font-semibold text-blue-600; }
    .ff-amount { @apply font-medium text-neutral-400 line-through; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class CarryForwardPreviewComponent implements OnInit, OnDestroy {
  private readonly previewService = inject(CarryForwardPreviewService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly yearOptions = buildPreviewYearOptions(new Date().getFullYear());

  // ─── State signals ─────────────────────────────────────
  readonly selectedYear = signal(new Date().getFullYear());
  readonly allRows = signal<ICarryForwardPreviewRow[]>([]);
  readonly isLoading = signal(true);

  // ─── Filter signals ────────────────────────────────────
  readonly filterDepartment = signal('');
  readonly filterEmployee = signal('');
  readonly filterLeaveTypeId = signal('');

  // ─── Derived state ─────────────────────────────────────
  /** Filter option lists are derived from the loaded (denormalized) rows. */
  readonly departmentOptions = computed(() => distinctDepartments(this.allRows()));
  readonly leaveTypeOptions = computed(() => distinctLeaveTypes(this.allRows()));

  readonly filteredRows = computed(() => {
    const dept = this.filterDepartment();
    const emp = this.filterEmployee();
    const ltId = this.filterLeaveTypeId();
    return this.allRows().filter((r) => {
      if (dept && (r.departmentName ?? '') !== dept) {
        return false;
      }
      if (ltId && r.leaveTypeId !== ltId) {
        return false;
      }
      return matchesEmployeeTerm(r, emp);
    });
  });

  readonly totals = computed(() => sumTotals(this.filteredRows()));

  ngOnInit(): void {
    this.loadYear(this.selectedYear());
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Load (or reload) the preview for a given closing year. */
  loadYear(year: number): void {
    this.isLoading.set(true);
    this.previewService
      .getPreview(year)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (rows) => {
          this.allRows.set(rows ?? []);
          this.isLoading.set(false);
        },
        error: () => {
          this.allRows.set([]);
          this.isLoading.set(false);
          this.toastr.error('Failed to load the carry-forward preview.');
        },
      });
  }

  selectYear(year: number): void {
    if (year === this.selectedYear()) {
      return;
    }
    this.selectedYear.set(year);
    this.loadYear(year);
  }

  /** Stable track key for a row (an employee can have multiple leave-type rows). */
  trackRow(r: ICarryForwardPreviewRow): string {
    return `${r.employeeId}:${r.leaveTypeId}`;
  }
}
