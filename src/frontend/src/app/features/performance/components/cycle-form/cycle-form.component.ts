import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import {
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { startWith } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { CycleService } from '../../services/cycle.service';
import {
  CYCLE_NAME_MAX,
  CYCLE_TYPE_OPTIONS,
  CyclePhaseKind,
  ICycle,
  IPhaseDates,
  ISaveCycleRequest,
  PARTICIPANT_SCOPE_OPTIONS,
  PHASE_LABEL,
  PHASE_ORDER,
  ParticipantScopeType,
  RATING_SCALE_DEFAULT,
  RATING_SCALE_MAX,
  RATING_SCALE_MIN,
  validatePhaseSequencing,
} from '../../models/cycle.models';

/**
 * US-PRF-004 (AC-1 / AC-5): the Create / Edit Cycle form. One reactive form covers:
 *  - cycle name, type, period (start/end);
 *  - the five phase definitions (goal-setting → self-assessment → manager-review →
 *    calibration → publish), each with its own date range; calibration is hidden
 *    when the calibration toggle is off (FR-6);
 *  - participant scope selector (all / departments / grades / custom list, FR-3);
 *  - rating-scale selection, self:manager weight, 360-degree toggle, calibration
 *    toggle (FR-6).
 *
 * Client-side validation mirrors the backend FR-2 / BR-3: phases must be sequential,
 * non-overlapping, and within the cycle window. The validation is a pure helper
 * (`validatePhaseSequencing`) shared with the dashboard + QA; the "Save" button is
 * gated on it and the live error list is shown. The server re-validates on submit.
 */
@Component({
  selector: 'app-cycle-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <a
          routerLink="/performance/cycles"
          class="text-sm text-neutral-500 transition hover:text-neutral-800"
        >
          ← Back to cycles
        </a>
        <h1 class="mt-2 text-2xl font-semibold tracking-tight text-neutral-900">
          {{ isEdit() ? 'Edit cycle' : 'Create New Cycle' }}
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Define the timeline, phases, participants and scoring for this review
          period.
        </p>
      </div>

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-40 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else {
        <form [formGroup]="form" (ngSubmit)="save()" class="space-y-6">
          <!-- Basics -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-700">Basics</h2>
            <div class="grid gap-4 sm:grid-cols-2">
              <label class="block sm:col-span-2">
                <span class="mb-1 block text-sm font-medium text-neutral-700"
                  >Cycle name</span
                >
                <input
                  type="text"
                  formControlName="name"
                  [maxlength]="nameMax"
                  placeholder="e.g. 2026 Annual Review"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="name-input"
                />
                @if (showError('name')) {
                  <span class="mt-1 block text-xs text-rose-600"
                    >A cycle name is required.</span
                  >
                }
              </label>
              <label class="block">
                <span class="mb-1 block text-sm font-medium text-neutral-700"
                  >Type</span
                >
                <select
                  formControlName="type"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="type-select"
                >
                  @for (t of typeOptions; track t.value) {
                    <option [value]="t.value">{{ t.label }}</option>
                  }
                </select>
              </label>
              <label class="block">
                <span class="mb-1 block text-sm font-medium text-neutral-700"
                  >Rating scale maximum</span
                >
                <input
                  type="number"
                  formControlName="ratingScaleMax"
                  [min]="ratingScaleMin"
                  [max]="ratingScaleMax"
                  step="1"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="rating-scale-max"
                />
                <span class="mt-1 block text-xs text-neutral-400">
                  Ratings run 1–{{ ratingScaleMax }}. Locked once the cycle is
                  active.
                </span>
                @if (showError('ratingScaleMax')) {
                  <span class="mt-1 block text-xs text-rose-600"
                    >Enter a value between {{ ratingScaleMin }} and
                    {{ ratingScaleMax }}.</span
                  >
                }
              </label>
              <label class="block">
                <span class="mb-1 block text-sm font-medium text-neutral-700"
                  >Cycle start</span
                >
                <input
                  type="date"
                  formControlName="startDate"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="start-date"
                />
              </label>
              <label class="block">
                <span class="mb-1 block text-sm font-medium text-neutral-700"
                  >Cycle end</span
                >
                <input
                  type="date"
                  formControlName="endDate"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="end-date"
                />
              </label>
            </div>
          </section>

          <!-- Scoring (FR-6) -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-700">Scoring</h2>
            <label class="block">
              <span class="mb-1 block text-sm font-medium text-neutral-700">
                Self vs. manager weight — self {{ selfWeightPercent() }}% / manager
                {{ 100 - selfWeightPercent() }}%
              </span>
              <input
                type="range"
                min="0"
                max="100"
                step="5"
                formControlName="selfWeightPercent"
                class="w-full accent-[var(--brand-primary)]"
                data-testid="self-weight"
              />
            </label>
            <div class="mt-4 flex flex-col gap-3 sm:flex-row sm:gap-6">
              <label class="inline-flex items-center gap-2 text-sm text-neutral-700">
                <input
                  type="checkbox"
                  formControlName="is360Enabled"
                  class="h-4 w-4 rounded border-neutral-300"
                  data-testid="enable-360"
                />
                360-degree peer feedback
              </label>
              <label class="inline-flex items-center gap-2 text-sm text-neutral-700">
                <input
                  type="checkbox"
                  formControlName="isCalibrationEnabled"
                  class="h-4 w-4 rounded border-neutral-300"
                  data-testid="enable-calibration"
                />
                Calibration phase
              </label>
            </div>
          </section>

          <!-- Phases (FR-2) -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-1 text-sm font-semibold text-neutral-700">Phases</h2>
            <p class="mb-4 text-xs text-neutral-500">
              Phases must be sequential, non-overlapping, and within the cycle
              window.
            </p>
            <div class="space-y-3" formArrayName="phases">
              @for (
                phase of phases.controls;
                track phase.value.phaseType;
                let i = $index
              ) {
                @if (isPhaseVisible(phase.value.phaseType)) {
                  <div
                    [formGroupName]="i"
                    class="grid gap-3 rounded-lg border border-neutral-100 bg-neutral-50/60 p-3 sm:grid-cols-[1fr,auto,auto] sm:items-end"
                    data-testid="phase-row"
                  >
                    <div class="text-sm font-medium text-neutral-800">
                      {{ phaseLabel(phase.value.phaseType) }}
                    </div>
                    <label class="block">
                      <span class="mb-1 block text-xs text-neutral-500">Start</span>
                      <input
                        type="date"
                        formControlName="startDate"
                        class="rounded-lg border border-neutral-200 px-2 py-1.5 text-sm outline-none focus:border-neutral-400 focus:ring-1"
                        [attr.data-testid]="'phase-start-' + phase.value.phaseType"
                      />
                    </label>
                    <label class="block">
                      <span class="mb-1 block text-xs text-neutral-500">End</span>
                      <input
                        type="date"
                        formControlName="endDate"
                        class="rounded-lg border border-neutral-200 px-2 py-1.5 text-sm outline-none focus:border-neutral-400 focus:ring-1"
                        [attr.data-testid]="'phase-end-' + phase.value.phaseType"
                      />
                    </label>
                  </div>
                }
              }
            </div>

            @if (phaseErrors().length > 0) {
              <ul
                class="mt-3 space-y-1 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700"
                role="alert"
                data-testid="phase-errors"
              >
                @for (e of phaseErrors(); track e) {
                  <li>{{ e }}</li>
                }
              </ul>
            }
          </section>

          <!-- Participant scope (FR-3) -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-700">
              Participants
            </h2>
            <label class="block">
              <span class="mb-1 block text-sm font-medium text-neutral-700"
                >Scope</span
              >
              <select
                formControlName="scopeType"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                data-testid="scope-type"
              >
                @for (s of scopeOptions; track s.value) {
                  <option
                    [value]="s.value"
                    [disabled]="s.disabled"
                    [attr.title]="s.hint"
                  >
                    {{ s.label }}{{ s.disabled ? ' — not available yet' : '' }}
                  </option>
                }
              </select>
            </label>
            @if (selectedScopeHint(); as hint) {
              <p class="mt-1 text-xs text-neutral-400" data-testid="scope-hint">
                {{ hint }}
              </p>
            }

            @if (scopeType() !== 'AllEmployees') {
              <label class="mt-3 block">
                <span class="mb-1 block text-sm font-medium text-neutral-700">
                  {{ scopeIdsLabel() }}
                </span>
                <input
                  type="text"
                  formControlName="scopeIds"
                  [placeholder]="scopeIdsPlaceholder()"
                  class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                  data-testid="scope-ids"
                />
                <span class="mt-1 block text-xs text-neutral-400">
                  Comma-separated; the server resolves these to participants.
                </span>
              </label>
            }
          </section>

          <!-- Actions -->
          <div class="flex items-center justify-end gap-3">
            <a
              routerLink="/performance/cycles"
              class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
            >
              Cancel
            </a>
            <button
              type="submit"
              [disabled]="!canSave() || saving()"
              class="inline-flex items-center justify-center rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              [style.--tw-ring-color]="'var(--brand-primary)'"
              data-testid="save-cycle"
            >
              @if (saving()) {
                <span
                  class="mr-2 h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"
                ></span>
              }
              {{ isEdit() ? 'Save changes' : 'Create Cycle' }}
            </button>
          </div>
        </form>
      }
    </div>
  `,
})
export class CycleFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(CycleService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly nameMax = CYCLE_NAME_MAX;
  readonly typeOptions = CYCLE_TYPE_OPTIONS;
  readonly scopeOptions = PARTICIPANT_SCOPE_OPTIONS;
  readonly ratingScaleMin = RATING_SCALE_MIN;
  readonly ratingScaleMax = RATING_SCALE_MAX;

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly cycleId = signal<string | null>(null);

  readonly isEdit = computed(() => this.cycleId() !== null);

  readonly form: FormGroup = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(CYCLE_NAME_MAX)]],
    type: ['Annual', Validators.required],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
    ratingScaleMax: [
      RATING_SCALE_DEFAULT,
      [
        Validators.required,
        Validators.min(RATING_SCALE_MIN),
        Validators.max(RATING_SCALE_MAX),
      ],
    ],
    selfWeightPercent: [40, Validators.required],
    is360Enabled: [false],
    isCalibrationEnabled: [false],
    scopeType: ['AllEmployees' as ParticipantScopeType, Validators.required],
    scopeIds: [''],
    phases: this.fb.array(
      PHASE_ORDER.map((phaseType) =>
        this.fb.group({
          phaseType: [phaseType],
          startDate: [''],
          endDate: [''],
        }),
      ),
    ),
  });

  /** Live mirror of the whole form value for reactive validation. */
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(startWith(this.form.value)),
    { initialValue: this.form.value },
  );

  readonly scopeType = computed(
    () => this.formValue().scopeType as ParticipantScopeType,
  );

  readonly isCalibrationEnabled = computed(
    () => !!this.formValue().isCalibrationEnabled,
  );

  readonly selfWeightPercent = computed(
    () => Number(this.formValue().selfWeightPercent) || 0,
  );

  /** BUG-257: full hint text when the current scope is a disabled/unavailable option. */
  readonly selectedScopeHint = computed<string | null>(() => {
    const opt = PARTICIPANT_SCOPE_OPTIONS.find((s) => s.value === this.scopeType());
    return opt?.disabled ? (opt.hint ?? null) : null;
  });

  /** FR-2/BR-3 validation errors for the currently-visible phases. */
  readonly phaseErrors = computed(() => {
    const v = this.formValue();
    const visible: IPhaseDates[] = (v.phases as IPhaseDates[]).filter((p) =>
      this.isPhaseVisible(p.phaseType),
    );
    // Only validate once the cycle window is set, to avoid noise while typing.
    if (!v.startDate || !v.endDate) {
      return [];
    }
    return validatePhaseSequencing(visible, v.startDate, v.endDate);
  });

  /**
   * "Save" gate: form valid + phases pass sequencing (FR-2/BR-3). A method (not a
   * computed) because it reads `form.valid`, which is not a signal — a computed would
   * cache against its signal deps only and miss form-validity changes.
   */
  canSave(): boolean {
    return this.form.valid && this.phaseErrors().length === 0;
  }

  get phases(): FormArray {
    return this.form.get('phases') as FormArray;
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('cycleId');

    if (id) {
      this.cycleId.set(id);
      this.service.get(id).subscribe({
        next: (cycle) => {
          this.patchFromCycle(cycle);
          this.loading.set(false);
        },
        error: () => {
          this.toastr.error('Unable to load this cycle.');
          this.loading.set(false);
        },
      });
    } else {
      this.loading.set(false);
    }
  }

  /** Calibration row hides when the calibration toggle is off (FR-6). */
  isPhaseVisible(kind: CyclePhaseKind): boolean {
    if (kind === 'Calibration') {
      return this.isCalibrationEnabled();
    }
    return true;
  }

  phaseLabel(kind: CyclePhaseKind): string {
    return PHASE_LABEL[kind];
  }

  scopeIdsLabel(): string {
    switch (this.scopeType()) {
      case 'Departments':
        return 'Department ids';
      case 'Grades':
        return 'Grade / band ids';
      case 'CustomList':
        return 'Employee ids';
      default:
        return '';
    }
  }

  scopeIdsPlaceholder(): string {
    switch (this.scopeType()) {
      case 'Departments':
        return 'dept-1, dept-2';
      case 'Grades':
        return 'grade-a, grade-b';
      case 'CustomList':
        return 'emp-1, emp-2';
      default:
        return '';
    }
  }

  showError(controlName: string): boolean {
    const c = this.form.get(controlName);
    return !!c && c.invalid && (c.dirty || c.touched);
  }

  save(): void {
    this.form.markAllAsTouched();
    if (!this.canSave()) {
      return;
    }
    this.saving.set(true);
    const request = this.toRequest();
    const id = this.cycleId();
    const obs = id
      ? this.service.update(id, request)
      : this.service.create(request);
    obs.subscribe({
      next: (cycle) => {
        this.saving.set(false);
        this.toastr.success(
          id ? 'Cycle updated.' : 'Cycle created.',
        );
        this.router.navigate(['/performance/cycles', cycle.id]);
      },
      error: () => {
        this.saving.set(false);
        this.toastr.error('Unable to save the cycle. Please try again.');
      },
    });
  }

  private patchFromCycle(cycle: ICycle): void {
    const idLists =
      cycle.scope.scopeType === 'Departments'
        ? cycle.scope.departmentIds
        : cycle.scope.scopeType === 'CustomList'
          ? cycle.scope.employeeIds
          : [];
    this.form.patchValue({
      name: cycle.name,
      type: cycle.type,
      startDate: cycle.startDate,
      endDate: cycle.endDate,
      ratingScaleMax: cycle.ratingScaleMax,
      selfWeightPercent: cycle.selfWeightPercent,
      is360Enabled: cycle.is360Enabled,
      isCalibrationEnabled: cycle.isCalibrationEnabled,
      scopeType: cycle.scope.scopeType,
      scopeIds: idLists.join(', '),
    });
    // Patch phase dates by phaseType so order/visibility stays canonical.
    for (const ctrl of this.phases.controls) {
      const phaseType = ctrl.value.phaseType as CyclePhaseKind;
      const match = cycle.phases.find((p) => p.phaseType === phaseType);
      if (match) {
        ctrl.patchValue({
          startDate: match.startDate,
          endDate: match.endDate,
        });
      }
    }
    // BR-5: the rating scale locks once the cycle leaves Draft.
    if (cycle.status !== 'Draft') {
      this.form.get('ratingScaleMax')?.disable();
    }
  }

  private toRequest(): ISaveCycleRequest {
    const v = this.form.getRawValue();
    const ids = (v.scopeIds as string)
      .split(',')
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
    const scopeType = v.scopeType as ParticipantScopeType;
    const phases = (v.phases as IPhaseDates[]).filter((p) =>
      this.isPhaseVisible(p.phaseType),
    );
    return {
      name: (v.name as string).trim(),
      type: v.type,
      startDate: v.startDate,
      endDate: v.endDate,
      ratingScaleMax: Number(v.ratingScaleMax),
      // managerWeightPercent is derived server-side (100 − self), so not sent (BUG-257).
      selfWeightPercent: Number(v.selfWeightPercent),
      is360Enabled: !!v.is360Enabled,
      isCalibrationEnabled: !!v.isCalibrationEnabled,
      phases,
      scope: {
        scopeType,
        departmentIds: scopeType === 'Departments' ? ids : [],
        employeeIds: scopeType === 'CustomList' ? ids : [],
      },
    };
  }
}
