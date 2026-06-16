import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';

import { PayrollReportService } from '../../services/payroll-report.service';
import {
  IDashboardAnalytics,
  IPayrollAnalyticsResult,
  defaultReportPeriod,
  periodLabel,
  polylinePoints,
  seriesMax,
} from '../../models/payroll-report.models';

/** A trend line (one series, e.g. Gross / Deductions / Net) rendered as an SVG polyline. */
interface ITrendLine {
  key: string;
  label: string;
  color: string;
  points: string;
  /** The last value, for the inline legend figure. */
  last: number;
}

/** A stacked-bar segment for one statutory month. */
interface IStackSegment {
  name: string;
  color: string;
  value: number;
  /** Height fraction (0–1) of this segment within its month's total. */
  frac: number;
}

interface IStackBar {
  /** Category label (the month). */
  category: string;
  segments: IStackSegment[];
  total: number;
}

/** A single department cost bar. */
interface IDeptBar {
  departmentName: string;
  totalCost: number;
}

/** Palette for the statutory stacked bar (cycled by series index). */
const STATUTORY_COLORS = ['#6366f1', '#0ea5e9', '#10b981', '#f59e0b', '#f43f5e', '#a855f7'];

/** Trend-line palette, cycled by series index. */
const TREND_COLORS = ['#6366f1', '#f43f5e', '#10b981', '#f59e0b', '#0ea5e9'];

/** Department-bar palette accent (single hue; sorted-desc list). */
const DEPT_BAR_COLOR = '#6366f1';

/**
 * US-PAY-009 (FR-5, §8): the payroll analytics dashboard.
 *
 * A Notion-style, card-based layout of pre-aggregated charts — all drawn with pure
 * SVG/CSS (NO charting library; mirrors the US-ATT-010 attendance dashboard):
 *  - Monthly payroll trend: a multi-line chart (one polyline per series, e.g.
 *    gross / deductions / net) over the shared `categories` x-axis.
 *  - Department cost distribution: a horizontal bar chart, sorted by cost desc (§8).
 *  - Statutory deduction breakdown: a stacked bar chart per month (FR-5).
 *
 * The dashboard is assembled FE-side by `forkJoin`ing three separate
 * `/payroll/analytics/{chartType}` calls (there is no single dashboard endpoint).
 * Charts remain viewable on mobile (§8); the layout collapses to a single column.
 */
@Component({
  selector: 'app-payroll-analytics',
  standalone: true,
  imports: [CommonModule, RouterLink],
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
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Payroll Analytics</h1>
          <p class="text-sm text-neutral-500 mt-1">Payroll trends, cost distribution and statutory breakdown.</p>
        </div>
        <a routerLink="/payroll/reports" class="btn-secondary text-sm" data-test="reports-link">Reports</a>
      </div>

      @if (isLoading()) {
        <div class="grid gap-6 lg:grid-cols-2" aria-busy="true" data-test="analytics-skeleton">
          @for (_ of [1,2,3,4]; track $index) {
            <div class="card-notion">
              <div class="skeleton-line h-3 w-32 mb-4"></div>
              <div class="skeleton-line h-40 w-full"></div>
            </div>
          }
        </div>
      } @else if (analytics(); as a) {
        <div class="grid gap-6 lg:grid-cols-2">
          <!-- ─── Monthly payroll trend (line) ─────────────────── -->
          <section class="card-notion lg:col-span-2" data-test="trend-card">
            <h2 class="card-title">Monthly payroll trend</h2>
            @if (trendLines().length === 0) {
              <p class="empty-note" data-test="trend-empty">No payroll history yet.</p>
            } @else {
              <svg [attr.viewBox]="'0 0 ' + chartW + ' ' + chartH" class="w-full h-48"
                preserveAspectRatio="none" role="img" aria-label="Monthly payroll trend" data-test="trend-chart">
                @for (line of trendLines(); track line.key) {
                  <polyline [attr.points]="line.points" fill="none" [attr.stroke]="line.color"
                    stroke-width="2" stroke-linejoin="round" stroke-linecap="round" />
                }
              </svg>
              <div class="flex flex-wrap gap-4 mt-3">
                @for (line of trendLines(); track line.key) {
                  <span class="legend-item" [attr.data-test]="'legend-' + line.key">
                    <span class="legend-dot" [style.background-color]="line.color"></span>
                    {{ line.label }}
                    <span class="text-neutral-400 ml-1">{{ line.last | number:'1.0-0' }}</span>
                  </span>
                }
              </div>
              <div class="flex justify-between mt-2 text-[10px] text-neutral-400">
                @for (c of a.trend.categories; track c) { <span>{{ c }}</span> }
              </div>
            }
          </section>

          <!-- ─── Department cost distribution (horizontal bars) ── -->
          <section class="card-notion" data-test="dept-card">
            <h2 class="card-title">Department cost distribution</h2>
            @if (sortedDepartments().length === 0) {
              <p class="empty-note" data-test="dept-empty">No department data.</p>
            } @else {
              <ul class="space-y-3" data-test="dept-bars">
                @for (d of sortedDepartments(); track d.departmentName) {
                  <li>
                    <div class="flex items-center justify-between text-xs mb-1">
                      <span class="text-neutral-600 truncate">{{ d.departmentName }}</span>
                      <span class="font-medium text-neutral-700 font-mono tabular-nums">
                        {{ d.totalCost | number:'1.0-0' }}
                      </span>
                    </div>
                    <div class="h-2.5 rounded-full bg-neutral-100 overflow-hidden">
                      <div class="h-full rounded-full transition-all duration-500"
                        [style.width.%]="deptBarWidth(d.totalCost)"
                        [style.background-color]="deptColor"></div>
                    </div>
                  </li>
                }
              </ul>
            }
          </section>

          <!-- ─── Statutory breakdown (stacked bars) ───────────── -->
          <section class="card-notion" data-test="statutory-card">
            <h2 class="card-title">Statutory deduction breakdown</h2>
            @if (statutoryBars().length === 0) {
              <p class="empty-note" data-test="statutory-empty">No statutory data.</p>
            } @else {
              <div class="flex items-end gap-3 h-40" data-test="statutory-bars">
                @for (bar of statutoryBars(); track bar.category) {
                  <div class="flex-1 flex flex-col items-center gap-1 min-w-0">
                    <div class="w-full flex flex-col-reverse rounded-md overflow-hidden bg-neutral-50"
                      style="height: 8.5rem">
                      @for (seg of bar.segments; track seg.name) {
                        <div [style.height.%]="seg.frac * 100" [style.background-color]="seg.color"
                          [attr.title]="seg.name + ': ' + (seg.value | number:'1.0-0')"></div>
                      }
                    </div>
                    <span class="text-[10px] text-neutral-400 truncate w-full text-center">
                      {{ bar.category }}
                    </span>
                  </div>
                }
              </div>
              <div class="flex flex-wrap gap-3 mt-4">
                @for (s of statutoryLegend(); track s.name) {
                  <span class="legend-item">
                    <span class="legend-dot" [style.background-color]="s.color"></span>{{ s.name }}
                  </span>
                }
              </div>
            }
          </section>
        </div>
      } @else {
        <div class="card-notion text-center py-12" data-test="analytics-empty">
          <p class="text-sm text-neutral-500">Analytics are unavailable right now.</p>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-7xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .card-title { @apply text-sm font-medium text-neutral-700 mb-4; }
    .empty-note { @apply text-sm text-neutral-400 py-10 text-center; }

    .legend-item { @apply inline-flex items-center gap-1.5 text-xs text-neutral-600; }
    .legend-dot { @apply h-2.5 w-2.5 rounded-sm; }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50;
    }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class PayrollAnalyticsComponent implements OnInit {
  private readonly reportService = inject(PayrollReportService);
  private readonly toastr = inject(ToastrService);

  readonly chartW = 600;
  readonly chartH = 160;
  readonly deptColor = DEPT_BAR_COLOR;

  readonly analytics = signal<IDashboardAnalytics | null>(null);
  readonly isLoading = signal(true);

  // ─── Trend lines (one polyline per series) ──────────────────
  readonly trendLines = computed<ITrendLine[]>(() => {
    const trend = this.analytics()?.trend;
    if (!trend || trend.series.length === 0) {
      return [];
    }
    const max = seriesMax(trend.series) || 1;
    return trend.series.map((s, i) => {
      const values = s.points.map((p) => p.value);
      return {
        key: s.name,
        label: s.name,
        color: TREND_COLORS[i % TREND_COLORS.length],
        points: polylinePoints(values, max, this.chartW, this.chartH),
        last: values[values.length - 1] ?? 0,
      };
    });
  });

  // ─── Department bars (sorted desc, §8) ──────────────────────
  readonly sortedDepartments = computed<IDeptBar[]>(() => {
    const dept = this.analytics()?.departmentCosts;
    if (!dept) {
      return [];
    }
    return dept.points
      .map((p) => ({ departmentName: p.label, totalCost: p.value }))
      .sort((x, y) => y.totalCost - x.totalCost);
  });

  private readonly maxDeptCost = computed(() =>
    this.sortedDepartments().reduce((m, d) => Math.max(m, d.totalCost), 0),
  );

  // ─── Statutory stacked bars ─────────────────────────────────
  /** The statutory component series names (one legend entry each). */
  private readonly statutoryNames = computed<string[]>(() => {
    const stat = this.analytics()?.statutory;
    return stat ? stat.series.map((s) => s.name) : [];
  });

  readonly statutoryLegend = computed(() =>
    this.statutoryNames().map((name, i) => ({ name, color: this.colorFor(i) })),
  );

  /**
   * Build one stacked bar per category (month). Each series contributes one segment;
   * segment values are read positionally from the series' points against the shared
   * `categories` axis.
   */
  readonly statutoryBars = computed<IStackBar[]>(() => {
    const stat = this.analytics()?.statutory;
    if (!stat || stat.categories.length === 0 || stat.series.length === 0) {
      return [];
    }
    return stat.categories.map((category, idx) => this.buildStackBar(stat, category, idx));
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    const filters = { period: defaultReportPeriod(), departmentId: null };
    this.reportService.getDashboardAnalytics(filters).subscribe({
      next: (a) => {
        this.analytics.set(a);
        this.isLoading.set(false);
      },
      error: () => {
        this.analytics.set(null);
        this.isLoading.set(false);
        this.toastr.error('Failed to load payroll analytics.');
      },
    });
  }

  // ─── View helpers ───────────────────────────────────────────
  deptBarWidth(cost: number): number {
    const max = this.maxDeptCost();
    if (max <= 0) {
      return 0;
    }
    return Math.max(2, (Math.max(0, cost) / max) * 100);
  }

  shortPeriod(period: string): string {
    return periodLabel(period);
  }

  private colorFor(index: number): string {
    return STATUTORY_COLORS[index % STATUTORY_COLORS.length];
  }

  private buildStackBar(
    stat: IPayrollAnalyticsResult,
    category: string,
    categoryIndex: number,
  ): IStackBar {
    const segs = stat.series
      .map((s, i) => ({
        name: s.name,
        color: this.colorFor(i),
        value: Math.max(0, this.valueAt(s.points, category, categoryIndex)),
      }))
      .filter((s) => s.value > 0);
    const total = segs.reduce((sum, s) => sum + s.value, 0);
    return {
      category,
      total,
      segments: segs.map((s) => ({ ...s, frac: total > 0 ? s.value / total : 0 })),
    };
  }

  /** Read a series' value for a category — by matching label, else positionally. */
  private valueAt(
    points: { label: string; value: number }[],
    category: string,
    index: number,
  ): number {
    const byLabel = points.find((p) => p.label === category);
    if (byLabel) {
      return byLabel.value;
    }
    return points[index]?.value ?? 0;
  }
}
