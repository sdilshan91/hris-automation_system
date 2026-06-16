import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Feedback360Service } from '../../services/feedback-360.service';
import {
  IFeedbackForm,
  IFeedbackQuestion,
  REVIEWER_CATEGORY_LABEL,
  REVIEWER_CATEGORY_BADGE,
  ISubmitFeedbackRequest,
  canSubmitFeedback,
  unratedQuestionTitles,
} from '../../models/feedback-360.models';

/**
 * US-PRF-005 (FR-4 / AC-3 / NFR-5): a single reviewer's 360 feedback form. One
 * competency/goal question card per row with a star rating on the tenant scale + an
 * optional free-text comment, and a "Submit Feedback" button gated on all-questions
 * rated. Mobile single-column with collapsible sections (NFR-5).
 *
 * Reached via the AC-2 deep link `/performance/feedback-360/assignment/:assignmentId`.
 * The reviewer is resolved server-side; this form never reveals the reviewee's other
 * reviewers. Once submitted (BR-3) the form locks read-only. Notion-like cards.
 */
@Component({
  selector: 'app-feedback-360-form',
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
    <div class="mx-auto max-w-3xl px-4 py-6 sm:px-6 lg:px-8">
      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
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
      } @else if (form(); as f) {
        <div @fadeIn>
          <!-- Header -->
          <div class="mb-6">
            <span
              class="inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
              [class]="categoryBadge(f)"
              >{{ categoryLabel(f) }} feedback</span
            >
            <h1
              class="mt-2 text-2xl font-semibold tracking-tight text-neutral-900"
            >
              Feedback for {{ f.revieweeName }}
            </h1>
            <p class="mt-1 text-sm text-neutral-500">
              {{ f.revieweeJobTitle }} · {{ f.cycleName }}
            </p>
            @if (f.anonymous) {
              <p
                class="mt-2 inline-flex items-center gap-1 rounded-lg bg-neutral-50 px-3 py-1.5 text-xs text-neutral-600 ring-1 ring-inset ring-neutral-500/15"
              >
                &#128274; Your identity is hidden — this is anonymous feedback.
              </p>
            }
          </div>

          @if (f.submitted) {
            <div
              class="mb-6 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800"
              role="status"
              data-testid="submitted-banner"
            >
              You submitted this feedback
              @if (f.submittedOn) {
                on {{ f.submittedOn | date: 'mediumDate' }}
              }. It can no longer be edited.
            </div>
          }

          <!-- Question cards (FR-4) -->
          <div class="space-y-4">
            @for (q of f.questions; track q.questionId) {
              <section
                class="rounded-xl border border-neutral-200 bg-white shadow-sm"
                data-testid="question-card"
              >
                <button
                  type="button"
                  class="flex w-full items-center justify-between px-5 py-4 text-left"
                  (click)="toggle(q.questionId)"
                  [attr.aria-expanded]="!isCollapsed(q.questionId)"
                >
                  <div>
                    <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                      {{ q.kind === 'Goal' ? 'Goal' : 'Competency' }}
                    </p>
                    <h2 class="mt-0.5 text-sm font-semibold text-neutral-900">
                      {{ q.title }}
                    </h2>
                  </div>
                  <span
                    class="ml-3 shrink-0 text-neutral-400 transition"
                    [class.rotate-180]="!isCollapsed(q.questionId)"
                    aria-hidden="true"
                    >&#9662;</span
                  >
                </button>

                @if (!isCollapsed(q.questionId)) {
                  <div class="border-t border-neutral-100 px-5 py-4">
                    @if (q.description) {
                      <p class="mb-3 text-sm text-neutral-500">
                        {{ q.description }}
                      </p>
                    }

                    <!-- Star rating on the tenant scale (FR-4) -->
                    <div class="mb-4">
                      <p class="mb-1.5 text-xs font-medium text-neutral-600">
                        Rating
                      </p>
                      <div
                        class="flex flex-wrap items-center gap-1"
                        role="radiogroup"
                        [attr.aria-label]="'Rating for ' + q.title"
                      >
                        @for (n of stars(); track n) {
                          <button
                            type="button"
                            class="flex h-9 w-9 items-center justify-center rounded-lg text-lg transition focus:outline-none focus:ring-2 focus:ring-offset-1"
                            [class.text-amber-400]="(q.rating ?? 0) >= n"
                            [class.text-neutral-300]="(q.rating ?? 0) < n"
                            [disabled]="f.submitted"
                            role="radio"
                            [attr.aria-checked]="q.rating === n"
                            [attr.aria-label]="n + ' of ' + f.ratingScaleMax"
                            (click)="setRating(q, n)"
                            data-testid="star"
                          >
                            &#9733;
                          </button>
                        }
                        <span class="ml-2 text-sm text-neutral-500">
                          @if (q.rating) {
                            {{ q.rating }} / {{ f.ratingScaleMax }}
                          } @else {
                            Not rated
                          }
                        </span>
                      </div>
                    </div>

                    <!-- Optional free-text comment (FR-4) -->
                    <label class="block">
                      <span class="mb-1.5 block text-xs font-medium text-neutral-600"
                        >Comment (optional)</span
                      >
                      <textarea
                        rows="3"
                        [ngModel]="q.comment"
                        (ngModelChange)="setComment(q, $event)"
                        [disabled]="f.submitted"
                        placeholder="Share specific, constructive feedback…"
                        class="w-full resize-y rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                        data-testid="comment-input"
                      ></textarea>
                    </label>
                  </div>
                }
              </section>
            }
          </div>

          <!-- Submit (AC-3) -->
          @if (!f.submitted) {
            @if (!canSubmit() && unrated().length > 0) {
              <div
                class="mt-4 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
                role="status"
                data-testid="unrated-warning"
              >
                Rate every item before submitting. Still unrated:
                {{ unrated().join(', ') }}.
              </div>
            }
            <div class="mt-6 flex justify-end">
              <button
                type="button"
                class="inline-flex items-center justify-center rounded-lg px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="!canSubmit() || submitting()"
                (click)="submit()"
                data-testid="submit-feedback"
              >
                @if (submitting()) {
                  <span
                    class="mr-2 inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent"
                  ></span>
                }
                Submit feedback
              </button>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class Feedback360FormComponent implements OnInit {
  private readonly service = inject(Feedback360Service);
  private readonly route = inject(ActivatedRoute);
  private readonly toastr = inject(ToastrService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly form = signal<IFeedbackForm | null>(null);
  readonly submitting = signal(false);

  /** Collapsed question ids (NFR-5 collapsible sections). */
  private readonly collapsed = signal<Set<string>>(new Set());

  private assignmentId = '';

  readonly canSubmit = computed(() =>
    canSubmitFeedback(this.form()?.questions ?? []),
  );

  readonly unrated = computed(() =>
    unratedQuestionTitles(this.form()?.questions ?? []),
  );

  ngOnInit(): void {
    this.assignmentId = this.route.snapshot.paramMap.get('assignmentId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getFeedbackForm(this.assignmentId).subscribe({
      next: (f) => {
        this.form.set(f);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load this feedback form.');
        this.loading.set(false);
      },
    });
  }

  stars(): number[] {
    const max = this.form()?.ratingScaleMax ?? 5;
    return Array.from({ length: max }, (_, i) => i + 1);
  }

  setRating(question: IFeedbackQuestion, rating: number): void {
    const f = this.form();
    if (!f || f.submitted) {
      return;
    }
    this.form.set({
      ...f,
      questions: f.questions.map((q) =>
        q.questionId === question.questionId ? { ...q, rating } : q,
      ),
    });
  }

  setComment(question: IFeedbackQuestion, comment: string): void {
    const f = this.form();
    if (!f || f.submitted) {
      return;
    }
    this.form.set({
      ...f,
      questions: f.questions.map((q) =>
        q.questionId === question.questionId ? { ...q, comment } : q,
      ),
    });
  }

  toggle(questionId: string): void {
    const next = new Set(this.collapsed());
    if (next.has(questionId)) {
      next.delete(questionId);
    } else {
      next.add(questionId);
    }
    this.collapsed.set(next);
  }

  isCollapsed(questionId: string): boolean {
    return this.collapsed().has(questionId);
  }

  /** AC-3: submit the reviewer's feedback; locks the form on success. */
  submit(): void {
    const f = this.form();
    if (!f || !this.canSubmit() || this.submitting()) {
      return;
    }
    const request: ISubmitFeedbackRequest = {
      answers: f.questions.map((q) => ({
        questionId: q.questionId,
        rating: q.rating,
        comment: q.comment,
      })),
    };
    this.submitting.set(true);
    this.service.submitFeedback(this.assignmentId, request).subscribe({
      next: (updated) => {
        this.form.set(updated);
        this.submitting.set(false);
        this.toastr.success('Feedback submitted. Thank you!');
      },
      error: () => {
        this.submitting.set(false);
        this.toastr.error('Unable to submit feedback. Please try again.');
      },
    });
  }

  categoryLabel(f: IFeedbackForm): string {
    return REVIEWER_CATEGORY_LABEL[f.category];
  }

  categoryBadge(f: IFeedbackForm): string {
    return REVIEWER_CATEGORY_BADGE[f.category];
  }
}
