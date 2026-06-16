import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { startWith } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { ManagerReviewService } from '../../services/manager-review.service';
import {
  IManagerReview,
  IManagerReviewGoalInput,
  ISaveManagerReviewRequest,
  MANAGER_COMMENT_MIN,
  MANAGER_REVIEW_STATUS_BADGE,
  MANAGER_REVIEW_STATUS_LABEL,
  REVIEW_FLAG_BADGE,
  REVIEW_FLAG_OPTIONS,
  ReviewFlag,
  SUMMARY_COMMENT_MAX,
  canSubmitManagerReview,
  managerGoalIsComplete,
  ratedManagerGoalCount,
  ratingBandClasses,
  unratedManagerGoalTitles,
  weightedManagerScore,
} from '../../models/manager-review.models';

interface IManagerGoalFormValue {
  managerRating: number | null;
  managerComment: string;
}

/**
 * US-PRF-003: Per-employee MANAGER review view (AC-1/AC-2/AC-3/AC-5).
 *
 * - AC-1: side-by-side per goal — the employee's self-rating + self-comment (read-
 *   only, FR-1) on the LEFT, manager rating + manager comment inputs on the RIGHT.
 *   On mobile (360px) the two columns stack (§8/NFR-4).
 * - FR-2: manager rating uses the same tenant-configured scale (`ratingScaleMax`)
 *   as the self-assessment, rendered as a keyboard-navigable radiogroup (NFR-4).
 * - FR-3: each manager comment requires ≥20 chars; FR-5: an overall summary comment
 *   (max 5000); FR-6: a flag control (Recognition / Promotion / PIP).
 * - AC-2: "Submit Manager Review" gated on ALL goals rated + each comment ≥20 chars.
 * - AC-3: submitting with unrated goals shows a validation error LISTING them.
 * - AC-5: after submit (or status already submitted/completed) the view is locked
 *   read-only; HR reopen is a backend concern.
 * - BR-4: the final combined score is shown if the server returns it — display only.
 *
 * The form is a single FormArray mirroring `review().goals` by index, plus summary +
 * flag controls. No chart library is added (FE has none); the self-vs-manager
 * comparison is the side-by-side layout + colored rating badges (§8).
 */
@Component({
  selector: 'app-manager-review',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl px-4 pb-28 pt-6 sm:px-6 lg:px-8">
      <!-- Back link -->
      <a
        routerLink="/performance/team-reviews"
        class="mb-4 inline-flex items-center gap-1 text-sm text-neutral-500 transition hover:text-neutral-800"
      >
        <span aria-hidden="true">←</span> Team reviews
      </a>

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ loadError() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (review(); as r) {
        <!-- Header -->
        <div class="mb-6 flex flex-wrap items-start justify-between gap-3">
          <div class="min-w-0">
            <h1
              class="text-2xl font-semibold tracking-tight text-neutral-900"
            >
              {{ r.employeeName }}
            </h1>
            <p class="mt-1 text-sm text-neutral-500">
              {{ r.cycleName }}@if (r.jobTitle) {
                · {{ r.jobTitle }}
              }
            </p>
          </div>
          <span
            class="rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
            [class]="statusBadge(r.status)"
            data-testid="status-chip"
          >
            {{ statusLabel(r.status) }}
          </span>
        </div>

        <!-- AC-5: locked banner -->
        @if (isLocked()) {
          <div
            class="mb-6 flex items-start gap-3 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800"
            role="status"
            data-testid="locked-banner"
          >
            <span aria-hidden="true">✓</span>
            <div class="min-w-0">
              <p class="font-medium">Manager review submitted</p>
              <p class="mt-0.5 text-emerald-700/80">
                This review is locked. Editing is only possible if HR reopens it.
              </p>
              <!-- US-PRF-006 AC-1: proceed to meeting notes + sign-off. -->
              <a
                [routerLink]="['/performance/reviews', r.employeeId, 'signoff']"
                [queryParams]="{ reviewId: r.id }"
                class="mt-2 inline-flex items-center gap-1 rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-medium text-white transition hover:bg-emerald-700"
                data-testid="add-meeting-notes-link"
              >
                Add Meeting Notes
              </a>
            </div>
          </div>
        } @else if (!r.windowOpen) {
          <div
            class="mb-6 flex items-start gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
            role="status"
          >
            <span aria-hidden="true">🔒</span>
            <span>The manager-review window for this cycle is not open.</span>
          </div>
        }

        <!-- Scores (BR-4: final shown only if server returns it) -->
        <div
          class="mb-6 grid grid-cols-1 gap-3 sm:grid-cols-3"
          data-testid="score-cards"
        >
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <p class="text-xs font-medium text-neutral-500">Self score</p>
            <p class="mt-1 text-xl font-semibold text-neutral-900">
              {{ r.selfScore != null ? r.selfScore + '%' : '—' }}
            </p>
          </div>
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <p class="text-xs font-medium text-neutral-500">
              Manager score{{ r.managerScore == null ? ' (preview)' : '' }}
            </p>
            <p class="mt-1 text-xl font-semibold text-neutral-900">
              {{ (r.managerScore ?? liveManagerScore()) }}%
            </p>
          </div>
          <div class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
            <p class="text-xs font-medium text-neutral-500">Final score</p>
            <p class="mt-1 text-xl font-semibold text-neutral-900" data-testid="final-score">
              {{ r.finalScore != null ? r.finalScore + '%' : '—' }}
            </p>
          </div>
        </div>

        <!-- AC-3: unrated-goals validation error -->
        @if (showUnratedError() && unratedTitles().length > 0) {
          <div
            class="mb-6 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
            role="alert"
            data-testid="unrated-error"
          >
            <p class="font-medium">
              Rate every goal with a {{ commentMin }}+ character comment before
              submitting. Outstanding goals:
            </p>
            <ul class="mt-1 list-disc pl-5">
              @for (title of unratedTitles(); track title) {
                <li>{{ title }}</li>
              }
            </ul>
          </div>
        }

        @if (totalGoals() === 0) {
          <div
            class="rounded-xl border border-neutral-200 bg-white p-8 text-center text-sm text-neutral-500"
          >
            No goals have been assigned to this employee for the cycle.
          </div>
        }

        <!-- Progress -->
        @if (totalGoals() > 0) {
          <div
            class="mb-6 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
          >
            <div class="flex items-center justify-between text-sm">
              <span class="font-semibold text-neutral-900" data-testid="rated-count">
                {{ ratedCount() }} of {{ totalGoals() }} goals rated
              </span>
            </div>
            <div
              class="mt-2 h-2 w-full overflow-hidden rounded-full bg-neutral-100"
              role="progressbar"
              [attr.aria-valuenow]="ratedCount()"
              [attr.aria-valuemin]="0"
              [attr.aria-valuemax]="totalGoals()"
            >
              <div
                class="h-full rounded-full bg-emerald-500 transition-all"
                [style.width.%]="progressPercent()"
              ></div>
            </div>
          </div>
        }

        <!-- Goals: side-by-side self vs manager (AC-1) -->
        <form [formGroup]="form" (ngSubmit)="submit()">
          <div formArrayName="goals" class="space-y-4">
            @for (goal of r.goals; track goal.goalId; let i = $index) {
              <div
                [formGroupName]="i"
                class="rounded-xl border border-neutral-200 bg-white shadow-sm"
              >
                <div class="border-b border-neutral-100 p-4">
                  <div class="flex items-start gap-2">
                    @if (goalComplete(i)) {
                      <span
                        class="mt-0.5 inline-flex h-5 w-5 flex-none items-center justify-center rounded-full bg-emerald-100 text-xs text-emerald-700"
                        aria-label="Rated"
                        >✓</span
                      >
                    } @else {
                      <span
                        class="mt-0.5 inline-flex h-5 w-5 flex-none items-center justify-center rounded-full bg-neutral-100 text-xs text-neutral-400"
                        aria-label="Not yet rated"
                        >{{ i + 1 }}</span
                      >
                    }
                    <div class="min-w-0">
                      <h3 class="text-sm font-semibold text-neutral-900">
                        {{ goal.title }}
                      </h3>
                      <p class="mt-0.5 text-xs text-neutral-500">
                        Weight {{ goal.weight }}% · Target {{ goal.targetValue }}
                        {{ goal.measurementUnit }} · Due {{ goal.dueDate }}
                      </p>
                    </div>
                  </div>
                  @if (goal.description) {
                    <p class="mt-2 text-sm text-neutral-600">
                      {{ goal.description }}
                    </p>
                  }
                </div>

                <!-- Side-by-side: self (left) | manager (right); stacks <md -->
                <div class="grid grid-cols-1 md:grid-cols-2">
                  <!-- Employee self-assessment (read-only, FR-1) -->
                  <div
                    class="border-b border-neutral-100 p-4 md:border-b-0 md:border-r"
                    data-testid="self-panel"
                  >
                    <p
                      class="mb-2 text-xs font-semibold uppercase tracking-wide text-neutral-400"
                    >
                      Employee self-assessment
                    </p>
                    <div class="mb-2 flex items-center gap-2">
                      <span class="text-xs text-neutral-500">Self-rating</span>
                      @if (goal.selfRating != null) {
                        <span
                          class="inline-flex rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                          [class]="band(goal.selfRating, r.ratingScaleMax)"
                          data-testid="self-rating"
                        >
                          {{ goal.selfRating }}/{{ r.ratingScaleMax }}
                        </span>
                      } @else {
                        <span class="text-xs text-neutral-400">Not rated</span>
                      }
                    </div>
                    <p
                      class="whitespace-pre-line text-sm text-neutral-600"
                      data-testid="self-comment"
                    >
                      {{ goal.selfComment || 'No comment provided.' }}
                    </p>
                  </div>

                  <!-- Manager rating (editable, FR-2/FR-3) -->
                  <div class="p-4" data-testid="manager-panel">
                    <p
                      class="mb-2 text-xs font-semibold uppercase tracking-wide text-neutral-400"
                    >
                      Your review
                    </p>

                    <div class="mb-3">
                      <span
                        class="mb-1 block text-xs font-medium text-neutral-600"
                        >Manager rating (1–{{ r.ratingScaleMax }})</span
                      >
                      <div
                        class="flex flex-wrap gap-1"
                        role="radiogroup"
                        [attr.aria-label]="'Manager rating for ' + goal.title"
                      >
                        @for (n of scaleStars(); track n) {
                          <button
                            type="button"
                            class="flex h-9 w-9 items-center justify-center rounded-lg text-lg outline-none transition focus-visible:ring-2 focus-visible:ring-offset-1 disabled:cursor-not-allowed"
                            [class.text-amber-400]="(ratingOf(i) ?? 0) >= n"
                            [class.text-neutral-300]="(ratingOf(i) ?? 0) < n"
                            [class.hover:bg-neutral-100]="canEdit()"
                            [style.--tw-ring-color]="'var(--brand-primary)'"
                            role="radio"
                            [attr.aria-checked]="ratingOf(i) === n"
                            [attr.aria-label]="n + ' of ' + r.ratingScaleMax"
                            [attr.tabindex]="ratingTabindex(i, n)"
                            [disabled]="!canEdit()"
                            (click)="setRating(i, n)"
                            (keydown)="onRatingKeydown(i, n, $event)"
                          >
                            ★
                          </button>
                        }
                        @if (ratingOf(i) != null) {
                          <span
                            class="ml-2 self-center text-sm font-medium text-neutral-700"
                            >{{ ratingOf(i) }}/{{ r.ratingScaleMax }}</span
                          >
                        }
                      </div>
                    </div>

                    <div>
                      <label
                        class="mb-1 block text-xs font-medium text-neutral-600"
                        [attr.for]="'mcmt-' + i"
                        >Manager comment</label
                      >
                      <textarea
                        [id]="'mcmt-' + i"
                        formControlName="managerComment"
                        rows="3"
                        class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                        placeholder="Provide evidence-based feedback (min 20 characters)…"
                      ></textarea>
                      <div class="mt-1 flex items-center justify-between text-xs">
                        @if (commentTooShort(i)) {
                          <span class="text-rose-600" role="alert"
                            >At least {{ commentMin }} characters required.</span
                          >
                        } @else {
                          <span class="text-neutral-400">Looks good.</span>
                        }
                        <span class="text-neutral-400"
                          >{{ commentLength(i) }}/{{ commentMin }}</span
                        >
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            }
          </div>

          <!-- Overall summary + flag (FR-5 / FR-6) -->
          @if (totalGoals() > 0) {
            <div
              class="mt-4 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
            >
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="summary"
                >Overall summary comment</label
              >
              <textarea
                id="summary"
                formControlName="summaryComment"
                rows="4"
                [attr.maxlength]="summaryMax"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                placeholder="Summarise overall performance, strengths, and development areas…"
              ></textarea>
              <div class="mt-1 text-right text-xs text-neutral-400">
                {{ summaryLength() }}/{{ summaryMax }}
              </div>

              <div class="mt-4">
                <span class="mb-2 block text-sm font-medium text-neutral-700"
                  >Flag for follow-up</span
                >
                <div class="flex flex-wrap gap-2" role="radiogroup" aria-label="Follow-up flag">
                  @for (opt of flagOptions; track opt.value) {
                    <button
                      type="button"
                      class="rounded-full px-3 py-1.5 text-xs font-medium ring-1 ring-inset outline-none transition focus-visible:ring-2 disabled:cursor-not-allowed disabled:opacity-60"
                      [class]="flagButtonClasses(opt.value)"
                      role="radio"
                      [attr.aria-checked]="flagValue() === opt.value"
                      [disabled]="!canEdit()"
                      (click)="setFlag(opt.value)"
                      data-testid="flag-option"
                    >
                      {{ opt.label }}
                    </button>
                  }
                </div>
              </div>
            </div>
          }

          <!-- Actions (AC-2) — sticky on mobile -->
          @if (canEdit() && totalGoals() > 0) {
            <div
              class="fixed inset-x-0 bottom-0 z-10 border-t border-neutral-200 bg-white/95 p-4 backdrop-blur sm:static sm:mt-6 sm:border-0 sm:bg-transparent sm:p-0 sm:backdrop-blur-none"
            >
              <div
                class="mx-auto flex max-w-4xl flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-end"
              >
                <button
                  type="button"
                  class="rounded-lg px-4 py-2.5 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50 disabled:opacity-50"
                  (click)="saveDraft()"
                  [disabled]="busy()"
                >
                  Save as Draft
                </button>
                <button
                  type="submit"
                  class="rounded-lg px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
                  [style.background-color]="'var(--brand-primary)'"
                  [disabled]="busy()"
                  data-testid="submit-btn"
                >
                  @if (submitting()) {
                    <span class="inline-flex items-center gap-2">
                      <span
                        class="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"
                      ></span>
                      Submitting…
                    </span>
                  } @else {
                    Submit Manager Review
                  }
                </button>
              </div>
              @if (!canSubmit()) {
                <p
                  class="mx-auto mt-2 max-w-4xl text-center text-xs text-neutral-400 sm:text-right"
                >
                  Rate every goal and write a {{ commentMin }}+ character comment
                  to submit.
                </p>
              }
            </div>
          }
        </form>
      }
    </div>
  `,
})
export class ManagerReviewComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(ManagerReviewService);
  private readonly toastr = inject(ToastrService);

  readonly commentMin = MANAGER_COMMENT_MIN;
  readonly summaryMax = SUMMARY_COMMENT_MAX;
  readonly flagOptions = REVIEW_FLAG_OPTIONS;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly savingDraft = signal(false);
  readonly submitting = signal(false);
  readonly review = signal<IManagerReview | null>(null);
  /** AC-3: only show the unrated-goals error after a blocked submit attempt. */
  readonly showUnratedError = signal(false);

  private employeeId = '';

  readonly form: FormGroup = this.fb.group({
    goals: this.fb.array([]),
    summaryComment: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(SUMMARY_COMMENT_MAX)],
    }),
    flag: new FormControl<ReviewFlag>('None', { nonNullable: true }),
  });

  /** Live snapshot of the goals FormArray value (drives the submit gate + preview). */
  private readonly goalsValue = toSignal(
    this.goals.valueChanges.pipe(startWith(this.goals.value)),
    { initialValue: this.goals.value as IManagerGoalFormValue[] },
  );

  private readonly summaryValue = toSignal(
    this.summaryControl.valueChanges.pipe(
      startWith(this.summaryControl.value),
    ),
    { initialValue: this.summaryControl.value },
  );

  private readonly flagControlValue = toSignal(
    this.flagControl.valueChanges.pipe(startWith(this.flagControl.value)),
    { initialValue: this.flagControl.value },
  );

  readonly busy = computed(() => this.savingDraft() || this.submitting());

  readonly isLocked = computed(() => {
    const status = this.review()?.status;
    return status === 'ManagerReviewSubmitted' || status === 'Completed';
  });

  /** AC-1/AC-5: editing allowed only when the window is open AND not locked. */
  readonly canEdit = computed(
    () => (this.review()?.windowOpen ?? false) && !this.isLocked(),
  );

  readonly totalGoals = computed(() => this.review()?.goals.length ?? 0);

  readonly ratedCount = computed(() =>
    ratedManagerGoalCount(this.goalsValue() as IManagerGoalFormValue[]),
  );

  readonly progressPercent = computed(() => {
    const total = this.totalGoals();
    return total === 0 ? 0 : Math.round((this.ratedCount() / total) * 100);
  });

  /** Live weighted manager-score preview (FR-4); server is authoritative on submit. */
  readonly liveManagerScore = computed(() => {
    const r = this.review();
    if (!r) {
      return 0;
    }
    const values = this.goalsValue() as IManagerGoalFormValue[];
    const goals = r.goals.map((g, i) => ({
      weight: g.weight,
      managerRating: values[i]?.managerRating ?? null,
    }));
    return weightedManagerScore(goals, r.ratingScaleMax);
  });

  /** AC-3: titles of goals not yet fully rated/commented. */
  readonly unratedTitles = computed(() => {
    const r = this.review();
    if (!r) {
      return [];
    }
    const values = this.goalsValue() as IManagerGoalFormValue[];
    const goals = r.goals.map((g, i) => ({
      title: g.title,
      managerRating: values[i]?.managerRating ?? null,
      managerComment: values[i]?.managerComment ?? '',
    }));
    return unratedManagerGoalTitles(goals);
  });

  /** AC-2 submit gate: all goals complete + edit allowed. */
  readonly canSubmit = computed(
    () =>
      this.canEdit() &&
      canSubmitManagerReview(this.goalsValue() as IManagerGoalFormValue[]),
  );

  readonly summaryLength = computed(
    () => (this.summaryValue() ?? '').length,
  );

  readonly flagValue = computed(
    () => (this.flagControlValue() as ReviewFlag) ?? 'None',
  );

  get goals(): FormArray {
    return this.form.get('goals') as FormArray;
  }

  get summaryControl(): FormControl<string> {
    return this.form.get('summaryComment') as FormControl<string>;
  }

  get flagControl(): FormControl<ReviewFlag> {
    return this.form.get('flag') as FormControl<ReviewFlag>;
  }

  ngOnInit(): void {
    this.employeeId = this.route.snapshot.paramMap.get('employeeId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getEmployeeReview(this.employeeId).subscribe({
      next: (review) => {
        this.review.set(review);
        this.hydrate(review);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load this employee review.');
        this.loading.set(false);
      },
    });
  }

  private hydrate(review: IManagerReview): void {
    this.goals.clear();
    review.goals.forEach((g) => {
      this.goals.push(
        this.fb.group({
          managerRating: [g.managerRating],
          managerComment: [g.managerComment ?? ''],
        }),
      );
    });
    this.summaryControl.setValue(review.summaryComment ?? '');
    this.flagControl.setValue(review.flag ?? 'None');
    if (!this.canEdit()) {
      this.form.disable({ emitEvent: false });
    }
  }

  // ─── Per-goal field access ──────────────────────────────────

  private groupAt(index: number): FormGroup {
    return this.goals.at(index) as FormGroup;
  }

  ratingOf(index: number): number | null {
    return this.groupAt(index)?.get('managerRating')?.value ?? null;
  }

  commentLength(index: number): number {
    return (
      (this.groupAt(index)?.get('managerComment')?.value as string) ?? ''
    ).trim().length;
  }

  commentTooShort(index: number): boolean {
    const len = this.commentLength(index);
    return len > 0 && len < this.commentMin;
  }

  goalComplete(index: number): boolean {
    const g = this.groupAt(index)?.value as IManagerGoalFormValue;
    return !!g && managerGoalIsComplete(g);
  }

  scaleStars(): number[] {
    const max = this.review()?.ratingScaleMax ?? 5;
    return Array.from({ length: max }, (_, i) => i + 1);
  }

  /** NFR-4: keep one star per goal tabbable (roving tabindex on the selected/first). */
  ratingTabindex(index: number, star: number): number {
    const current = this.ratingOf(index);
    if (current != null) {
      return current === star ? 0 : -1;
    }
    return star === 1 ? 0 : -1;
  }

  setRating(index: number, value: number): void {
    if (!this.canEdit()) {
      return;
    }
    const ctrl = this.groupAt(index).get('managerRating');
    // Tap the same star again to clear it.
    ctrl?.setValue(ctrl.value === value ? null : value);
    ctrl?.markAsDirty();
  }

  /** NFR-4: arrow keys move the rating within a goal's radiogroup. */
  onRatingKeydown(index: number, star: number, event: KeyboardEvent): void {
    if (!this.canEdit()) {
      return;
    }
    const max = this.review()?.ratingScaleMax ?? 5;
    let next: number | null = null;
    switch (event.key) {
      case 'ArrowRight':
      case 'ArrowUp':
        next = Math.min(max, star + 1);
        break;
      case 'ArrowLeft':
      case 'ArrowDown':
        next = Math.max(1, star - 1);
        break;
      default:
        return;
    }
    event.preventDefault();
    this.groupAt(index).get('managerRating')?.setValue(next);
    this.groupAt(index).get('managerRating')?.markAsDirty();
  }

  // ─── Summary / flag ─────────────────────────────────────────

  setFlag(flag: ReviewFlag): void {
    if (!this.canEdit()) {
      return;
    }
    this.flagControl.setValue(flag);
  }

  flagButtonClasses(flag: ReviewFlag): string {
    const selected = this.flagValue() === flag;
    if (selected) {
      return `${REVIEW_FLAG_BADGE[flag]} ring-2`;
    }
    return 'bg-white text-neutral-600 ring-neutral-200 hover:bg-neutral-50';
  }

  // ─── Save / submit ──────────────────────────────────────────

  private buildRequest(): ISaveManagerReviewRequest {
    const review = this.review();
    const values = this.goals.value as IManagerGoalFormValue[];
    const goals: IManagerReviewGoalInput[] = (review?.goals ?? []).map(
      (g, i) => ({
        goalId: g.goalId,
        managerRating: values[i]?.managerRating ?? null,
        managerComment: values[i]?.managerComment ?? '',
      }),
    );
    return {
      goals,
      summaryComment: this.summaryControl.value ?? '',
      flag: this.flagControl.value ?? 'None',
    };
  }

  saveDraft(): void {
    const review = this.review();
    if (!review || !this.canEdit() || this.busy()) {
      return;
    }
    this.savingDraft.set(true);
    this.service.saveDraft(review.id, this.buildRequest()).subscribe({
      next: (saved) => {
        this.savingDraft.set(false);
        // Sync server-side score/status state without clobbering live edits.
        this.review.set({ ...saved });
        this.toastr.success('Draft saved.');
      },
      error: () => {
        this.savingDraft.set(false);
        this.toastr.error('Could not save your draft. Please try again.');
      },
    });
  }

  /** AC-2/AC-3: submit when complete; otherwise list the unrated goals and stop. */
  submit(): void {
    const review = this.review();
    if (!review || !this.canEdit() || this.busy()) {
      return;
    }
    if (!this.canSubmit()) {
      // AC-3: surface the validation error listing unrated goals; don't call the API.
      this.showUnratedError.set(true);
      this.toastr.error('Please rate all goals before submitting.');
      return;
    }
    this.showUnratedError.set(false);
    this.submitting.set(true);
    this.service.submit(review.id, this.buildRequest()).subscribe({
      next: (saved) => {
        this.submitting.set(false);
        this.review.set(saved);
        this.form.disable({ emitEvent: false });
        this.toastr.success(
          'Manager review submitted. The employee has been notified.',
        );
      },
      error: () => {
        this.submitting.set(false);
        this.toastr.error('Could not submit. Please try again.');
      },
    });
  }

  // ─── Display helpers ────────────────────────────────────────

  statusBadge(status: IManagerReview['status']): string {
    return MANAGER_REVIEW_STATUS_BADGE[status];
  }

  statusLabel(status: IManagerReview['status']): string {
    return MANAGER_REVIEW_STATUS_LABEL[status];
  }

  band(rating: number | null, scaleMax: number): string {
    return ratingBandClasses(rating, scaleMax);
  }
}
