import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  DestroyRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { TranslateModule } from '@ngx-translate/core';
import { ReportChartComponent } from '../reports/components/report-chart/report-chart.component';
import { IReportChart } from '../reports/models/reports.models';
import { DashboardService } from './dashboard.service';
import {
  IDashboardResponse,
  IDashboardWidget,
} from './dashboard.models';

/** Auto-refresh cadence (FR-9: every 5 minutes, configurable). */
const AUTO_REFRESH_MS = 5 * 60 * 1000;

/**
 * US-RPT-005: server-driven, role-based dashboard.
 *
 * The backend returns the ROLE-APPROPRIATE widget set; this component renders
 * them GENERICALLY through ONE widget-card template (KPI value + trend + mini
 * chart + item list + card click-through), driven entirely by which fields are
 * present on each {@link IDashboardWidget}. There is NO role logic here beyond
 * the responsive grid layout (BR-1 — visibility is server-authoritative).
 *
 * Trend colour follows `trendIsPositive` (NOT the arrow direction): true→green,
 * false→red, null→neutral (§8). Card click-through navigates to `linkUrl` with
 * `linkFilters` as query params (AC-4 / FR-5).
 *
 * Responsive grid (NFR-4 / §8): 4-col ≥1280px, 2-col 768–1279px, 1-col <768px.
 * A11y (NFR-5): each card is a <section> with an aria-label and is keyboard
 * activable when it is a click-through.
 */
@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslateModule, ReportChartComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-container">
      <!-- Header: greeting + date + refresh (UI §8 / FR-9) -->
      <div class="mb-8 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            {{ greeting() }}
          </h1>
          <p class="mt-1 text-sm text-neutral-500">{{ todayLabel() }}</p>
        </div>
        <button
          type="button"
          class="btn-secondary inline-flex items-center gap-2"
          [disabled]="loading()"
          (click)="refresh()"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 20 20"
            fill="currentColor"
            class="w-4 h-4"
            [class.animate-spin]="loading()"
          >
            <path
              fill-rule="evenodd"
              d="M15.312 11.424a5.5 5.5 0 0 1-9.201 2.466l-.312-.311h1.701a.75.75 0 0 0 0-1.5H3.5a.75.75 0 0 0-.75.75v3.5a.75.75 0 0 0 1.5 0v-1.6l.31.31a7 7 0 0 0 11.712-3.138.75.75 0 0 0-1.449-.39Zm-9.201-3.85a5.5 5.5 0 0 1 9.201-2.466l.312.311H13.92a.75.75 0 0 0 0 1.5h3.5a.75.75 0 0 0 .75-.75v-3.5a.75.75 0 0 0-1.5 0v1.6l-.31-.31A7 7 0 0 0 4.662 7.21a.75.75 0 1 0 1.449.39Z"
              clip-rule="evenodd"
            />
          </svg>
          <span>{{ 'dashboard.refresh' | translate }}</span>
        </button>
      </div>

      <!-- Skeleton grid while loading (UI §8) -->
      @if (loading() && widgets().length === 0) {
        <div class="dashboard-grid" aria-busy="true">
          @for (s of skeletonSlots; track s) {
            <div class="card-notion animate-pulse">
              <div class="h-3 w-24 bg-neutral-200 rounded mb-4"></div>
              <div class="h-8 w-20 bg-neutral-200 rounded mb-3"></div>
              <div class="h-10 w-full bg-neutral-100 rounded"></div>
            </div>
          }
        </div>
      } @else if (error()) {
        <div class="card-notion text-center py-10">
          <p class="text-sm text-neutral-600 mb-3">
            {{ 'dashboard.loadError' | translate }}
          </p>
          <button type="button" class="btn-primary" (click)="refresh()">
            {{ 'dashboard.retry' | translate }}
          </button>
        </div>
      } @else {
        <div class="dashboard-grid">
          @for (w of widgets(); track w.widgetKey) {
            <section
              class="card-notion dashboard-card animate-fade-in"
              [class.is-clickable]="!!w.linkUrl"
              [attr.role]="w.linkUrl ? 'link' : 'region'"
              [attr.aria-label]="w.label"
              [attr.tabindex]="w.linkUrl ? 0 : null"
              (click)="onCardClick(w)"
              (keydown.enter)="onCardClick(w)"
              (keydown.space)="onCardClick(w); $event.preventDefault()"
            >
              <div class="flex items-center justify-between mb-2">
                <span class="text-sm font-medium text-neutral-500">{{ w.label }}</span>
                @if (w.linkUrl) {
                  <svg
                    xmlns="http://www.w3.org/2000/svg"
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    class="w-4 h-4 text-neutral-300"
                    aria-hidden="true"
                  >
                    <path
                      fill-rule="evenodd"
                      d="M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z"
                      clip-rule="evenodd"
                    />
                  </svg>
                }
              </div>

              <!-- KPI value + unit (UI §8) -->
              @if (w.value !== null && w.value !== undefined) {
                <div class="flex items-baseline gap-1">
                  <span class="text-3xl font-bold text-neutral-900">{{ w.value }}</span>
                  @if (w.unit) {
                    <span class="text-base font-medium text-neutral-400">{{ w.unit }}</span>
                  }
                </div>
              }

              <!-- Trend arrow + % — coloured by trendIsPositive, NOT direction (§8) -->
              @if (w.trendDirection) {
                <div
                  class="mt-1 flex items-center gap-1 text-xs font-medium"
                  [ngClass]="trendClass(w)"
                >
                  <span aria-hidden="true">{{ trendArrow(w) }}</span>
                  @if (w.trendPercentage !== null) {
                    <span>{{ w.trendPercentage }}%</span>
                  }
                  <span class="sr-only">{{ trendSrText(w) }}</span>
                </div>
              }

              <!-- Mini visualisation (FR-2) -->
              @if (w.miniChart) {
                <div class="mt-3">
                  @switch (w.miniChart.type) {
                    @case ('sparkline') {
                      <div class="dashboard-sparkline">
                        <app-report-chart [chart]="chartFor(w)" />
                      </div>
                    }
                    @case ('donut') {
                      <div class="dashboard-donut">
                        <app-report-chart [chart]="chartFor(w)" />
                      </div>
                    }
                    @case ('progress') {
                      <div
                        class="w-full bg-neutral-100 rounded-full h-2.5 overflow-hidden"
                        role="progressbar"
                        [attr.aria-valuenow]="progressValue(w)"
                        [attr.aria-valuemin]="0"
                        [attr.aria-valuemax]="w.miniChart.max ?? 100"
                        [attr.aria-label]="w.label"
                      >
                        <div
                          class="h-2.5 rounded-full bg-indigo-500 transition-all duration-300"
                          [style.width.%]="progressPercent(w)"
                        ></div>
                      </div>
                    }
                  }
                </div>
              }

              <!-- Item list: approvals / quick-actions / pending tasks (FR-6/FR-7) -->
              @if (w.items && w.items.length > 0) {
                <ul class="mt-3 space-y-1.5" (click)="$event.stopPropagation()">
                  @for (item of w.items; track item.label) {
                    <li>
                      @if (item.linkUrl) {
                        <a
                          [routerLink]="item.linkUrl"
                          [queryParams]="item.linkFilters || {}"
                          class="flex items-center justify-between gap-2 text-sm text-neutral-700 hover:text-indigo-600 rounded-lg px-2 py-1.5 hover:bg-neutral-50 transition-colors"
                        >
                          <span class="truncate">{{ item.label }}</span>
                          @if (item.value) {
                            <span class="text-xs font-medium text-neutral-400 shrink-0">{{ item.value }}</span>
                          }
                        </a>
                      } @else {
                        <div class="flex items-center justify-between gap-2 text-sm text-neutral-700 px-2 py-1.5">
                          <span class="truncate">{{ item.label }}</span>
                          @if (item.value) {
                            <span class="text-xs font-medium text-neutral-400 shrink-0">{{ item.value }}</span>
                          }
                        </div>
                      }
                    </li>
                  }
                </ul>
              }
            </section>
          } @empty {
            <div class="card-notion col-span-full text-center py-10 text-sm text-neutral-500">
              {{ 'dashboard.empty' | translate }}
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      /* NFR-4 / §8: 1-col <768px, 2-col 768–1279px, 4-col ≥1280px. */
      .dashboard-grid {
        display: grid;
        grid-template-columns: repeat(1, minmax(0, 1fr));
        gap: 1rem;
      }
      @media (min-width: 768px) {
        .dashboard-grid {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }
      }
      @media (min-width: 1280px) {
        .dashboard-grid {
          grid-template-columns: repeat(4, minmax(0, 1fr));
        }
      }
      .dashboard-card.is-clickable {
        cursor: pointer;
      }
      .dashboard-card.is-clickable:hover {
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
        transform: translateY(-2px);
      }
      .dashboard-card {
        transition: box-shadow 0.2s ease, transform 0.2s ease;
      }
      /* Compact sparkline (~120x40, §8). app-report-chart fills its wrapper. */
      .dashboard-sparkline {
        height: 40px;
      }
      .dashboard-donut {
        height: 140px;
      }
      .animate-fade-in {
        animation: dashFadeIn 0.25s ease-out both;
      }
      @keyframes dashFadeIn {
        from {
          opacity: 0;
          transform: translateY(4px);
        }
        to {
          opacity: 1;
          transform: translateY(0);
        }
      }
    `,
  ],
})
export class DashboardComponent {
  private readonly service = inject(DashboardService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Skeleton placeholders shown on first load (UI §8). */
  readonly skeletonSlots = [0, 1, 2, 3, 4, 5, 6, 7];

  readonly loading = signal(true);
  readonly error = signal(false);
  private readonly data = signal<IDashboardResponse | null>(null);

  readonly widgets = computed<IDashboardWidget[]>(
    () => this.data()?.widgets ?? []
  );

  /** "Good morning/afternoon/evening, {name}" (§8). */
  readonly greeting = computed(() => {
    const name = this.data()?.greetingName?.trim();
    const hour = new Date().getHours();
    const part =
      hour < 12 ? 'morning' : hour < 18 ? 'afternoon' : 'evening';
    const lead =
      part === 'morning'
        ? 'Good morning'
        : part === 'afternoon'
          ? 'Good afternoon'
          : 'Good evening';
    return name ? `${lead}, ${name}` : lead;
  });

  /** Current date, long format (§8). */
  readonly todayLabel = computed(() =>
    new Date().toLocaleDateString(undefined, {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    })
  );

  constructor() {
    // Initial load now (synchronous-friendly for OnPush + tests).
    this.load(false);

    // Auto-refresh every 5 minutes (FR-9). interval() fires on each tick (not
    // on subscribe); takeUntilDestroyed tears it down on component destroy.
    interval(AUTO_REFRESH_MS)
      .pipe(
        switchMap(() => this.service.getWidgets(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (res) => {
          this.data.set(res);
          this.loading.set(false);
          this.error.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.error.set(true);
        },
      });
  }

  /** Fetch widgets and reflect the result into the view signals. */
  private load(refresh: boolean): void {
    this.loading.set(true);
    this.service.getWidgets(refresh).subscribe({
      next: (res) => {
        this.data.set(res);
        this.loading.set(false);
        this.error.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set(true);
      },
    });
  }

  /** FR-9: on-demand refresh (cache-bypassing). */
  refresh(): void {
    this.load(true);
  }

  // ─── Card click-through (AC-4 / FR-5) ───────────────────────────────────

  onCardClick(w: IDashboardWidget): void {
    if (!w.linkUrl) {
      return;
    }
    this.router.navigate([w.linkUrl], {
      queryParams: w.linkFilters ?? {},
    });
  }

  // ─── Trend rendering (§8: colour by trendIsPositive, NOT direction) ──────

  trendArrow(w: IDashboardWidget): string {
    switch (w.trendDirection) {
      case 'up':
        return '↑';
      case 'down':
        return '↓';
      default:
        return '→';
    }
  }

  trendClass(w: IDashboardWidget): string {
    if (w.trendIsPositive === true) {
      return 'text-green-600';
    }
    if (w.trendIsPositive === false) {
      return 'text-red-600';
    }
    return 'text-neutral-400';
  }

  trendSrText(w: IDashboardWidget): string {
    const dir =
      w.trendDirection === 'up'
        ? 'increased'
        : w.trendDirection === 'down'
          ? 'decreased'
          : 'unchanged';
    const pct = w.trendPercentage !== null ? ` by ${w.trendPercentage}%` : '';
    return `${dir}${pct} versus the previous period`;
  }

  // ─── Mini-chart adaptation (reuse the US-RPT-001 chart wrapper) ──────────

  /**
   * Adapt a widget's miniChart into the generic IReportChart the US-RPT-001
   * wrapper consumes: 'sparkline' → a single-series line, 'donut' → a pie.
   */
  chartFor(w: IDashboardWidget): IReportChart {
    const mc = w.miniChart!;
    const kind: IReportChart['kind'] = mc.type === 'donut' ? 'pie' : 'line';
    return {
      kind,
      title: w.label,
      series: [
        {
          name: w.label,
          points: mc.series.map((p) => ({ label: p.label, value: p.value })),
        },
      ],
    };
  }

  // ─── Progress-bar helpers ────────────────────────────────────────────────

  /** Current value for a progress mini chart (sum of the series). */
  progressValue(w: IDashboardWidget): number {
    return (w.miniChart?.series ?? []).reduce((n, p) => n + p.value, 0);
  }

  /** Filled percentage (0–100) for a progress bar, clamped. */
  progressPercent(w: IDashboardWidget): number {
    const max = w.miniChart?.max ?? 0;
    if (max <= 0) {
      return 0;
    }
    const pct = (this.progressValue(w) / max) * 100;
    return Math.max(0, Math.min(100, pct));
  }
}
