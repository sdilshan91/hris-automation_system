import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import {
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { ExitInterviewService } from '../../services/exit-interview.service';
import {
  IExitInterviewTemplate,
  IExitQuestion,
  IRecordExitInterviewRequest,
  IExitResponse,
  InterviewMode,
  RATING_MIN,
  RATING_MAX,
  ratingLabel,
  MAX_FREE_TEXT_LEN,
  MAX_ADDITIONAL_COMMENTS_LEN,
  todayIso,
  totalRequiredQuestions,
  isAnswered,
  completionPercent,
} from '../../models/exit-interview.models';

/** Render row pairing a flat question with its reactive control + category index. */
interface IQuestionRow {
  question: IExitQuestion;
  control: FormControl;
  categoryIndex: number;
}

/**
 * US-ONB-006: Exit Interview questionnaire form (AC-1/AC-2/AC-3).
 *
 * Routed at `exit-interview/:offboardingId` (routed input() with a route-snapshot
 * fallback). Renders the tenant's template grouped by category, one question per
 * card: rating → 1–5 star/emoji selector with labels; multiple_choice → radio
 * chips; free_text → auto-expanding textarea with char count; yes_no → toggle.
 * A progress bar tracks completion across the REQUIRED questions. The overall
 * experience rating, would-recommend toggle, and additional comments sit below.
 *
 * Works for BOTH modes (FR-2): the mode is DERIVED, not user-chosen — an HR user
 * (HR Officer / HR Manager / Tenant Admin) reaching this is `hr_conducted`; a plain
 * departing employee is `self_service`. `conducted_by`, `tenant_id`, and the
 * notification fan-out are all server-set (FR-6/FR-8); the FE only POSTs the answers.
 *
 * BR-1/BR-2: one immutable interview per offboarding — if GET ?offboardingId= returns
 * an existing record we show the read-only submitted state instead of the form.
 *
 * Standalone, signals-first, OnPush. WCAG 2.1 AA: radiogroup roles on the rating
 * selector, labelled controls, inline field errors with role="alert", live-region
 * status. Responsive 360px+ with large touch-friendly rating targets.
 */
@Component({
  selector: 'app-exit-interview-form',
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
    <div class="mx-auto max-w-3xl px-4 py-6 sm:px-6">
      <!-- Header + breadcrumb -->
      <div class="mb-6">
        <nav class="text-xs text-neutral-400" aria-label="Breadcrumb">
          <span>Offboarding</span>
          <span class="mx-1">/</span>
          <span class="text-neutral-600">Exit interview</span>
        </nav>
        <h1 class="mt-1 text-2xl font-semibold tracking-tight text-neutral-900">
          Exit interview
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          {{ mode() === 'self_service'
            ? 'Your honest feedback helps us improve. All answers are confidential.'
            : 'Record the departing employee’s responses.' }}
        </p>
      </div>

      @if (loading()) {
        <div class="space-y-4" data-testid="skeletons">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ loadError() }}
          <button class="ml-2 font-medium underline" (click)="reload()">Retry</button>
        </div>
      } @else if (submitted()) {
        <!-- Success state (UI/UX §8) -->
        <div
          @fadeIn
          class="rounded-xl border border-emerald-200 bg-emerald-50 px-6 py-10 text-center"
          role="status"
          data-testid="success"
        >
          <div
            class="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-emerald-100 text-3xl"
            aria-hidden="true"
          >
            🎉
          </div>
          <p class="text-lg font-semibold text-emerald-800">
            Exit interview recorded. Thank you for the feedback.
          </p>
          @if (mode() === 'hr_conducted') {
            <button
              type="button"
              class="mt-5 rounded-lg bg-neutral-900 px-4 py-2 text-sm font-medium text-white transition hover:bg-neutral-700"
              (click)="backToDashboard()"
              data-testid="back-to-dashboard"
            >
              Back to offboarding
            </button>
          }
        </div>
      } @else if (alreadyRecorded()) {
        <!-- BR-1/BR-2: an interview already exists — immutable -->
        <div
          class="rounded-xl border border-neutral-200 bg-white px-6 py-8 text-center shadow-sm"
          role="status"
          data-testid="already-recorded"
        >
          <p class="text-base font-semibold text-neutral-800">
            An exit interview has already been recorded for this offboarding.
          </p>
          <p class="mt-1 text-sm text-neutral-500">
            Responses are immutable once submitted.
          </p>
        </div>
      } @else if (template(); as tmpl) {
        <!-- Progress bar (UI/UX §8) -->
        <div class="sticky top-0 z-10 -mx-4 mb-6 bg-neutral-50/90 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6">
          <div class="flex items-center justify-between text-xs font-medium text-neutral-500">
            <span>Progress</span>
            <span data-testid="progress-label">{{ progress() }}%</span>
          </div>
          <div
            class="mt-1.5 h-2 w-full overflow-hidden rounded-full bg-neutral-200"
            role="progressbar"
            [attr.aria-valuenow]="progress()"
            aria-valuemin="0"
            aria-valuemax="100"
          >
            <div
              class="h-full rounded-full bg-neutral-900 transition-all duration-300"
              [style.width.%]="progress()"
              data-testid="progress-fill"
            ></div>
          </div>
        </div>

        <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-8" novalidate>
          <!-- Questions grouped by category -->
          @for (cat of tmpl.categories; track cat.category; let ci = $index) {
            <section data-testid="category">
              <h2 class="mb-3 text-sm font-semibold uppercase tracking-wide text-neutral-500">
                {{ cat.category }}
              </h2>
              <div class="space-y-4">
                @for (q of cat.questions; track q.questionId) {
                  <div
                    class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
                    data-testid="question-card"
                  >
                    <p class="text-sm font-medium text-neutral-800">
                      {{ q.text }}
                      @if (q.required) {
                        <span class="text-rose-500" aria-hidden="true">*</span>
                      }
                    </p>

                    @let ctrl = controlFor(q.questionId);

                    <!-- rating: 1–5 star/emoji selector -->
                    @if (q.type === 'rating') {
                      <div
                        class="mt-3 flex flex-wrap gap-1.5"
                        role="radiogroup"
                        [attr.aria-label]="q.text"
                      >
                        @for (n of ratingScale; track n) {
                          <button
                            type="button"
                            role="radio"
                            [attr.aria-checked]="ctrl.value === n"
                            [attr.aria-label]="n + ' — ' + label(n)"
                            class="flex min-h-11 min-w-11 flex-col items-center justify-center rounded-lg border px-2 py-1.5 text-2xl transition"
                            [class.border-neutral-900]="ctrl.value === n"
                            [class.bg-neutral-50]="ctrl.value === n"
                            [class.border-neutral-200]="ctrl.value !== n"
                            (click)="setRating(q.questionId, n)"
                            [attr.data-testid]="'rating-' + q.questionId + '-' + n"
                          >
                            <span aria-hidden="true">{{ n <= (ctrl.value || 0) ? '★' : '☆' }}</span>
                            <span class="mt-0.5 text-[10px] font-medium text-neutral-500">{{ n }}</span>
                          </button>
                        }
                      </div>
                      @if (ctrl.value) {
                        <p class="mt-1.5 text-xs text-neutral-500" data-testid="rating-label">
                          {{ label(ctrl.value) }}
                        </p>
                      }
                    }

                    <!-- multiple_choice: radio chips -->
                    @if (q.type === 'multiple_choice') {
                      <div class="mt-3 flex flex-wrap gap-2" role="radiogroup" [attr.aria-label]="q.text">
                        @for (opt of q.options ?? []; track opt) {
                          <button
                            type="button"
                            role="radio"
                            [attr.aria-checked]="ctrl.value === opt"
                            class="min-h-11 rounded-full border px-4 py-2 text-sm transition"
                            [class.border-neutral-900]="ctrl.value === opt"
                            [class.bg-neutral-900]="ctrl.value === opt"
                            [class.text-white]="ctrl.value === opt"
                            [class.border-neutral-200]="ctrl.value !== opt"
                            [class.text-neutral-700]="ctrl.value !== opt"
                            (click)="setChoice(q.questionId, opt)"
                            [attr.data-testid]="'choice-' + q.questionId"
                          >
                            {{ opt }}
                          </button>
                        }
                      </div>
                    }

                    <!-- free_text: auto-expanding textarea + char count -->
                    @if (q.type === 'free_text') {
                      <textarea
                        [formControl]="ctrl"
                        rows="3"
                        [maxlength]="maxFreeText"
                        class="mt-3 w-full resize-none rounded-lg border border-neutral-200 px-3 py-2 text-sm transition focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
                        [attr.aria-label]="q.text"
                        (input)="autoGrow($event)"
                        [attr.data-testid]="'freetext-' + q.questionId"
                      ></textarea>
                      <p class="mt-1 text-right text-xs text-neutral-400">
                        {{ (ctrl.value || '').length }} / {{ maxFreeText }}
                      </p>
                    }

                    <!-- yes_no: toggle -->
                    @if (q.type === 'yes_no') {
                      <div class="mt-3 inline-flex rounded-lg border border-neutral-200 p-0.5" role="radiogroup" [attr.aria-label]="q.text">
                        <button
                          type="button"
                          role="radio"
                          [attr.aria-checked]="ctrl.value === true"
                          class="min-h-11 rounded-md px-5 py-1.5 text-sm font-medium transition"
                          [class.bg-neutral-900]="ctrl.value === true"
                          [class.text-white]="ctrl.value === true"
                          [class.text-neutral-600]="ctrl.value !== true"
                          (click)="setYesNo(q.questionId, true)"
                          [attr.data-testid]="'yes-' + q.questionId"
                        >
                          Yes
                        </button>
                        <button
                          type="button"
                          role="radio"
                          [attr.aria-checked]="ctrl.value === false"
                          class="min-h-11 rounded-md px-5 py-1.5 text-sm font-medium transition"
                          [class.bg-neutral-900]="ctrl.value === false"
                          [class.text-white]="ctrl.value === false"
                          [class.text-neutral-600]="ctrl.value !== false"
                          (click)="setYesNo(q.questionId, false)"
                          [attr.data-testid]="'no-' + q.questionId"
                        >
                          No
                        </button>
                      </div>
                    }

                    @if (q.required && ctrl.touched && !answered(ctrl.value)) {
                      <p class="mt-2 text-xs text-rose-600" role="alert" data-testid="required-error">
                        This question is required.
                      </p>
                    }
                  </div>
                }
              </div>
            </section>
          }

          <!-- Overall fields (Data Requirements §7) -->
          <section class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm">
            <h2 class="mb-4 text-sm font-semibold uppercase tracking-wide text-neutral-500">
              Overall
            </h2>

            <p class="text-sm font-medium text-neutral-800">
              Overall experience rating
            </p>
            <div class="mt-2 flex flex-wrap gap-1.5" role="radiogroup" aria-label="Overall experience rating">
              @for (n of ratingScale; track n) {
                <button
                  type="button"
                  role="radio"
                  [attr.aria-checked]="form.controls.overallExperienceRating.value === n"
                  [attr.aria-label]="n + ' — ' + label(n)"
                  class="flex min-h-11 min-w-11 items-center justify-center rounded-lg border px-2 py-1.5 text-2xl transition"
                  [class.border-neutral-900]="form.controls.overallExperienceRating.value === n"
                  [class.border-neutral-200]="form.controls.overallExperienceRating.value !== n"
                  (click)="setOverall(n)"
                  [attr.data-testid]="'overall-' + n"
                >
                  <span aria-hidden="true">{{ n <= (form.controls.overallExperienceRating.value || 0) ? '★' : '☆' }}</span>
                </button>
              }
            </div>

            <p class="mt-5 text-sm font-medium text-neutral-800">
              Would you recommend this employer?
            </p>
            <div class="mt-2 inline-flex rounded-lg border border-neutral-200 p-0.5" role="radiogroup" aria-label="Would you recommend this employer">
              <button
                type="button"
                role="radio"
                [attr.aria-checked]="form.controls.wouldRecommendEmployer.value === true"
                class="min-h-11 rounded-md px-5 py-1.5 text-sm font-medium transition"
                [class.bg-neutral-900]="form.controls.wouldRecommendEmployer.value === true"
                [class.text-white]="form.controls.wouldRecommendEmployer.value === true"
                [class.text-neutral-600]="form.controls.wouldRecommendEmployer.value !== true"
                (click)="setRecommend(true)"
                data-testid="recommend-yes"
              >
                Yes
              </button>
              <button
                type="button"
                role="radio"
                [attr.aria-checked]="form.controls.wouldRecommendEmployer.value === false"
                class="min-h-11 rounded-md px-5 py-1.5 text-sm font-medium transition"
                [class.bg-neutral-900]="form.controls.wouldRecommendEmployer.value === false"
                [class.text-white]="form.controls.wouldRecommendEmployer.value === false"
                [class.text-neutral-600]="form.controls.wouldRecommendEmployer.value !== false"
                (click)="setRecommend(false)"
                data-testid="recommend-no"
              >
                No
              </button>
            </div>

            <label for="additionalComments" class="mt-5 block text-sm font-medium text-neutral-800">
              Additional comments
            </label>
            <textarea
              id="additionalComments"
              formControlName="additionalComments"
              rows="3"
              [maxlength]="maxComments"
              class="mt-2 w-full resize-none rounded-lg border border-neutral-200 px-3 py-2 text-sm transition focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
              (input)="autoGrow($event)"
              data-testid="additional-comments"
            ></textarea>
            <p class="mt-1 text-right text-xs text-neutral-400">
              {{ (form.controls.additionalComments.value || '').length }} / {{ maxComments }}
            </p>
          </section>

          <!-- Submit -->
          <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-end">
            @if (submitError()) {
              <p class="flex-1 text-sm text-rose-600" role="alert" data-testid="submit-error">
                {{ submitError() }}
              </p>
            }
            <button
              type="submit"
              class="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg bg-neutral-900 px-6 py-2.5 text-sm font-medium text-white transition hover:bg-neutral-700 disabled:opacity-50"
              [disabled]="submitting()"
              data-testid="submit"
            >
              @if (submitting()) {
                <span class="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"></span>
              }
              Submit exit interview
            </button>
          </div>
        </form>
      }
    </div>
  `,
})
export class ExitInterviewFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(ExitInterviewService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  /** Routed input — offboarding instance id this interview attaches to. */
  readonly offboardingId = input<string>('');

  readonly ratingScale = Array.from(
    { length: RATING_MAX - RATING_MIN + 1 },
    (_, i) => RATING_MIN + i,
  );
  readonly maxFreeText = MAX_FREE_TEXT_LEN;
  readonly maxComments = MAX_ADDITIONAL_COMMENTS_LEN;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly template = signal<IExitInterviewTemplate | null>(null);
  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly submitted = signal(false);
  readonly alreadyRecorded = signal(false);

  /**
   * Interview mode is DERIVED from role, never chosen (FR-2): an HR persona reaching
   * this screen records on the employee's behalf; anyone else is the departing
   * employee filling it in themselves. The backend re-derives + authorizes.
   */
  readonly mode = signal<InterviewMode>('self_service');

  readonly form = new FormGroup({
    responses: new FormArray<FormControl>([]),
    overallExperienceRating: new FormControl<number | null>(null),
    wouldRecommendEmployer: new FormControl<boolean | null>(null),
    additionalComments: new FormControl<string>('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_ADDITIONAL_COMMENTS_LEN)],
    }),
  });

  /** Flat questionId → control row map built once the template loads. */
  private rows: IQuestionRow[] = [];

  /** Live form value signal so progress recomputes on every answer change. */
  private readonly formValue = toSignal(this.form.valueChanges, {
    initialValue: this.form.value,
  });

  /** Completion percent across the REQUIRED questions (drives the progress bar). */
  readonly progress = computed(() => {
    // Touch formValue so this recomputes on input.
    this.formValue();
    const tmpl = this.template();
    const total = totalRequiredQuestions(tmpl);
    const answeredRequired = this.rows.filter(
      (r) => r.question.required && isAnswered(r.control.value),
    ).length;
    return completionPercent(answeredRequired, total);
  });

  ngOnInit(): void {
    this.mode.set(
      this.auth.hasAnyPermission(['ExitInterview.Conduct']) ||
        this.isHrRole()
        ? 'hr_conducted'
        : 'self_service',
    );
    this.reload();
  }

  reload(): void {
    const id = this.resolveOffboardingId();
    if (!id) {
      this.loadError.set('No offboarding reference was provided.');
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.loadError.set(null);

    // BR-1: if an interview already exists, show the immutable state instead of
    // the form. A 404 is the expected "none yet" path → load the template.
    this.service.getByOffboarding(id).subscribe({
      next: () => {
        this.alreadyRecorded.set(true);
        this.loading.set(false);
      },
      error: () => this.loadTemplate(),
    });
  }

  private loadTemplate(): void {
    this.service.getTemplate().subscribe({
      next: (tmpl) => {
        this.buildControls(tmpl);
        this.template.set(tmpl);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load the exit interview questionnaire.');
        this.loading.set(false);
      },
    });
  }

  private buildControls(tmpl: IExitInterviewTemplate): void {
    const arr = this.form.controls.responses;
    arr.clear();
    this.rows = [];
    tmpl.categories.forEach((cat, ci) => {
      cat.questions.forEach((q) => {
        const control = this.fb.control<unknown>(
          q.type === 'free_text' ? '' : null,
          q.type === 'free_text'
            ? [Validators.maxLength(MAX_FREE_TEXT_LEN)]
            : [],
        );
        arr.push(control);
        this.rows.push({ question: q, control: control as FormControl, categoryIndex: ci });
      });
    });
  }

  // ── Template helpers ─────────────────────────────────────

  controlFor(questionId: string): FormControl {
    const row = this.rows.find((r) => r.question.questionId === questionId);
    return row!.control;
  }

  label(value: number): string {
    return ratingLabel(value);
  }

  answered(value: unknown): boolean {
    return isAnswered(value);
  }

  setRating(questionId: string, n: number): void {
    const c = this.controlFor(questionId);
    c.setValue(c.value === n ? null : n);
    c.markAsTouched();
  }

  setChoice(questionId: string, opt: string): void {
    const c = this.controlFor(questionId);
    c.setValue(opt);
    c.markAsTouched();
  }

  setYesNo(questionId: string, value: boolean): void {
    const c = this.controlFor(questionId);
    c.setValue(c.value === value ? null : value);
    c.markAsTouched();
  }

  setOverall(n: number): void {
    const c = this.form.controls.overallExperienceRating;
    c.setValue(c.value === n ? null : n);
  }

  setRecommend(value: boolean): void {
    const c = this.form.controls.wouldRecommendEmployer;
    c.setValue(c.value === value ? null : value);
  }

  autoGrow(event: Event): void {
    const el = event.target as HTMLTextAreaElement;
    el.style.height = 'auto';
    el.style.height = `${el.scrollHeight}px`;
  }

  // ── Submit (AC-2/AC-3) ───────────────────────────────────

  submit(): void {
    if (this.submitting()) return;

    // Required-question gate (FR-1): mark every required control touched so the
    // inline errors render, then block the POST if any are unanswered.
    let firstMissing: IQuestionRow | null = null;
    for (const row of this.rows) {
      if (row.question.required && !isAnswered(row.control.value)) {
        row.control.markAsTouched();
        firstMissing ??= row;
      }
    }
    if (firstMissing) {
      this.submitError.set('Please answer all required questions before submitting.');
      return;
    }

    const id = this.resolveOffboardingId();
    if (!id) {
      this.submitError.set('No offboarding reference was provided.');
      return;
    }

    this.submitError.set(null);
    this.submitting.set(true);

    const request: IRecordExitInterviewRequest = {
      offboardingId: id,
      interviewMode: this.mode(),
      interviewDate: todayIso(),
      responses: this.collectResponses(),
    };
    const overall = this.form.controls.overallExperienceRating.value;
    const recommend = this.form.controls.wouldRecommendEmployer.value;
    const comments = this.form.controls.additionalComments.value?.trim();
    if (overall != null) request.overallExperienceRating = overall;
    if (recommend != null) request.wouldRecommendEmployer = recommend;
    if (comments) request.additionalComments = comments;

    this.service.record(request).subscribe({
      next: () => {
        this.submitting.set(false);
        this.submitted.set(true);
        this.toastr.success('Exit interview recorded. Thank you for the feedback.');
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.submitError.set(ExitInterviewService.parseErrorMessage(err));
      },
    });
  }

  /** Map each answered control onto a typed IExitResponse (skips blank answers). */
  private collectResponses(): IExitResponse[] {
    const out: IExitResponse[] = [];
    for (const row of this.rows) {
      const value = row.control.value;
      if (!isAnswered(value)) continue;
      const entry: IExitResponse = { questionId: row.question.questionId };
      switch (row.question.type) {
        case 'rating':
          entry.rating = value as number;
          break;
        case 'multiple_choice':
          entry.selectedOption = value as string;
          break;
        case 'free_text':
          entry.freeText = (value as string).trim();
          break;
        case 'yes_no':
          entry.selectedOption = (value as boolean) ? 'Yes' : 'No';
          break;
      }
      out.push(entry);
    }
    return out;
  }

  backToDashboard(): void {
    const id = this.resolveOffboardingId();
    if (id) {
      this.router.navigate(['/offboarding', id]);
    }
  }

  // ── Internal ─────────────────────────────────────────────

  private resolveOffboardingId(): string {
    return this.offboardingId() || this.route.snapshot.paramMap.get('offboardingId') || '';
  }

  private isHrRole(): boolean {
    return ['HR Officer', 'HR Manager', 'Tenant Admin'].some((r) =>
      this.auth.hasRole(r),
    );
  }
}
