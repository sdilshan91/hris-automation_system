import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { GoalProgressService } from '../../services/goal-progress.service';
import {
  GOAL_PROGRESS_STATUSES,
  GOAL_STATUS_BADGE,
  GOAL_STATUS_BAR,
  GOAL_STATUS_LABEL,
  GOAL_UPDATE_MAX_FILES,
  GOAL_UPDATE_MAX_FILE_BYTES,
  GOAL_UPDATE_NOTES_MAX,
  GOAL_WINDOW_CLOSED_MESSAGE,
  GoalProgressStatus,
  IGoalProgress,
  IGoalUpdate,
  IMyGoals,
  clampPercent,
  completionBandClass,
  completionDonut,
  statusForProgress,
  weightedOverallCompletion,
} from '../../models/goal-progress.models';

/**
 * US-PRF-009 EMPLOYEE side (AC-1/AC-2/AC-3). The self-service "My Goals" screen under
 * `/my-review/my-goals`. Goal cards show title, target, current progress % with an
 * animated bar, a color-coded status chip, and the last-update date. "Add Update"
 * opens a bottom-sheet (NFR-4 mobile) with a progress slider, status selector, notes
 * textarea (≤2000), and optional file attachments (≤3); BR-2 auto-selects Completed
 * at 100% (overridable). Expanding a card lazily loads the append-only update timeline
 * (AC-3) including the per-update manager comment thread (FR-8). An SVG/CSS donut shows
 * the weighted overall completion (§8, NO chart lib).
 *
 * The backend enforces `Performance.Read.Self` + RLS so an employee sees only their
 * OWN goals; the closed-window state is rendered off the authoritative `windowOpen`
 * flag (BR-1), never computed from cycle dates.
 */
@Component({
  selector: 'app-my-goals',
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
    trigger('sheet', [
      transition(':enter', [
        style({ transform: 'translateY(100%)' }),
        animate('250ms cubic-bezier(0.32,0.72,0,1)', style({ transform: 'translateY(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateY(100%)' })),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-3xl px-4 pb-24 pt-6 sm:px-6 lg:px-8">
      <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
        My goals
      </h1>
      <p class="mt-1 text-sm text-neutral-500">
        Track your progress and keep your manager in the loop.
      </p>

      @if (loading()) {
        <div class="mt-6 space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="mt-6 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ loadError() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (data(); as d) {
        <!-- Overall completion donut (§8, SVG — no chart lib) -->
        <section
          class="mt-6 flex items-center gap-5 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          @fadeIn
          data-testid="overall-widget"
        >
          <svg viewBox="0 0 120 120" class="h-24 w-24 -rotate-90" aria-hidden="true">
            <circle
              cx="60" cy="60" r="50" fill="none"
              class="stroke-neutral-100" stroke-width="12"
            />
            <circle
              cx="60" cy="60" r="50" fill="none" stroke-width="12"
              stroke-linecap="round"
              class="stroke-[var(--brand-primary)] transition-all duration-700 ease-out"
              [attr.stroke-dasharray]="donut().circumference"
              [attr.stroke-dashoffset]="donut().dashOffset"
            />
          </svg>
          <div>
            <p
              class="text-3xl font-semibold tabular-nums"
              [class]="bandClass()"
              data-testid="overall-pct"
            >
              {{ overallCompletion() }}%
            </p>
            <p class="text-sm text-neutral-500">
              Overall completion · {{ d.cycleName }}
            </p>
          </div>
        </section>

        @if (!d.windowOpen) {
          <div
            class="mt-4 rounded-lg border border-neutral-200 bg-neutral-50 px-4 py-3 text-sm text-neutral-600"
            data-testid="window-closed"
          >
            {{ windowClosedMessage }}
          </div>
        }

        @if (d.goals.length === 0) {
          <div
            class="mt-6 rounded-xl border border-dashed border-neutral-300 bg-white px-6 py-10 text-center text-sm text-neutral-500"
          >
            No goals have been assigned for this cycle yet.
          </div>
        }

        <!-- Goal cards (swipeable list on mobile, NFR-4) -->
        <ul class="mt-6 space-y-4">
          @for (goal of d.goals; track goal.goalId) {
            <li
              class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm transition hover:shadow-md"
              @fadeIn
              data-testid="goal-card"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="truncate font-medium text-neutral-900">
                    {{ goal.title }}
                  </p>
                  <p class="mt-0.5 text-sm text-neutral-500">{{ goal.target }}</p>
                </div>
                <span
                  class="shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="statusBadge(goal.status)"
                  data-testid="status-chip"
                >
                  {{ statusLabel(goal.status) }}
                </span>
              </div>

              @if (goal.needsAttention) {
                <p
                  class="mt-2 inline-flex items-center gap-1 rounded-md bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700"
                  data-testid="needs-attention"
                >
                  Needs attention
                </p>
              }

              <!-- Animated progress bar (§8 CSS transition) -->
              <div class="mt-4">
                <div class="mb-1 flex justify-between text-xs text-neutral-500">
                  <span>Progress</span>
                  <span class="tabular-nums">{{ goal.progressPercent }}%</span>
                </div>
                <div
                  class="h-2 w-full overflow-hidden rounded-full bg-neutral-100"
                  role="progressbar"
                  [attr.aria-valuenow]="goal.progressPercent"
                  aria-valuemin="0"
                  aria-valuemax="100"
                >
                  <div
                    class="h-full rounded-full transition-all duration-700 ease-out"
                    [class]="statusBar(goal.status)"
                    [style.width.%]="goal.progressPercent"
                    data-testid="progress-bar"
                  ></div>
                </div>
              </div>

              <div class="mt-4 flex flex-wrap items-center gap-3 text-xs">
                <span class="text-neutral-400">
                  @if (goal.lastUpdatedOn) {
                    Last updated {{ goal.lastUpdatedOn | date: 'mediumDate' }}
                  } @else {
                    No updates yet
                  }
                </span>
                <span class="flex-1"></span>
                <button
                  type="button"
                  class="font-medium text-neutral-600 underline-offset-2 hover:underline"
                  (click)="toggleHistory(goal.goalId)"
                  [attr.aria-expanded]="isExpanded(goal.goalId)"
                  data-testid="toggle-history"
                >
                  {{ isExpanded(goal.goalId) ? 'Hide history' : 'View history' }}
                </button>
                @if (d.windowOpen) {
                  <button
                    type="button"
                    class="rounded-lg px-3 py-1.5 text-xs font-medium text-white shadow-sm transition hover:opacity-90"
                    [style.background-color]="'var(--brand-primary)'"
                    (click)="openUpdate(goal)"
                    data-testid="add-update-btn"
                  >
                    Add update
                  </button>
                }
              </div>

              <!-- Update history timeline (AC-3) -->
              @if (isExpanded(goal.goalId)) {
                <div class="mt-4 border-t border-neutral-100 pt-4" @fadeIn>
                  @if (historyLoading()) {
                    <div class="space-y-2">
                      @for (i of [1, 2]; track i) {
                        <div class="h-12 animate-pulse rounded-lg bg-neutral-100"></div>
                      }
                    </div>
                  } @else if (history().length === 0) {
                    <p class="text-sm text-neutral-400">No updates posted yet.</p>
                  } @else {
                    <ol class="space-y-4" data-testid="timeline">
                      @for (u of history(); track u.updateId) {
                        <li class="relative pl-5" data-testid="timeline-item">
                          <span
                            class="absolute left-0 top-1.5 h-2 w-2 rounded-full ring-2 ring-white"
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
                            <span class="font-medium text-neutral-700">
                              {{ u.progressPercent }}%
                            </span>
                            · {{ u.authorName }}
                          </p>
                          @if (u.notes) {
                            <p class="mt-1 whitespace-pre-line text-sm text-neutral-600">
                              {{ u.notes }}
                            </p>
                          }
                          @if (u.attachments.length) {
                            <ul class="mt-2 flex flex-wrap gap-2">
                              @for (a of u.attachments; track a.attachmentId) {
                                <li
                                  class="rounded-md bg-neutral-100 px-2 py-0.5 text-xs text-neutral-600"
                                >
                                  {{ a.fileName }}
                                </li>
                              }
                            </ul>
                          }
                          <!-- Manager comment thread (FR-8) -->
                          @if (u.comments.length) {
                            <ul
                              class="mt-2 space-y-1 border-l-2 border-neutral-100 pl-3"
                              data-testid="comment-thread"
                            >
                              @for (c of u.comments; track c.commentId) {
                                <li class="text-xs text-neutral-600">
                                  <span class="font-medium text-neutral-700">
                                    {{ c.authorName }}
                                  </span>
                                  · {{ c.comment }}
                                </li>
                              }
                            </ul>
                          }
                        </li>
                      }
                    </ol>
                  }
                </div>
              }
            </li>
          }
        </ul>
      }
    </div>

    <!-- Add Update bottom-sheet (AC-2, NFR-4 mobile-first) -->
    @if (updateGoal(); as goal) {
      <div
        class="fixed inset-0 z-50 flex items-end justify-center bg-black/40 sm:items-center sm:p-4"
        role="dialog"
        aria-modal="true"
        aria-labelledby="update-title"
        (click)="closeUpdate()"
        data-testid="update-sheet"
      >
        <div
          class="w-full rounded-t-2xl bg-white p-6 shadow-xl sm:max-w-lg sm:rounded-2xl"
          @sheet
          (click)="$event.stopPropagation()"
        >
          <h2 id="update-title" class="text-lg font-semibold text-neutral-900">
            Update progress
          </h2>
          <p class="mt-0.5 truncate text-sm text-neutral-500">{{ goal.title }}</p>

          <!-- Progress slider -->
          <div class="mt-5">
            <div class="mb-1 flex justify-between text-sm">
              <label for="progress-slider" class="font-medium text-neutral-700">
                Progress
              </label>
              <span class="tabular-nums text-neutral-900" data-testid="slider-value">
                {{ progress() }}%
              </span>
            </div>
            <input
              id="progress-slider"
              type="range"
              min="0"
              max="100"
              step="5"
              class="w-full accent-[var(--brand-primary)]"
              [ngModel]="progress()"
              (ngModelChange)="onProgressChange($event)"
              data-testid="progress-slider"
            />
          </div>

          <!-- Status selector chips -->
          <div class="mt-5">
            <p class="mb-1.5 text-sm font-medium text-neutral-700">Status</p>
            <div class="flex flex-wrap gap-2" data-testid="status-selector">
              @for (s of statuses; track s) {
                <button
                  type="button"
                  class="rounded-full px-3 py-1 text-xs font-medium ring-1 ring-inset transition"
                  [class]="
                    status() === s
                      ? statusBadge(s) + ' ring-2'
                      : 'bg-white text-neutral-500 ring-neutral-200 hover:bg-neutral-50'
                  "
                  (click)="status.set(s)"
                  [attr.aria-pressed]="status() === s"
                  [attr.data-status]="s"
                >
                  {{ statusLabel(s) }}
                </button>
              }
            </div>
          </div>

          <!-- Notes -->
          <div class="mt-5">
            <label for="notes" class="text-sm font-medium text-neutral-700">
              Notes
            </label>
            <textarea
              id="notes"
              rows="3"
              [maxlength]="notesMax"
              [ngModel]="notes()"
              (ngModelChange)="notes.set($event)"
              class="mt-1 w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm shadow-sm focus:border-[var(--brand-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-primary)]"
              placeholder="What did you accomplish?"
              data-testid="notes-input"
            ></textarea>
            <p class="mt-0.5 text-right text-xs text-neutral-400">
              {{ notes().length }}/{{ notesMax }}
            </p>
          </div>

          <!-- Attachments (optional, ≤3) -->
          <div class="mt-3">
            <label
              class="inline-flex cursor-pointer items-center gap-2 text-sm font-medium text-neutral-600 hover:text-neutral-800"
            >
              <input
                type="file"
                class="hidden"
                multiple
                (change)="onFiles($event)"
                data-testid="file-input"
              />
              Attach evidence
            </label>
            @if (files().length) {
              <ul class="mt-2 space-y-1" data-testid="file-list">
                @for (f of files(); track f.name) {
                  <li
                    class="flex items-center justify-between rounded-md bg-neutral-100 px-2 py-1 text-xs text-neutral-600"
                  >
                    <span class="truncate">{{ f.name }}</span>
                    <button
                      type="button"
                      class="ml-2 text-neutral-400 hover:text-rose-600"
                      (click)="removeFile(f)"
                      aria-label="Remove file"
                    >
                      ✕
                    </button>
                  </li>
                }
              </ul>
            }
          </div>

          <div class="mt-6 flex flex-col gap-2 sm:flex-row sm:justify-end">
            <button
              type="button"
              class="rounded-lg px-4 py-3 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
              (click)="closeUpdate()"
              data-testid="cancel-update"
            >
              Cancel
            </button>
            <button
              type="button"
              class="rounded-lg px-5 py-3 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              [disabled]="saving()"
              (click)="save()"
              data-testid="save-update"
            >
              @if (saving()) {
                Saving…
              } @else {
                Save update
              }
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class MyGoalsComponent implements OnInit {
  private readonly service = inject(GoalProgressService);
  private readonly toastr = inject(ToastrService);

  readonly statuses = GOAL_PROGRESS_STATUSES;
  readonly notesMax = GOAL_UPDATE_NOTES_MAX;
  readonly windowClosedMessage = GOAL_WINDOW_CLOSED_MESSAGE;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly data = signal<IMyGoals | null>(null);

  // Update history (per expanded goal)
  readonly expandedGoalId = signal<string | null>(null);
  readonly history = signal<IGoalUpdate[]>([]);
  readonly historyLoading = signal(false);

  // Add-update form state
  readonly updateGoal = signal<IGoalProgress | null>(null);
  readonly progress = signal(0);
  readonly status = signal<GoalProgressStatus>('NotStarted');
  readonly notes = signal('');
  readonly files = signal<File[]>([]);
  readonly saving = signal(false);

  /** Server value is authoritative; FE recomputes as a fallback for optimistic display. */
  readonly overallCompletion = computed(() => {
    const d = this.data();
    if (!d) {
      return 0;
    }
    return d.overallCompletionPercent ?? weightedOverallCompletion(d.goals);
  });

  readonly donut = computed(() => completionDonut(this.overallCompletion(), 50));
  readonly bandClass = computed(() => completionBandClass(this.overallCompletion()));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getMyGoals().subscribe({
      next: (data) => {
        this.data.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load your goals.');
        this.loading.set(false);
      },
    });
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
    this.loadHistory(goalId);
  }

  private loadHistory(goalId: string): void {
    this.historyLoading.set(true);
    this.history.set([]);
    this.service.getGoalUpdates(goalId).subscribe({
      next: (updates) => {
        this.history.set(updates);
        this.historyLoading.set(false);
      },
      error: () => {
        this.historyLoading.set(false);
        this.toastr.error('Could not load the update history.');
      },
    });
  }

  openUpdate(goal: IGoalProgress): void {
    this.updateGoal.set(goal);
    this.progress.set(clampPercent(goal.progressPercent));
    this.status.set(goal.status);
    this.notes.set('');
    this.files.set([]);
  }

  closeUpdate(): void {
    this.updateGoal.set(null);
  }

  /** BR-2: hitting 100% auto-selects Completed (overridable). */
  onProgressChange(value: number | string): void {
    const pct = clampPercent(Number(value));
    this.progress.set(pct);
    this.status.set(statusForProgress(pct, this.status()));
  }

  onFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files) {
      return;
    }
    const incoming = Array.from(input.files);
    const valid = incoming.filter((f) => f.size <= GOAL_UPDATE_MAX_FILE_BYTES);
    if (valid.length !== incoming.length) {
      this.toastr.error('Each file must be 10MB or smaller.');
    }
    const merged = [...this.files(), ...valid].slice(0, GOAL_UPDATE_MAX_FILES);
    if (this.files().length + valid.length > GOAL_UPDATE_MAX_FILES) {
      this.toastr.error(`You can attach up to ${GOAL_UPDATE_MAX_FILES} files.`);
    }
    this.files.set(merged);
    input.value = '';
  }

  removeFile(file: File): void {
    this.files.set(this.files().filter((f) => f !== file));
  }

  save(): void {
    const goal = this.updateGoal();
    if (!goal || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.service
      .addGoalUpdate(
        goal.goalId,
        {
          progressPercent: this.progress(),
          status: this.status(),
          notes: this.notes().trim(),
        },
        this.files(),
      )
      .subscribe({
        next: (update) => {
          this.saving.set(false);
          this.updateGoal.set(null);
          this.applyUpdate(goal.goalId, update);
          this.toastr.success('Progress update saved.');
        },
        error: () => {
          this.saving.set(false);
          this.toastr.error('Could not save your update. Please try again.');
        },
      });
  }

  /** Optimistically reflect the new update on the card + the open timeline (AC-2/AC-3). */
  private applyUpdate(goalId: string, update: IGoalUpdate): void {
    const d = this.data();
    if (d) {
      const goals = d.goals.map((g) =>
        g.goalId === goalId
          ? {
              ...g,
              progressPercent: update.progressPercent,
              status: update.status,
              lastUpdatedOn: update.createdOn,
              needsAttention: false,
            }
          : g,
      );
      this.data.set({
        ...d,
        goals,
        overallCompletionPercent: weightedOverallCompletion(goals),
      });
    }
    if (this.expandedGoalId() === goalId) {
      this.history.set([update, ...this.history()]);
    }
  }

  statusBadge = (status: GoalProgressStatus): string => GOAL_STATUS_BADGE[status];
  statusLabel = (status: GoalProgressStatus): string => GOAL_STATUS_LABEL[status];
  statusBar = (status: GoalProgressStatus): string => GOAL_STATUS_BAR[status];
}
