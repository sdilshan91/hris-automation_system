import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { ScorecardService } from '../../services/scorecard.service';
import {
  IScorecard,
  IScorecardCriterion,
  IScorecardRequest,
  ICriterionRating,
  OverallRecommendation,
  RatingScore,
  RECOMMENDATION_OPTIONS,
  SCORE_VALUES,
  SCORE_LABELS,
  recommendationLabel,
  averageOf,
} from '../../models/scorecard.models';

/**
 * US-REC-006: Interview scorecard submission form (AC-1 / FR-1).
 *
 * Right slide-over (full-screen on mobile, NFR-3) opened from the interview card
 * by the assigned interviewer. Reactive forms + signals + OnPush.
 *
 * Layout (§8): one row per tenant criterion with a 1-5 rating control
 * (1=Poor…5=Excellent, large touch targets) and an expandable per-criterion
 * comment; an overall recommendation as a color-coded segmented control
 * (Strong Hire=green / Hire=light green / No Hire=orange / Strong No Hire=red,
 * mandatory — BR-3); and optional general notes.
 *
 * Two-step: fill → a pre-submit summary with Edit (§8); confirm submits. In
 * `scorecard` mode the form pre-fills and PUTs an edit within the lock window
 * (FR-2 / BR-4); otherwise it POSTs a new scorecard (AC-1). The interviewer
 * identity is server-side (BR-1) — never sent by the FE.
 */
@Component({
  selector: 'app-scorecard-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate(
          '260ms cubic-bezier(0.22, 1, 0.36, 1)',
          style({ transform: 'translateX(0)' }),
        ),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <!-- Backdrop -->
    <div
      @backdrop
      class="fixed inset-0 z-[55] bg-neutral-900/30"
      (click)="cancel()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap -->
    <div class="pointer-events-none fixed inset-0 z-[56] flex justify-end">
      <div
        @drawer
        class="pointer-events-auto flex h-full w-full max-w-xl flex-col bg-white shadow-xl sm:w-[60%]"
        role="dialog"
        aria-modal="true"
        aria-labelledby="scorecard-form-title"
      >
        <!-- Header -->
        <div
          class="flex items-start justify-between gap-3 border-b border-neutral-100 px-6 py-4"
        >
          <div>
            <h2
              id="scorecard-form-title"
              class="text-base font-semibold text-neutral-900"
            >
              {{ editing() ? 'Edit scorecard' : 'Submit scorecard' }}
            </h2>
            <p class="mt-0.5 text-sm text-neutral-500">
              {{ applicantName() || 'Candidate' }}
              @if (roundNumber()) {
                <span class="text-neutral-400">· Round {{ roundNumber() }}</span>
              }
            </p>
          </div>
          <button
            type="button"
            class="rounded-md p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            (click)="cancel()"
            aria-label="Close"
          >
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
            >
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-6 py-5">
          @if (loadingCriteria()) {
            <div class="space-y-3">
              @for (n of [1, 2, 3, 4]; track n) {
                <div class="h-20 animate-pulse rounded-xl bg-neutral-100"></div>
              }
            </div>
          } @else if (criteria().length === 0) {
            <p class="text-sm text-neutral-500">
              No evaluation criteria are configured for this tenant. Ask an admin
              to set up interview score sheet criteria.
            </p>
          } @else {
            <!-- STEP 1: fill -->
            @if (step() === 'fill') {
              <form [formGroup]="form">
                <div formArrayName="ratings" class="space-y-4">
                  @for (c of criteria(); track c.key; let i = $index) {
                    <div
                      [formGroupName]="i"
                      class="rounded-xl border border-neutral-200 p-4"
                    >
                      <div class="flex flex-wrap items-start justify-between gap-2">
                        <div>
                          <p class="text-sm font-semibold text-neutral-800">
                            {{ c.label }}
                          </p>
                        </div>
                        @if (ratingScore(i)) {
                          <span class="text-xs text-neutral-400">
                            {{ scoreLabelFor(i) }}
                          </span>
                        }
                      </div>

                      <!-- 1-5 rating buttons (large touch targets, NFR-3) -->
                      <div
                        class="mt-3 grid grid-cols-5 gap-1.5"
                        role="radiogroup"
                        [attr.aria-label]="c.label + ' rating'"
                      >
                        @for (s of scores; track s) {
                          <button
                            type="button"
                            role="radio"
                            [attr.aria-checked]="ratingScore(i) === s"
                            [attr.aria-label]="s + ' ' + scoreLabels[s]"
                            class="flex h-11 items-center justify-center rounded-lg border text-sm font-semibold transition"
                            [class]="
                              ratingScore(i) === s
                                ? 'border-indigo-500 bg-indigo-600 text-white shadow-sm'
                                : 'border-neutral-200 bg-white text-neutral-600 hover:border-indigo-300 hover:bg-indigo-50'
                            "
                            (click)="setScore(i, s)"
                          >
                            {{ s }}
                          </button>
                        }
                      </div>

                      <!-- Expandable per-criterion comment -->
                      @if (commentOpen()[c.key] || ratingComment(i)) {
                        <textarea
                          formControlName="comment"
                          rows="2"
                          class="mt-3 w-full resize-none rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-800 focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                          [attr.aria-label]="c.label + ' comment'"
                          placeholder="Add a comment (optional)"
                        ></textarea>
                      } @else {
                        <button
                          type="button"
                          class="mt-2 text-xs font-medium text-indigo-600 hover:underline"
                          (click)="toggleComment(c.key)"
                        >
                          + Add comment
                        </button>
                      }
                    </div>
                  }
                </div>

                <!-- Overall recommendation (mandatory, BR-3, §8 color-coded) -->
                <fieldset class="mt-6">
                  <legend class="text-sm font-semibold text-neutral-800">
                    Overall recommendation
                    <span class="text-red-500" aria-hidden="true">*</span>
                  </legend>
                  <div
                    class="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-4"
                    role="radiogroup"
                    aria-label="Overall recommendation"
                  >
                    @for (r of recommendations; track r.value) {
                      <button
                        type="button"
                        role="radio"
                        [attr.aria-checked]="recommendation() === r.value"
                        class="flex min-h-11 items-center justify-center rounded-lg border px-2 py-2 text-center text-xs font-semibold ring-inset transition"
                        [class]="
                          recommendation() === r.value
                            ? r.activeClass + ' ring-1'
                            : 'border-neutral-200 bg-white text-neutral-600 hover:bg-neutral-50'
                        "
                        (click)="setRecommendation(r.value)"
                      >
                        {{ r.label }}
                      </button>
                    }
                  </div>
                  @if (recommendationTouched() && !recommendation()) {
                    <p class="mt-1.5 text-xs text-red-600">
                      A recommendation is required.
                    </p>
                  }
                </fieldset>

                <!-- General notes -->
                <div class="mt-6">
                  <label
                    for="general-notes"
                    class="text-sm font-semibold text-neutral-800"
                  >
                    General notes
                    <span class="font-normal text-neutral-400">(optional)</span>
                  </label>
                  <textarea
                    id="general-notes"
                    [formControl]="notesControl"
                    rows="3"
                    class="mt-2 w-full resize-none rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-800 focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-200"
                    placeholder="Anything else the recruiter should know"
                  ></textarea>
                </div>
              </form>
            }

            <!-- STEP 2: summary -->
            @if (step() === 'summary') {
              <div class="space-y-4">
                <p class="text-sm text-neutral-500">
                  Review your scores before submitting. Average:
                  <span class="font-semibold text-neutral-800"
                    >{{ liveAverage() }} / 5</span
                  >
                </p>
                <ul class="divide-y divide-neutral-100 rounded-xl border border-neutral-200">
                  @for (c of criteria(); track c.key; let i = $index) {
                    <li class="flex items-start justify-between gap-3 px-4 py-3">
                      <div>
                        <p class="text-sm font-medium text-neutral-800">
                          {{ c.label }}
                        </p>
                        @if (ratingComment(i)) {
                          <p class="mt-0.5 text-xs text-neutral-500">
                            {{ ratingComment(i) }}
                          </p>
                        }
                      </div>
                      <span
                        class="shrink-0 rounded-md bg-neutral-100 px-2 py-0.5 text-sm font-semibold text-neutral-800"
                      >
                        {{ ratingScore(i) || '—' }}
                      </span>
                    </li>
                  }
                </ul>
                <div class="rounded-xl border border-neutral-200 px-4 py-3">
                  <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                    Recommendation
                  </p>
                  <p class="mt-1 text-sm font-semibold text-neutral-800">
                    {{ recommendationText() }}
                  </p>
                </div>
                @if (notesControl.value) {
                  <div class="rounded-xl border border-neutral-200 px-4 py-3">
                    <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                      Notes
                    </p>
                    <p class="mt-1 whitespace-pre-wrap text-sm text-neutral-700">
                      {{ notesControl.value }}
                    </p>
                  </div>
                }
              </div>
            }
          }
        </div>

        <!-- Footer -->
        @if (!loadingCriteria() && criteria().length > 0) {
          <div
            class="flex items-center justify-end gap-2 border-t border-neutral-100 px-6 py-4"
          >
            @if (step() === 'fill') {
              <button
                type="button"
                class="rounded-lg border border-neutral-200 bg-white px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-50"
                (click)="cancel()"
              >
                Cancel
              </button>
              <button
                type="button"
                class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:opacity-50"
                [disabled]="!canReview()"
                (click)="goToSummary()"
              >
                Review
              </button>
            } @else {
              <button
                type="button"
                class="rounded-lg border border-neutral-200 bg-white px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-50"
                (click)="step.set('fill')"
              >
                Edit
              </button>
              <button
                type="button"
                class="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700 disabled:opacity-50"
                [disabled]="submitting()"
                (click)="submit()"
              >
                {{ submitting() ? 'Submitting…' : editing() ? 'Save changes' : 'Submit scorecard' }}
              </button>
            }
          </div>
        }
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
    `,
  ],
})
export class ScorecardFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly scorecardService = inject(ScorecardService);
  private readonly toastr = inject(ToastrService);

  /** The interview this scorecard belongs to (AC-1). */
  readonly interviewId = input.required<string>();
  /** Candidate name for the header context (optional). */
  readonly applicantName = input<string>('');
  /** Round number for the header context (optional). */
  readonly roundNumber = input<number | null>(null);
  /** An existing scorecard to edit (FR-2), or null to submit a new one (AC-1). */
  readonly scorecard = input<IScorecard | null>(null);

  /** Emitted with the saved scorecard after a successful submit/edit. */
  readonly saved = output<IScorecard>();
  /** Emitted when the user dismisses the form. */
  readonly cancelled = output<void>();

  readonly scores = SCORE_VALUES;
  readonly scoreLabels = SCORE_LABELS;
  readonly recommendations = RECOMMENDATION_OPTIONS;

  readonly criteria = signal<IScorecardCriterion[]>([]);
  readonly loadingCriteria = signal(true);
  readonly submitting = signal(false);
  readonly step = signal<'fill' | 'summary'>('fill');
  readonly recommendation = signal<OverallRecommendation | null>(null);
  readonly recommendationTouched = signal(false);
  /** Which criteria have their comment box expanded (by key). */
  readonly commentOpen = signal<Record<string, boolean>>({});

  /** True when editing an existing scorecard (PUT) vs submitting a new one. */
  readonly editing = computed(() => this.scorecard() !== null);

  readonly form: FormGroup = this.fb.group({
    ratings: this.fb.array([]),
  });
  readonly notesControl = this.fb.control<string>('');

  get ratingsArray(): FormArray {
    return this.form.get('ratings') as FormArray;
  }

  /** Live average of the entered scores (FR-3 preview); server is authoritative. */
  readonly liveAverage = computed(() => {
    // Recompute on every formChange tick via the signal below.
    this.formTick();
    return averageOf(this.collectRatings());
  });

  /** Bumped on form value changes so computed previews re-run. */
  private readonly formTick = signal(0);

  readonly recommendationText = computed(() => {
    const r = this.recommendation();
    return r ? recommendationLabel(r) : '—';
  });

  /** All criteria rated AND a recommendation chosen (FR-1 / BR-3). */
  readonly canReview = computed(() => {
    this.formTick();
    const allRated = this.collectRatings().every((r) => r.score >= 1);
    return (
      this.criteria().length > 0 &&
      allRated &&
      this.recommendation() !== null
    );
  });

  constructor() {
    this.form.valueChanges.subscribe(() => this.formTick.update((n) => n + 1));

    // Build the form once criteria + any existing scorecard are known.
    effect(() => {
      const crit = this.criteria();
      const existing = this.scorecard();
      if (crit.length > 0) {
        this.buildForm(crit, existing);
      }
    });

    this.loadCriteria();
  }

  private loadCriteria(): void {
    this.loadingCriteria.set(true);
    this.scorecardService.listCriteria().subscribe({
      next: (rows) => {
        this.criteria.set(rows);
        this.loadingCriteria.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loadingCriteria.set(false);
        this.toastr.error(ScorecardService.parseErrorMessage(err));
      },
    });
  }

  /** (Re)build the ratings FormArray, pre-filling from an existing scorecard. */
  private buildForm(
    criteria: IScorecardCriterion[],
    existing: IScorecard | null,
  ): void {
    const byKey = new Map<string, ICriterionRating>();
    for (const r of existing?.ratings ?? []) {
      byKey.set(r.criterionKey, r);
    }
    this.ratingsArray.clear();
    const opened: Record<string, boolean> = {};
    for (const c of criteria) {
      const pre = byKey.get(c.key);
      this.ratingsArray.push(
        this.fb.group({
          criterionKey: this.fb.control(c.key, Validators.required),
          score: this.fb.control<number>(pre?.score ?? 0),
          comment: this.fb.control<string>(pre?.comment ?? ''),
        }),
      );
      if (pre?.comment) {
        opened[c.key] = true;
      }
    }
    this.commentOpen.set(opened);
    this.notesControl.setValue(existing?.generalNotes ?? '');
    this.recommendation.set(existing?.overallRecommendation ?? null);
    this.step.set('fill');
  }

  ratingScore(i: number): RatingScore | 0 {
    return (this.ratingsArray.at(i)?.get('score')?.value ?? 0) as RatingScore | 0;
  }

  ratingComment(i: number): string {
    return (this.ratingsArray.at(i)?.get('comment')?.value ?? '') as string;
  }

  /** The 1-5 label for the row's current score, or '' when unrated. */
  scoreLabelFor(i: number): string {
    const s = this.ratingScore(i);
    return s ? SCORE_LABELS[s] : '';
  }

  setScore(i: number, score: RatingScore): void {
    this.ratingsArray.at(i)?.get('score')?.setValue(score);
  }

  toggleComment(key: string): void {
    this.commentOpen.update((m) => ({ ...m, [key]: !m[key] }));
  }

  setRecommendation(value: OverallRecommendation): void {
    this.recommendation.set(value);
    this.recommendationTouched.set(true);
  }

  /** Build the typed ratings list from the form (filters unrated when collecting for submit). */
  private collectRatings(): ICriterionRating[] {
    return this.ratingsArray.controls.map((g) => ({
      criterionKey: g.get('criterionKey')?.value as string,
      score: (g.get('score')?.value ?? 0) as RatingScore,
      comment: ((g.get('comment')?.value as string) || '').trim() || null,
    }));
  }

  goToSummary(): void {
    this.recommendationTouched.set(true);
    if (!this.canReview()) {
      return;
    }
    this.step.set('summary');
  }

  submit(): void {
    if (this.submitting() || !this.canReview()) {
      return;
    }
    const recommendation = this.recommendation();
    if (!recommendation) {
      return;
    }
    const request: IScorecardRequest = {
      ratings: this.collectRatings(),
      overallRecommendation: recommendation,
      generalNotes: (this.notesControl.value || '').trim() || null,
    };
    this.submitting.set(true);
    const existing = this.scorecard();
    const call$ = existing
      ? this.scorecardService.update(existing.id, request)
      : this.scorecardService.submit(this.interviewId(), request);
    call$.subscribe({
      next: (saved) => {
        this.submitting.set(false);
        this.toastr.success(
          existing ? 'Scorecard updated.' : 'Scorecard submitted.',
        );
        this.saved.emit(saved);
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.toastr.error(ScorecardService.parseErrorMessage(err));
      },
    });
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
