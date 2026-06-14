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
import { AttendanceService } from '../../services/attendance.service';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  ILateEarlyRow,
  ILateEarlyReportResult,
  LateEarlyScope,
  formatLateBadgeMinutes,
  todayLocalIso,
} from '../../models/attendance.models';

/** Sortable late/early report columns (AC-5, §8). */
type SortKey =
  | 'employeeName'
  | 'lateCount'
  | 'totalLateMinutes'
  | 'earlyDepartureCount'
  | 'totalEarlyMinutes';
type SortDir = 'asc' | 'desc';

/**
 * US-ATT-008 (AC-5, FR-6): Late arrival & early-departure report.
 *
 * A Notion-style table of per-employee late and early-departure counts/minutes for a
 * date range, with department + employee filters and a scope tab (My Team vs All).
 * Chronic-lateness rows are highlighted amber (FR-7). HR sees the All-scope tab;
 * managers are restricted to their team. Mirrors the overtime-report component.
 */
@Component({
  selector: 'app-late-early-report',
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
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Late &amp; Early Departure</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Late arrivals and early departures by employee for the selected range.
          </p>
        </div>
        <button type="button" class="btn-secondary text-sm self-start" (click)="exportCsv()"
          [disabled]="rows().length === 0" data-test="export-csv">Export CSV</button>
      </div>

      <!-- Scope tabs (My Team vs All) — All is HR-only. -->
      @if (canSeeAll()) {
        <div class="scope-tabs mb-4" role="tablist" aria-label="Report scope">
          <button type="button" role="tab" class="scope-tab" [class.scope-tab-active]="scope() === 'team'"
            [attr.aria-selected]="scope() === 'team'" (click)="setScope('team')" data-test="scope-team">My Team</button>
          <button type="button" role="tab" class="scope-tab" [class.scope-tab-active]="scope() === 'all'"
            [attr.aria-selected]="scope() === 'all'" (click)="setScope('all')" data-test="scope-all">All Employees</button>
        </div>
      }

      <!-- Filters -->
      <div class="card-notion mb-4 grid grid-cols-1 sm:grid-cols-4 gap-3">
        <div>
          <label class="filter-label" for="from">From</label>
          <input id="from" type="date" class="field" [ngModel]="from()" [max]="to()"
            (ngModelChange)="onFromChange($event)" data-test="from" />
        </div>
        <div>
          <label class="filter-label" for="to">To</label>
          <input id="to" type="date" class="field" [ngModel]="to()" [min]="from()" [max]="maxDate"
            (ngModelChange)="onToChange($event)" data-test="to" />
        </div>
        <div>
          <label class="filter-label" for="dept">Department</label>
          <input id="dept" type="text" class="field" placeholder="Department ID"
            [ngModel]="departmentId()" (ngModelChange)="onDeptChange($event)" data-test="department" />
        </div>
        <div>
          <label class="filter-label" for="emp">Employee</label>
          <input id="emp" type="text" class="field" placeholder="Employee ID"
            [ngModel]="employeeId()" (ngModelChange)="onEmployeeChange($event)" data-test="employee" />
        </div>
      </div>

      @if (isLoading()) {
        <div class="card-notion space-y-3" aria-busy="true" data-test="skeleton">
          @for (_ of [1,2,3,4]; track $index) {
            <div class="skeleton-line h-10 w-full"></div>
          }
        </div>
      } @else if (rows().length === 0) {
        <div class="card-notion text-center py-16" @fadeIn data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No late or early records</h3>
          <p class="text-sm text-neutral-500">No late arrivals or early departures in this range.</p>
        </div>
      } @else {
        <div class="card-notion !p-0 overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="Late and early departure report">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th"><button type="button" class="th-btn" (click)="sortBy('employeeName')"
                  data-test="sort-employeeName">Employee {{ sortIndicator('employeeName') }}</button></th>
                <th class="th">Department</th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('lateCount')"
                  data-test="sort-lateCount">Late {{ sortIndicator('lateCount') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('totalLateMinutes')"
                  data-test="sort-totalLateMinutes">Late time {{ sortIndicator('totalLateMinutes') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('earlyDepartureCount')"
                  data-test="sort-earlyDepartureCount">Early {{ sortIndicator('earlyDepartureCount') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('totalEarlyMinutes')"
                  data-test="sort-totalEarlyMinutes">Early time {{ sortIndicator('totalEarlyMinutes') }}</button></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.employeeId) {
                <tr class="row" [class.row-chronic]="row.isChronic" data-test="report-row">
                  <td class="td font-medium text-neutral-900">
                    {{ row.employeeName }}
                    @if (row.isChronic) {
                      <span class="chronic-badge" data-test="chronic-badge">Chronic</span>
                    }
                  </td>
                  <td class="td text-neutral-600">{{ row.departmentName || '—' }}</td>
                  <td class="td text-right" [class.text-amber-700]="row.lateCount > 0">{{ row.lateCount }}</td>
                  <td class="td text-right text-neutral-600">{{ minutes(row.totalLateMinutes) }}</td>
                  <td class="td text-right" [class.text-red-700]="row.earlyDepartureCount > 0">{{ row.earlyDepartureCount }}</td>
                  <td class="td text-right text-neutral-600">{{ minutes(row.totalEarlyMinutes) }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-5xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-4 sm:p-5; }
    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .th-btn { @apply inline-flex items-center gap-1 hover:text-neutral-600 transition-colors; }
    .td { @apply py-3 px-4 align-middle; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60; }
    .row-chronic { @apply bg-amber-50/70 hover:bg-amber-50; }
    .chronic-badge {
      @apply ml-2 inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-[10px]
        font-semibold uppercase tracking-wide text-amber-700;
    }
    .filter-label { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .field {
      @apply block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .scope-tabs { @apply inline-flex rounded-lg bg-neutral-100 p-1; }
    .scope-tab {
      @apply rounded-md px-4 py-1.5 text-sm font-medium text-neutral-500 transition-colors;
    }
    .scope-tab-active { @apply bg-white text-neutral-900 shadow-sm; }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class LateEarlyReportComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly authService = inject(AuthService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** HR roles may switch to the all-employee scope; managers are team-only (FR-6). */
  private static readonly HR_ROLES = ['HR Officer', 'HR Manager', 'Tenant Admin'];

  readonly maxDate = todayLocalIso();

  // ─── State ──────────────────────────────────────────────────
  readonly from = signal(firstOfMonthIso());
  readonly to = signal(todayLocalIso());
  readonly departmentId = signal('');
  readonly employeeId = signal('');
  readonly scope = signal<LateEarlyScope>('team');
  readonly isLoading = signal(true);
  readonly result = signal<ILateEarlyReportResult | null>(null);
  readonly sortKey = signal<SortKey>('lateCount');
  readonly sortDir = signal<SortDir>('desc');

  // ─── Computed ───────────────────────────────────────────────

  /** FR-6: only HR/Tenant Admin see the scope tabs and the All-employees option. */
  readonly canSeeAll = computed(() =>
    this.authService
      .roles()
      .some((r) => LateEarlyReportComponent.HR_ROLES.includes(r)),
  );

  readonly rows = computed<ILateEarlyRow[]>(() => {
    const items = this.result()?.rows ?? [];
    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...items].sort((a, b) => {
      const av = a[key];
      const bv = b[key];
      if (typeof av === 'string' && typeof bv === 'string') {
        return av.localeCompare(bv) * dir;
      }
      return ((av as number) - (bv as number)) * dir;
    });
  });

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    // Default HR to the team scope but allow All; managers stay on team.
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getLateEarlyReport({
        from: this.from(),
        to: this.to(),
        departmentId: this.departmentId() || undefined,
        employeeId: this.employeeId() || undefined,
        scope: this.scope(),
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.result.set(res);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.result.set(null);
          this.toastr.error('Failed to load the late/early report.');
        },
      });
  }

  setScope(scope: LateEarlyScope): void {
    if (this.scope() === scope) {
      return;
    }
    // Guard: only HR may switch to the all-employee scope (backend also enforces).
    if (scope === 'all' && !this.canSeeAll()) {
      return;
    }
    this.scope.set(scope);
    this.load();
  }

  onFromChange(value: string): void {
    if (!value) {
      return;
    }
    this.from.set(value);
    this.load();
  }

  onToChange(value: string): void {
    if (!value) {
      return;
    }
    this.to.set(value);
    this.load();
  }

  onDeptChange(value: string): void {
    this.departmentId.set(value ?? '');
    this.load();
  }

  onEmployeeChange(value: string): void {
    this.employeeId.set(value ?? '');
    this.load();
  }

  sortBy(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDir.set(key === 'employeeName' ? 'asc' : 'desc');
    }
  }

  sortIndicator(key: SortKey): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.sortDir() === 'asc' ? '↑' : '↓';
  }

  minutes(m: number): string {
    return formatLateBadgeMinutes(m);
  }

  /** AC-5 (§8): export the currently sorted rows to a CSV download, client-side. */
  exportCsv(): void {
    const rows = this.rows();
    if (rows.length === 0) {
      return;
    }
    const header = [
      'Employee',
      'Department',
      'Late Count',
      'Total Late Minutes',
      'Early Departure Count',
      'Total Early Minutes',
      'Chronic',
    ];
    const lines = rows.map((r) =>
      [
        csvCell(r.employeeName),
        csvCell(r.departmentName ?? ''),
        r.lateCount,
        r.totalLateMinutes,
        r.earlyDepartureCount,
        r.totalEarlyMinutes,
        r.isChronic ? 'Yes' : 'No',
      ].join(','),
    );
    const csv = [header.join(','), ...lines].join('\r\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `late-early-report-${this.from()}_to_${this.to()}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    this.toastr.success('Report exported.');
  }
}

/** First day of the current month as `yyyy-MM-dd` in local time (default range start). */
function firstOfMonthIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  return `${y}-${m}-01`;
}

/** Quote a CSV cell when it contains a comma, quote or newline. */
function csvCell(value: string): string {
  const v = value ?? '';
  if (/[",\r\n]/.test(v)) {
    return `"${v.replace(/"/g, '""')}"`;
  }
  return v;
}
