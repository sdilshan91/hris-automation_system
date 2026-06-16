import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { PerformanceDashboardService } from '../../services/performance-dashboard.service';
import {
  IDepartmentDrilldown,
  PerformerTrend,
  scorePercent,
  PERFORMER_TREND_CLASSES,
  PERFORMER_TREND_GLYPH,
} from '../../models/dashboard.models';

/**
 * US-PRF-007 (FR-5): the department drill-down. Reached by clicking a department bar
 * on the dashboard; shows the department's employees with individual scores and a
 * breadcrumb trail (Dashboard > Department > Employee). Each employee links to their
 * own manager-review screen.
 *
 * No chart lib (option b) — score bars are CSS/Tailwind. Reached at
 * `/performance/dashboard/departments/:departmentId?cycleId=…` (manager/HR-gated).
 */
@Component({
  selector: 'app-department-drilldown',
  standalone: true,
  imports: [CommonModule, RouterLink],
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
      <!-- Breadcrumb (FR-5: Dashboard > Department > Employee) -->
      <nav class="text-xs text-neutral-400" aria-label="Breadcrumb">
        <a
          [routerLink]="['/performance', 'dashboard']"
          class="transition hover:text-neutral-700"
          data-testid="crumb-dashboard"
          >Dashboard</a
        >
        <span class="mx-1">/</span>
        <span class="text-neutral-600" data-testid="crumb-department">
          {{ drilldown()?.departmentName ?? 'Department' }}
        </span>
      </nav>

      <h1 class="mt-1 text-2xl font-semibold tracking-tight text-neutral-900">
        {{ drilldown()?.departmentName ?? 'Department' }}
      </h1>
      @if (drilldown(); as d) {
        <p class="mt-1 text-sm text-neutral-500">
          {{ d.cycleLabel }} ·
          @if (d.averageScore != null) {
            avg {{ d.averageScore | number: '1.1-1' }} / {{ d.scoreScaleMax }}
          } @else {
            no rated reviews
          }
          · {{ d.employees.length }} employee(s)
        </p>
      }

      <div class="mt-6">
        @if (loading()) {
          <div class="space-y-3" data-testid="skeletons">
            @for (i of [1, 2, 3, 4]; track i) {
              <div class="h-14 animate-pulse rounded-lg bg-neutral-100"></div>
            }
          </div>
        } @else if (error()) {
          <div
            class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
            role="alert"
          >
            {{ error() }}
            <button class="ml-2 font-medium underline" (click)="load()">Retry</button>
          </div>
        } @else if (drilldown(); as d) {
          @if (d.employees.length === 0) {
            <p class="text-sm text-neutral-400">No employees in this department.</p>
          } @else {
            <ul @fadeIn class="space-y-2">
              @for (e of d.employees; track e.employeeId) {
                <li
                  class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
                  data-testid="employee-row"
                >
                  <div class="flex items-center gap-3">
                    <div class="min-w-0 flex-1">
                      <!-- Employee link completes the breadcrumb's 3rd level (FR-5) -->
                      <a
                        [routerLink]="['/performance', 'reviews', e.employeeId]"
                        class="truncate text-sm font-medium text-neutral-900 transition hover:text-neutral-600"
                        data-testid="employee-link"
                        >{{ e.employeeName }}</a
                      >
                      <p class="truncate text-xs text-neutral-400">
                        @if (e.jobTitle) { {{ e.jobTitle }} }
                        @if (e.grade) { · {{ e.grade }} }
                      </p>
                    </div>
                    <span class="text-xs" [class]="trendClass(e.trend)" [attr.title]="e.trend">
                      {{ trendGlyph(e.trend) }}
                    </span>
                    <span class="w-12 text-right text-sm font-semibold text-neutral-900">
                      @if (e.score != null) { {{ e.score | number: '1.0-1' }} } @else { — }
                    </span>
                  </div>
                  <div class="mt-2 h-2 w-full overflow-hidden rounded-full bg-neutral-100">
                    <div
                      class="h-full rounded-full bg-neutral-700 transition-all"
                      [style.width.%]="percent(e.score, d.scoreScaleMax)"
                      data-testid="employee-bar"
                    ></div>
                  </div>
                </li>
              }
            </ul>
          }
        }
      </div>
    </div>
  `,
})
export class DepartmentDrilldownComponent implements OnInit {
  private readonly service = inject(PerformanceDashboardService);
  private readonly route = inject(ActivatedRoute);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly drilldown = signal<IDepartmentDrilldown | null>(null);

  private departmentId = '';
  private cycleId?: string;

  ngOnInit(): void {
    this.departmentId = this.route.snapshot.paramMap.get('departmentId') ?? '';
    this.cycleId = this.route.snapshot.queryParamMap.get('cycleId') ?? undefined;
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getDepartmentDrilldown(this.departmentId, this.cycleId).subscribe({
      next: (d) => {
        this.drilldown.set(d);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load the department drill-down.');
        this.loading.set(false);
      },
    });
  }

  percent(score: number | null, scaleMax: number): number {
    return scorePercent(score, scaleMax);
  }

  trendClass(trend: PerformerTrend): string {
    return PERFORMER_TREND_CLASSES[trend];
  }

  trendGlyph(trend: PerformerTrend): string {
    return PERFORMER_TREND_GLYPH[trend];
  }
}
