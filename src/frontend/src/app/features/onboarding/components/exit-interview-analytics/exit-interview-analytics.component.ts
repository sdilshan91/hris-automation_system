import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ExitInterviewService } from '../../services/exit-interview.service';
import {
  IExitInterviewAnalytics,
  chartColor,
  pieSlices,
  ratingBarPercent,
  trendPolyline,
} from '../../models/exit-interview.models';

/**
 * US-ONB-006 (AC-4/FR-4/FR-5): the Exit Interview Summary analytics view.
 *
 * Shows three aggregated, tenant-scoped visualisations over a date-range window:
 *   • reason distribution — pie chart (SVG wedge paths + legend)
 *   • average rating per category — horizontal bar chart (CSS/Tailwind)
 *   • trend over time — line chart (inline-SVG polyline)
 *
 * CHARTING DECISION: this repo ships NO chart library, so every chart is drawn in
 * pure SVG/CSS — identical to the US-PRF-007 performance dashboard (donut + polyline)
 * and US-PAY-009 (see the no-chart-lib memory). Geometry lives in pure helpers in
 * exit-interview.models so it unit-tests without a component / DOM.
 *
 * Analytics show AGGREGATES ONLY (FR-5) — never individual responses — and are
 * tenant-isolated server-side (AC-5/NFR-2). Routed at `exit-interview/analytics`
 * under the HR roleGuard. Standalone, signals-first, OnPush, responsive 360px+.
 */
@Component({
  selector: 'app-exit-interview-analytics',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <nav class="text-xs text-neutral-400" aria-label="Breadcrumb">
          <span>Offboarding</span>
          <span class="mx-1">/</span>
          <span class="text-neutral-600">Exit interview summary</span>
        </nav>
        <h1 class="mt-1 text-2xl font-semibold tracking-tight text-neutral-900">
          Exit interview summary
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Aggregated, anonymised feedback across your organisation.
        </p>
      </div>

      <!-- Date-range filter -->
      <form
        [formGroup]="filterForm"
        (ngSubmit)="apply()"
        class="mb-6 flex flex-wrap items-end gap-3 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
      >
        <div>
          <label for="fromDate" class="block text-xs font-medium text-neutral-500">From</label>
          <input
            id="fromDate"
            type="date"
            formControlName="fromDate"
            class="mt-1 rounded-lg border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
            data-testid="from-date"
          />
        </div>
        <div>
          <label for="toDate" class="block text-xs font-medium text-neutral-500">To</label>
          <input
            id="toDate"
            type="date"
            formControlName="toDate"
            class="mt-1 rounded-lg border border-neutral-200 px-3 py-2 text-sm focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
            data-testid="to-date"
          />
        </div>
        <button
          type="submit"
          class="min-h-11 rounded-lg bg-neutral-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-neutral-700 disabled:opacity-50"
          [disabled]="loading()"
          data-testid="apply-filter"
        >
          Apply
        </button>
        @if (filterForm.controls.fromDate.value || filterForm.controls.toDate.value) {
          <button
            type="button"
            class="text-xs font-medium text-neutral-500 underline transition hover:text-neutral-800"
            (click)="clear()"
            data-testid="clear-filter"
          >
            Clear
          </button>
        }
      </form>

      @if (loading()) {
        <div class="grid grid-cols-1 gap-4 lg:grid-cols-2" data-testid="skeletons">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-64 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700" role="alert">
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">Retry</button>
        </div>
      } @else if (analytics(); as a) {
        <div @fadeIn class="grid grid-cols-1 gap-6 lg:grid-cols-2">
          <!-- Reason distribution — pie chart -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            data-testid="widget-reasons"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-900">Reasons for leaving</h2>
            @if (slices().length === 0) {
              <p class="text-sm text-neutral-400" data-testid="reasons-empty">No exit interviews in this range.</p>
            } @else {
              <div class="flex flex-col items-center gap-5 sm:flex-row">
                <svg viewBox="0 0 100 100" class="h-40 w-40 shrink-0" role="img" aria-label="Reason distribution pie chart">
                  @for (s of slices(); track s.reason) {
                    <path [attr.d]="s.path" [attr.fill]="s.color" data-testid="pie-slice" />
                  }
                </svg>
                <ul class="w-full space-y-1.5">
                  @for (s of slices(); track s.reason) {
                    <li class="flex items-center gap-2 text-sm" data-testid="reason-legend">
                      <span class="h-3 w-3 shrink-0 rounded-sm" [style.background-color]="s.color"></span>
                      <span class="min-w-0 flex-1 truncate text-neutral-700">{{ s.reason }}</span>
                      <span class="text-neutral-500">{{ s.count }} · {{ s.percent }}%</span>
                    </li>
                  }
                </ul>
              </div>
            }
          </section>

          <!-- Average rating per category — bar chart -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            data-testid="widget-ratings"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-900">Average rating per category</h2>
            @if (a.averageRatingsPerCategory.length === 0) {
              <p class="text-sm text-neutral-400" data-testid="ratings-empty">No category ratings yet.</p>
            } @else {
              <div class="space-y-3">
                @for (c of a.averageRatingsPerCategory; track c.category) {
                  <div data-testid="rating-bar">
                    <div class="mb-1 flex items-center justify-between text-xs">
                      <span class="truncate text-neutral-600">{{ c.category }}</span>
                      <span class="text-neutral-500">{{ c.averageRating | number: '1.1-1' }} / 5</span>
                    </div>
                    <div class="h-3 w-full overflow-hidden rounded-full bg-neutral-100">
                      <div
                        class="h-full rounded-full bg-neutral-700 transition-all"
                        [style.width.%]="barPercent(c.averageRating)"
                      ></div>
                    </div>
                  </div>
                }
              </div>
            }
          </section>

          <!-- Trend over time — line chart -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm lg:col-span-2"
            data-testid="widget-trend"
          >
            <h2 class="mb-1 text-sm font-semibold text-neutral-900">Trend over time</h2>
            <p class="mb-4 text-xs text-neutral-500">Average rating per period.</p>
            @if (a.trend.length === 0) {
              <p class="text-sm text-neutral-400" data-testid="trend-empty">No trend data in this range.</p>
            } @else {
              <svg
                [attr.viewBox]="'0 0 ' + trendWidth + ' ' + trendHeight"
                class="h-48 w-full"
                preserveAspectRatio="none"
                role="img"
                aria-label="Average exit interview rating trend"
              >
                <polyline
                  fill="none"
                  stroke-width="2"
                  [attr.stroke]="lineColor"
                  [attr.points]="trendPoints()"
                  data-testid="trend-line"
                />
              </svg>
              <div class="mt-2 flex justify-between text-[10px] text-neutral-400">
                @for (t of a.trend; track t.period) {
                  <span>{{ t.period }}</span>
                }
              </div>
            }
          </section>
        </div>
      }
    </div>
  `,
})
export class ExitInterviewAnalyticsComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(ExitInterviewService);

  readonly trendWidth = 600;
  readonly trendHeight = 160;
  readonly lineColor = chartColor(1);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly analytics = signal<IExitInterviewAnalytics | null>(null);

  readonly filterForm = this.fb.group({
    fromDate: this.fb.control<string>(''),
    toDate: this.fb.control<string>(''),
  });

  /** Pie wedge geometry derived from the reason distribution (pure SVG). */
  readonly slices = computed(() =>
    pieSlices(this.analytics()?.reasonDistribution ?? [], 50, 50, 48),
  );

  /** Trend polyline points derived from the trend series (pure SVG). */
  readonly trendPoints = computed(() =>
    trendPolyline(this.analytics()?.trend ?? [], this.trendWidth, this.trendHeight),
  );

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    const from = this.filterForm.controls.fromDate.value || undefined;
    const to = this.filterForm.controls.toDate.value || undefined;
    this.service.getAnalytics(from, to).subscribe({
      next: (a) => {
        this.analytics.set(a);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load exit interview analytics.');
        this.loading.set(false);
      },
    });
  }

  apply(): void {
    this.load();
  }

  clear(): void {
    this.filterForm.reset({ fromDate: '', toDate: '' });
    this.load();
  }

  barPercent(rating: number): number {
    return ratingBarPercent(rating);
  }
}
