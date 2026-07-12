import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
} from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { Subscription, interval } from 'rxjs';
import { startWith } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { SelfAssessmentService } from '../../services/self-assessment.service';
import {
  ATTACHMENT_MAX_FILES,
  AUTO_SAVE_INTERVAL_MS,
  IAssessmentAttachment,
  ISaveSelfAssessmentRequest,
  ISelfAssessment,
  ISelfAssessmentGoalInput,
  SELF_COMMENT_MIN,
  SELF_ASSESSMENT_STATUS_BADGE,
  SELF_ASSESSMENT_STATUS_LABEL,
  WINDOW_CLOSED_MESSAGE,
  canSubmitSelfAssessment,
  formatFileSize,
  goalIsComplete,
  ratedGoalCount,
  validateAttachment,
  weightedSelfScore,
} from '../../models/self-assessment.models';

interface IGoalFormValue {
  selfRating: number | null;
  achievementPercent: number | null;
  comment: string;
}

/**
 * US-PRF-002: Employee self-assessment ("My Review") view (AC-1..AC-4).
 *
 * - AC-1: lists every assigned goal (title, description, weight, target, due date)
 *   each with a self-rating, achievement %, and comment input (FR-1).
 * - FR-2: rating uses the tenant-configured scale (`ratingScaleMax`) as a touch-
 *   friendly star row.
 * - AC-2: "Submit Self-Assessment" is gated on ALL goals rated + each comment
 *   ≥20 chars (FR-3 / BR-2); on submit the view goes locked/read-only.
 * - AC-3: "Save as Draft" persists partial progress (FR-6).
 * - AC-4: when the window is closed (`windowOpen` false) the view is read-only with
 *   the WINDOW_CLOSED_MESSAGE.
 * - NFR-3: the draft auto-saves every 60s while editing and dirty.
 * - §8: expandable Notion-like cards, "N of M rated" progress, sticky mobile submit.
 *
 * The rating scale is data-driven so no chart/UI library is added; the form is a
 * single FormArray mirroring `assessment().goals` by index.
 */
@Component({
  selector: 'app-my-review',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl px-4 pb-28 pt-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          My Review
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          @if (assessment(); as a) {
            {{ a.cycleName }} — rate your performance against each goal.
          } @else {
            Rate your performance against each assigned goal.
          }
        </p>
      </div>

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-28 animate-pulse rounded-xl bg-neutral-100"></div>
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
      } @else if (assessment(); as a) {
        <!-- AC-4: closed-window banner -->
        @if (!a.windowOpen && !isSubmitted()) {
          <div
            class="mb-6 flex items-start gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
            role="status"
          >
            <span aria-hidden="true">🔒</span>
            <span>{{ windowClosedMessage }}</span>
          </div>
        }

        <!-- AC-2: submitted/locked banner -->
        @if (isSubmitted()) {
          <div
            class="mb-6 flex items-start gap-3 rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-800"
            role="status"
          >
            <span aria-hidden="true">✓</span>
            <div>
              <p class="font-medium">{{ statusLabel(a.status) }}</p>
              <p class="mt-0.5 text-emerald-700/80">
                Your self-assessment is locked while your manager reviews it.
                @if (a.weightedScore != null) {
                  Weighted self-score: {{ a.weightedScore }}%.
                }
              </p>
            </div>
          </div>
        }

        <!-- Progress indicator (§8) -->
        <div
          class="mb-6 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
        >
          <div class="mb-2 flex items-center justify-between">
            <span class="text-sm font-medium text-neutral-700">Progress</span>
            <span
              class="rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
              [class]="statusBadge(a.status)"
            >
              {{ statusLabel(a.status) }}
            </span>
          </div>
          <div class="flex items-center justify-between text-sm">
            <span class="font-semibold text-neutral-900" data-testid="rated-count">
              {{ ratedCount() }} of {{ totalGoals() }} goals rated
            </span>
            <span class="text-neutral-500">
              Weighted score: {{ liveScore() }}%
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

        @if (totalGoals() === 0) {
          <div
            class="rounded-xl border border-neutral-200 bg-white p-8 text-center text-sm text-neutral-500"
          >
            No goals have been assigned for this cycle yet.
          </div>
        }

        <!-- Goals -->
        <form [formGroup]="form" (ngSubmit)="submit()">
          <div formArrayName="goals" class="space-y-4">
            @for (goal of a.goals; track goal.goalId; let i = $index) {
              <div
                [formGroupName]="i"
                class="rounded-xl border border-neutral-200 bg-white shadow-sm transition"
                [class.ring-1]="goalComplete(i)"
                [class.ring-emerald-200]="goalComplete(i)"
              >
                <!-- Card header (expand toggle) -->
                <button
                  type="button"
                  class="flex w-full items-start justify-between gap-3 p-4 text-left"
                  (click)="toggle(i)"
                  [attr.aria-expanded]="isExpanded(i)"
                >
                  <div class="min-w-0">
                    <div class="flex items-center gap-2">
                      @if (goalComplete(i)) {
                        <span
                          class="inline-flex h-5 w-5 flex-none items-center justify-center rounded-full bg-emerald-100 text-xs text-emerald-700"
                          aria-label="Rated"
                          >✓</span
                        >
                      } @else {
                        <span
                          class="inline-flex h-5 w-5 flex-none items-center justify-center rounded-full bg-neutral-100 text-xs text-neutral-400"
                          aria-label="Not yet rated"
                          >{{ i + 1 }}</span
                        >
                      }
                      <h3
                        class="truncate text-sm font-semibold text-neutral-900"
                      >
                        {{ goal.title }}
                      </h3>
                    </div>
                    <p class="mt-1 text-xs text-neutral-500">
                      Weight {{ goal.weight }}% · Target {{ goal.targetValue }}
                      {{ goal.measurementUnit }} · Due {{ goal.dueDate }}
                    </p>
                  </div>
                  <span
                    class="mt-1 flex-none text-neutral-400 transition-transform"
                    [class.rotate-180]="isExpanded(i)"
                    aria-hidden="true"
                    >▾</span
                  >
                </button>

                <!-- Card body -->
                @if (isExpanded(i)) {
                  <div class="border-t border-neutral-100 p-4">
                    @if (goal.description) {
                      <p class="mb-4 text-sm text-neutral-600">
                        {{ goal.description }}
                      </p>
                    }

                    <!-- Rating (FR-2): tenant-scaled stars -->
                    <div class="mb-4">
                      <span
                        class="mb-1 block text-xs font-medium text-neutral-600"
                        >Self-rating (1–{{ a.ratingScaleMax }})</span
                      >
                      <div
                        class="flex flex-wrap gap-1"
                        role="radiogroup"
                        [attr.aria-label]="'Self-rating for ' + goal.title"
                      >
                        @for (n of scaleStars(); track n) {
                          <button
                            type="button"
                            class="flex h-9 w-9 items-center justify-center rounded-lg text-lg transition disabled:cursor-not-allowed"
                            [class.text-amber-400]="(ratingOf(i) ?? 0) >= n"
                            [class.text-neutral-300]="(ratingOf(i) ?? 0) < n"
                            [class.hover:bg-neutral-100]="canEdit()"
                            role="radio"
                            [attr.aria-checked]="ratingOf(i) === n"
                            [attr.aria-label]="n + ' of ' + a.ratingScaleMax"
                            [disabled]="!canEdit()"
                            (click)="setRating(i, n)"
                          >
                            ★
                          </button>
                        }
                        @if (ratingOf(i) != null) {
                          <span
                            class="ml-2 self-center text-sm font-medium text-neutral-700"
                            >{{ ratingOf(i) }}/{{ a.ratingScaleMax }}</span
                          >
                        }
                      </div>
                    </div>

                    <!-- Achievement % (FR-3) -->
                    <div class="mb-4">
                      <label
                        class="mb-1 flex items-center justify-between text-xs font-medium text-neutral-600"
                        [attr.for]="'ach-' + i"
                      >
                        <span>Achievement</span>
                        <span class="text-neutral-900"
                          >{{ achievementOf(i) ?? 0 }}%</span
                        >
                      </label>
                      <input
                        [id]="'ach-' + i"
                        type="range"
                        formControlName="achievementPercent"
                        min="0"
                        max="100"
                        step="5"
                        class="w-full accent-emerald-500 disabled:opacity-50"
                      />
                    </div>

                    <!-- Comment (FR-3, min 20 chars) -->
                    <div>
                      <label
                        class="mb-1 block text-xs font-medium text-neutral-600"
                        [attr.for]="'cmt-' + i"
                        >Self-assessment comment</label
                      >
                      <textarea
                        [id]="'cmt-' + i"
                        formControlName="comment"
                        rows="3"
                        class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                        placeholder="Describe your achievements and evidence (min 20 characters)…"
                      ></textarea>
                      <div
                        class="mt-1 flex items-center justify-between text-xs"
                      >
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

                    <!-- Attachments (FR-5) -->
                    <div class="mt-4 border-t border-neutral-100 pt-4">
                      <span
                        class="mb-2 block text-xs font-medium text-neutral-600"
                        >Evidence ({{ goal.attachments.length }}/{{
                          maxFiles
                        }})</span
                      >
                      @if (goal.attachments.length) {
                        <ul class="mb-2 space-y-1">
                          @for (att of goal.attachments; track att.id) {
                            <li
                              class="flex items-center justify-between rounded-lg bg-neutral-50 px-3 py-1.5 text-sm"
                            >
                              <span class="truncate text-neutral-700">
                                📎 {{ att.fileName }}
                                <span class="text-neutral-400"
                                  >({{ sizeLabel(att.sizeBytes) }})</span
                                >
                              </span>
                              @if (canEdit()) {
                                <button
                                  type="button"
                                  class="ml-2 flex-none text-xs font-medium text-rose-600 hover:underline"
                                  (click)="removeAttachment(i, att)"
                                  [attr.aria-label]="'Remove ' + att.fileName"
                                >
                                  Remove
                                </button>
                              }
                            </li>
                          }
                        </ul>
                      }
                      @if (canEdit() && goal.attachments.length < maxFiles) {
                        <label
                          class="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-dashed border-neutral-300 px-3 py-2 text-xs font-medium text-neutral-600 transition hover:border-neutral-400 hover:bg-neutral-50"
                        >
                          <span>＋ Add evidence file</span>
                          <input
                            type="file"
                            class="hidden"
                            (change)="onFileSelected(i, $event)"
                          />
                        </label>
                      }
                      @if (uploadingIndex() === i) {
                        <p class="mt-1 text-xs text-neutral-500">
                          Uploading… {{ uploadProgress() }}%
                        </p>
                      }
                    </div>
                  </div>
                }
              </div>
            }
          </div>

          <!-- Actions (AC-2 / AC-3) — sticky on mobile (§8) -->
          @if (canEdit() && totalGoals() > 0) {
            <div
              class="fixed inset-x-0 bottom-0 z-10 border-t border-neutral-200 bg-white/95 p-4 backdrop-blur sm:static sm:mt-6 sm:border-0 sm:bg-transparent sm:p-0 sm:backdrop-blur-none"
            >
              <div
                class="mx-auto flex max-w-3xl flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-end"
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
                  [disabled]="!canSubmit() || busy()"
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
                    Submit Self-Assessment
                  }
                </button>
              </div>
              @if (!canSubmit()) {
                <p
                  class="mx-auto mt-2 max-w-3xl text-center text-xs text-neutral-400 sm:text-right"
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
export class MyReviewComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(SelfAssessmentService);
  private readonly toastr = inject(ToastrService);

  readonly commentMin = SELF_COMMENT_MIN;
  readonly maxFiles = ATTACHMENT_MAX_FILES;
  readonly windowClosedMessage = WINDOW_CLOSED_MESSAGE;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly savingDraft = signal(false);
  readonly submitting = signal(false);
  readonly assessment = signal<ISelfAssessment | null>(null);
  readonly expanded = signal<Set<number>>(new Set([0]));
  readonly uploadingIndex = signal<number | null>(null);
  readonly uploadProgress = signal(0);

  private autoSaveSub?: Subscription;
  /** Set true on every edit; gates the 60s auto-save so it only fires when dirty. */
  private pendingChanges = false;

  readonly form: FormGroup = this.fb.group({
    goals: this.fb.array([]),
  });

  /** Live snapshot of the goals FormArray value. */
  private readonly goalsValue = toSignal(
    this.goals.valueChanges.pipe(startWith(this.goals.value)),
    { initialValue: this.goals.value as IGoalFormValue[] },
  );

  readonly busy = computed(() => this.savingDraft() || this.submitting());

  readonly isSubmitted = computed(
    () => this.assessment()?.status === 'Submitted',
  );

  /** AC-2/AC-4: editing allowed only when the window is open AND not yet submitted. */
  readonly canEdit = computed(
    () => (this.assessment()?.windowOpen ?? false) && !this.isSubmitted(),
  );

  readonly totalGoals = computed(() => this.assessment()?.goals.length ?? 0);

  readonly ratedCount = computed(() =>
    ratedGoalCount(this.goalsValue() as IGoalFormValue[]),
  );

  readonly progressPercent = computed(() => {
    const total = this.totalGoals();
    return total === 0 ? 0 : Math.round((this.ratedCount() / total) * 100);
  });

  /** Live weighted self-score preview (FR-4); backend is authoritative on submit. */
  readonly liveScore = computed(() => {
    const a = this.assessment();
    if (!a) {
      return 0;
    }
    const values = this.goalsValue() as IGoalFormValue[];
    const goals = a.goals.map((g, i) => ({
      weight: g.weight,
      selfRating: values[i]?.selfRating ?? null,
    }));
    return weightedSelfScore(goals, a.ratingScaleMax);
  });

  /** AC-2 submit gate: all goals complete (rated + comment ≥20) and edit allowed. */
  readonly canSubmit = computed(
    () =>
      this.canEdit() &&
      canSubmitSelfAssessment(this.goalsValue() as IGoalFormValue[]),
  );

  get goals(): FormArray {
    return this.form.get('goals') as FormArray;
  }

  ngOnInit(): void {
    this.load();
    // NFR-3: auto-save the draft every 60s while editing and dirty.
    this.autoSaveSub = interval(AUTO_SAVE_INTERVAL_MS).subscribe(() => {
      if (this.canEdit() && this.pendingChanges && !this.busy()) {
        this.saveDraft(true);
      }
    });
  }

  ngOnDestroy(): void {
    this.autoSaveSub?.unsubscribe();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getActive().subscribe({
      next: (assessment) => {
        this.assessment.set(assessment);
        this.hydrate(assessment);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load your self-assessment.');
        this.loading.set(false);
      },
    });
  }

  private hydrate(assessment: ISelfAssessment): void {
    this.goals.clear();
    assessment.goals.forEach((g) => {
      this.goals.push(
        this.fb.group({
          selfRating: [g.selfRating],
          achievementPercent: [g.achievementPercent ?? 0],
          comment: [g.comment ?? ''],
        }),
      );
    });
    if (!this.canEdit()) {
      this.form.disable({ emitEvent: false });
    } else {
      this.goals.valueChanges.subscribe(() => (this.pendingChanges = true));
    }
  }

  // ─── Expand / collapse ──────────────────────────────────────

  toggle(index: number): void {
    const next = new Set(this.expanded());
    if (next.has(index)) {
      next.delete(index);
    } else {
      next.add(index);
    }
    this.expanded.set(next);
  }

  isExpanded(index: number): boolean {
    return this.expanded().has(index);
  }

  // ─── Per-goal field access (template helpers) ───────────────

  private groupAt(index: number): FormGroup {
    return this.goals.at(index) as FormGroup;
  }

  ratingOf(index: number): number | null {
    return this.groupAt(index)?.get('selfRating')?.value ?? null;
  }

  achievementOf(index: number): number | null {
    return this.groupAt(index)?.get('achievementPercent')?.value ?? null;
  }

  commentLength(index: number): number {
    return ((this.groupAt(index)?.get('comment')?.value as string) ?? '').trim()
      .length;
  }

  commentTooShort(index: number): boolean {
    const len = this.commentLength(index);
    return len > 0 && len < this.commentMin;
  }

  goalComplete(index: number): boolean {
    const g = this.groupAt(index)?.value as IGoalFormValue;
    return !!g && goalIsComplete(g);
  }

  scaleStars(): number[] {
    const max = this.assessment()?.ratingScaleMax ?? 5;
    return Array.from({ length: max }, (_, i) => i + 1);
  }

  setRating(index: number, value: number): void {
    if (!this.canEdit()) {
      return;
    }
    const ctrl = this.groupAt(index).get('selfRating');
    // Tap the same star again to clear it (touch-friendly).
    ctrl?.setValue(ctrl.value === value ? null : value);
    ctrl?.markAsDirty();
    this.pendingChanges = true;
  }

  // ─── Attachments (FR-5) ─────────────────────────────────────

  onFileSelected(index: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }
    input.value = ''; // allow re-selecting the same file later
    const assessment = this.assessment();
    if (!assessment) {
      return;
    }
    const goal = assessment.goals[index];
    const reason = validateAttachment(file, goal.attachments.length);
    if (reason === 'too_many') {
      this.toastr.error(`A maximum of ${this.maxFiles} files is allowed.`);
      return;
    }
    if (reason === 'too_large') {
      this.toastr.error('Each file must be 10MB or smaller.');
      return;
    }
    this.uploadingIndex.set(index);
    this.uploadProgress.set(0);
    this.service
      .uploadAttachment(assessment.cycleId, goal.goalId, file)
      .subscribe({
        next: (event) => {
          if (event.type === 'progress') {
            this.uploadProgress.set(event.progress);
          } else {
            this.addAttachmentToState(index, event.attachment);
            this.uploadingIndex.set(null);
            this.toastr.success('Evidence uploaded.');
          }
        },
        error: () => {
          this.uploadingIndex.set(null);
          this.toastr.error('Could not upload the file. Please try again.');
        },
      });
  }

  removeAttachment(index: number, att: IAssessmentAttachment): void {
    const assessment = this.assessment();
    if (!assessment || !this.canEdit()) {
      return;
    }
    this.service.deleteAttachment(assessment.id, att.id).subscribe({
      next: () => this.removeAttachmentFromState(index, att.id),
      error: () => this.toastr.error('Could not remove the file.'),
    });
  }

  private addAttachmentToState(
    index: number,
    att: IAssessmentAttachment,
  ): void {
    const current = this.assessment();
    if (!current) {
      return;
    }
    const goals = current.goals.map((g, i) =>
      i === index ? { ...g, attachments: [...g.attachments, att] } : g,
    );
    this.assessment.set({ ...current, goals });
  }

  private removeAttachmentFromState(index: number, attId: string): void {
    const current = this.assessment();
    if (!current) {
      return;
    }
    const goals = current.goals.map((g, i) =>
      i === index
        ? { ...g, attachments: g.attachments.filter((a) => a.id !== attId) }
        : g,
    );
    this.assessment.set({ ...current, goals });
  }

  // ─── Save / submit ──────────────────────────────────────────

  private buildRequest(): ISaveSelfAssessmentRequest {
    const assessment = this.assessment();
    const values = this.goals.value as IGoalFormValue[];
    const items: ISelfAssessmentGoalInput[] = (assessment?.goals ?? []).map(
      (g, i) => ({
        goalId: g.goalId,
        selfRating: values[i]?.selfRating ?? null,
        achievementPercentage: values[i]?.achievementPercent ?? null,
        comment: values[i]?.comment ?? '',
      }),
    );
    return { cycleId: assessment?.cycleId ?? '', items };
  }

  /** AC-3 / NFR-3: persist a partial draft. `silent` suppresses the toast for auto-save. */
  saveDraft(silent = false): void {
    const assessment = this.assessment();
    if (!assessment || !this.canEdit() || this.busy()) {
      return;
    }
    this.savingDraft.set(true);
    this.service.saveDraft(this.buildRequest()).subscribe({
      next: (saved) => {
        this.savingDraft.set(false);
        this.pendingChanges = false;
        // Keep server-side attachment/score state in sync without re-hydrating
        // the editable controls (don't clobber what the user is typing).
        this.assessment.set({ ...saved });
        if (!silent) {
          this.toastr.success('Draft saved.');
        }
      },
      error: () => {
        this.savingDraft.set(false);
        if (!silent) {
          this.toastr.error('Could not save your draft. Please try again.');
        }
      },
    });
  }

  /** AC-2: submit when every goal is rated + commented; locks the view on success. */
  submit(): void {
    const assessment = this.assessment();
    if (!assessment || !this.canSubmit() || this.busy()) {
      return;
    }
    this.submitting.set(true);
    this.service.submit(this.buildRequest()).subscribe({
      next: (saved) => {
        this.submitting.set(false);
        this.pendingChanges = false;
        this.assessment.set(saved);
        this.form.disable({ emitEvent: false });
        this.toastr.success(
          'Self-assessment submitted. Your manager has been notified.',
        );
      },
      error: () => {
        this.submitting.set(false);
        this.toastr.error('Could not submit. Please try again.');
      },
    });
  }

  // ─── Display helpers ────────────────────────────────────────

  statusBadge(status: ISelfAssessment['status']): string {
    return SELF_ASSESSMENT_STATUS_BADGE[status];
  }

  statusLabel(status: ISelfAssessment['status']): string {
    return SELF_ASSESSMENT_STATUS_LABEL[status];
  }

  sizeLabel(bytes: number): string {
    return formatFileSize(bytes);
  }
}
