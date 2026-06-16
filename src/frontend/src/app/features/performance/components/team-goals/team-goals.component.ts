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
import { PerformanceGoalService } from '../../services/performance-goal.service';
import {
  GOAL_STATUS_BADGE,
  GOAL_STATUS_LABEL,
  GoalSettingStatus,
  IAppraisalCycle,
  ITeamGoalStatus,
} from '../../models/goal.models';

/**
 * US-PRF-001 (AC-4): Team goals dashboard. Lists the manager's direct reports
 * with their goal-setting status (draft / submitted / acknowledged) and a weight
 * progress indicator. Each card links into the per-member goal-setting view.
 *
 * The active-cycle window state (AC-5) is shown as a banner here so the manager
 * knows up-front whether editing is possible. This is the default `/performance`
 * route. Notion-like card grid, responsive from 360px.
 */
@Component({
  selector: 'app-team-goals',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '220ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          Team goals
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          @if (cycle(); as c) {
            {{ c.name }} — goal-setting for your direct reports.
          } @else {
            Set and track goals for your team this appraisal cycle.
          }
        </p>
      </div>

      <!-- AC-5: closed-window banner -->
      @if (cycle(); as c) {
        @if (!c.goalSettingOpen) {
          <div
            @fadeIn
            class="mb-6 flex items-start gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
            role="status"
          >
            <span aria-hidden="true">🔒</span>
            <span>The goal-setting window for this cycle has closed</span>
          </div>
        }
      }

      <!-- Loading skeletons -->
      @if (loading()) {
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          @for (i of [1, 2, 3, 4, 5, 6]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (members().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-12 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">
            No team members yet
          </p>
          <p class="mt-1 text-sm text-neutral-500">
            You have no direct reports assigned in the org tree for this cycle.
          </p>
        </div>
      } @else {
        <div
          class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
          @fadeIn
        >
          @for (m of members(); track m.employeeId) {
            <a
              [routerLink]="memberLink(m.employeeId)"
              class="group block rounded-xl border border-neutral-200 bg-white p-4 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus:ring-2 focus:ring-offset-2"
              [style.--tw-ring-color]="'var(--brand-primary)'"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p
                    class="truncate text-sm font-semibold text-neutral-900"
                    [title]="m.employeeName"
                  >
                    {{ m.employeeName }}
                  </p>
                  @if (m.jobTitle) {
                    <p class="truncate text-xs text-neutral-500">
                      {{ m.jobTitle }}
                    </p>
                  }
                </div>
                <span
                  class="shrink-0 rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="statusBadge(m.status)"
                >
                  {{ statusLabel(m.status) }}
                </span>
              </div>

              <!-- Weight progress (AC-4 progress indicator) -->
              <div class="mt-4">
                <div
                  class="flex items-center justify-between text-xs text-neutral-500"
                >
                  <span>{{ m.goalCount }} goal{{ m.goalCount === 1 ? '' : 's' }}</span>
                  <span>{{ m.totalWeight }}% allocated</span>
                </div>
                <div
                  class="mt-1.5 h-2 w-full overflow-hidden rounded-full bg-neutral-100"
                  role="progressbar"
                  [attr.aria-valuenow]="m.totalWeight"
                  aria-valuemin="0"
                  aria-valuemax="100"
                  [attr.aria-label]="'Weight allocated for ' + m.employeeName"
                >
                  <div
                    class="h-full rounded-full transition-all"
                    [style.width.%]="clamp(m.totalWeight)"
                    [style.background-color]="
                      m.totalWeight === 100
                        ? '#10b981'
                        : 'var(--brand-primary)'
                    "
                  ></div>
                </div>
              </div>
            </a>
          }
        </div>
      }
    </div>
  `,
})
export class TeamGoalsComponent implements OnInit {
  private readonly goalService = inject(PerformanceGoalService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly cycle = signal<IAppraisalCycle | null>(null);
  readonly members = signal<ITeamGoalStatus[]>([]);

  /** Whether the goal-setting window is open (AC-5); defaults closed until known. */
  readonly windowOpen = computed(() => this.cycle()?.goalSettingOpen ?? false);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.goalService.getActiveCycle().subscribe({
      next: (cycle) => {
        this.cycle.set(cycle);
        this.loadTeam(cycle.id);
      },
      error: () => {
        this.error.set('Unable to load the active appraisal cycle.');
        this.loading.set(false);
      },
    });
  }

  private loadTeam(cycleId: string): void {
    this.goalService.getTeamStatus(cycleId).subscribe({
      next: (rows) => {
        this.members.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load your team goals.');
        this.loading.set(false);
      },
    });
  }

  memberLink(employeeId: string): unknown[] {
    return ['/performance', 'goals', employeeId];
  }

  statusBadge(status: GoalSettingStatus): string {
    return GOAL_STATUS_BADGE[status];
  }

  statusLabel(status: GoalSettingStatus): string {
    return GOAL_STATUS_LABEL[status];
  }

  clamp(value: number): number {
    return Math.max(0, Math.min(100, value));
  }
}
