import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { RecommendationService } from '../../services/recommendation.service';
import {
  ICycleComparisonStat,
  IDepartmentRecommendationStat,
  IRecommendationSummary,
  comparisonDelta,
  deptBarPercent,
} from '../../models/recommendation.models';

/**
 * US-PRF-010 AC-4 / FR-6: the recommendation SUMMARY (leadership view). Aggregate
 * stats for a cycle — total promotions, bonus pool allocated, increment distribution
 * by department, and a comparison vs the previous cycle. Per the no-chart-lib
 * convention (US-PRF-003..009), the distribution is drawn with pure CSS/SVG bars
 * (NO chart.js). HR-gated by the parent /performance route guard. OnPush + signals.
 */
@Component({
  selector: 'app-recommendation-summary',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
      <div class="mb-6 flex items-center justify-between gap-3">
        <div>
          <a
            routerLink="/performance/recommendations"
            class="text-xs font-medium text-neutral-400 hover:text-neutral-600"
          >
            ← Back to workspace
          </a>
          <h1 class="mt-1 text-2xl font-semibold tracking-tight text-neutral-900">
            Recommendation Summary
          </h1>
          @if (summary(); as s) {
            <p class="mt-1 text-sm text-neutral-500">{{ s.cycleName }}</p>
          }
        </div>
      </div>

      @if (loading()) {
        <div class="grid gap-4 sm:grid-cols-4">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="h-24 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="reload()">
            Retry
          </button>
        </div>
      } @else if (summary(); as s) {
        <!-- Stat cards -->
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4" @fadeIn>
          <div
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            data-testid="stat-promotions"
          >
            <p class="text-xs font-medium uppercase text-neutral-400">
              Promotions
            </p>
            <p class="mt-1 text-3xl font-semibold text-neutral-900">
              {{ s.totalPromotions }}
            </p>
          </div>
          <div
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            data-testid="stat-bonus-pool"
          >
            <p class="text-xs font-medium uppercase text-neutral-400">
              Bonus pool
            </p>
            <p class="mt-1 text-3xl font-semibold text-neutral-900">
              {{ s.bonusPoolAllocated | number: '1.0-0' }}
            </p>
            <p class="text-xs text-neutral-400">{{ s.currency }}</p>
          </div>
          <div
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            data-testid="stat-increments"
          >
            <p class="text-xs font-medium uppercase text-neutral-400">
              Increments
            </p>
            <p class="mt-1 text-3xl font-semibold text-neutral-900">
              {{ s.totalIncrements }}
            </p>
          </div>
          <div
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            data-testid="stat-training"
          >
            <p class="text-xs font-medium uppercase text-neutral-400">
              Training nominations
            </p>
            <p class="mt-1 text-3xl font-semibold text-neutral-900">
              {{ s.totalTrainingNominations }}
            </p>
          </div>
        </div>

        <!-- Increment distribution by department (CSS bars, no chart lib) -->
        <div
          class="mt-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          @fadeIn
        >
          <h2 class="mb-4 text-sm font-semibold text-neutral-700">
            Allocation by department
          </h2>
          @if (s.byDepartment.length === 0) {
            <p class="text-sm text-neutral-400">No department data.</p>
          } @else {
            <div class="space-y-3">
              @for (d of s.byDepartment; track d.departmentId) {
                <div data-testid="dept-bar">
                  <div class="mb-1 flex items-center justify-between text-xs">
                    <span class="font-medium text-neutral-700">{{
                      d.departmentName
                    }}</span>
                    <span class="text-neutral-400"
                      >{{ d.allocatedAmount | number: '1.0-0' }}
                      {{ s.currency }}</span
                    >
                  </div>
                  <div
                    class="h-3 w-full overflow-hidden rounded-full bg-neutral-100"
                    role="progressbar"
                    [attr.aria-valuenow]="d.allocatedAmount"
                    aria-valuemin="0"
                    [attr.aria-valuemax]="allocationPeak()"
                  >
                    <div
                      class="h-full rounded-full bg-sky-500 transition-all duration-300"
                      [style.width.%]="barPercent(d)"
                      [attr.data-testid]="'dept-bar-fill-' + d.departmentId"
                    ></div>
                  </div>
                  <p class="mt-0.5 text-xs text-neutral-400">
                    {{ d.promotionCount }} promo · {{ d.bonusCount }} bonus ·
                    {{ d.incrementCount }} increment
                  </p>
                </div>
              }
            </div>
          }
        </div>

        <!-- Comparison vs previous cycle -->
        <div
          class="mt-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          @fadeIn
        >
          <h2 class="mb-4 text-sm font-semibold text-neutral-700">
            Versus previous cycle
          </h2>
          @if (s.comparison.length === 0) {
            <p class="text-sm text-neutral-400">No previous cycle to compare.</p>
          } @else {
            <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              @for (c of s.comparison; track c.label) {
                <div
                  class="rounded-lg border border-neutral-100 p-3"
                  data-testid="comparison-row"
                >
                  <p class="text-xs text-neutral-400">{{ c.label }}</p>
                  <div class="mt-1 flex items-baseline gap-2">
                    <span class="text-xl font-semibold text-neutral-900">{{
                      c.current
                    }}</span>
                    <span
                      class="text-xs font-medium"
                      [class.text-emerald-600]="delta(c) > 0"
                      [class.text-rose-600]="delta(c) < 0"
                      [class.text-neutral-400]="delta(c) === 0"
                      [attr.data-testid]="'delta-' + c.label"
                    >
                      {{ delta(c) > 0 ? '▲' : delta(c) < 0 ? '▼' : '–' }}
                      {{ absDelta(c) }}
                    </span>
                  </div>
                  <p class="text-xs text-neutral-400">
                    was {{ c.previous }}
                  </p>
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class RecommendationSummaryComponent implements OnInit {
  private readonly service = inject(RecommendationService);
  private readonly route = inject(ActivatedRoute);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<IRecommendationSummary | null>(null);
  readonly cycleId = signal<string>('');

  readonly allocationPeak = computed(() => {
    const s = this.summary();
    if (!s) return 0;
    return s.byDepartment.reduce(
      (max, d) => Math.max(max, d.allocatedAmount),
      0,
    );
  });

  ngOnInit(): void {
    this.cycleId.set(this.route.snapshot.queryParamMap.get('cycleId') ?? '');
    this.reload();
  }

  reload(): void {
    if (!this.cycleId()) {
      this.error.set('No cycle selected.');
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    this.service.getSummary(this.cycleId()).subscribe({
      next: (s) => {
        this.summary.set(s);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load the summary.');
        this.loading.set(false);
      },
    });
  }

  barPercent(d: IDepartmentRecommendationStat): number {
    return deptBarPercent(d.allocatedAmount, this.allocationPeak());
  }
  delta(c: ICycleComparisonStat): number {
    return comparisonDelta(c);
  }
  absDelta(c: ICycleComparisonStat): number {
    return Math.abs(comparisonDelta(c));
  }
}
