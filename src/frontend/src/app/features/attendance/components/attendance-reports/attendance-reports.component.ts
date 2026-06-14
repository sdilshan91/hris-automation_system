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
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { AttendanceService } from '../../services/attendance.service';
import {
  IDeptComparisonRow,
  ICustomReportFilters,
  ICustomReportRow,
  CustomReportExportFormat,
  ITrendPoint,
  ITrendsResult,
  IScheduledReportConfig,
  ScheduledReportFrequency,
  ScheduledReportFormat,
  attendanceRateColor,
  buildTrendPoints,
  trendPointsToString,
  trendMax,
  formatWorkMinutes,
  todayIso,
} from '../../models/attendance.models';

type ReportTab = 'departments' | 'custom' | 'trends' | 'scheduled';

interface ITrendSeriesView {
  key: keyof ITrendsResult;
  title: string;
  color: string;
  suffix: string;
}

const TREND_SERIES: ITrendSeriesView[] = [
  { key: 'attendanceRate', title: 'Attendance rate', color: '#6366f1', suffix: '%' },
  { key: 'lateArrivals', title: 'Avg late arrivals', color: '#f59e0b', suffix: '' },
  { key: 'overtimeHours', title: 'Overtime hours', color: '#14b8a6', suffix: 'h' },
  { key: 'absenteeismRate', title: 'Absenteeism rate', color: '#f43f5e', suffix: '%' },
];

const FREQUENCY_OPTIONS: ScheduledReportFrequency[] = ['DAILY', 'WEEKLY', 'MONTHLY'];
const FORMAT_OPTIONS: ScheduledReportFormat[] = ['CSV', 'XLSX', 'PDF'];
const REPORT_TYPE_OPTIONS = [
  { value: 'daily-attendance', label: 'Daily Attendance' },
  { value: 'weekly-summary', label: 'Weekly Summary' },
  { value: 'monthly-summary', label: 'Monthly Summary' },
  { value: 'department-comparison', label: 'Departmental Comparison' },
  { value: 'late-arrival', label: 'Late Arrival' },
  { value: 'overtime', label: 'Overtime' },
  { value: 'absenteeism', label: 'Absenteeism' },
];

/** Geometry of one rendered trend line for the SVG template. */
interface ITrendChartView {
  key: string;
  title: string;
  color: string;
  suffix: string;
  points: string;
  categories: string[];
  values: number[];
  max: number;
}

/**
 * US-ATT-010 (AC-3/AC-4/AC-5, FR-8, §8): the HR attendance reports hub.
 *
 * A single Notion-style page with four sections (tabs):
 *  - Departments (AC-3): horizontal CSS bars color-coded by attendance rate
 *    (green > 90%, amber 80–90%, red < 80%), driven by `attendanceRateColor`.
 *  - Custom (AC-4): a filter palette (date range + department/location/shift/status)
 *    → Generate → table; an export dropdown (CSV / Excel / PDF) downloading the
 *    backend-generated blob (xlsx/pdf can't be built client-side).
 *  - Trends (AC-5): four simple SVG line charts over 12 months with hover tooltips,
 *    built with the pure `buildTrendPoints` helper — NO charting library.
 *  - Scheduled (FR-8): a form (report type, frequency, filters, recipients, delivery
 *    time, format, active) backed by the scheduled CRUD, plus the existing-config list.
 */
@Component({
  selector: 'app-attendance-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="mb-6">
        <a routerLink="/attendance/dashboard" class="text-xs text-neutral-400 hover:text-neutral-700 transition-colors">
          ← Dashboard
        </a>
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight mt-1">Attendance Reports</h1>
        <p class="text-sm text-neutral-500 mt-1">
          Departmental comparison, custom date-range reports, trends and scheduled delivery.
        </p>
      </div>

      <!-- Tabs -->
      <div class="tabs mb-6" role="tablist">
        @for (t of tabs; track t.key) {
          <button type="button" class="tab" role="tab" [class.tab-active]="tab() === t.key"
            [attr.aria-selected]="tab() === t.key" (click)="selectTab(t.key)"
            [attr.data-test]="'tab-' + t.key">{{ t.label }}</button>
        }
      </div>

      <!-- ═══ Departments (AC-3) ════════════════════════════════ -->
      @if (tab() === 'departments') {
        <div @fadeIn data-test="departments-panel">
          <div class="flex items-center gap-2 mb-4">
            <label class="sr-only" for="dept-month">Month</label>
            <input id="dept-month" type="month" class="field" [ngModel]="deptMonth()"
              (ngModelChange)="onDeptMonthChange($event)" data-test="dept-month" />
          </div>
          @if (isLoadingDept()) {
            <div class="card-notion space-y-4" aria-busy="true" data-test="dept-skeleton">
              @for (_ of [1,2,3,4]; track $index) { <div class="skeleton-line h-8 w-full"></div> }
            </div>
          } @else if (deptRows().length === 0) {
            <div class="card-notion text-center py-12" data-test="dept-empty">
              <p class="text-sm text-neutral-500">No department data for {{ deptMonth() }}.</p>
            </div>
          } @else {
            <div class="card-notion space-y-4" data-test="dept-bars">
              @for (row of deptRows(); track row.departmentId) {
                <div data-test="dept-bar">
                  <div class="flex items-center justify-between text-sm mb-1">
                    <span class="font-medium text-neutral-800">{{ row.departmentName }}</span>
                    <span class="text-neutral-500">
                      {{ row.attendanceRatePct | number:'1.0-1' }}% · {{ row.employeeCount }} ppl
                    </span>
                  </div>
                  <div class="bar-track">
                    <div class="bar-fill" data-test="dept-bar-fill"
                      [style.width.%]="clamp(row.attendanceRatePct)"
                      [style.background-color]="rateColor(row.attendanceRatePct)"></div>
                  </div>
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- ═══ Custom report (AC-4) ══════════════════════════════ -->
      @if (tab() === 'custom') {
        <div class="lg:grid lg:grid-cols-[16rem_1fr] lg:gap-6" @fadeIn data-test="custom-panel">
          <!-- Filter palette -->
          <aside class="card-notion mb-4 lg:mb-0 lg:sticky lg:top-4 self-start space-y-3" aria-label="Report filters">
            <h2 class="text-sm font-semibold text-neutral-900 mb-1">Filters</h2>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="label-sm" for="c-from">From</label>
                <input id="c-from" type="date" class="field-sm" [ngModel]="filters().from"
                  (ngModelChange)="patchFilter('from', $event)" data-test="custom-from" />
              </div>
              <div>
                <label class="label-sm" for="c-to">To</label>
                <input id="c-to" type="date" class="field-sm" [ngModel]="filters().to"
                  (ngModelChange)="patchFilter('to', $event)" data-test="custom-to" />
              </div>
            </div>
            <div>
              <label class="label-sm" for="c-dept">Department</label>
              <input id="c-dept" type="text" class="field-sm" placeholder="Department ID (optional)"
                [ngModel]="filters().departmentId" (ngModelChange)="patchFilter('departmentId', $event)"
                data-test="custom-dept" />
            </div>
            <div>
              <label class="label-sm" for="c-loc">Location</label>
              <input id="c-loc" type="text" class="field-sm" placeholder="Location ID (optional)"
                [ngModel]="filters().locationId" (ngModelChange)="patchFilter('locationId', $event)" />
            </div>
            <div>
              <label class="label-sm" for="c-shift">Shift</label>
              <input id="c-shift" type="text" class="field-sm" placeholder="Shift ID (optional)"
                [ngModel]="filters().shiftId" (ngModelChange)="patchFilter('shiftId', $event)" />
            </div>
            <div>
              <label class="label-sm" for="c-status">Status</label>
              <input id="c-status" type="text" class="field-sm" placeholder="e.g. Active (optional)"
                [ngModel]="filters().status" (ngModelChange)="patchFilter('status', $event)" />
            </div>
            <button type="button" class="btn-primary w-full" (click)="generate()"
              [disabled]="isGenerating()" data-test="generate-btn">
              @if (isGenerating()) { Generating… } @else { Generate }
            </button>
          </aside>

          <!-- Results + export -->
          <section class="min-w-0">
            <div class="flex items-center justify-end mb-3">
              <div class="relative">
                <button type="button" class="btn-secondary text-sm" (click)="toggleExportMenu()"
                  [disabled]="customRows().length === 0 || isExporting()"
                  [attr.aria-expanded]="exportMenuOpen()" data-test="export-btn">
                  @if (isExporting()) { Exporting… } @else { Export ▾ }
                </button>
                @if (exportMenuOpen()) {
                  <div class="export-menu" role="menu" data-test="export-menu">
                    <button type="button" role="menuitem" class="export-item" (click)="exportAs('csv')"
                      data-test="export-csv">CSV (.csv)</button>
                    <button type="button" role="menuitem" class="export-item" (click)="exportAs('xlsx')"
                      data-test="export-xlsx">Excel (.xlsx)</button>
                    <button type="button" role="menuitem" class="export-item" (click)="exportAs('pdf')"
                      data-test="export-pdf">PDF (.pdf)</button>
                  </div>
                }
              </div>
            </div>

            @if (isGenerating()) {
              <div class="card-notion space-y-3" aria-busy="true" data-test="custom-skeleton">
                @for (_ of [1,2,3,4,5]; track $index) { <div class="skeleton-line h-10 w-full"></div> }
              </div>
            } @else if (customRows().length === 0) {
              <div class="card-notion text-center py-12" data-test="custom-empty">
                <p class="text-sm text-neutral-500">Set a date range and click Generate.</p>
              </div>
            } @else {
              <div class="card-notion !p-0 overflow-x-auto" data-test="custom-table">
                <table class="w-full text-sm" aria-label="Custom attendance report">
                  <thead>
                    <tr class="border-b border-neutral-100">
                      <th class="th">Employee</th>
                      <th class="th text-right">Present</th>
                      <th class="th text-right">Absent</th>
                      <th class="th text-right">Late</th>
                      <th class="th text-right">Overtime</th>
                      <th class="th text-right">Worked</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of customRows(); track row.employeeId) {
                      <tr class="row" data-test="custom-row">
                        <td class="td font-medium text-neutral-900">{{ row.employeeName }}</td>
                        <td class="td text-right text-emerald-700">{{ row.presentDays }}</td>
                        <td class="td text-right text-rose-700">{{ row.absentDays }}</td>
                        <td class="td text-right text-amber-700">{{ row.lateCount }}</td>
                        <td class="td text-right text-neutral-700">{{ mins(row.overtimeMinutes) }}</td>
                        <td class="td text-right text-neutral-700">{{ mins(row.workMinutes) }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>
        </div>
      }

      <!-- ═══ Trends (AC-5) ═════════════════════════════════════ -->
      @if (tab() === 'trends') {
        <div @fadeIn data-test="trends-panel">
          @if (isLoadingTrends()) {
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-4" aria-busy="true" data-test="trends-skeleton">
              @for (_ of [1,2,3,4]; track $index) {
                <div class="card-notion"><div class="skeleton-line h-40 w-full"></div></div>
              }
            </div>
          } @else if (!trends()) {
            <div class="card-notion text-center py-12" data-test="trends-empty">
              <p class="text-sm text-neutral-500">Trend data is unavailable.</p>
            </div>
          } @else {
            <div class="grid grid-cols-1 lg:grid-cols-2 gap-4" data-test="trends-charts">
              @for (chart of trendCharts(); track chart.key) {
                <div class="card-notion" [attr.data-test]="'trend-' + chart.key">
                  <h3 class="text-sm font-medium text-neutral-700 mb-3">{{ chart.title }}</h3>
                  @if (chart.values.length === 0) {
                    <p class="text-sm text-neutral-400 py-8 text-center">No data.</p>
                  } @else {
                    <svg [attr.viewBox]="'0 0 ' + chartW + ' ' + (chartH + 18)" class="w-full"
                      role="img" [attr.aria-label]="chart.title + ' trend over 12 months'"
                      data-test="trend-svg">
                      <polyline [attr.points]="chart.points" fill="none" [attr.stroke]="chart.color"
                        stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />
                      @for (v of chart.values; track $index; let i = $index) {
                        <circle [attr.cx]="pointX(i, chart.values.length)"
                          [attr.cy]="pointY(v, chart.max)" r="3" [attr.fill]="chart.color"
                          class="trend-dot">
                          <title>{{ chart.categories[i] }}: {{ v | number:'1.0-1' }}{{ chart.suffix }}</title>
                        </circle>
                      }
                    </svg>
                  }
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- ═══ Scheduled reports (FR-8) ══════════════════════════ -->
      @if (tab() === 'scheduled') {
        <div class="lg:grid lg:grid-cols-2 lg:gap-6" @fadeIn data-test="scheduled-panel">
          <!-- Config form -->
          <form class="card-notion space-y-3" (ngSubmit)="saveSchedule()" data-test="schedule-form">
            <h2 class="text-sm font-semibold text-neutral-900 mb-1">New scheduled report</h2>
            <div>
              <label class="label-sm" for="s-type">Report type</label>
              <select id="s-type" class="field-sm" [ngModel]="scheduleForm().reportType" name="reportType"
                (ngModelChange)="patchSchedule('reportType', $event)" data-test="schedule-type">
                @for (o of reportTypeOptions; track o.value) {
                  <option [value]="o.value">{{ o.label }}</option>
                }
              </select>
            </div>
            <div class="grid grid-cols-2 gap-2">
              <div>
                <label class="label-sm" for="s-freq">Frequency</label>
                <select id="s-freq" class="field-sm" [ngModel]="scheduleForm().frequency" name="frequency"
                  (ngModelChange)="patchSchedule('frequency', $event)" data-test="schedule-frequency">
                  @for (f of frequencyOptions; track f) { <option [value]="f">{{ f }}</option> }
                </select>
              </div>
              <div>
                <label class="label-sm" for="s-fmt">Format</label>
                <select id="s-fmt" class="field-sm" [ngModel]="scheduleForm().format" name="format"
                  (ngModelChange)="patchSchedule('format', $event)" data-test="schedule-format">
                  @for (f of formatOptions; track f) { <option [value]="f">{{ f }}</option> }
                </select>
              </div>
            </div>
            <div>
              <label class="label-sm" for="s-time">Delivery time</label>
              <input id="s-time" type="time" class="field-sm" [ngModel]="scheduleForm().deliveryTime"
                name="deliveryTime" (ngModelChange)="patchSchedule('deliveryTime', $event)"
                data-test="schedule-time" />
            </div>
            <div>
              <label class="label-sm" for="s-recipients">Recipients (comma-separated)</label>
              <input id="s-recipients" type="text" class="field-sm" placeholder="hr@acme.com, ops@acme.com"
                [ngModel]="recipientsText()" name="recipients" (ngModelChange)="onRecipientsChange($event)"
                data-test="schedule-recipients" />
            </div>
            <label class="flex items-center gap-2 text-sm text-neutral-700">
              <input type="checkbox" [ngModel]="scheduleForm().isActive" name="isActive"
                (ngModelChange)="patchSchedule('isActive', $event)" data-test="schedule-active" />
              Active
            </label>
            <button type="submit" class="btn-primary w-full" [disabled]="isSavingSchedule()"
              data-test="schedule-save">
              @if (isSavingSchedule()) { Saving… } @else { Save schedule }
            </button>
          </form>

          <!-- Existing configs -->
          <section class="mt-4 lg:mt-0">
            <h2 class="text-sm font-semibold text-neutral-900 mb-3">Scheduled reports</h2>
            @if (isLoadingSchedules()) {
              <div class="card-notion space-y-3" aria-busy="true">
                @for (_ of [1,2]; track $index) { <div class="skeleton-line h-10 w-full"></div> }
              </div>
            } @else if (schedules().length === 0) {
              <div class="card-notion text-center py-10" data-test="schedule-empty">
                <p class="text-sm text-neutral-500">No scheduled reports yet.</p>
              </div>
            } @else {
              <ul class="space-y-3" data-test="schedule-list">
                @for (s of schedules(); track s.id) {
                  <li class="card-notion flex items-center justify-between gap-3" data-test="schedule-item">
                    <div class="min-w-0">
                      <p class="font-medium text-neutral-900">{{ s.reportType }}</p>
                      <p class="text-xs text-neutral-500">
                        {{ s.frequency }} · {{ s.deliveryTime }} · {{ s.format }} ·
                        {{ s.recipients.length }} recipient(s)
                        <span [class.text-emerald-600]="s.isActive" [class.text-neutral-400]="!s.isActive">
                          · {{ s.isActive ? 'Active' : 'Paused' }}
                        </span>
                      </p>
                    </div>
                    <button type="button" class="text-xs text-rose-600 hover:underline shrink-0"
                      (click)="deleteSchedule(s)" data-test="schedule-delete">Delete</button>
                  </li>
                }
              </ul>
            }
          </section>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-7xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .tabs { @apply inline-flex rounded-lg border border-neutral-200 bg-neutral-50 p-0.5 flex-wrap; }
    .tab { @apply rounded-md px-3 py-1.5 text-sm font-medium text-neutral-500 transition-colors; }
    .tab-active { @apply bg-white text-neutral-900 shadow-sm; }

    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .field, .field-sm {
      @apply block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .field-sm { @apply py-1.5; }

    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .td { @apply py-3 px-4 align-middle; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60; }

    .bar-track { @apply h-3 w-full rounded-full bg-neutral-100 overflow-hidden; }
    .bar-fill { @apply h-full rounded-full transition-all duration-300; }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2 text-sm
        font-medium text-white shadow-sm transition-all duration-200 hover:bg-brand-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .export-menu {
      @apply absolute right-0 mt-1 w-40 rounded-lg bg-white border border-neutral-100 shadow-md z-20 py-1;
    }
    .export-item { @apply block w-full text-left px-3 py-2 text-sm text-neutral-700 hover:bg-neutral-50; }

    .trend-dot { @apply transition-transform; }
    .trend-dot:hover { transform: scale(1.4); transform-origin: center; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class AttendanceReportsComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // SVG canvas
  readonly chartW = 300;
  readonly chartH = 120;

  readonly tabs: { key: ReportTab; label: string }[] = [
    { key: 'departments', label: 'Departments' },
    { key: 'custom', label: 'Custom report' },
    { key: 'trends', label: 'Trends' },
    { key: 'scheduled', label: 'Scheduled' },
  ];
  readonly frequencyOptions = FREQUENCY_OPTIONS;
  readonly formatOptions = FORMAT_OPTIONS;
  readonly reportTypeOptions = REPORT_TYPE_OPTIONS;

  readonly tab = signal<ReportTab>('departments');

  // ─── Departments (AC-3) ─────────────────────────────────────
  readonly deptMonth = signal(currentMonthIso());
  readonly deptRows = signal<IDeptComparisonRow[]>([]);
  readonly isLoadingDept = signal(false);

  // ─── Custom (AC-4) ──────────────────────────────────────────
  readonly filters = signal<ICustomReportFilters>(defaultFilters());
  readonly customRows = signal<ICustomReportRow[]>([]);
  readonly isGenerating = signal(false);
  readonly hasGenerated = signal(false);
  readonly exportMenuOpen = signal(false);
  readonly isExporting = signal(false);

  // ─── Trends (AC-5) ──────────────────────────────────────────
  readonly trends = signal<ITrendsResult | null>(null);
  readonly isLoadingTrends = signal(false);

  // ─── Scheduled (FR-8) ───────────────────────────────────────
  readonly schedules = signal<IScheduledReportConfig[]>([]);
  readonly isLoadingSchedules = signal(false);
  readonly scheduleForm = signal<IScheduledReportConfig>(defaultSchedule());
  readonly recipientsText = signal('');
  readonly isSavingSchedule = signal(false);

  // ─── Derived: trend charts ──────────────────────────────────
  readonly trendCharts = computed<ITrendChartView[]>(() => {
    const t = this.trends();
    if (!t) {
      return [];
    }
    return TREND_SERIES.map((s) => {
      const series: ITrendPoint[] = t[s.key] ?? [];
      const values = series.map((p) => p.value);
      const max = trendMax(series);
      const pts = buildTrendPoints(values, this.chartW, this.chartH, max);
      return {
        key: s.key,
        title: s.title,
        color: s.color,
        suffix: s.suffix,
        points: trendPointsToString(pts),
        categories: series.map((p) => p.period),
        values,
        max,
      };
    });
  });

  ngOnInit(): void {
    this.loadDepartments();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  selectTab(tab: ReportTab): void {
    if (this.tab() === tab) {
      return;
    }
    this.tab.set(tab);
    if (tab === 'trends' && !this.trends()) {
      this.loadTrends();
    }
    if (tab === 'scheduled' && this.schedules().length === 0 && !this.isLoadingSchedules()) {
      this.loadSchedules();
    }
  }

  // ─── Departments ────────────────────────────────────────────
  loadDepartments(): void {
    this.isLoadingDept.set(true);
    this.attendanceService
      .getDepartmentComparison(this.deptMonth())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.deptRows.set(res.rows ?? []);
          this.isLoadingDept.set(false);
        },
        error: () => {
          this.deptRows.set([]);
          this.isLoadingDept.set(false);
          this.toastr.error('Failed to load the department comparison.');
        },
      });
  }

  onDeptMonthChange(value: string): void {
    if (!value) {
      return;
    }
    this.deptMonth.set(value);
    this.loadDepartments();
  }

  rateColor(pct: number): string {
    return attendanceRateColor(pct);
  }

  clamp(pct: number): number {
    return Math.max(0, Math.min(100, pct));
  }

  // ─── Custom report ──────────────────────────────────────────
  patchFilter<K extends keyof ICustomReportFilters>(key: K, value: ICustomReportFilters[K]): void {
    this.filters.update((f) => ({ ...f, [key]: value === '' ? null : value }));
  }

  generate(): void {
    const f = this.filters();
    if (!f.from || !f.to) {
      this.toastr.error('Select a from and to date.');
      return;
    }
    this.isGenerating.set(true);
    this.attendanceService
      .getCustomReport(f)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.customRows.set(res.rows ?? []);
          this.hasGenerated.set(true);
          this.isGenerating.set(false);
        },
        error: () => {
          this.customRows.set([]);
          this.isGenerating.set(false);
          this.toastr.error('Failed to generate the report.');
        },
      });
  }

  toggleExportMenu(): void {
    this.exportMenuOpen.update((v) => !v);
  }

  exportAs(format: CustomReportExportFormat): void {
    this.exportMenuOpen.set(false);
    this.isExporting.set(true);
    this.attendanceService
      .exportCustomReport(this.filters(), format)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (resp) => {
          const blob = resp.body;
          if (blob) {
            const filename =
              filenameFromDisposition(resp.headers.get('Content-Disposition')) ??
              `attendance-report-${this.filters().from}_${this.filters().to}.${format}`;
            downloadBlob(blob, filename);
            this.toastr.success('Export downloaded.');
          }
          this.isExporting.set(false);
        },
        error: () => {
          this.isExporting.set(false);
          this.toastr.error('Export failed.');
        },
      });
  }

  mins(m: number): string {
    return formatWorkMinutes(m);
  }

  // ─── Trends ─────────────────────────────────────────────────
  loadTrends(): void {
    this.isLoadingTrends.set(true);
    this.attendanceService
      .getTrends(12)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.trends.set(res);
          this.isLoadingTrends.set(false);
        },
        error: () => {
          this.trends.set(null);
          this.isLoadingTrends.set(false);
          this.toastr.error('Failed to load trends.');
        },
      });
  }

  pointX(index: number, count: number): number {
    if (count <= 1) {
      return this.chartW / 2;
    }
    return (index / (count - 1)) * this.chartW;
  }

  pointY(value: number, max: number): number {
    const m = max > 0 ? max : 1;
    return this.chartH - (value / m) * this.chartH;
  }

  // ─── Scheduled ──────────────────────────────────────────────
  loadSchedules(): void {
    this.isLoadingSchedules.set(true);
    this.attendanceService
      .getScheduledReports()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) => {
          this.schedules.set(list ?? []);
          this.isLoadingSchedules.set(false);
        },
        error: () => {
          this.schedules.set([]);
          this.isLoadingSchedules.set(false);
          this.toastr.error('Failed to load scheduled reports.');
        },
      });
  }

  patchSchedule<K extends keyof IScheduledReportConfig>(
    key: K,
    value: IScheduledReportConfig[K],
  ): void {
    this.scheduleForm.update((s) => ({ ...s, [key]: value }));
  }

  onRecipientsChange(text: string): void {
    this.recipientsText.set(text);
    const recipients = text
      .split(',')
      .map((r) => r.trim())
      .filter(Boolean);
    this.scheduleForm.update((s) => ({ ...s, recipients }));
  }

  saveSchedule(): void {
    const config = this.scheduleForm();
    if (config.recipients.length === 0) {
      this.toastr.error('Add at least one recipient.');
      return;
    }
    this.isSavingSchedule.set(true);
    this.attendanceService
      .createScheduledReport(config)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (created) => {
          this.schedules.update((list) => [created, ...list]);
          this.scheduleForm.set(defaultSchedule());
          this.recipientsText.set('');
          this.isSavingSchedule.set(false);
          this.toastr.success('Scheduled report created.');
        },
        error: () => {
          this.isSavingSchedule.set(false);
          this.toastr.error('Failed to create the scheduled report.');
        },
      });
  }

  deleteSchedule(config: IScheduledReportConfig): void {
    if (!config.id) {
      return;
    }
    this.attendanceService
      .deleteScheduledReport(config.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.schedules.update((list) => list.filter((s) => s.id !== config.id));
          this.toastr.success('Scheduled report deleted.');
        },
        error: () => this.toastr.error('Failed to delete the scheduled report.'),
      });
  }
}

// ─── Pure module-local helpers ────────────────────────────────

/** Current month as `yyyy-MM` in the browser's local timezone. */
function currentMonthIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  return `${y}-${m}`;
}

/** Default custom-report filters: a trailing 30-day window ending today. */
function defaultFilters(): ICustomReportFilters {
  const to = todayIso();
  const fromDate = new Date();
  fromDate.setDate(fromDate.getDate() - 29);
  return {
    from: todayIso(fromDate),
    to,
    departmentId: null,
    locationId: null,
    shiftId: null,
    status: null,
  };
}

/** A blank scheduled-report config for the form. */
function defaultSchedule(): IScheduledReportConfig {
  return {
    reportType: REPORT_TYPE_OPTIONS[0].value,
    frequency: 'DAILY',
    filters: {},
    recipients: [],
    deliveryTime: '08:00',
    format: 'CSV',
    isActive: true,
  };
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
  if (typeof document === 'undefined' || typeof URL === 'undefined' || !URL.createObjectURL) {
    return;
  }
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
