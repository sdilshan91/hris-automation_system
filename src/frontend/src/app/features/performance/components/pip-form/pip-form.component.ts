import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { startWith } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { PipService } from '../../services/pip.service';
import {
  EscalationAction,
  ESCALATION_ACTION_LABEL,
  ICreatePipRequest,
  IPipObjectiveInput,
  PIP_CONFIDENTIAL_NOTICE,
  PIP_DURATION_PRESETS,
  PIP_MIN_DURATION_DAYS,
  addDaysIso,
  pipDurationDays,
  pipDurationValid,
} from '../../models/pip.models';

/**
 * US-PRF-008 AC-1/AC-2: the PIP creation form. Notion-like card sections:
 *  - Employee (pre-filled if arriving from a flagged review via ?employeeId/?reviewId).
 *  - Reason.
 *  - Duration: 30/60/90 presets + custom (start/end dates), min 30 days (BR-3).
 *  - Improvement objectives (title, description, success criteria, due date) —
 *    repeatable, ≥1 required (FR-1).
 *  - Extra checkpoint dates.
 *  - Mentor assignment.
 *  - Escalation action selector (tenant-configured, BR-6).
 *
 * Client-side validation mirrors the backend (min 30 days, ≥1 objective). On
 * "Initiate PIP" the server creates the PIP (status → Active, AC-2) and the FE shows
 * a confirmation toast then routes to the detail/timeline. BR-2 (one active PIP per
 * employee) is surfaced from the draft (`hasActivePip`) and re-enforced server-side.
 */
@Component({
  selector: 'app-pip-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl px-4 py-6 sm:px-6 lg:px-8">
      <div class="mb-4">
        <a
          routerLink="/performance/pips"
          class="text-sm font-medium text-neutral-500 transition hover:text-neutral-700"
        >
          ← Back to PIPs
        </a>
      </div>

      <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
        Create Performance Improvement Plan
      </h1>

      <!-- §8 confidentiality banner -->
      <div
        class="mt-4 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-800"
        role="note"
        data-testid="confidential-banner"
      >
        <span aria-hidden="true">🔒</span>
        <span>{{ confidentialNotice }}</span>
      </div>

      @if (draftLoading()) {
        <div class="mt-6 space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-28 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else {
        @if (hasActivePip()) {
          <div
            class="mt-6 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
            role="alert"
            data-testid="active-pip-warning"
          >
            This employee already has an active PIP. Only one active PIP is
            allowed at a time (BR-2).
          </div>
        }

        <form [formGroup]="form" (ngSubmit)="submit()" class="mt-6 space-y-6">
          <!-- Employee + reason -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="text-sm font-semibold text-neutral-800">Employee</h2>
            <div class="mt-3">
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="employeeName"
              >
                Employee name
              </label>
              <input
                id="employeeName"
                type="text"
                formControlName="employeeName"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                data-testid="employee-name"
              />
              @if (prefilled()) {
                <p class="mt-1 text-xs text-neutral-400">
                  Pre-filled from the flagged review.
                </p>
              }
            </div>
            <div class="mt-4">
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="reason"
              >
                PIP reason
              </label>
              <textarea
                id="reason"
                rows="3"
                formControlName="reason"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                placeholder="Why is this PIP being initiated?"
                data-testid="reason"
              ></textarea>
            </div>
          </section>

          <!-- Duration -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="text-sm font-semibold text-neutral-800">Duration</h2>
            <p class="mt-1 text-xs text-neutral-500">
              Minimum {{ minDays }} days (BR-3). Pick a preset or set custom
              dates.
            </p>
            <div class="mt-3 flex flex-wrap gap-2">
              @for (preset of presets; track preset) {
                <button
                  type="button"
                  class="rounded-lg border px-3 py-1.5 text-sm font-medium transition"
                  [class.border-neutral-300]="true"
                  [class.bg-neutral-900]="false"
                  (click)="applyPreset(preset)"
                  data-testid="duration-preset"
                >
                  {{ preset }} days
                </button>
              }
            </div>
            <div class="mt-4 grid gap-4 sm:grid-cols-2">
              <div>
                <label
                  class="mb-1 block text-sm font-medium text-neutral-700"
                  for="startDate"
                >
                  Start date
                </label>
                <input
                  id="startDate"
                  type="date"
                  formControlName="startDate"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="start-date"
                />
              </div>
              <div>
                <label
                  class="mb-1 block text-sm font-medium text-neutral-700"
                  for="endDate"
                >
                  End date
                </label>
                <input
                  id="endDate"
                  type="date"
                  formControlName="endDate"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="end-date"
                />
              </div>
            </div>
            <p class="mt-2 text-xs" [class.text-rose-600]="durationTooShort()">
              @if (durationTooShort()) {
                <span role="alert" data-testid="duration-error">
                  PIP duration must be at least {{ minDays }} days.
                </span>
              } @else if (durationDays() > 0) {
                <span class="text-neutral-500">
                  {{ durationDays() }} days.
                </span>
              }
            </p>
          </section>

          <!-- Objectives (FR-1, repeatable, ≥1) -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <div class="flex items-center justify-between">
              <h2 class="text-sm font-semibold text-neutral-800">
                Improvement objectives
              </h2>
              <button
                type="button"
                class="rounded-lg px-3 py-1.5 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
                (click)="addObjective()"
                data-testid="add-objective"
              >
                + Add objective
              </button>
            </div>
            @if (objectives.length === 0) {
              <p
                class="mt-3 rounded-lg border border-dashed border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-600"
                role="alert"
                data-testid="objectives-required"
              >
                At least one objective is required.
              </p>
            }
            <div class="mt-4 space-y-4" formArrayName="objectives">
              @for (obj of objectives.controls; track $index) {
                <div
                  [formGroupName]="$index"
                  class="rounded-lg border border-neutral-200 p-4"
                  data-testid="objective-row"
                >
                  <div class="flex items-start justify-between gap-2">
                    <span class="text-xs font-medium text-neutral-400">
                      Objective {{ $index + 1 }}
                    </span>
                    <button
                      type="button"
                      class="text-xs font-medium text-rose-600 transition hover:text-rose-700"
                      (click)="removeObjective($index)"
                      data-testid="remove-objective"
                    >
                      Remove
                    </button>
                  </div>
                  <div class="mt-2 space-y-3">
                    <input
                      type="text"
                      formControlName="title"
                      placeholder="Objective title"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                    />
                    <textarea
                      rows="2"
                      formControlName="description"
                      placeholder="Description"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                    ></textarea>
                    <textarea
                      rows="2"
                      formControlName="successCriteria"
                      placeholder="Measurable success criteria"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                    ></textarea>
                    <div>
                      <label class="mb-1 block text-xs text-neutral-500">
                        Due date
                      </label>
                      <input
                        type="date"
                        formControlName="dueDate"
                        class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 sm:w-auto"
                      />
                    </div>
                  </div>
                </div>
              }
            </div>
          </section>

          <!-- Checkpoint dates + mentor + escalation -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="text-sm font-semibold text-neutral-800">
              Checkpoints, mentor &amp; escalation
            </h2>

            <div class="mt-3">
              <div class="flex items-center justify-between">
                <label class="text-sm font-medium text-neutral-700">
                  Review checkpoint dates
                </label>
                <button
                  type="button"
                  class="rounded-lg px-3 py-1.5 text-xs font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
                  (click)="addCheckpoint()"
                  data-testid="add-checkpoint"
                >
                  + Add date
                </button>
              </div>
              <div class="mt-2 space-y-2" formArrayName="checkpointDates">
                @for (cp of checkpointDates.controls; track $index) {
                  <div class="flex items-center gap-2">
                    <input
                      [formControlName]="$index"
                      type="date"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 sm:w-auto"
                      data-testid="checkpoint-date"
                    />
                    <button
                      type="button"
                      class="text-xs font-medium text-rose-600"
                      (click)="removeCheckpoint($index)"
                    >
                      Remove
                    </button>
                  </div>
                }
              </div>
            </div>

            <div class="mt-4">
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="mentorId"
              >
                Assigned mentor / coach (optional)
              </label>
              <input
                id="mentorId"
                type="text"
                formControlName="mentorId"
                placeholder="Mentor employee id"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                data-testid="mentor"
              />
            </div>

            <div class="mt-4">
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="escalationAction"
              >
                Escalation action if PIP is not met
              </label>
              <select
                id="escalationAction"
                formControlName="escalationAction"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                data-testid="escalation-action"
              >
                @for (opt of escalationOptions(); track opt) {
                  <option [value]="opt">{{ escalationLabel(opt) }}</option>
                }
              </select>
            </div>
          </section>

          <!-- Actions -->
          <div class="flex flex-col gap-2 sm:flex-row sm:justify-end">
            <a
              routerLink="/performance/pips"
              class="rounded-lg px-4 py-2.5 text-center text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
            >
              Cancel
            </a>
            <button
              type="submit"
              class="rounded-lg px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              [disabled]="!canSubmit() || submitting()"
              data-testid="initiate-pip"
            >
              @if (submitting()) {
                Initiating…
              } @else {
                Initiate PIP
              }
            </button>
          </div>
        </form>
      }
    </div>
  `,
})
export class PipFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(PipService);
  private readonly toastr = inject(ToastrService);

  readonly confidentialNotice = PIP_CONFIDENTIAL_NOTICE;
  readonly minDays = PIP_MIN_DURATION_DAYS;
  readonly presets = PIP_DURATION_PRESETS;

  readonly draftLoading = signal(true);
  readonly submitting = signal(false);
  readonly prefilled = signal(false);
  readonly hasActivePip = signal(false);
  readonly escalationOptions = signal<EscalationAction[]>([
    'Reassignment',
    'Demotion',
    'ContractNonRenewal',
    'TerminationRecommendation',
  ]);

  private employeeId: string | null = null;

  readonly form: FormGroup = this.fb.group({
    employeeName: [{ value: '', disabled: false }, Validators.required],
    reason: ['', [Validators.required, Validators.maxLength(2000)]],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    mentorId: [''],
    escalationAction: ['TerminationRecommendation', Validators.required],
    objectives: this.fb.array([this.newObjectiveGroup()]),
    checkpointDates: this.fb.array<string>([]),
  });

  // Live duration validation off start/end (mirrors backend BR-3).
  private readonly startValue = toSignal(
    this.form.get('startDate')!.valueChanges.pipe(
      startWith(this.form.get('startDate')!.value),
    ),
    { initialValue: '' },
  );
  private readonly endValue = toSignal(
    this.form.get('endDate')!.valueChanges.pipe(
      startWith(this.form.get('endDate')!.value),
    ),
    { initialValue: '' },
  );

  readonly durationDays = computed(() =>
    pipDurationDays(this.startValue(), this.endValue()),
  );
  readonly durationValid = computed(() =>
    pipDurationValid(this.startValue(), this.endValue()),
  );
  readonly durationTooShort = computed(
    () => this.durationDays() > 0 && !this.durationValid(),
  );

  get objectives(): FormArray {
    return this.form.get('objectives') as FormArray;
  }

  get checkpointDates(): FormArray {
    return this.form.get('checkpointDates') as FormArray;
  }

  ngOnInit(): void {
    this.employeeId = this.route.snapshot.queryParamMap.get('employeeId');
    const reviewId =
      this.route.snapshot.queryParamMap.get('reviewId') ?? undefined;
    this.service
      .getDraft(this.employeeId ?? undefined, reviewId)
      .subscribe({
        next: (draft) => {
          if (draft.employeeId) {
            this.employeeId = draft.employeeId;
          }
          if (draft.employeeName) {
            this.form.patchValue({ employeeName: draft.employeeName });
            this.form.get('employeeName')!.disable();
            this.prefilled.set(true);
          }
          if (draft.suggestedReason) {
            this.form.patchValue({ reason: draft.suggestedReason });
          }
          if (draft.escalationOptions.length > 0) {
            this.escalationOptions.set(draft.escalationOptions);
            this.form.patchValue({
              escalationAction: draft.escalationOptions[0],
            });
          }
          this.hasActivePip.set(draft.hasActivePip);
          this.draftLoading.set(false);
        },
        error: () => {
          // A missing draft endpoint shouldn't block a blank HR-initiated form.
          this.draftLoading.set(false);
        },
      });
  }

  private newObjectiveGroup(): FormGroup {
    return this.fb.group({
      title: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', Validators.required],
      successCriteria: ['', Validators.required],
      dueDate: ['', Validators.required],
    });
  }

  addObjective(): void {
    this.objectives.push(this.newObjectiveGroup());
  }

  removeObjective(index: number): void {
    this.objectives.removeAt(index);
  }

  addCheckpoint(): void {
    this.checkpointDates.push(this.fb.control('', Validators.required));
  }

  removeCheckpoint(index: number): void {
    this.checkpointDates.removeAt(index);
  }

  /** Apply a preset: from a chosen (or today's) start, set end = start + preset days. */
  applyPreset(days: number): void {
    const start = this.form.get('startDate')!.value || todayIso();
    this.form.patchValue({
      startDate: start,
      endDate: addDaysIso(start, days),
    });
  }

  escalationLabel(action: EscalationAction): string {
    return ESCALATION_ACTION_LABEL[action];
  }

  /**
   * Submit gate (mirrors backend): form valid + duration ≥30 days (BR-3) + ≥1
   * objective (FR-1) + no active PIP (BR-2). A method, not a computed, because it
   * reads `form.valid` which is not a signal.
   */
  canSubmit(): boolean {
    return (
      this.form.valid &&
      this.objectives.length >= 1 &&
      this.durationValid() &&
      !this.hasActivePip()
    );
  }

  submit(): void {
    if (!this.canSubmit() || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }
    const employeeId = this.employeeId;
    if (!employeeId) {
      this.toastr.error('No employee selected for this PIP.');
      return;
    }

    const raw = this.form.getRawValue();
    const objectives: IPipObjectiveInput[] = (
      raw.objectives as IPipObjectiveInput[]
    ).map((o) => ({
      title: o.title,
      description: o.description,
      successCriteria: o.successCriteria,
      dueDate: o.dueDate,
    }));

    const request: ICreatePipRequest = {
      employeeId,
      reason: raw.reason,
      startDate: raw.startDate,
      endDate: raw.endDate,
      mentorId: raw.mentorId ? raw.mentorId : null,
      escalationAction: raw.escalationAction,
      objectives,
      checkpointDates: (raw.checkpointDates as string[]).filter((d) => !!d),
    };

    this.submitting.set(true);
    this.service.createPip(request).subscribe({
      next: (pip) => {
        this.submitting.set(false);
        this.toastr.success(
          'PIP initiated. The employee, manager, and mentor have been notified.',
        );
        this.router.navigate(['/performance/pips', pip.pipId]);
      },
      error: () => {
        this.submitting.set(false);
        this.toastr.error('Could not initiate the PIP. Please try again.');
      },
    });
  }
}

/** Today as a yyyy-MM-dd string (local). */
function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}
