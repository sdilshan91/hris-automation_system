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

import { RecruitmentDashboardService } from '../../services/dashboard.service';
import { VACANCY_STATUS_LABELS } from '../../models/vacancy.models';
import {
  DATE_RANGE_PRESETS,
  DateRangePreset,
  IDateRange,
  IDashboardQuery,
  IRecruitmentDashboard,
  IDashboardFilterOption,
  IFunnelStage,
  ISourceEffectiveness,
  IVacancyStatusCount,
  ITimeToHirePoint,
  IDonutSegment,
  ActivityType,
  ACTIVITY_META,
  VACANCY_STATUS_COLOR,
  FUNNEL_COLOR,
  presetRange,
  todayIso,
  barPercent,
  seriesMax,
  sourceColor,
  buildDonutSegments,
  buildTrendPoints,
  trendPointsToString,
  relativeTime,
  formatDays,
  trendDirection,
} from '../../models/dashboard.models';

/** Trend-line viewBox dimensions (FR-4 SVG line). */
const TREND_W = 320;
const TREND_H = 96;

/**
 * US-REC-009: the recruiter/HR Recruitment Dashboard (FR-1..FR-9).
 *
 * A Notion-style analytics page mounted under the recruiter-guarded `recruitment`
 * route. All visualizations are pure SVG/CSS (NO charting library — mirrors
 * US-ATT-010 + US-REC-006):
 *  - KPI cards with up/down trend arrows vs. the previous period (FR-1/AC-1, §8).
 *  - Recruitment funnel as horizontal bars with conversion % between stages (FR-2/AC-3).
 *  - Source effectiveness as horizontal bars + hire-conversion per source (FR-3/AC-4).
 *  - Time-to-hire trend as an SVG polyline over the period (FR-4).
 *  - Vacancy status summary as an SVG donut + legend (FR-5).
 *  - Global date-range filter: preset pills (7d/30d/90d/1y) + custom range (FR-6),
 *    plus optional department/vacancy drill-down (FR-7).
 *  - Recent activity feed with relative timestamps (FR-9).
 *  - CSV export -> backend export endpoint (FR-8). PDF/Excel are server-generated and
 *    offered too; there is no client-side chart-to-PDF (no lib).
 *
 * BR-4: data refreshes on load + on every filter change (no real-time streaming).
 * Tenant scoping is server-side (AC-5).
 */
@Component({
  selector: 'app-recruitment-dashboard',
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
      <!-- Header + export -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Recruitment Dashboard</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Hiring analytics for {{ range().from }} – {{ range().to }}
          </p>
        </div>
        <div class="relative">
          <button type="button" class="btn-secondary text-sm" (click)="toggleExportMenu()"
            [disabled]="isExporting() || isLoading()" data-test="export-btn" aria-haspopup="menu"
            [attr.aria-expanded]="exportMenuOpen()">
            {{ isExporting() ? 'Exporting…' : 'Export' }}
            <svg class="w-3.5 h-3.5 ml-1" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 011.06.02L10 11.17l3.71-3.94a.75.75 0 111.08 1.04l-4.25 4.5a.75.75 0 01-1.08 0l-4.25-4.5a.75.75 0 01.02-1.06z" clip-rule="evenodd"/>
            </svg>
          </button>
          @if (exportMenuOpen()) {
            <div class="export-menu" role="menu" data-test="export-menu">
              <button type="button" class="export-item" role="menuitem" (click)="exportAs('csv')" data-test="export-csv">CSV</button>
              <button type="button" class="export-item" role="menuitem" (click)="exportAs('xlsx')" data-test="export-xlsx">Excel (.xlsx)</button>
              <button type="button" class="export-item" role="menuitem" (click)="exportAs('pdf')" data-test="export-pdf">PDF</button>
            </div>
          }
        </div>
      </div>

      <!-- ─── Date-range filter (FR-6) + drill-down (FR-7) ───────── -->
      <div class="card-notion mb-6 flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between" data-test="filters">
        <div>
          <p class="metric-label mb-2">Period</p>
          <div class="preset-bar" role="group" aria-label="Date range presets">
            @for (p of presets; track p.key) {
              <button type="button" class="preset-btn" [class.preset-active]="preset() === p.key"
                (click)="selectPreset(p.key)" [attr.data-test]="'preset-' + p.key">{{ p.label }}</button>
            }
            <button type="button" class="preset-btn" [class.preset-active]="preset() === 'custom'"
              (click)="selectPreset('custom')" data-test="preset-custom">Custom</button>
          </div>
          @if (preset() === 'custom') {
            <div class="flex items-center gap-2 mt-3" data-test="custom-range">
              <input type="date" class="input-date" [max]="today" [value]="range().from"
                (change)="setCustomFrom($any($event.target).value)" aria-label="From date" data-test="custom-from" />
              <span class="text-neutral-400 text-sm">to</span>
              <input type="date" class="input-date" [max]="today" [value]="range().to"
                (change)="setCustomTo($any($event.target).value)" aria-label="To date" data-test="custom-to" />
            </div>
          }
        </div>

        <div class="flex flex-col sm:flex-row gap-3">
          <div>
            <p class="metric-label mb-1.5">Department</p>
            <select class="input-select" [ngModel]="departmentId()" (ngModelChange)="setDepartment($event)"
              data-test="filter-department" aria-label="Department">
              <option [ngValue]="''">All departments</option>
              @for (d of departments(); track d.id) {
                <option [ngValue]="d.id">{{ d.label }}</option>
              }
            </select>
          </div>
          <div>
            <p class="metric-label mb-1.5">Vacancy</p>
            <select class="input-select" [ngModel]="vacancyId()" (ngModelChange)="setVacancy($event)"
              data-test="filter-vacancy" aria-label="Vacancy">
              <option [ngValue]="''">All vacancies</option>
              @for (v of vacancies(); track v.id) {
                <option [ngValue]="v.id">{{ v.label }}</option>
              }
            </select>
          </div>
        </div>
      </div>

      <!-- ─── KPI cards (FR-1 / AC-1) ────────────────────────────── -->
      @if (isLoading()) {
        <div class="kpi-grid mb-6" aria-busy="true" data-test="kpi-skeleton">
          @for (_ of [1,2,3,4,5,6]; track $index) {
            <div class="card-notion">
              <div class="skeleton-line h-3 w-24 mb-3"></div>
              <div class="skeleton-line h-8 w-16"></div>
            </div>
          }
        </div>
      } @else if (dashboard(); as d) {
        <div class="kpi-grid mb-6" data-test="kpi-cards">
          <div class="card-notion" data-test="kpi-openVacancies">
            <p class="metric-label">Open Vacancies</p>
            <p class="metric-value text-neutral-900">{{ d.kpis.openVacancies }}</p>
          </div>
          <div class="card-notion" data-test="kpi-totalApplicants">
            <p class="metric-label">Total Applicants</p>
            <p class="metric-value text-indigo-600">{{ d.kpis.totalApplicants }}</p>
            <ng-container *ngTemplateOutlet="trendArrow; context: { $implicit: dir('applicants') }"></ng-container>
          </div>
          <div class="card-notion" data-test="kpi-hires">
            <p class="metric-label">Hires</p>
            <p class="metric-value text-emerald-600">{{ d.kpis.hires }}</p>
            <ng-container *ngTemplateOutlet="trendArrow; context: { $implicit: dir('hires') }"></ng-container>
          </div>
          <div class="card-notion" data-test="kpi-avgTimeToHire">
            <p class="metric-label">Avg Time-to-Hire</p>
            <p class="metric-value text-sky-600">{{ daysLabel(d.kpis.avgTimeToHireDays) }}</p>
          </div>
          <div class="card-notion" data-test="kpi-offerAcceptance">
            <p class="metric-label">Offer Acceptance</p>
            <p class="metric-value text-violet-600">{{ d.kpis.offerAcceptanceRate | number:'1.0-1' }}%</p>
          </div>
          <div class="card-notion" data-test="kpi-offersPending">
            <p class="metric-label">Offers Pending</p>
            <p class="metric-value text-amber-600">{{ d.kpis.offersPending }}</p>
          </div>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <!-- ─── Funnel (FR-2 / AC-3) ───────────────────────────── -->
          <section class="card-notion" data-test="funnel-section">
            <h2 class="section-title">Recruitment funnel</h2>
            @if (d.funnel.length === 0) {
              <p class="empty-msg" data-test="funnel-empty">No data for the selected period.</p>
            } @else {
              <div class="space-y-3" data-test="funnel-bars">
                @for (s of d.funnel; track s.stage; let i = $index) {
                  <div>
                    <div class="flex items-center justify-between text-sm mb-1">
                      <span class="font-medium text-neutral-700">{{ s.stage }}</span>
                      <span class="text-neutral-500">{{ s.count }}</span>
                    </div>
                    <div class="bar-track">
                      <div class="bar-fill" [style.width.%]="funnelWidth(d.funnel, s.count)"
                        [style.background-color]="funnelColor" data-test="funnel-bar"></div>
                    </div>
                    @if (s.conversionRate != null && i > 0) {
                      <p class="text-xs text-neutral-400 mt-1" data-test="funnel-conversion">
                        {{ s.conversionRate }}% from {{ d.funnel[i - 1].stage }}
                      </p>
                    }
                  </div>
                }
              </div>
            }
          </section>

          <!-- ─── Source effectiveness (FR-3 / AC-4) ─────────────── -->
          <section class="card-notion" data-test="source-section">
            <h2 class="section-title">Source effectiveness</h2>
            @if (d.sources.length === 0) {
              <p class="empty-msg" data-test="source-empty">No data for the selected period.</p>
            } @else {
              <div class="space-y-3" data-test="source-bars">
                @for (s of d.sources; track s.source) {
                  <div>
                    <div class="flex items-center justify-between text-sm mb-1">
                      <span class="font-medium text-neutral-700">{{ s.source }}</span>
                      <span class="text-neutral-500">
                        {{ s.applicants }} applicants · {{ s.hires }} hired ({{ s.conversionRate }}%)
                      </span>
                    </div>
                    <div class="bar-track">
                      <div class="bar-fill" [style.width.%]="sourceWidth(d.sources, s.applicants)"
                        [style.background-color]="sourceBarColor(s.source)" data-test="source-bar"></div>
                    </div>
                  </div>
                }
              </div>
            }
          </section>

          <!-- ─── Time-to-hire trend (FR-4) ──────────────────────── -->
          <section class="card-notion" data-test="trend-section">
            <h2 class="section-title">Time-to-hire trend</h2>
            @if (d.timeToHireTrend.length === 0) {
              <p class="empty-msg" data-test="trend-empty">No data for the selected period.</p>
            } @else {
              <svg [attr.viewBox]="'0 0 ' + trendW + ' ' + (trendH + 20)" class="w-full h-32" role="img"
                aria-label="Time-to-hire trend over the selected period" data-test="trend-chart">
                <polyline [attr.points]="trendLine()" fill="none" stroke="#0ea5e9" stroke-width="2"
                  stroke-linejoin="round" stroke-linecap="round" />
                @for (pt of trendPoints(); track $index) {
                  <circle [attr.cx]="pt.x" [attr.cy]="pt.y" r="2.5" fill="#0ea5e9" />
                }
              </svg>
              <div class="flex justify-between text-xs text-neutral-400 mt-1" data-test="trend-labels">
                @for (p of d.timeToHireTrend; track $index) {
                  <span class="truncate">{{ p.label }}</span>
                }
              </div>
            }
          </section>

          <!-- ─── Vacancy status summary (FR-5) ──────────────────── -->
          <section class="card-notion" data-test="vacancy-status-section">
            <h2 class="section-title">Vacancy status</h2>
            @if (vacancyDonut().length === 0) {
              <p class="empty-msg" data-test="vacancy-status-empty">No data for the selected period.</p>
            } @else {
              <div class="flex items-center gap-5">
                <svg viewBox="0 0 120 120" width="120" height="120" role="img"
                  aria-label="Donut chart of vacancy counts by status" data-test="vacancy-donut">
                  @for (seg of vacancyDonut(); track seg.label) {
                    <path [attr.d]="seg.path" fill="none" [attr.stroke]="seg.color" stroke-width="14" />
                  }
                  <text x="60" y="64" text-anchor="middle" class="donut-num">{{ totalVacancies() }}</text>
                </svg>
                <ul class="text-xs space-y-1.5 flex-1">
                  @for (seg of vacancyDonut(); track seg.label) {
                    <li class="flex items-center justify-between gap-2" [attr.data-test]="'vacancy-legend-' + seg.label">
                      <span class="flex items-center gap-1.5">
                        <span class="h-2.5 w-2.5 rounded-sm" [style.background-color]="seg.color"></span>
                        {{ seg.label }}
                      </span>
                      <span class="font-medium text-neutral-700">{{ seg.value }}</span>
                    </li>
                  }
                </ul>
              </div>
            }
          </section>
        </div>

        <!-- ─── Recent activity feed (FR-9) ──────────────────────── -->
        <section class="card-notion mt-6" data-test="activity-section">
          <h2 class="section-title">Recent activity</h2>
          @if (d.recentActivity.length === 0) {
            <p class="empty-msg" data-test="activity-empty">No recent activity for the selected period.</p>
          } @else {
            <ul class="space-y-4" data-test="activity-feed">
              @for (a of d.recentActivity; track a.id) {
                <li class="flex items-start gap-3" data-test="activity-item">
                  <span class="mt-1.5 h-2 w-2 shrink-0 rounded-full" [ngClass]="activityDot(a.type)" aria-hidden="true"></span>
                  <div class="min-w-0 flex-1">
                    <p class="text-sm text-neutral-700">{{ a.description }}</p>
                    <p class="text-xs text-neutral-400 mt-0.5">{{ activityLabel(a.type) }} · {{ ago(a.occurredAt) }}</p>
                  </div>
                </li>
              }
            </ul>
          }
        </section>
      } @else {
        <div class="card-notion text-center py-12" data-test="dashboard-empty">
          <p class="text-sm text-neutral-500">No data for the selected period.</p>
        </div>
      }
    </div>

    <!-- KPI trend-arrow template -->
    <ng-template #trendArrow let-d>
      @if (d === 'up') {
        <span class="trend trend-up" data-test="trend-up" aria-label="up vs previous period">▲</span>
      } @else if (d === 'down') {
        <span class="trend trend-down" data-test="trend-down" aria-label="down vs previous period">▼</span>
      }
    </ng-template>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-7xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .section-title { @apply text-sm font-medium text-neutral-700 mb-4; }
    .empty-msg { @apply text-sm text-neutral-400 py-8 text-center; }

    .kpi-grid { @apply grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3; }
    .metric-label { @apply text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .metric-value { @apply text-2xl font-semibold mt-1; }
    .trend { @apply text-xs font-semibold ml-0.5; }
    .trend-up { @apply text-emerald-600; }
    .trend-down { @apply text-rose-600; }

    .preset-bar { @apply inline-flex flex-wrap gap-1 rounded-lg border border-neutral-200 bg-neutral-50 p-0.5; }
    .preset-btn { @apply rounded-md px-3 py-1.5 text-sm font-medium text-neutral-500 transition-colors; }
    .preset-active { @apply bg-white text-neutral-900 shadow-sm; }
    .input-date, .input-select {
      @apply rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-700
        transition-colors focus:border-indigo-400 focus:outline-none focus:ring-1 focus:ring-indigo-200;
    }

    .bar-track { @apply h-2.5 w-full rounded-full bg-neutral-100 overflow-hidden; }
    .bar-fill { @apply h-full rounded-full transition-all duration-300; }

    .donut-num { @apply text-base font-semibold; fill: #1f2937; }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .export-menu {
      @apply absolute right-0 z-10 mt-1 w-40 rounded-lg border border-neutral-100 bg-white shadow-md py-1;
    }
    .export-item { @apply block w-full px-4 py-2 text-left text-sm text-neutral-700 hover:bg-neutral-50; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class RecruitmentDashboardComponent implements OnInit, OnDestroy {
  private readonly dashboardService = inject(RecruitmentDashboardService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly presets = DATE_RANGE_PRESETS;
  readonly today = todayIso();
  readonly funnelColor = FUNNEL_COLOR;
  readonly trendW = TREND_W;
  readonly trendH = TREND_H;

  // ─── State ──────────────────────────────────────────────────
  readonly preset = signal<DateRangePreset>('30d');
  readonly range = signal<IDateRange>(presetRange(30));
  readonly departmentId = signal('');
  readonly vacancyId = signal('');
  readonly dashboard = signal<IRecruitmentDashboard | null>(null);
  readonly isLoading = signal(true);
  readonly isExporting = signal(false);
  readonly exportMenuOpen = signal(false);
  readonly departments = signal<IDashboardFilterOption[]>([]);
  readonly vacancies = signal<IDashboardFilterOption[]>([]);

  // ─── Derived chart geometry ─────────────────────────────────
  readonly vacancyDonut = computed<IDonutSegment[]>(() => {
    const d = this.dashboard();
    if (!d || d.vacancyStatus.length === 0) {
      return [];
    }
    return buildDonutSegments(
      d.vacancyStatus.map((v: IVacancyStatusCount) => ({
        label: VACANCY_STATUS_LABELS[v.status] ?? v.status,
        value: v.count,
        color: VACANCY_STATUS_COLOR[v.status] ?? '#9ca3af',
      })),
      60,
      60,
      46,
    );
  });

  readonly totalVacancies = computed(() =>
    (this.dashboard()?.vacancyStatus ?? []).reduce((s, v) => s + v.count, 0),
  );

  readonly trendPoints = computed(() => {
    const series = this.dashboard()?.timeToHireTrend ?? [];
    const values = series.map((p: ITimeToHirePoint) => p.avgDays);
    return buildTrendPoints(values, this.trendW, this.trendH, seriesMax(values));
  });

  readonly trendLine = computed(() => trendPointsToString(this.trendPoints()));

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.loadFilters();
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Build the current query from the applied range + drill-down filters. */
  private currentQuery(): IDashboardQuery {
    const r = this.range();
    const query: IDashboardQuery = { from: r.from, to: r.to };
    if (this.departmentId()) {
      query.departmentId = this.departmentId();
    }
    if (this.vacancyId()) {
      query.vacancyId = this.vacancyId();
    }
    return query;
  }

  /** Load the dashboard for the current range + filters (BR-4: on load + change). */
  load(): void {
    this.isLoading.set(true);
    this.dashboardService
      .getDashboard(this.currentQuery())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (d) => {
          this.dashboard.set(d);
          this.isLoading.set(false);
        },
        error: () => {
          this.dashboard.set(null);
          this.isLoading.set(false);
          this.toastr.error('Failed to load the recruitment dashboard.');
        },
      });
  }

  /** Load the department/vacancy drill-down options (FR-7); non-blocking on error. */
  private loadFilters(): void {
    this.dashboardService
      .getFilterOptions()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (opts) => {
          this.departments.set(opts.departments);
          this.vacancies.set(opts.vacancies);
        },
        error: () => {
          // Filters are optional; an empty list just hides the drill-down options.
        },
      });
  }

  // ─── Filter handlers (FR-6 / FR-7) ──────────────────────────
  selectPreset(preset: DateRangePreset): void {
    this.preset.set(preset);
    if (preset === 'custom') {
      return; // wait for the user to pick dates
    }
    const days = this.presets.find((p) => p.key === preset)?.days ?? 30;
    this.range.set(presetRange(days));
    this.load();
  }

  setCustomFrom(from: string): void {
    if (!from) {
      return;
    }
    this.range.update((r) => ({ ...r, from }));
    this.applyCustomRange();
  }

  setCustomTo(to: string): void {
    if (!to) {
      return;
    }
    this.range.update((r) => ({ ...r, to }));
    this.applyCustomRange();
  }

  /** Reload only when the custom range is valid (from <= to). */
  private applyCustomRange(): void {
    const r = this.range();
    if (r.from && r.to && r.from <= r.to) {
      this.load();
    }
  }

  setDepartment(id: string): void {
    this.departmentId.set(id ?? '');
    this.load();
  }

  setVacancy(id: string): void {
    this.vacancyId.set(id ?? '');
    this.load();
  }

  // ─── Export (FR-8) ──────────────────────────────────────────
  toggleExportMenu(): void {
    this.exportMenuOpen.update((v) => !v);
  }

  exportAs(format: 'csv' | 'xlsx' | 'pdf'): void {
    this.exportMenuOpen.set(false);
    this.isExporting.set(true);
    const query = this.currentQuery();
    this.dashboardService
      .exportDashboard(query, format)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (resp) => {
          const blob = resp.body;
          if (blob) {
            const filename = RecruitmentDashboardService.filenameFromResponse(
              resp,
              `recruitment-dashboard-${query.from}_${query.to}.${format}`,
            );
            RecruitmentDashboardService.downloadBlob(blob, filename);
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

  // ─── View helpers ───────────────────────────────────────────
  funnelWidth(funnel: IFunnelStage[], count: number): number {
    return barPercent(count, seriesMax(funnel.map((s) => s.count)));
  }

  sourceWidth(sources: ISourceEffectiveness[], applicants: number): number {
    return barPercent(applicants, seriesMax(sources.map((s) => s.applicants)));
  }

  sourceBarColor(source: string): string {
    return sourceColor(source);
  }

  daysLabel(days: number): string {
    return formatDays(days);
  }

  ago(iso: string): string {
    return relativeTime(iso);
  }

  activityLabel(type: ActivityType): string {
    return ACTIVITY_META[type]?.label ?? type;
  }

  activityDot(type: ActivityType): string {
    return ACTIVITY_META[type]?.dot ?? 'bg-neutral-400';
  }

  /** Trend direction for a KPI vs. its previous-period companion (§8). */
  dir(metric: 'applicants' | 'hires'): 'up' | 'down' | 'flat' | null {
    const k = this.dashboard()?.kpis;
    if (!k) {
      return null;
    }
    return metric === 'applicants'
      ? trendDirection(k.totalApplicants, k.previousTotalApplicants)
      : trendDirection(k.hires, k.previousHires);
  }
}
