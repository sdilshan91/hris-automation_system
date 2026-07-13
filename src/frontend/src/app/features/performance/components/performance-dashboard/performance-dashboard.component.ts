import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { PerformanceDashboardService } from '../../services/performance-dashboard.service';
import {
  ExportFormat,
  IDashboardFilters,
  IDashboardOverview,
  IFilterOption,
  ITrendResponse,
  ITrendSeries,
  PerformerTrend,
  activeFilterCount,
  donutGeometry,
  emptyFilters,
  hasActiveFilters,
  histogramBarPercent,
  histogramPeak,
  scorePercent,
  trendPolylinePoints,
  trendSeriesColor,
  PERFORMER_TREND_CLASSES,
  PERFORMER_TREND_GLYPH,
} from '../../models/dashboard.models';

/** A filter facet descriptor — pairs the option list with the selected-id array. */
type FilterFacet = 'cycleIds' | 'departmentIds' | 'grades' | 'employmentTypes' | 'locations';

/**
 * US-PRF-007 (AC-1..AC-5): the Performance Dashboard & Analytics screen. Shows the
 * overview widgets (completion donut, average score, score-distribution histogram,
 * department-wise average bar, top/bottom performers, cycle progress), a collapsible
 * multi-select filter panel that re-fetches all widgets, a multi-cycle trend view, and
 * server-generated exports.
 *
 * CHARTING DECISION (option b): chart.js is deliberately NOT added as a dependency —
 * consistent with US-PRF-003/004/005 (see the no-chart-lib-comparison-table memory).
 * The donut is pure SVG (two arcs), the histogram + department bars are CSS/Tailwind,
 * and the multi-cycle trend is an inline-SVG polyline (clear and zero-dependency).
 *
 * SCOPE (BR-1/BR-3/AC-5) is authoritative from the backend: the overview payload's
 * `scope` decides HR (org-wide top/bottom) vs manager (team ranking only). A user who
 * lacks BOTH org + team read permission is redirected to `/my-review` (employees only
 * see their own data). The route is already role-gated; this is the defensive net.
 *
 * Reached at `/performance/dashboard` (manager/HR-gated parent route).
 */
@Component({
  selector: 'app-performance-dashboard',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('panel', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(-8px)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'translateX(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header + breadcrumb + actions -->
      <div class="mb-6 flex flex-wrap items-start justify-between gap-3">
        <div>
          <nav class="text-xs text-neutral-400" aria-label="Breadcrumb">
            <span>Performance</span>
            <span class="mx-1">/</span>
            <span class="text-neutral-600">Dashboard</span>
          </nav>
          <h1 class="mt-1 text-2xl font-semibold tracking-tight text-neutral-900">
            Performance dashboard
          </h1>
          @if (overview(); as o) {
            <p class="mt-1 text-sm text-neutral-500" data-testid="scope-label">
              {{ o.scope === 'Team' ? 'Team view' : 'Organization view' }}
              · {{ o.scopeLabel }}
            </p>
          }
        </div>
        <div class="flex items-center gap-2">
          <!-- Filter toggle (AC-2) -->
          <button
            type="button"
            class="inline-flex items-center gap-2 rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
            (click)="toggleFilters()"
            [attr.aria-expanded]="filtersOpen()"
            data-testid="toggle-filters"
          >
            Filters
            @if (filterBadge() > 0) {
              <span
                class="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-neutral-900 px-1.5 text-xs font-semibold text-white"
                data-testid="filter-badge"
                >{{ filterBadge() }}</span
              >
            }
          </button>
          <!-- Export menu (AC-4/FR-8) -->
          @if (overview(); as o) {
            @for (fmt of o.availableExportFormats; track fmt) {
              <button
                type="button"
                class="inline-flex items-center gap-1.5 rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50 disabled:opacity-50"
                [disabled]="exporting()"
                (click)="export(fmt)"
                [attr.data-testid]="'export-' + fmt"
              >
                @if (exporting() === fmt) {
                  <span
                    class="h-4 w-4 animate-spin rounded-full border-2 border-neutral-400 border-t-transparent"
                  ></span>
                }
                Export {{ fmt }}
              </button>
            }
          }
        </div>
      </div>

      <div class="flex flex-col gap-6 lg:flex-row">
        <!-- Filter panel (collapsible sidebar, AC-2/FR-4) -->
        @if (filtersOpen()) {
          <aside
            @panel
            class="w-full shrink-0 space-y-4 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm lg:w-64"
            data-testid="filter-panel"
          >
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-neutral-900">Filters</h2>
              @if (anyFilterActive()) {
                <button
                  type="button"
                  class="text-xs font-medium text-neutral-500 underline transition hover:text-neutral-800"
                  (click)="clearFilters()"
                  data-testid="clear-filters"
                >
                  Clear all
                </button>
              }
            </div>

            @for (group of filterGroups; track group.facet) {
              <div data-testid="filter-group">
                <p class="mb-1.5 text-xs font-medium uppercase tracking-wide text-neutral-400">
                  {{ group.label }}
                </p>
                @if (group.options().length === 0) {
                  <p class="text-xs text-neutral-300">No options</p>
                } @else {
                  <div class="space-y-1">
                    @for (opt of group.options(); track opt.id) {
                      <label
                        class="flex cursor-pointer items-center gap-2 text-sm text-neutral-700"
                      >
                        <input
                          type="checkbox"
                          class="h-4 w-4 rounded border-neutral-300 text-neutral-900 focus:ring-neutral-500"
                          [checked]="isSelected(group.facet, opt.id)"
                          (change)="toggleFilter(group.facet, opt.id)"
                          [attr.data-testid]="'filter-' + group.facet + '-' + opt.id"
                        />
                        <span class="truncate">{{ opt.label }}</span>
                      </label>
                    }
                  </div>
                }
              </div>
            }
          </aside>
        }

        <!-- Main content -->
        <div class="min-w-0 flex-1">
          @if (loading()) {
            <!-- Loading skeletons for chart areas (§8) -->
            <div
              class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
              data-testid="skeletons"
            >
              @for (i of [1, 2, 3, 4, 5, 6]; track i) {
                <div class="h-40 animate-pulse rounded-xl bg-neutral-100"></div>
              }
            </div>
          } @else if (error()) {
            <div
              class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
              role="alert"
            >
              {{ error() }}
              <button class="ml-2 font-medium underline" (click)="loadOverview()">
                Retry
              </button>
            </div>
          } @else if (overview(); as o) {
            <div @fadeIn class="space-y-6">
              <!-- KPI / overview widgets (AC-1) — responsive grid 3/2/1 -->
              <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                <!-- Completion donut -->
                <section
                  class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                  data-testid="widget-completion"
                >
                  <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                    Cycle completion
                  </p>
                  <div class="mt-3 flex items-center gap-4">
                    <svg viewBox="0 0 80 80" class="h-20 w-20 -rotate-90">
                      <circle
                        cx="40"
                        cy="40"
                        [attr.r]="donutRadius"
                        fill="none"
                        stroke="#f1f1f1"
                        stroke-width="10"
                      />
                      <circle
                        cx="40"
                        cy="40"
                        [attr.r]="donutRadius"
                        fill="none"
                        stroke="#171717"
                        stroke-width="10"
                        stroke-linecap="round"
                        [attr.stroke-dasharray]="donut().circumference"
                        [attr.stroke-dashoffset]="donut().dashOffset"
                        data-testid="donut-arc"
                      />
                    </svg>
                    <div>
                      <p class="text-2xl font-semibold text-neutral-900">
                        {{ o.completionRate | number: '1.0-0' }}%
                      </p>
                      <p class="text-xs text-neutral-500">reviews completed</p>
                    </div>
                  </div>
                </section>

                <!-- Average score -->
                <section
                  class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                  data-testid="widget-average"
                >
                  <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                    Average score
                  </p>
                  <p class="mt-3 text-3xl font-semibold text-neutral-900">
                    @if (o.averageScore != null) {
                      {{ o.averageScore | number: '1.1-1' }}
                      <span class="text-lg text-neutral-400">/ {{ o.scoreScaleMax }}</span>
                    } @else {
                      <span class="text-lg text-neutral-400">No data</span>
                    }
                  </p>
                  <p class="mt-1 text-xs text-neutral-500">
                    across {{ o.ratedCount }} rated employee(s)
                  </p>
                </section>

                <!-- Cycle progress metrics (FR-6) -->
                <section
                  class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                  data-testid="widget-progress"
                >
                  <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                    Cycle progress
                  </p>
                  <dl class="mt-3 space-y-1.5 text-sm">
                    @for (row of progressRows(); track row.label) {
                      <div class="flex items-center justify-between">
                        <dt class="text-neutral-500">{{ row.label }}</dt>
                        <dd class="font-medium text-neutral-800">
                          {{ row.value }} / {{ o.cycleProgress.totalParticipants }}
                        </dd>
                      </div>
                    }
                  </dl>
                </section>
              </div>

              <!-- Score-distribution histogram (FR-1) -->
              <section
                class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                data-testid="widget-histogram"
              >
                <h2 class="mb-4 text-sm font-semibold text-neutral-900">
                  Score distribution
                </h2>
                @if (o.histogram.length === 0) {
                  <p class="text-sm text-neutral-400">No rated reviews yet.</p>
                } @else {
                  <div class="flex h-40 items-end gap-2" role="img" aria-label="Score distribution histogram">
                    @for (b of o.histogram; track b.label) {
                      <div class="flex flex-1 flex-col items-center gap-1">
                        <span class="text-xs text-neutral-500">{{ b.count }}</span>
                        <div
                          class="w-full rounded-t bg-neutral-700 transition-all"
                          [style.height.%]="barHeight(b)"
                          data-testid="histogram-bar"
                        ></div>
                        <span class="text-[10px] text-neutral-400">{{ b.label }}</span>
                      </div>
                    }
                  </div>
                }
              </section>

              <!-- Department-wise average (FR-2 horizontal bar) + drill-down (FR-5) -->
              <section
                class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                data-testid="widget-departments"
              >
                <h2 class="mb-1 text-sm font-semibold text-neutral-900">
                  Department averages
                </h2>
                <p class="mb-4 text-xs text-neutral-500">
                  Click a department to drill down to its employees.
                </p>
                @if (o.departmentAverages.length === 0) {
                  <p class="text-sm text-neutral-400">No department data.</p>
                } @else {
                  <div class="space-y-3">
                    @for (d of o.departmentAverages; track d.departmentId) {
                      <button
                        type="button"
                        class="flex w-full items-center gap-3 rounded-lg px-1 py-1 text-left transition hover:bg-neutral-50"
                        (click)="drillDown(d.departmentId)"
                        [attr.data-testid]="'dept-bar-' + d.departmentId"
                      >
                        <span class="w-36 shrink-0 truncate text-xs font-medium text-neutral-600">
                          {{ d.departmentName }}
                        </span>
                        <div class="h-3 flex-1 overflow-hidden rounded-full bg-neutral-100">
                          <div
                            class="h-full rounded-full bg-neutral-700 transition-all"
                            [style.width.%]="deptPercent(d.averageScore, o.scoreScaleMax)"
                          ></div>
                        </div>
                        <span class="w-20 shrink-0 text-right text-xs text-neutral-500">
                          @if (d.averageScore != null) {
                            {{ d.averageScore | number: '1.1-1' }}
                          } @else {
                            —
                          }
                          · {{ d.headcount }}
                        </span>
                      </button>
                    }
                  </div>
                }
              </section>

              <!-- Top / bottom performers OR team ranking (FR-3/BR-3) -->
              <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
                @if (o.scope === 'Organization') {
                  <section
                    class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                    data-testid="widget-top"
                  >
                    <h2 class="mb-3 text-sm font-semibold text-neutral-900">
                      Top performers
                    </h2>
                    <ng-container
                      [ngTemplateOutlet]="performerList"
                      [ngTemplateOutletContext]="{ rows: o.topPerformers, scaleMax: o.scoreScaleMax }"
                    ></ng-container>
                  </section>
                  <section
                    class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                    data-testid="widget-bottom"
                  >
                    <h2 class="mb-3 text-sm font-semibold text-neutral-900">
                      Bottom performers
                    </h2>
                    <ng-container
                      [ngTemplateOutlet]="performerList"
                      [ngTemplateOutletContext]="{ rows: o.bottomPerformers, scaleMax: o.scoreScaleMax }"
                    ></ng-container>
                  </section>
                } @else {
                  <section
                    class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm lg:col-span-2"
                    data-testid="widget-team-ranking"
                  >
                    <h2 class="mb-3 text-sm font-semibold text-neutral-900">
                      Team ranking
                    </h2>
                    <ng-container
                      [ngTemplateOutlet]="performerList"
                      [ngTemplateOutletContext]="{ rows: o.teamRanking, scaleMax: o.scoreScaleMax }"
                    ></ng-container>
                  </section>
                }
              </div>

              <!-- Multi-cycle trend (AC-3/FR-7) — inline-SVG polyline -->
              <section
                class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                data-testid="widget-trend"
              >
                <div class="mb-3 flex items-center justify-between">
                  <div>
                    <h2 class="text-sm font-semibold text-neutral-900">
                      Multi-cycle trend
                    </h2>
                    <p class="text-xs text-neutral-500">
                      Average score across selected cycles.
                    </p>
                  </div>
                  <button
                    type="button"
                    class="text-xs font-medium text-neutral-500 underline transition hover:text-neutral-800"
                    (click)="toggleTrend()"
                    data-testid="toggle-trend"
                  >
                    {{ trendOpen() ? 'Hide' : 'Show' }} trend
                  </button>
                </div>

                @if (trendOpen()) {
                  @if (trendLoading()) {
                    <div class="h-48 animate-pulse rounded-lg bg-neutral-100" data-testid="trend-skeleton"></div>
                  } @else if (trend(); as t) {
                    @if (trendHasData()) {
                      <svg
                        [attr.viewBox]="'0 0 ' + trendWidth + ' ' + trendHeight"
                        class="h-48 w-full"
                        preserveAspectRatio="none"
                        role="img"
                        aria-label="Multi-cycle average score trend"
                      >
                        @for (s of t.series; track s.key; let i = $index) {
                          <polyline
                            fill="none"
                            stroke-width="2"
                            [attr.stroke]="seriesColor(i)"
                            [attr.points]="polyline(s, t.scoreScaleMax)"
                            data-testid="trend-line"
                          />
                        }
                      </svg>
                      <!-- legend -->
                      <div class="mt-3 flex flex-wrap gap-3">
                        @for (s of t.series; track s.key; let i = $index) {
                          <span class="inline-flex items-center gap-1.5 text-xs text-neutral-600">
                            <span class="h-2.5 w-2.5 rounded-full" [style.background-color]="seriesColor(i)"></span>
                            {{ s.label }}
                          </span>
                        }
                      </div>
                    } @else {
                      <p class="text-sm text-neutral-400" data-testid="trend-empty">
                        Select at least one cycle to see the trend.
                      </p>
                    }
                  }
                }
              </section>
            </div>
          }
        </div>
      </div>
    </div>

    <!-- Reusable performer-list template (top/bottom/team ranking) -->
    <ng-template #performerList let-rows="rows" let-scaleMax="scaleMax">
      @if (rows.length === 0) {
        <p class="text-sm text-neutral-400">No data.</p>
      } @else {
        <ol class="space-y-2">
          @for (p of rows; track p.employeeId; let idx = $index) {
            <li class="flex items-center gap-3" data-testid="performer-row">
              <span class="w-5 text-right text-xs font-semibold text-neutral-400">{{ idx + 1 }}</span>
              <div class="min-w-0 flex-1">
                <p class="truncate text-sm font-medium text-neutral-800">{{ p.employeeName }}</p>
                <p class="truncate text-xs text-neutral-400">
                  {{ p.departmentName }}@if (p.jobTitle) { · {{ p.jobTitle }} }
                </p>
              </div>
              <span class="text-xs" [class]="trendClass(p.trend)" [attr.title]="p.trend">
                {{ trendGlyph(p.trend) }}
              </span>
              <span class="w-12 text-right text-sm font-semibold text-neutral-900">
                @if (p.score != null) { {{ p.score | number: '1.0-1' }} } @else { — }
              </span>
            </li>
          }
        </ol>
      }
    </ng-template>
  `,
})
export class PerformanceDashboardComponent implements OnInit {
  private readonly service = inject(PerformanceDashboardService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly donutRadius = 35;
  readonly trendWidth = 600;
  readonly trendHeight = 180;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly overview = signal<IDashboardOverview | null>(null);
  readonly filters = signal<IDashboardFilters>(emptyFilters());
  readonly filtersOpen = signal(false);
  readonly exporting = signal<ExportFormat | null>(null);

  readonly trendOpen = signal(false);
  readonly trendLoading = signal(false);
  readonly trend = signal<ITrendResponse | null>(null);

  /** Donut geometry derived from the completion rate (pure SVG, no chart lib). */
  readonly donut = computed(() =>
    donutGeometry(this.overview()?.completionRate ?? 0, this.donutRadius),
  );

  /** Badge count on the collapsed filter toggle (FR-4). */
  readonly filterBadge = computed(() => activeFilterCount(this.filters()));
  readonly anyFilterActive = computed(() => hasActiveFilters(this.filters()));

  /** FR-6 cycle progress rows in display order. */
  readonly progressRows = computed(() => {
    const p = this.overview()?.cycleProgress;
    if (!p) {
      return [];
    }
    return [
      { label: 'Goal-setting', value: p.goalSettingComplete },
      { label: 'Self-assessment', value: p.selfAssessmentComplete },
      { label: 'Manager review', value: p.managerReviewComplete },
      { label: 'Signed off', value: p.signedOff },
    ];
  });

  /** True when the trend has at least one renderable point. */
  readonly trendHasData = computed(() => {
    const t = this.trend();
    return !!t && t.series.some((s) => s.points.some((pt) => pt.averageScore != null));
  });

  /** Filter groups bind the facet to its (signal-derived) option list. */
  readonly filterGroups: { facet: FilterFacet; label: string; options: () => IFilterOption[] }[] = [
    { facet: 'cycleIds', label: 'Cycle', options: () => this.overview()?.filterOptions.cycles ?? [] },
    { facet: 'departmentIds', label: 'Department', options: () => this.overview()?.filterOptions.departments ?? [] },
    { facet: 'grades', label: 'Grade / band', options: () => this.overview()?.filterOptions.grades ?? [] },
    { facet: 'employmentTypes', label: 'Employment type', options: () => this.overview()?.filterOptions.employmentTypes ?? [] },
    { facet: 'locations', label: 'Location', options: () => this.overview()?.filterOptions.locations ?? [] },
  ];

  ngOnInit(): void {
    // Defensive scope net (BR-1/AC-5): a user with neither org nor team read scope
    // is an employee — redirect to their own review page. The route is already
    // role-gated; this catches a permission-only mismatch.
    if (
      // DEC-1 removed the aspirational 'Reports.View.All' admit: it did not exist
      // until DEC-1 seeds it, and admitting report-admins into the Performance
      // dashboard would be an unintended cross-module coupling. Gate on Performance
      // scope perms only.
      //
      // ISSUE-290: gate on the REAL backend permissions. The story names
      // Performance.Read.All / Performance.Read.Team are NOT concrete permissions
      // (PermissionCatalog has no such strings); the backend controller
      // (PerformanceDashboardController) reuses the existing equivalents
      // Performance.View.All / Performance.View.Team. Using the story-name strings
      // here matched no user's permission set and redirected EVERY user (incl. HR/
      // admin holding Performance.View.All) to /my-review, making this page
      // unreachable.
      !this.auth.hasAnyPermission([
        'Performance.View.All',
        'Performance.View.Team',
      ])
    ) {
      this.router.navigate(['/my-review']);
      return;
    }
    this.loadOverview();
  }

  loadOverview(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getOverview(this.filters()).subscribe({
      next: (o) => {
        this.overview.set(o);
        this.loading.set(false);
        if (this.trendOpen()) {
          this.loadTrend();
        }
      },
      error: () => {
        this.error.set('Unable to load the performance dashboard.');
        this.loading.set(false);
      },
    });
  }

  // ── Filters (AC-2/FR-4) ──────────────────────────────────

  toggleFilters(): void {
    this.filtersOpen.update((v) => !v);
  }

  isSelected(facet: FilterFacet, id: string): boolean {
    return this.filters()[facet].includes(id);
  }

  /** Toggle one option in a facet, then refetch every widget (AC-2). */
  toggleFilter(facet: FilterFacet, id: string): void {
    this.filters.update((f) => {
      const current = f[facet];
      const next = current.includes(id)
        ? current.filter((x) => x !== id)
        : [...current, id];
      return { ...f, [facet]: next };
    });
    this.loadOverview();
  }

  clearFilters(): void {
    this.filters.set(emptyFilters());
    this.loadOverview();
  }

  // ── Trend (AC-3/FR-7) ────────────────────────────────────

  toggleTrend(): void {
    const open = !this.trendOpen();
    this.trendOpen.set(open);
    if (open && !this.trend()) {
      this.loadTrend();
    }
  }

  loadTrend(): void {
    this.trendLoading.set(true);
    this.service.getTrend(this.filters()).subscribe({
      next: (t) => {
        this.trend.set(t);
        this.trendLoading.set(false);
      },
      error: () => {
        this.trendLoading.set(false);
        this.toastr.error('Unable to load the trend chart.');
      },
    });
  }

  polyline(series: ITrendSeries, scaleMax: number): string {
    return trendPolylinePoints(series, scaleMax, this.trendWidth, this.trendHeight);
  }

  seriesColor(index: number): string {
    return trendSeriesColor(index);
  }

  // ── Drill-down (FR-5) ────────────────────────────────────

  /** Navigate to the department employee list. The first selected cycle scopes it. */
  drillDown(departmentId: string): void {
    const cycleId = this.filters().cycleIds[0];
    this.router.navigate(
      ['/performance', 'dashboard', 'departments', departmentId],
      cycleId ? { queryParams: { cycleId } } : {},
    );
  }

  // ── Export (AC-4/FR-8) ───────────────────────────────────

  export(format: ExportFormat): void {
    if (this.exporting()) {
      return;
    }
    this.exporting.set(format);
    this.service.export(format, this.filters()).subscribe({
      next: (response) => {
        const blob = response.body;
        if (blob) {
          const filename =
            this.filenameFromDisposition(response.headers.get('Content-Disposition')) ??
            `performance-dashboard.${this.extensionFor(format)}`;
          this.triggerDownload(blob, filename);
        }
        this.exporting.set(null);
      },
      error: () => {
        this.exporting.set(null);
        this.toastr.error('Unable to generate the export. Please try again.');
      },
    });
  }

  private extensionFor(format: ExportFormat): string {
    return format === 'Csv' ? 'csv' : format === 'Excel' ? 'xlsx' : 'pdf';
  }

  private filenameFromDisposition(disposition: string | null): string | null {
    if (!disposition) {
      return null;
    }
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    return match ? decodeURIComponent(match[1]) : null;
  }

  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  }

  // ── visual helpers (pure math delegated to model fns) ────

  barHeight(bucket: Parameters<typeof histogramBarPercent>[0]): number {
    return histogramBarPercent(bucket, histogramPeak(this.overview()?.histogram ?? []));
  }

  deptPercent(score: number | null, scaleMax: number): number {
    return scorePercent(score, scaleMax);
  }

  trendClass(trend: PerformerTrend): string {
    return PERFORMER_TREND_CLASSES[trend];
  }

  trendGlyph(trend: PerformerTrend): string {
    return PERFORMER_TREND_GLYPH[trend];
  }
}
