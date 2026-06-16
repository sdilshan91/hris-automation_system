import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { GoalProgressService } from '../../services/goal-progress.service';
import {
  GOAL_COMMENT_MAX,
  GOAL_STATUS_BADGE,
  GOAL_STATUS_BAR,
  GOAL_STATUS_LABEL,
  GoalProgressStatus,
  IEmployeeGoalProgress,
  IGoalUpdate,
  ITeamGoalProgressRow,
  completionDonut,
} from '../../models/goal-progress.models';

/**
 * US-PRF-009 MANAGER side (AC-4). A DISTINCT view from the existing US-PRF-001
 * `team-goals` goal-SETTING dashboard: this is goal-PROGRESS tracking. Route
 * `/performance/team-goal-progress` (manager/HR role-gated by the parent /performance
 * guard; the backend restricts to the caller's direct reports via
 * `Performance.Review.Team` + RLS, BR-5).
 *
 * Top level: a summary table of direct reports — overall completion %, # goals at
 * risk, last-update date. Selecting a row drills down (AC-4) to that report's goal
 * cards + per-goal update timeline, where the manager can post a comment on any
 * update (FR-8). Notion-like table, responsive from 360px (horizontal scroll).
 */
@Component({
  selector: 'app-team-goal-progress',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
    <div class="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8">
      <div class="mb-6 flex items-center gap-3">
        @if (selected()) {
          <button
            type="button"
            class="rounded-lg px-2 py-1 text-sm text-neutral-500 hover:bg-neutral-100"
            (click)="backToTeam()"
            data-testid="back-btn"
          >
            ← Team
          </button>
        }
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          @if (selected(); as e) {
            {{ e.employeeName }}
          } @else {
            Team goal progress
          }
        </h1>
      </div>

      @if (loading()) {
        <div class="space-y-3">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-14 animate-pulse rounded-lg bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ loadError() }}
          <button class="ml-2 font-medium underline" (click)="loadTeam()">
            Retry
          </button>
        </div>
      } @else if (selected(); as emp) {
        <!-- AC-4 drill-down: one employee's goals + timelines -->
        <div
          class="mb-5 flex items-center gap-4 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          @fadeIn
        >
          <svg viewBox="0 0 120 120" class="h-16 w-16 -rotate-90" aria-hidden="true">
            <circle cx="60" cy="60" r="50" fill="none" class="stroke-neutral-100" stroke-width="12" />
            <circle
              cx="60" cy="60" r="50" fill="none" stroke-width="12" stroke-linecap="round"
              class="stroke-[var(--brand-primary)] transition-all duration-700 ease-out"
              [attr.stroke-dasharray]="empDonut(emp.overallCompletionPercent).circumference"
              [attr.stroke-dashoffset]="empDonut(emp.overallCompletionPercent).dashOffset"
            />
          </svg>
          <div>
            <p class="text-2xl font-semibold tabular-nums text-neutral-900">
              {{ emp.overallCompletionPercent }}%
            </p>
            <p class="text-sm text-neutral-500">Overall completion</p>
          </div>
        </div>

        <ul class="space-y-4">
          @for (goal of emp.goals; track goal.goalId) {
            <li
              class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
              @fadeIn
              data-testid="emp-goal-card"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="truncate font-medium text-neutral-900">{{ goal.title }}</p>
                  <p class="mt-0.5 text-sm text-neutral-500">{{ goal.target }}</p>
                </div>
                <span
                  class="shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="statusBadge(goal.status)"
                >
                  {{ statusLabel(goal.status) }}
                </span>
              </div>
              <div class="mt-3 h-2 w-full overflow-hidden rounded-full bg-neutral-100">
                <div
                  class="h-full rounded-full transition-all duration-700 ease-out"
                  [class]="statusBar(goal.status)"
                  [style.width.%]="goal.progressPercent"
                ></div>
              </div>

              <button
                type="button"
                class="mt-3 text-xs font-medium text-neutral-600 hover:underline"
                (click)="toggleHistory(goal.goalId)"
                [attr.aria-expanded]="isExpanded(goal.goalId)"
                data-testid="emp-toggle-history"
              >
                {{ isExpanded(goal.goalId) ? 'Hide updates' : 'View updates' }}
              </button>

              @if (isExpanded(goal.goalId)) {
                <div class="mt-3 border-t border-neutral-100 pt-3" @fadeIn>
                  @if (historyLoading()) {
                    <div class="h-10 animate-pulse rounded-lg bg-neutral-100"></div>
                  } @else if (history().length === 0) {
                    <p class="text-sm text-neutral-400">No updates posted yet.</p>
                  } @else {
                    <ol class="space-y-4" data-testid="emp-timeline">
                      @for (u of history(); track u.updateId) {
                        <li class="relative pl-5" data-testid="emp-timeline-item">
                          <span
                            class="absolute left-0 top-1.5 h-2 w-2 rounded-full"
                            [class]="statusBar(u.status)"
                          ></span>
                          <div class="flex items-center justify-between gap-2">
                            <span
                              class="rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                              [class]="statusBadge(u.status)"
                            >
                              {{ statusLabel(u.status) }}
                            </span>
                            <span class="text-xs text-neutral-400">
                              {{ u.createdOn | date: 'medium' }}
                            </span>
                          </div>
                          <p class="mt-1 text-xs text-neutral-500">
                            @if (u.previousProgressPercent !== null) {
                              {{ u.previousProgressPercent }}% →
                            }
                            <span class="font-medium text-neutral-700">{{ u.progressPercent }}%</span>
                          </p>
                          @if (u.notes) {
                            <p class="mt-1 whitespace-pre-line text-sm text-neutral-600">
                              {{ u.notes }}
                            </p>
                          }
                          <!-- FR-8 comment thread + manager input -->
                          @if (u.comments.length) {
                            <ul
                              class="mt-2 space-y-1 border-l-2 border-neutral-100 pl-3"
                              data-testid="comment-thread"
                            >
                              @for (c of u.comments; track c.commentId) {
                                <li class="text-xs text-neutral-600">
                                  <span class="font-medium text-neutral-700">{{ c.authorName }}</span>
                                  · {{ c.comment }}
                                </li>
                              }
                            </ul>
                          }
                          <div class="mt-2 flex gap-2">
                            <input
                              type="text"
                              [maxlength]="commentMax"
                              [ngModel]="commentDraft()[u.updateId] || ''"
                              (ngModelChange)="setCommentDraft(u.updateId, $event)"
                              class="flex-1 rounded-lg border border-neutral-200 px-3 py-1.5 text-xs shadow-sm focus:border-[var(--brand-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-primary)]"
                              placeholder="Add a comment…"
                              [attr.data-testid]="'comment-input-' + u.updateId"
                            />
                            <button
                              type="button"
                              class="rounded-lg px-3 py-1.5 text-xs font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                              [style.background-color]="'var(--brand-primary)'"
                              [disabled]="
                                commenting() || !(commentDraft()[u.updateId] || '').trim()
                              "
                              (click)="postComment(u)"
                              [attr.data-testid]="'comment-send-' + u.updateId"
                            >
                              Send
                            </button>
                          </div>
                        </li>
                      }
                    </ol>
                  }
                </div>
              }
            </li>
          }
        </ul>
      } @else {
        <!-- AC-4 summary table -->
        @if (team().length === 0) {
          <div
            class="rounded-xl border border-dashed border-neutral-300 bg-white px-6 py-10 text-center text-sm text-neutral-500"
          >
            You have no direct reports with active goals.
          </div>
        } @else {
          <div
            class="overflow-x-auto rounded-xl border border-neutral-200 bg-white shadow-sm"
            @fadeIn
          >
            <table class="min-w-full divide-y divide-neutral-100 text-sm">
              <thead class="bg-neutral-50 text-left text-xs uppercase tracking-wide text-neutral-500">
                <tr>
                  <th class="px-4 py-3 font-medium">Employee</th>
                  <th class="px-4 py-3 font-medium">Completion</th>
                  <th class="px-4 py-3 font-medium">At risk</th>
                  <th class="px-4 py-3 font-medium">Last update</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-neutral-50">
                @for (row of team(); track row.employeeId) {
                  <tr
                    class="cursor-pointer transition hover:bg-neutral-50"
                    (click)="openEmployee(row)"
                    data-testid="team-row"
                  >
                    <td class="px-4 py-3">
                      <p class="font-medium text-neutral-900">{{ row.employeeName }}</p>
                      @if (row.jobTitle) {
                        <p class="text-xs text-neutral-400">{{ row.jobTitle }}</p>
                      }
                    </td>
                    <td class="px-4 py-3">
                      <div class="flex items-center gap-2">
                        <div class="h-1.5 w-24 overflow-hidden rounded-full bg-neutral-100">
                          <div
                            class="h-full rounded-full bg-[var(--brand-primary)] transition-all duration-700 ease-out"
                            [style.width.%]="row.overallCompletionPercent"
                          ></div>
                        </div>
                        <span class="tabular-nums text-neutral-700">
                          {{ row.overallCompletionPercent }}%
                        </span>
                      </div>
                    </td>
                    <td class="px-4 py-3">
                      @if (row.goalsAtRisk > 0) {
                        <span
                          class="rounded-full bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700"
                          data-testid="at-risk-badge"
                        >
                          {{ row.goalsAtRisk }}
                        </span>
                      } @else {
                        <span class="text-neutral-300">—</span>
                      }
                    </td>
                    <td class="px-4 py-3 text-neutral-500">
                      @if (row.lastUpdatedOn) {
                        {{ row.lastUpdatedOn | date: 'mediumDate' }}
                      } @else {
                        <span class="text-neutral-300">No updates</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </div>
  `,
})
export class TeamGoalProgressComponent implements OnInit {
  private readonly service = inject(GoalProgressService);
  private readonly toastr = inject(ToastrService);

  readonly commentMax = GOAL_COMMENT_MAX;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly team = signal<ITeamGoalProgressRow[]>([]);

  readonly selected = signal<IEmployeeGoalProgress | null>(null);

  readonly expandedGoalId = signal<string | null>(null);
  readonly history = signal<IGoalUpdate[]>([]);
  readonly historyLoading = signal(false);

  readonly commentDraft = signal<Record<string, string>>({});
  readonly commenting = signal(false);

  ngOnInit(): void {
    this.loadTeam();
  }

  loadTeam(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getTeamProgress().subscribe({
      next: (rows) => {
        this.team.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load team goal progress.');
        this.loading.set(false);
      },
    });
  }

  openEmployee(row: ITeamGoalProgressRow): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.expandedGoalId.set(null);
    this.history.set([]);
    this.service.getEmployeeProgress(row.employeeId).subscribe({
      next: (emp) => {
        this.selected.set(emp);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load this employee’s goals.');
        this.loading.set(false);
      },
    });
  }

  backToTeam(): void {
    this.selected.set(null);
    this.expandedGoalId.set(null);
    this.history.set([]);
  }

  isExpanded(goalId: string): boolean {
    return this.expandedGoalId() === goalId;
  }

  toggleHistory(goalId: string): void {
    if (this.expandedGoalId() === goalId) {
      this.expandedGoalId.set(null);
      this.history.set([]);
      return;
    }
    this.expandedGoalId.set(goalId);
    this.historyLoading.set(true);
    this.history.set([]);
    this.service.getGoalUpdates(goalId).subscribe({
      next: (updates) => {
        this.history.set(updates);
        this.historyLoading.set(false);
      },
      error: () => {
        this.historyLoading.set(false);
        this.toastr.error('Could not load updates.');
      },
    });
  }

  setCommentDraft(updateId: string, value: string): void {
    this.commentDraft.set({ ...this.commentDraft(), [updateId]: value });
  }

  postComment(update: IGoalUpdate): void {
    const text = (this.commentDraft()[update.updateId] || '').trim();
    if (!text || this.commenting()) {
      return;
    }
    this.commenting.set(true);
    this.service.addComment(update.updateId, text).subscribe({
      next: (comment) => {
        this.commenting.set(false);
        this.history.set(
          this.history().map((u) =>
            u.updateId === update.updateId
              ? { ...u, comments: [...u.comments, comment] }
              : u,
          ),
        );
        this.setCommentDraft(update.updateId, '');
        this.toastr.success('Comment posted.');
      },
      error: () => {
        this.commenting.set(false);
        this.toastr.error('Could not post your comment. Please try again.');
      },
    });
  }

  empDonut = (pct: number) => completionDonut(pct, 50);
  statusBadge = (status: GoalProgressStatus): string => GOAL_STATUS_BADGE[status];
  statusLabel = (status: GoalProgressStatus): string => GOAL_STATUS_LABEL[status];
  statusBar = (status: GoalProgressStatus): string => GOAL_STATUS_BAR[status];
}
