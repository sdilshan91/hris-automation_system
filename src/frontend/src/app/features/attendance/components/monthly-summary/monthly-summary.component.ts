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
import { Subject, timer } from 'rxjs';
import { switchMap, takeUntil, takeWhile, finalize } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import { DepartmentService } from '../../../core-hr/departments/services/department.service';
import { LocationService } from '../../../core-hr/locations/services/location.service';
import {
  IEmployeeMonthlySummary,
  IMonthlySummaryResult,
  IMonthlySummaryBanner,
  IMonthlySummaryQuery,
  IEmployeeDailyBreakdownResult,
  IShift,
  SummaryExportFormat,
  DAILY_STATUS_CLASSES,
  dailyStatusLabel,
  summaryCellClass,
  attendancePercent,
  formatWorkMinutes,
} from '../../models/attendance.models';
import { IDepartment } from '../../../core-hr/departments/models/department.models';
import { ILocation } from '../../../core-hr/locations/models/location.models';

/** Sortable summary columns (AC-1, §8). */
type SortKey =
  | 'employeeName'
  | 'presentDays'
  | 'absentDays'
  | 'lateCount'
  | 'earlyDepartureCount'
  | 'overtimeMinutes'
  | 'leaveDays'
  | 'workMinutes';
type SortDir = 'asc' | 'desc';

/** A late-count above this is highlighted amber (§8). */
const HIGH_LATE_THRESHOLD = 3;
/** An absent-count at/above this is highlighted red (§8). */
const HIGH_ABSENT_THRESHOLD = 3;

/**
 * US-ATT-007: Monthly attendance summary per employee (HR view).
 *
 * A Notion-style sortable/filterable table — one row per employee with present/absent
 * days, late + early-departure counts, overtime, leave and total work hours (AC-1) —
 * plus a summary banner (total employees, avg attendance %, total LOP), department/
 * location/shift filter chips (AC-5), a month-year picker, on-demand generation when
 * the summary has not been computed yet (AC-3), CSV/Excel/PDF export (AC-4), and a
 * per-employee day-by-day drill-down drawer (AC-2). Role-gated via the route guard.
 */
@Component({
  selector: 'app-monthly-summary',
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
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('250ms ease-out', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header + month picker -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Monthly Attendance Summary
          </h1>
          <p class="text-sm text-neutral-500 mt-1">
            Present, absent, late, overtime and leave per employee for the selected month.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <button type="button" class="arrow-btn" (click)="stepMonth(-1)"
            aria-label="Previous month" data-test="prev-month">‹</button>
          <label class="sr-only" for="summary-month">Month</label>
          <input id="summary-month" type="month" class="field" [ngModel]="month()"
            (ngModelChange)="onMonthChange($event)" data-test="month-selector" />
          <button type="button" class="arrow-btn" (click)="stepMonth(1)"
            aria-label="Next month" data-test="next-month">›</button>
        </div>
      </div>

      <!-- Filter chips + export -->
      <div class="flex flex-col lg:flex-row lg:items-center lg:justify-between gap-3 mb-5">
        <div class="flex flex-wrap items-center gap-2">
          <select class="chip" [ngModel]="departmentId()" (ngModelChange)="onFilterChange('department', $event)"
            aria-label="Filter by department" data-test="filter-department">
            <option value="">All departments</option>
            @for (d of departments(); track d.departmentId) {
              <option [value]="d.departmentId">{{ d.name }}</option>
            }
          </select>
          <select class="chip" [ngModel]="locationId()" (ngModelChange)="onFilterChange('location', $event)"
            aria-label="Filter by location" data-test="filter-location">
            <option value="">All locations</option>
            @for (l of locations(); track l.locationId) {
              <option [value]="l.locationId">{{ l.name }}</option>
            }
          </select>
          <select class="chip" [ngModel]="shiftId()" (ngModelChange)="onFilterChange('shift', $event)"
            aria-label="Filter by shift" data-test="filter-shift">
            <option value="">All shifts</option>
            @for (s of shifts(); track s.id) {
              <option [value]="s.id">{{ s.name }}</option>
            }
          </select>
          @if (hasFilters()) {
            <button type="button" class="text-xs text-neutral-500 hover:text-neutral-700 underline"
              (click)="clearFilters()" data-test="clear-filters">Clear</button>
          }
        </div>
        <div class="flex items-center gap-2">
          <span class="text-xs text-neutral-400 mr-1">Export</span>
          <button type="button" class="btn-secondary text-sm" (click)="exportAs('csv')"
            [disabled]="rows().length === 0 || isExporting()" data-test="export-csv">CSV</button>
          <button type="button" class="btn-secondary text-sm" (click)="exportAs('xlsx')"
            [disabled]="rows().length === 0 || isExporting()" data-test="export-xlsx">Excel</button>
          <button type="button" class="btn-secondary text-sm" (click)="exportAs('pdf')"
            [disabled]="rows().length === 0 || isExporting()" data-test="export-pdf">PDF</button>
        </div>
      </div>

      <!-- Banner -->
      @if (banner(); as b) {
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-3 mb-5" data-test="banner" @fadeIn>
          <div class="stat-card">
            <span class="stat-label">Total employees</span>
            <span class="stat-value">{{ b.totalEmployees }}</span>
          </div>
          <div class="stat-card">
            <span class="stat-label">Avg attendance</span>
            <span class="stat-value text-green-700">{{ b.averageAttendancePercent }}%</span>
          </div>
          <div class="stat-card">
            <span class="stat-label">Total LOP days</span>
            <span class="stat-value text-red-700">{{ b.totalLopDays }}</span>
          </div>
        </div>
      }

      @if (isLoading()) {
        <div class="card-notion space-y-3" aria-busy="true" data-test="skeleton">
          @for (_ of [1,2,3,4,5]; track $index) {
            <div class="skeleton-line h-10 w-full"></div>
          }
        </div>
      } @else if (notGenerated()) {
        <!-- AC-3: summary not yet generated -->
        <div class="card-notion text-center py-16" @fadeIn data-test="not-generated">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">Summary not generated yet</h3>
          <p class="text-sm text-neutral-500 mb-5">
            The attendance summary for {{ month() }} hasn't been computed.
          </p>
          @if (isGenerating()) {
            <div class="inline-flex items-center gap-2 text-sm text-neutral-600" data-test="generating">
              <span class="spinner"></span>
              <span>Generating… ({{ generationStatus() }})</span>
            </div>
          } @else {
            <button type="button" class="btn-primary" (click)="generate()" data-test="generate">
              Generate summary
            </button>
          }
        </div>
      } @else if (rows().length === 0) {
        <div class="card-notion text-center py-16" @fadeIn data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No employees match</h3>
          <p class="text-sm text-neutral-500">No attendance summary rows for {{ month() }} with the current filters.</p>
        </div>
      } @else {
        <!-- Desktop table -->
        <div class="card-notion !p-0 overflow-x-auto hidden md:block" @fadeIn>
          <table class="w-full text-sm" aria-label="Monthly attendance summary">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th"><button type="button" class="th-btn" (click)="sortBy('employeeName')"
                  data-test="sort-employeeName">Employee {{ ind('employeeName') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('presentDays')">Present {{ ind('presentDays') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('absentDays')">Absent {{ ind('absentDays') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('lateCount')">Late {{ ind('lateCount') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('earlyDepartureCount')">Early out {{ ind('earlyDepartureCount') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('overtimeMinutes')">Overtime {{ ind('overtimeMinutes') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('leaveDays')">Leave {{ ind('leaveDays') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('workMinutes')">Work hrs {{ ind('workMinutes') }}</button></th>
                <th class="th text-right">Attendance</th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.employeeId) {
                <tr class="row cursor-pointer" data-test="summary-row"
                  (click)="openDrillDown(row)" tabindex="0"
                  (keydown.enter)="openDrillDown(row)">
                  <td class="td font-medium text-neutral-900">
                    {{ row.employeeName }}
                    @if (row.departmentName) {
                      <span class="block text-xs text-neutral-400">{{ row.departmentName }}</span>
                    }
                  </td>
                  <td class="td text-right text-green-700">{{ row.presentDays }}</td>
                  <td class="td text-right" [ngClass]="absentClass(row)" data-test="absent-cell">{{ row.absentDays }}</td>
                  <td class="td text-right" [ngClass]="lateClass(row)" data-test="late-cell">{{ row.lateCount }}</td>
                  <td class="td text-right text-neutral-600">{{ row.earlyDepartureCount }}</td>
                  <td class="td text-right text-neutral-700">{{ minutes(row.overtimeMinutes) }}</td>
                  <td class="td text-right text-blue-700">{{ row.leaveDays }}</td>
                  <td class="td text-right text-neutral-700">{{ minutes(row.workMinutes) }}</td>
                  <td class="td">
                    <div class="flex items-center justify-end gap-2">
                      <div class="bar-track" aria-hidden="true">
                        <div class="bar-fill" [class.bar-full]="percent(row) === 100"
                          [style.width.%]="percent(row)"></div>
                      </div>
                      <span class="text-xs tabular-nums w-9 text-right"
                        [class.text-green-700]="percent(row) === 100">{{ percent(row) }}%</span>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards -->
        <div class="space-y-3 md:hidden" @fadeIn>
          @for (row of rows(); track row.employeeId) {
            <button type="button" class="mobile-card w-full text-left" (click)="openDrillDown(row)"
              data-test="summary-card">
              <div class="flex items-center justify-between mb-2">
                <span class="font-medium text-neutral-900">{{ row.employeeName }}</span>
                <span class="text-xs tabular-nums" [class.text-green-700]="percent(row) === 100">{{ percent(row) }}%</span>
              </div>
              <div class="grid grid-cols-3 gap-y-1 text-xs text-neutral-500">
                <span>Present <b class="text-green-700">{{ row.presentDays }}</b></span>
                <span>Absent <b [ngClass]="absentClass(row)">{{ row.absentDays }}</b></span>
                <span>Late <b [ngClass]="lateClass(row)">{{ row.lateCount }}</b></span>
                <span>OT <b class="text-neutral-700">{{ minutes(row.overtimeMinutes) }}</b></span>
                <span>Leave <b class="text-blue-700">{{ row.leaveDays }}</b></span>
                <span>Work <b class="text-neutral-700">{{ minutes(row.workMinutes) }}</b></span>
              </div>
            </button>
          }
        </div>
      }
    </div>

    <!-- AC-2: drill-down drawer -->
    @if (drillEmployeeId(); as eid) {
      <div class="fixed inset-0 z-40 bg-neutral-900/30" (click)="closeDrillDown()" data-test="drawer-backdrop"></div>
      <div class="fixed inset-y-0 right-0 z-50 w-full max-w-lg bg-white shadow-xl overflow-y-auto"
        @drawer role="dialog" aria-label="Daily attendance breakdown" data-test="drill-drawer">
        <div class="p-5 border-b border-neutral-100 flex items-center justify-between sticky top-0 bg-white">
          <div>
            <h2 class="text-lg font-semibold text-neutral-900">{{ drillName() }}</h2>
            <p class="text-xs text-neutral-500">{{ month() }} daily breakdown</p>
          </div>
          <button type="button" class="arrow-btn" (click)="closeDrillDown()"
            aria-label="Close" data-test="drawer-close">✕</button>
        </div>
        <div class="p-5">
          @if (drillLoading()) {
            <div class="space-y-2" aria-busy="true" data-test="drill-skeleton">
              @for (_ of [1,2,3,4,5,6]; track $index) {
                <div class="skeleton-line h-9 w-full"></div>
              }
            </div>
          } @else if (drillDays().length === 0) {
            <p class="text-sm text-neutral-500 text-center py-8" data-test="drill-empty">No daily records.</p>
          } @else {
            <div class="grid grid-cols-1 gap-1.5" data-test="drill-grid">
              @for (day of drillDays(); track day.date) {
                <div class="day-row" data-test="day-row">
                  <span class="text-sm text-neutral-700 w-28">{{ day.date }}</span>
                  <span class="status-pill ring-1" [ngClass]="statusClass(day.status)">{{ statusLabel(day.status) }}</span>
                  <span class="text-xs text-neutral-500 flex-1 text-right">
                    @if (day.clockIn || day.clockOut) {
                      {{ time(day.clockIn) }} – {{ time(day.clockOut) }}
                    }
                    @if (day.isLate) { <span class="text-amber-600 ml-1">late</span> }
                    @if (day.isEarlyDeparture) { <span class="text-amber-600 ml-1">early out</span> }
                    @if (day.isRegularized) { <span class="text-blue-600 ml-1">regularized</span> }
                  </span>
                </div>
              }
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-6xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider whitespace-nowrap; }
    .th-btn { @apply inline-flex items-center gap-1 hover:text-neutral-600 transition-colors; }
    .td { @apply py-3 px-4 align-middle whitespace-nowrap; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60 outline-none focus-visible:bg-brand-50/40; }
    .field {
      @apply block rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .chip {
      @apply rounded-full border border-neutral-200 bg-white px-3 py-1.5 text-sm text-neutral-700
        transition-colors hover:bg-neutral-50 focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .arrow-btn {
      @apply inline-flex items-center justify-center h-9 w-9 rounded-lg border border-neutral-200 bg-white
        text-neutral-600 transition-colors hover:bg-neutral-50;
    }
    .stat-card { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-4 flex flex-col gap-1; }
    .stat-label { @apply text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .stat-value { @apply text-2xl font-semibold text-neutral-900 tabular-nums; }
    .mobile-card { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-4 transition-colors hover:bg-neutral-50/60; }
    .bar-track { @apply h-1.5 w-20 rounded-full bg-neutral-100 overflow-hidden; }
    .bar-fill { @apply h-full rounded-full bg-neutral-400 transition-all duration-300; }
    .bar-full { @apply bg-green-500; }
    .day-row { @apply flex items-center gap-3 py-2 px-3 rounded-lg hover:bg-neutral-50; }
    .status-pill { @apply inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium; }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
    .spinner {
      @apply inline-block h-4 w-4 rounded-full border-2 border-neutral-300 border-t-brand-500;
      animation: spin 0.8s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5 text-sm font-medium
        text-white transition-all duration-200 hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-3.5 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class MonthlySummaryComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly departmentService = inject(DepartmentService);
  private readonly locationService = inject(LocationService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // ─── State ──────────────────────────────────────────────────
  readonly month = signal(currentMonthIso());
  readonly isLoading = signal(true);
  readonly result = signal<IMonthlySummaryResult | null>(null);

  readonly departmentId = signal('');
  readonly locationId = signal('');
  readonly shiftId = signal('');

  readonly departments = signal<IDepartment[]>([]);
  readonly locations = signal<ILocation[]>([]);
  readonly shifts = signal<IShift[]>([]);

  readonly sortKey = signal<SortKey>('absentDays');
  readonly sortDir = signal<SortDir>('desc');

  // AC-3 generation state
  readonly isGenerating = signal(false);
  readonly generationStatus = signal<'PENDING' | 'RUNNING' | 'COMPLETED' | ''>('');

  // AC-4 export state
  readonly isExporting = signal(false);

  // AC-2 drill-down state
  readonly drillEmployeeId = signal<string | null>(null);
  readonly drillName = signal('');
  readonly drillLoading = signal(false);
  readonly drillResult = signal<IEmployeeDailyBreakdownResult | null>(null);

  // ─── Computed ───────────────────────────────────────────────
  readonly banner = computed<IMonthlySummaryBanner | null>(
    () => this.result()?.banner ?? null,
  );

  /** AC-3: summary has not been generated for the month (loaded, generatedAt null). */
  readonly notGenerated = computed(
    () => this.result() != null && this.result()!.generatedAt == null,
  );

  readonly hasFilters = computed(
    () => !!this.departmentId() || !!this.locationId() || !!this.shiftId(),
  );

  readonly drillDays = computed(() => this.drillResult()?.days ?? []);

  readonly rows = computed<IEmployeeMonthlySummary[]>(() => {
    const items = this.result()?.rows ?? [];
    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...items].sort((a, b) => {
      const av = a[key];
      const bv = b[key];
      if (typeof av === 'string' && typeof bv === 'string') {
        return av.localeCompare(bv) * dir;
      }
      return (((av as number) ?? 0) - ((bv as number) ?? 0)) * dir;
    });
  });

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.loadFilters();
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ───────────────────────────────────────────
  private buildQuery(): IMonthlySummaryQuery {
    const q: IMonthlySummaryQuery = { month: this.month() };
    if (this.departmentId()) {
      q.departmentId = this.departmentId();
    }
    if (this.locationId()) {
      q.locationId = this.locationId();
    }
    if (this.shiftId()) {
      q.shiftId = this.shiftId();
    }
    return q;
  }

  private loadFilters(): void {
    this.departmentService
      .getDepartments()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (d) => this.departments.set(d),
        error: () => this.departments.set([]),
      });
    this.locationService
      .getLocations(true)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (l) => this.locations.set(l),
        error: () => this.locations.set([]),
      });
    this.attendanceService
      .getShifts()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (s) => this.shifts.set(s),
        error: () => this.shifts.set([]),
      });
  }

  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getMonthlySummary(this.buildQuery())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.result.set(res);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.result.set(null);
          this.toastr.error('Failed to load the attendance summary.');
        },
      });
  }

  // ─── Month navigation ───────────────────────────────────────
  onMonthChange(value: string): void {
    if (!value) {
      return;
    }
    this.month.set(value);
    this.load();
  }

  /** Step the selected month by `delta` months (left/right arrows, §8). */
  stepMonth(delta: number): void {
    const [y, m] = this.month().split('-').map(Number);
    const d = new Date(y, m - 1 + delta, 1);
    this.month.set(
      `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}`,
    );
    this.load();
  }

  // ─── Filters (AC-5) ─────────────────────────────────────────
  onFilterChange(kind: 'department' | 'location' | 'shift', value: string): void {
    if (kind === 'department') {
      this.departmentId.set(value);
    } else if (kind === 'location') {
      this.locationId.set(value);
    } else {
      this.shiftId.set(value);
    }
    this.load();
  }

  clearFilters(): void {
    this.departmentId.set('');
    this.locationId.set('');
    this.shiftId.set('');
    this.load();
  }

  // ─── Sorting ────────────────────────────────────────────────
  sortBy(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDir.set(key === 'employeeName' ? 'asc' : 'desc');
    }
  }

  ind(key: SortKey): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.sortDir() === 'asc' ? '↑' : '↓';
  }

  // ─── Cell formatting (§8) ───────────────────────────────────
  minutes(m: number): string {
    return formatWorkMinutes(m);
  }

  percent(row: IEmployeeMonthlySummary): number {
    return attendancePercent(row);
  }

  absentClass(row: IEmployeeMonthlySummary): string {
    return summaryCellClass(row.absentDays, HIGH_ABSENT_THRESHOLD, 'absent');
  }

  lateClass(row: IEmployeeMonthlySummary): string {
    return summaryCellClass(row.lateCount, HIGH_LATE_THRESHOLD, 'late');
  }

  // ─── On-demand generation (AC-3) ────────────────────────────
  generate(): void {
    if (this.isGenerating()) {
      return;
    }
    this.isGenerating.set(true);
    this.generationStatus.set('PENDING');
    const month = this.month();

    // Kick off the job, then poll the same endpoint every 2s until COMPLETED.
    this.attendanceService
      .generateMonthlySummary(month)
      .pipe(
        switchMap((first) => {
          this.generationStatus.set(first.status);
          if (first.status === 'COMPLETED') {
            // Already done — short-circuit the poll.
            return timer(0).pipe(switchMap(() => [first]));
          }
          return timer(2000, 2000).pipe(
            switchMap(() => this.attendanceService.generateMonthlySummary(month)),
          );
        }),
        takeWhile((s) => s.status !== 'COMPLETED', true),
        takeUntil(this.destroy$),
        finalize(() => this.isGenerating.set(false)),
      )
      .subscribe({
        next: (status) => {
          this.generationStatus.set(status.status);
          if (status.status === 'COMPLETED') {
            this.toastr.success('Summary generated.');
            this.load();
          }
        },
        error: () => {
          this.isGenerating.set(false);
          this.toastr.error('Failed to generate the summary.');
        },
      });
  }

  // ─── Export (AC-4) ──────────────────────────────────────────
  exportAs(format: SummaryExportFormat): void {
    if (this.rows().length === 0 || this.isExporting()) {
      return;
    }
    this.isExporting.set(true);
    this.attendanceService
      .exportMonthlySummary(this.buildQuery(), format)
      .pipe(takeUntil(this.destroy$), finalize(() => this.isExporting.set(false)))
      .subscribe({
        next: (resp) => {
          const blob = resp.body;
          if (!blob) {
            this.toastr.error('Export returned no data.');
            return;
          }
          const filename =
            filenameFromDisposition(resp.headers.get('Content-Disposition')) ??
            `attendance-summary-${this.month()}.${format}`;
          downloadBlob(blob, filename);
          this.toastr.success('Summary exported.');
        },
        error: () => this.toastr.error('Failed to export the summary.'),
      });
  }

  // ─── Drill-down (AC-2) ──────────────────────────────────────
  openDrillDown(row: IEmployeeMonthlySummary): void {
    this.drillEmployeeId.set(row.employeeId);
    this.drillName.set(row.employeeName);
    this.drillResult.set(null);
    this.drillLoading.set(true);
    this.attendanceService
      .getEmployeeDailyBreakdown(row.employeeId, this.month())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.drillResult.set(res);
          this.drillLoading.set(false);
        },
        error: () => {
          this.drillLoading.set(false);
          this.toastr.error('Failed to load the daily breakdown.');
        },
      });
  }

  closeDrillDown(): void {
    this.drillEmployeeId.set(null);
    this.drillResult.set(null);
  }

  statusClass(status: IEmployeeDailyBreakdownResult['days'][number]['status']): string {
    return DAILY_STATUS_CLASSES[status];
  }

  statusLabel(status: IEmployeeDailyBreakdownResult['days'][number]['status']): string {
    return dailyStatusLabel(status);
  }

  /** Format a UTC timestamptz to local HH:mm; em-dash for null. */
  time(iso: string | null | undefined): string {
    if (!iso) {
      return '—';
    }
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) {
      return '—';
    }
    return `${d.getHours().toString().padStart(2, '0')}:${d
      .getMinutes()
      .toString()
      .padStart(2, '0')}`;
  }
}

/** Current month as `yyyy-MM` in the browser's local timezone. */
function currentMonthIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  return `${y}-${m}`;
}

/** Parse a filename from a Content-Disposition header, if present. */
function filenameFromDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf8?.[1]) {
    return decodeURIComponent(utf8[1]);
  }
  const quoted = /filename="?([^";]+)"?/i.exec(header);
  return quoted?.[1] ?? null;
}

/** Trigger a browser download for a blob with the given filename. */
function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
