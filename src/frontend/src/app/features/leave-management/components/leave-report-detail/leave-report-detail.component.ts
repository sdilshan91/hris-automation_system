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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { LeaveReportsService } from '../../services/leave-reports.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import { DepartmentService } from '../../../core-hr/departments/services/department.service';
import { ILeaveType } from '../../models/leave-type.models';
import { IDepartment } from '../../../core-hr/departments/models/department.models';
import {
  ReportType,
  ChartType,
  IReportRow,
  IReportColumn,
  IReportFilters,
  IReportQuery,
  IAnalyticsResponse,
  IChartDatum,
  IBarGeom,
  IPieSlice,
  IExportJobResponse,
  ExportFormat,
  EMPLOYMENT_TYPE_OPTIONS,
  REPORT_COLUMNS,
  findReportCard,
  emptyFilters,
  hasActiveFilters,
  totalPages,
  buildBarGeometry,
  buildPieSlices,
  buildLinePoints,
  seriesMax,
  pointsToString,
  paletteColor,
} from '../../models/leave-reports.models';

interface ILineSeriesGeom {
  name: string;
  color: string;
  points: string;
}

/**
 * US-LV-012: Report detail view (parameterized by `:reportType`).
 *
 * A full-page report with a collapsible filter sidebar (FR-2) and a results area:
 *  - Balance Summary (AC-1): sortable/paginated Notion table; export button.
 *  - Utilization (AC-2): table + bar/pie charts (custom SVG).
 *  - Absenteeism (AC-3): table with threshold-flagged rows + a trend line (SVG).
 *  - Trend Analysis (AC-4): 12-month line chart by type with YoY comparison (SVG).
 *  - Carry-Forward / LOP summary: plain sortable/paginated tables.
 *
 * Server-side sort + pagination (FR-3) are wired to the API. Export (AC-5) is a
 * top-right dropdown (CSV/Excel); a large dataset returns a background-job
 * envelope and the UI shows a "you'll be notified" state. Print button uses
 * print-friendly CSS (NFR-5). Mobile: table becomes a card list, charts simplify.
 *
 * Charting (NFR-4): custom SVG — no charting library added, consistent with the
 * US-LV-006 custom-SVG arc approach. Bar/pie/line geometry is built by pure
 * helpers in leave-reports.models.ts (unit-tested for scaling).
 */
@Component({
  selector: 'app-leave-report-detail',
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
      <!-- Breadcrumb + header -->
      <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4 mb-6 no-print">
        <div>
          <a routerLink="/leave/reports" class="text-xs text-neutral-400 hover:text-neutral-700 transition-colors">
            ← All reports
          </a>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight mt-1">{{ title() }}</h1>
          <p class="text-sm text-neutral-500 mt-1">{{ description() }}</p>
        </div>

        <!-- Action buttons: filter toggle (mobile), Export dropdown, Print -->
        <div class="flex items-center gap-2 shrink-0">
          <button type="button" class="btn-ghost lg:hidden" (click)="toggleFilters()"
            [attr.aria-expanded]="filtersOpen()" data-testid="toggle-filters">
            Filters
          </button>

          <div class="relative">
            <button type="button" class="btn-primary" (click)="toggleExportMenu()"
              [attr.aria-expanded]="exportMenuOpen()" [disabled]="isExporting()"
              data-testid="export-button">
              @if (isExporting()) { Exporting… } @else { Export ▾ }
            </button>
            @if (exportMenuOpen()) {
              <div class="export-menu" role="menu" data-testid="export-menu">
                <button type="button" role="menuitem" class="export-item"
                  (click)="export('csv')" data-testid="export-csv">CSV (.csv)</button>
                <button type="button" role="menuitem" class="export-item"
                  (click)="export('xlsx')" data-testid="export-xlsx">Excel (.xlsx)</button>
              </div>
            }
          </div>

          <button type="button" class="btn-ghost" (click)="print()" data-testid="print-button">
            Print
          </button>
        </div>
      </div>

      <!-- Background-export "processing" banner (AC-5) -->
      @if (exportProcessing()) {
        <div class="processing-banner no-print" @fadeIn role="status" data-testid="export-processing">
          <span class="h-2 w-2 rounded-full bg-amber-500 animate-pulse" aria-hidden="true"></span>
          <span>
            Your export is large and is being generated in the background. You’ll be notified when it’s
            ready to download.
          </span>
          <button type="button" class="ml-auto text-amber-700 hover:underline" (click)="dismissProcessing()">
            Dismiss
          </button>
        </div>
      }

      <div class="lg:grid lg:grid-cols-[16rem_1fr] lg:gap-6">
        <!-- ─── Filter sidebar (FR-2) ─────────────────────────── -->
        <aside class="filter-sidebar no-print" [class.hidden]="!filtersOpen()" [class.lg:block]="true"
          aria-label="Report filters">
          <div class="card-notion lg:sticky lg:top-4">
            <div class="flex items-center justify-between mb-4">
              <h2 class="text-sm font-semibold text-neutral-900">Filters</h2>
              @if (hasFilters()) {
                <button type="button" class="text-xs text-neutral-400 hover:text-neutral-700"
                  (click)="clearFilters()" data-testid="clear-filters">Clear</button>
              }
            </div>

            <div class="space-y-3">
              <div class="grid grid-cols-2 gap-2">
                <div>
                  <label class="label-sm" for="f-from">From</label>
                  <input id="f-from" type="date" class="input-sm" [ngModel]="filters().from"
                    (ngModelChange)="patchFilter('from', $event)" />
                </div>
                <div>
                  <label class="label-sm" for="f-to">To</label>
                  <input id="f-to" type="date" class="input-sm" [ngModel]="filters().to"
                    (ngModelChange)="patchFilter('to', $event)" />
                </div>
              </div>

              <div>
                <label class="label-sm" for="f-dept">Department</label>
                <select id="f-dept" class="input-sm select-input" [ngModel]="filters().departmentId"
                  (ngModelChange)="patchFilter('departmentId', $event)">
                  <option [ngValue]="null">All departments</option>
                  @for (d of departments(); track d.departmentId) {
                    <option [ngValue]="d.departmentId">{{ d.name }}</option>
                  }
                </select>
              </div>

              <div>
                <label class="label-sm" for="f-job">Job level</label>
                <input id="f-job" type="text" class="input-sm" placeholder="e.g. Senior"
                  [ngModel]="filters().jobLevel" (ngModelChange)="patchFilter('jobLevel', $event)" />
              </div>

              <div>
                <label class="label-sm" for="f-emp-type">Employment type</label>
                <select id="f-emp-type" class="input-sm select-input" [ngModel]="filters().employmentType"
                  (ngModelChange)="patchFilter('employmentType', $event)">
                  <option [ngValue]="null">All</option>
                  @for (e of employmentTypes; track e.value) {
                    <option [ngValue]="e.value">{{ e.label }}</option>
                  }
                </select>
              </div>

              <div>
                <label class="label-sm" for="f-lt">Leave type</label>
                <select id="f-lt" class="input-sm select-input" [ngModel]="filters().leaveTypeId"
                  (ngModelChange)="patchFilter('leaveTypeId', $event)">
                  <option [ngValue]="null">All leave types</option>
                  @for (lt of leaveTypes(); track lt.leaveTypeId) {
                    <option [ngValue]="lt.leaveTypeId">{{ lt.name }}</option>
                  }
                </select>
              </div>

              <div>
                <label class="label-sm" for="f-search">Employee search</label>
                <input id="f-search" type="text" class="input-sm" placeholder="Name or number…"
                  [ngModel]="filters().search" (ngModelChange)="patchFilter('search', $event)" />
              </div>

              <button type="button" class="btn-primary w-full mt-2" (click)="applyFilters()"
                data-testid="apply-filters">Apply filters</button>
            </div>
          </div>
        </aside>

        <!-- ─── Results area ──────────────────────────────────── -->
        <section class="min-w-0">
          <!-- Charts (utilization / absenteeism / trend) -->
          @if (hasCharts()) {
            <div class="card-notion mb-4 print-block" data-testid="charts">
              @if (isLoadingChart()) {
                <div class="skeleton-line h-48 w-full" aria-busy="true"></div>
              } @else if (reportType() === 'utilization') {
                <!-- AC-2: bar chart of per-department utilization + pie of per-type share -->
                <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
                  <div>
                    <h3 class="chart-title">Utilization by department</h3>
                    @if (barData().length === 0) {
                      <p class="chart-empty">No data for the current filters.</p>
                    } @else {
                      <svg [attr.viewBox]="'0 0 ' + chartW + ' ' + (chartH + 24)" class="w-full"
                        role="img" aria-label="Bar chart of utilization by department"
                        data-testid="bar-chart">
                        @for (bar of bars(); track bar.datum.label) {
                          <rect [attr.x]="bar.x" [attr.y]="bar.y" [attr.width]="bar.width"
                            [attr.height]="bar.height" [attr.fill]="bar.color" rx="3" />
                          <text [attr.x]="bar.x + bar.width / 2" [attr.y]="chartH + 14"
                            text-anchor="middle" class="axis-label">{{ bar.datum.label }}</text>
                        }
                      </svg>
                    }
                  </div>
                  <div>
                    <h3 class="chart-title">Share by leave type</h3>
                    @if (pieData().length === 0) {
                      <p class="chart-empty">No data for the current filters.</p>
                    } @else {
                      <div class="flex items-center gap-4">
                        <svg viewBox="0 0 120 120" width="120" height="120" role="img"
                          aria-label="Pie chart of leave share by type" data-testid="pie-chart">
                          @for (slice of pieSlices(); track slice.datum.label) {
                            <path [attr.d]="slice.path" [attr.fill]="slice.color" />
                          }
                        </svg>
                        <ul class="text-xs space-y-1">
                          @for (slice of pieSlices(); track slice.datum.label) {
                            <li class="flex items-center gap-1.5">
                              <span class="h-2.5 w-2.5 rounded-sm" [style.background-color]="slice.color"></span>
                              {{ slice.datum.label }} · {{ slice.percent | number:'1.0-0' }}%
                            </li>
                          }
                        </ul>
                      </div>
                    }
                  </div>
                </div>
              } @else {
                <!-- AC-3 absenteeism trend / AC-4 monthly trend: multi-series line chart -->
                <h3 class="chart-title">
                  {{ reportType() === 'absenteeism' ? 'Absenteeism trend' : 'Monthly leave by type (12 months, YoY)' }}
                </h3>
                @if (lineSeries().length === 0) {
                  <p class="chart-empty">No data for the current filters.</p>
                } @else {
                  <svg [attr.viewBox]="'0 0 ' + chartW + ' ' + (chartH + 24)" class="w-full"
                    role="img" aria-label="Line chart of leave trends" data-testid="line-chart">
                    @for (s of lineSeries(); track s.name) {
                      <polyline [attr.points]="s.points" fill="none" [attr.stroke]="s.color"
                        stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />
                    }
                    @for (cat of categories(); track cat; let i = $index) {
                      <text [attr.x]="categoryX(i)" [attr.y]="chartH + 16" text-anchor="middle"
                        class="axis-label">{{ cat }}</text>
                    }
                  </svg>
                  <ul class="flex flex-wrap gap-3 mt-2 text-xs">
                    @for (s of lineSeries(); track s.name) {
                      <li class="flex items-center gap-1.5">
                        <span class="h-2.5 w-4 rounded-sm" [style.background-color]="s.color"></span>{{ s.name }}
                      </li>
                    }
                  </ul>
                }
              }
            </div>
          }

          <!-- Table results -->
          @if (isLoading()) {
            <div class="card-notion" aria-busy="true">
              <div class="space-y-3">
                @for (_ of [1,2,3,4,5,6]; track $index) { <div class="skeleton-line h-10 w-full"></div> }
              </div>
            </div>
          } @else if (rows().length === 0) {
            <div class="card-notion text-center py-16" data-testid="empty-state">
              <svg class="mx-auto mb-4 text-neutral-300" width="56" height="56" viewBox="0 0 24 24"
                fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
                <rect x="3" y="4" width="18" height="17" rx="2" /><path d="M3 10h18M9 14h6" />
              </svg>
              <h3 class="text-lg font-semibold text-neutral-900 mb-1">No results</h3>
              <p class="text-sm text-neutral-500">No rows match the current filters.</p>
            </div>
          } @else {
            <!-- Desktop table -->
            <div class="hidden md:block card-notion overflow-x-auto print-block">
              <table class="w-full text-sm" [attr.aria-label]="title()" data-testid="report-table">
                <thead>
                  <tr class="border-b border-neutral-100">
                    @for (col of columns; track col.key) {
                      <th class="th" [class.text-right]="col.align === 'right'"
                        [class.text-center]="col.align === 'center'">
                        @if (col.sortable) {
                          <button type="button" class="th-sort no-print" (click)="sortBy(col.key)"
                            [attr.data-testid]="'sort-' + col.key">
                            {{ col.label }}
                            @if (sortKey() === col.key) {
                              <span aria-hidden="true">{{ sortDir() === 'asc' ? '▲' : '▼' }}</span>
                            }
                          </button>
                          <span class="print-only">{{ col.label }}</span>
                        } @else { {{ col.label }} }
                      </th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of rows(); track $index; let odd = $odd) {
                    <tr class="border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors"
                      [class.bg-neutral-50/40]="odd" [class.flagged-row]="isFlaggedRow(row)">
                      @for (col of columns; track col.key) {
                        <td class="td" [class.text-right]="col.align === 'right'"
                          [class.text-center]="col.align === 'center'">
                          @if (col.kind === 'flag') {
                            @if (truthy(row[col.key])) {
                              <span class="flag-badge" data-testid="flag-badge">⚑ Flagged</span>
                            } @else { <span class="text-neutral-300">—</span> }
                          } @else {
                            {{ cell(row, col) }}
                          }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <!-- Mobile card list -->
            <div class="md:hidden space-y-3" data-testid="report-cards">
              @for (row of rows(); track $index) {
                <div class="card-notion" [class.flagged-row]="isFlaggedRow(row)">
                  @for (col of columns; track col.key) {
                    <div class="flex justify-between gap-3 py-1 text-sm">
                      <span class="text-neutral-400">{{ col.label }}</span>
                      @if (col.kind === 'flag') {
                        @if (truthy(row[col.key])) {
                          <span class="flag-badge">⚑ Flagged</span>
                        } @else { <span class="text-neutral-300">—</span> }
                      } @else {
                        <span class="font-medium text-neutral-900">{{ cell(row, col) }}</span>
                      }
                    </div>
                  }
                </div>
              }
            </div>

            <!-- Pagination (FR-3) -->
            <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 mt-4 no-print"
              data-testid="pager">
              <p class="text-xs text-neutral-500">
                {{ rangeStart() }}–{{ rangeEnd() }} of {{ totalCount() }}
              </p>
              <div class="flex items-center gap-2">
                <select class="input-sm select-input w-auto" [ngModel]="pageSize()"
                  (ngModelChange)="changePageSize(+$event)" aria-label="Rows per page">
                  @for (s of pageSizeOptions; track s) { <option [value]="s">{{ s }} / page</option> }
                </select>
                <button type="button" class="btn-ghost" [disabled]="page() <= 1"
                  (click)="goToPage(page() - 1)" data-testid="prev-page">Prev</button>
                <span class="text-xs text-neutral-500">{{ page() }} / {{ pageCount() }}</span>
                <button type="button" class="btn-ghost" [disabled]="page() >= pageCount()"
                  (click)="goToPage(page() + 1)" data-testid="next-page">Next</button>
              </div>
            </div>
          }
        </section>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-7xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .filter-sidebar { @apply mb-4 lg:mb-0; }
    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-sm {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900
        placeholder-neutral-400 transition-all duration-150 focus:outline-none
        focus:ring-2 focus:ring-brand-500/20 focus:border-brand-400;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center; background-repeat: no-repeat;
      background-size: 1.5em 1.5em; padding-right: 2.5rem;
    }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2 text-sm
        font-medium text-white shadow-sm transition-all duration-200 hover:bg-brand-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-ghost {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white
        px-3 py-2 text-sm font-medium text-neutral-700 transition-colors hover:bg-neutral-50
        disabled:opacity-40 disabled:cursor-not-allowed;
    }

    .export-menu {
      @apply absolute right-0 mt-1 w-40 rounded-lg bg-white border border-neutral-100 shadow-md z-20 py-1;
    }
    .export-item { @apply block w-full text-left px-3 py-2 text-sm text-neutral-700 hover:bg-neutral-50; }

    .processing-banner {
      @apply flex items-center gap-2 rounded-lg bg-amber-50 px-4 py-3 mb-4 text-sm text-amber-800
        ring-1 ring-inset ring-amber-200;
    }

    .th { @apply text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .th-sort { @apply inline-flex items-center gap-1 hover:text-neutral-700 transition-colors; }
    .td { @apply py-3 px-3 text-neutral-700; }
    .flagged-row { @apply bg-rose-50/60; }
    .flag-badge {
      @apply inline-flex items-center gap-1 rounded-full bg-rose-100 px-2 py-0.5 text-xs
        font-medium text-rose-700;
    }

    .chart-title { @apply text-sm font-medium text-neutral-700 mb-3; }
    .chart-empty { @apply text-sm text-neutral-400 py-8 text-center; }
    .axis-label { @apply text-[9px] fill-neutral-400; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .print-only { display: none; }
    /* Print-friendly layout (NFR-5): hide controls/sidebar, show clean tables. */
    @media print {
      .no-print { display: none !important; }
      .print-only { display: inline !important; }
      .card-notion { box-shadow: none !important; border-color: #e5e5e5 !important; }
      .page-container { max-width: none !important; }
    }
  `],
})
export class LeaveReportDetailComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly reportsService = inject(LeaveReportsService);
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly departmentService = inject(DepartmentService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // SVG chart canvas geometry
  readonly chartW = 320;
  readonly chartH = 160;
  readonly pieRadius = 56;

  readonly employmentTypes = EMPLOYMENT_TYPE_OPTIONS;
  readonly pageSizeOptions = [10, 25, 50, 100];

  // ─── Identity ──────────────────────────────────────────
  readonly reportType = signal<ReportType>('balance-summary');
  columns: IReportColumn[] = REPORT_COLUMNS['balance-summary'];

  // ─── Filter / lookup state ─────────────────────────────
  readonly filters = signal<IReportFilters>(emptyFilters());
  readonly filtersOpen = signal(false);
  readonly departments = signal<IDepartment[]>([]);
  readonly leaveTypes = signal<ILeaveType[]>([]);

  // ─── Table state ───────────────────────────────────────
  readonly rows = signal<IReportRow[]>([]);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly sortKey = signal<string | null>(null);
  readonly sortDir = signal<'asc' | 'desc'>('asc');
  readonly isLoading = signal(true);

  // ─── Chart state ───────────────────────────────────────
  readonly analytics = signal<IAnalyticsResponse | null>(null);
  readonly isLoadingChart = signal(false);

  // ─── Export state ──────────────────────────────────────
  readonly exportMenuOpen = signal(false);
  readonly isExporting = signal(false);
  readonly exportProcessing = signal(false);

  // ─── Derived ───────────────────────────────────────────
  readonly hasFilters = computed(() => hasActiveFilters(this.filters()));
  readonly hasCharts = computed(() => findReportCard(this.reportType())?.hasCharts ?? false);
  readonly pageCount = computed(() => totalPages(this.totalCount(), this.pageSize()));
  readonly rangeStart = computed(() =>
    this.totalCount() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1,
  );
  readonly rangeEnd = computed(() =>
    Math.min(this.page() * this.pageSize(), this.totalCount()),
  );

  // Charts — bar (utilization-by-department)
  readonly barData = computed<IChartDatum[]>(() => this.analytics()?.data ?? []);
  readonly bars = computed<IBarGeom[]>(
    () => buildBarGeometry(this.barData(), this.chartW, this.chartH).bars,
  );
  // Charts — pie (utilization-by-type) is fetched into a second analytics signal
  readonly pieAnalytics = signal<IAnalyticsResponse | null>(null);
  readonly pieData = computed<IChartDatum[]>(() => this.pieAnalytics()?.data ?? []);
  readonly pieSlices = computed<IPieSlice[]>(
    () => buildPieSlices(this.pieData(), 60, 60, this.pieRadius),
  );
  // Charts — line (absenteeism trend / monthly trend)
  readonly categories = computed<string[]>(() => this.analytics()?.categories ?? []);
  readonly lineSeries = computed<ILineSeriesGeom[]>(() => {
    const a = this.analytics();
    if (!a?.series?.length) {
      return [];
    }
    const max = seriesMax(a.series);
    return a.series.map((s, i) => ({
      name: s.name,
      color: s.color ?? paletteColor(i),
      points: pointsToString(buildLinePoints(s.values, this.chartW, this.chartH, max)),
    }));
  });

  ngOnInit(): void {
    this.loadLookups();
    this.route.paramMap.pipe(takeUntil(this.destroy$)).subscribe((pm) => {
      const type = (pm.get('reportType') ?? 'balance-summary') as ReportType;
      this.setReportType(type);
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Identity / labels ─────────────────────────────────

  private setReportType(type: ReportType): void {
    this.reportType.set(type);
    this.columns = REPORT_COLUMNS[type] ?? REPORT_COLUMNS['balance-summary'];
    // Reset table state on report switch.
    this.page.set(1);
    this.sortKey.set(null);
    this.sortDir.set('asc');
    this.exportProcessing.set(false);
    this.stampViewed(type);
    this.load();
    if (this.hasCharts()) {
      this.loadCharts();
    }
  }

  title(): string {
    return findReportCard(this.reportType())?.title ?? 'Report';
  }

  description(): string {
    return findReportCard(this.reportType())?.description ?? '';
  }

  // ─── Lookups for the filter sidebar ────────────────────

  private loadLookups(): void {
    this.departmentService
      .getDepartments()
      .pipe(takeUntil(this.destroy$))
      .subscribe({ next: (d) => this.departments.set(d ?? []), error: () => this.departments.set([]) });
    this.leaveTypeService
      .getLeaveTypes()
      .pipe(takeUntil(this.destroy$))
      .subscribe({ next: (t) => this.leaveTypes.set(t ?? []), error: () => this.leaveTypes.set([]) });
  }

  // ─── Data loading ──────────────────────────────────────

  load(): void {
    this.isLoading.set(true);
    const query: IReportQuery = {
      ...this.filters(),
      page: this.page(),
      pageSize: this.pageSize(),
      sortBy: this.sortKey(),
      sortDir: this.sortDir(),
    };
    this.reportsService
      .getReport(this.reportType(), query)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.rows.set(res?.items ?? []);
          this.totalCount.set(res?.totalCount ?? 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.rows.set([]);
          this.totalCount.set(0);
          this.isLoading.set(false);
          this.toastr.error(LeaveReportsService.parseErrorMessage(err));
        },
      });
  }

  private loadCharts(): void {
    const type = this.reportType();
    this.isLoadingChart.set(true);
    this.analytics.set(null);
    this.pieAnalytics.set(null);
    const f = this.filters();

    const primary: ChartType =
      type === 'utilization'
        ? 'utilization-by-department'
        : type === 'absenteeism'
          ? 'absenteeism-trend'
          : 'monthly-trend';

    this.reportsService
      .getAnalytics(primary, f)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (a) => {
          this.analytics.set(a ?? null);
          this.isLoadingChart.set(false);
        },
        error: () => {
          this.analytics.set(null);
          this.isLoadingChart.set(false);
        },
      });

    // Utilization also renders a pie of per-type share (AC-2).
    if (type === 'utilization') {
      this.reportsService
        .getAnalytics('utilization-by-type', f)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (a) => this.pieAnalytics.set(a ?? null),
          error: () => this.pieAnalytics.set(null),
        });
    }
  }

  // ─── Filters (FR-2) ────────────────────────────────────

  patchFilter<K extends keyof IReportFilters>(key: K, value: IReportFilters[K]): void {
    this.filters.update((f) => ({ ...f, [key]: value === '' ? null : value }));
  }

  applyFilters(): void {
    this.page.set(1);
    this.load();
    if (this.hasCharts()) {
      this.loadCharts();
    }
  }

  clearFilters(): void {
    this.filters.set(emptyFilters());
    this.applyFilters();
  }

  toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  // ─── Sort (FR-3) ───────────────────────────────────────

  sortBy(key: string): void {
    if (this.sortKey() === key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDir.set('asc');
    }
    this.page.set(1);
    this.load();
  }

  // ─── Pagination (FR-3) ─────────────────────────────────

  goToPage(p: number): void {
    if (p < 1 || p > this.pageCount() || p === this.page()) {
      return;
    }
    this.page.set(p);
    this.load();
  }

  changePageSize(size: number): void {
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  // ─── Export (FR-4 / AC-5) ──────────────────────────────

  toggleExportMenu(): void {
    this.exportMenuOpen.update((v) => !v);
  }

  export(format: ExportFormat): void {
    this.exportMenuOpen.set(false);
    this.isExporting.set(true);
    this.exportProcessing.set(false);
    this.reportsService
      .export(this.reportType(), format, this.filters())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: async ({ blob, contentType, filename }) => {
          const job: IExportJobResponse | null = await LeaveReportsService.readJobEnvelope(
            blob,
            contentType,
          );
          if (job && job.status === 'processing') {
            // AC-5 large-dataset path: background job. DEFER polling — show banner.
            this.exportProcessing.set(true);
            this.toastr.info('Large export queued. You’ll be notified when it’s ready.');
          } else {
            this.triggerDownload(blob, filename);
            this.toastr.success('Export downloaded.');
          }
          this.isExporting.set(false);
        },
        error: (err) => {
          this.isExporting.set(false);
          this.toastr.error(LeaveReportsService.parseErrorMessage(err));
        },
      });
  }

  /** Trigger a browser download of a Blob. Guarded for non-browser test envs. */
  private triggerDownload(blob: Blob, filename: string): void {
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

  dismissProcessing(): void {
    this.exportProcessing.set(false);
  }

  print(): void {
    if (typeof window !== 'undefined' && window.print) {
      window.print();
    }
  }

  // ─── View helpers ──────────────────────────────────────

  cell(row: IReportRow, col: IReportColumn): string {
    const v = row[col.key];
    if (v === null || v === undefined) {
      return '—';
    }
    return String(v);
  }

  truthy(v: unknown): boolean {
    return v === true || v === 'true' || v === 1;
  }

  /** A row is flagged when its `flagged` property is truthy (AC-3 threshold). */
  isFlaggedRow(row: IReportRow): boolean {
    return this.reportType() === 'absenteeism' && this.truthy(row['flagged']);
  }

  categoryX(i: number): number {
    const cats = this.categories();
    if (cats.length <= 1) {
      return this.chartW / 2;
    }
    return (i / (cats.length - 1)) * this.chartW;
  }

  private stampViewed(type: ReportType): void {
    try {
      if (typeof localStorage !== 'undefined') {
        localStorage.setItem(`leave-report:lastViewed:${type}`, new Date().toISOString());
      }
    } catch {
      /* ignore storage errors */
    }
  }
}
