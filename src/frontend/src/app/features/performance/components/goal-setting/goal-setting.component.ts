import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute } from '@angular/router';
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
import { PerformanceGoalService } from '../../services/performance-goal.service';
import {
  GOAL_CATEGORY_OPTIONS,
  GOAL_DESCRIPTION_MAX,
  GOAL_MAX_COUNT,
  GOAL_TITLE_MAX,
  GOAL_WEIGHT_STEP,
  GoalCategory,
  IAppraisalCycle,
  IGoal,
  IGoalInput,
  WEIGHT_BAR_COLORS,
  weightsTotalCorrect,
} from '../../models/goal.models';

/**
 * US-PRF-001: Goal-setting view for one team member (AC-1, AC-2, AC-3, AC-5).
 *
 * - AC-1: reactive form with title, description, category, weight, target value,
 *   measurement unit, due date. Validators mirror the backend (title ≤200,
 *   description ≤2000, weight a multiple of 5, 1-10 goals — FR-2/FR-3, BR-2/BR-3).
 * - AC-3: "Save Goals" is disabled until weights sum to EXACTLY 100%; the error
 *   message "Goal weights must total 100%" is shown live.
 * - AC-5: when the goal-setting window is closed, the form is read-only and a
 *   message is shown; no add / edit / save.
 *
 * Weight distribution is drawn as a CSS/Tailwind horizontal stacked bar (no chart
 * dependency added — §8 calls for chart.js but a lightweight bar satisfies the AC).
 */
@Component({
  selector: 'app-goal-setting',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-4xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <a
          routerLink="/performance"
          class="text-sm text-neutral-500 transition hover:text-neutral-800"
        >
          ← Back to team goals
        </a>
        <h1 class="mt-2 text-2xl font-semibold tracking-tight text-neutral-900">
          Set goals
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          @if (cycle(); as c) {
            {{ c.name }} — define clear, measurable objectives.
          } @else {
            Define clear, measurable objectives for this team member.
          }
        </p>
      </div>

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2]; track i) {
            <div class="h-44 animate-pulse rounded-xl bg-neutral-100"></div>
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
      } @else {
        <!-- AC-5: closed-window read-only banner -->
        @if (!windowOpen()) {
          <div
            class="mb-6 flex items-start gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
            role="status"
          >
            <span aria-hidden="true">🔒</span>
            <span>The goal-setting window for this cycle has closed</span>
          </div>
        }

        <!-- Weight distribution stacked bar (§8) -->
        <div
          class="mb-6 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
        >
          <div class="mb-2 flex items-center justify-between">
            <span class="text-sm font-medium text-neutral-700"
              >Weight distribution</span
            >
            <span
              class="text-sm font-semibold"
              [class.text-emerald-600]="totalIs100()"
              [class.text-rose-600]="!totalIs100()"
            >
              {{ totalWeight() }}% / 100%
            </span>
          </div>
          <div
            class="flex h-3 w-full overflow-hidden rounded-full bg-neutral-100"
            role="img"
            [attr.aria-label]="'Total allocated weight ' + totalWeight() + ' percent'"
          >
            @for (seg of segments(); track seg.index) {
              <div
                class="h-full transition-all"
                [style.width.%]="seg.width"
                [style.background-color]="seg.color"
                [title]="seg.title"
              ></div>
            }
          </div>
          <!-- AC-3: live total-must-be-100 error -->
          @if (!totalIs100() && goals.length > 0) {
            <p class="mt-2 text-sm font-medium text-rose-600" role="alert">
              Goal weights must total 100%
            </p>
          }
        </div>

        <!-- Goals -->
        <form [formGroup]="form" (ngSubmit)="save()">
          <div formArrayName="goals" class="space-y-4">
            @for (goalCtrl of goals.controls; track goalCtrl; let i = $index) {
              <div
                [formGroupName]="i"
                class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
              >
                <div class="mb-3 flex items-center justify-between">
                  <div class="flex items-center gap-2">
                    <span
                      class="inline-block h-3 w-3 rounded-full"
                      [style.background-color]="colorFor(i)"
                    ></span>
                    <span class="text-sm font-semibold text-neutral-700">
                      Goal {{ i + 1 }}
                    </span>
                  </div>
                  @if (windowOpen()) {
                    <button
                      type="button"
                      class="rounded-md px-2 py-1 text-xs font-medium text-rose-600 transition hover:bg-rose-50"
                      (click)="removeGoal(i)"
                      [attr.aria-label]="'Remove goal ' + (i + 1)"
                    >
                      Remove
                    </button>
                  }
                </div>

                <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <!-- Title -->
                  <div class="sm:col-span-2">
                    <label
                      class="mb-1 block text-xs font-medium text-neutral-600"
                      [attr.for]="'title-' + i"
                      >Title</label
                    >
                    <input
                      [id]="'title-' + i"
                      type="text"
                      formControlName="title"
                      [maxlength]="titleMax"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                      placeholder="e.g. Improve customer NPS"
                    />
                    @if (showError(goalCtrl, 'title')) {
                      <p class="mt-1 text-xs text-rose-600">
                        Title is required (max {{ titleMax }} characters).
                      </p>
                    }
                  </div>

                  <!-- Description -->
                  <div class="sm:col-span-2">
                    <label
                      class="mb-1 block text-xs font-medium text-neutral-600"
                      [attr.for]="'desc-' + i"
                      >Description</label
                    >
                    <textarea
                      [id]="'desc-' + i"
                      formControlName="description"
                      rows="2"
                      [maxlength]="descriptionMax"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                      placeholder="What does success look like?"
                    ></textarea>
                    @if (showError(goalCtrl, 'description')) {
                      <p class="mt-1 text-xs text-rose-600">
                        Description must be {{ descriptionMax }} characters or
                        fewer.
                      </p>
                    }
                  </div>

                  <!-- Category -->
                  <div>
                    <label
                      class="mb-1 block text-xs font-medium text-neutral-600"
                      [attr.for]="'cat-' + i"
                      >Category</label
                    >
                    <select
                      [id]="'cat-' + i"
                      formControlName="category"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 disabled:bg-neutral-50 disabled:text-neutral-500"
                    >
                      @for (cat of categories; track cat) {
                        <option [value]="cat">{{ cat }}</option>
                      }
                    </select>
                  </div>

                  <!-- Weight -->
                  <div>
                    <label
                      class="mb-1 block text-xs font-medium text-neutral-600"
                      [attr.for]="'weight-' + i"
                      >Weight (%)</label
                    >
                    <input
                      [id]="'weight-' + i"
                      type="number"
                      formControlName="weight"
                      [step]="weightStep"
                      min="0"
                      max="100"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                    />
                    @if (showError(goalCtrl, 'weight')) {
                      <p class="mt-1 text-xs text-rose-600">
                        Weight must be a multiple of {{ weightStep }}%.
                      </p>
                    }
                  </div>

                  <!-- Target value -->
                  <div>
                    <label
                      class="mb-1 block text-xs font-medium text-neutral-600"
                      [attr.for]="'target-' + i"
                      >Target value</label
                    >
                    <input
                      [id]="'target-' + i"
                      type="text"
                      formControlName="targetValue"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                      placeholder="e.g. 85"
                    />
                    @if (showError(goalCtrl, 'targetValue')) {
                      <p class="mt-1 text-xs text-rose-600">
                        Target value is required.
                      </p>
                    }
                  </div>

                  <!-- Measurement unit -->
                  <div>
                    <label
                      class="mb-1 block text-xs font-medium text-neutral-600"
                      [attr.for]="'unit-' + i"
                      >Measurement unit</label
                    >
                    <input
                      [id]="'unit-' + i"
                      type="text"
                      formControlName="measurementUnit"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                      placeholder="e.g. NPS score, %, days"
                    />
                    @if (showError(goalCtrl, 'measurementUnit')) {
                      <p class="mt-1 text-xs text-rose-600">
                        Measurement unit is required.
                      </p>
                    }
                  </div>

                  <!-- Due date -->
                  <div>
                    <label
                      class="mb-1 block text-xs font-medium text-neutral-600"
                      [attr.for]="'due-' + i"
                      >Due date</label
                    >
                    <input
                      [id]="'due-' + i"
                      type="date"
                      formControlName="dueDate"
                      class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 disabled:bg-neutral-50 disabled:text-neutral-500"
                    />
                    @if (showError(goalCtrl, 'dueDate')) {
                      <p class="mt-1 text-xs text-rose-600">
                        Due date is required.
                      </p>
                    }
                  </div>
                </div>
              </div>
            }
          </div>

          <!-- Add goal -->
          @if (windowOpen()) {
            <button
              type="button"
              class="mt-4 w-full rounded-xl border border-dashed border-neutral-300 px-4 py-3 text-sm font-medium text-neutral-600 transition hover:border-neutral-400 hover:bg-neutral-50 disabled:cursor-not-allowed disabled:opacity-50"
              (click)="addGoal()"
              [disabled]="goals.length >= maxGoals"
            >
              + Add goal
              @if (goals.length >= maxGoals) {
                <span class="text-neutral-400"> (max {{ maxGoals }})</span>
              }
            </button>
          }

          <!-- Actions (AC-2 / AC-3) -->
          @if (windowOpen()) {
            <div
              class="mt-6 flex flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-end"
            >
              <a
                routerLink="/performance"
                class="rounded-lg px-4 py-2 text-center text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
              >
                Cancel
              </a>
              <button
                type="submit"
                class="rounded-lg px-5 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="!canSave()"
              >
                @if (saving()) {
                  <span class="inline-flex items-center gap-2">
                    <span
                      class="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"
                    ></span>
                    Saving…
                  </span>
                } @else {
                  Save Goals
                }
              </button>
            </div>
          }
        </form>
      }
    </div>
  `,
})
export class GoalSettingComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly goalService = inject(PerformanceGoalService);
  private readonly toastr = inject(ToastrService);

  readonly titleMax = GOAL_TITLE_MAX;
  readonly descriptionMax = GOAL_DESCRIPTION_MAX;
  readonly weightStep = GOAL_WEIGHT_STEP;
  readonly maxGoals = GOAL_MAX_COUNT;
  readonly categories: GoalCategory[] = GOAL_CATEGORY_OPTIONS;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly saving = signal(false);
  readonly cycle = signal<IAppraisalCycle | null>(null);

  private employeeId = '';

  readonly form: FormGroup = this.fb.group({
    goals: this.fb.array([]),
  });

  /** Live snapshot of the goals FormArray value (re-emitted on every change). */
  private readonly goalsValue = toSignal(
    this.goals.valueChanges.pipe(startWith(this.goals.value)),
    { initialValue: this.goals.value as IGoalInput[] },
  );

  /** AC-5 gate: editing only when the backend reports the window open. */
  readonly windowOpen = computed(() => this.cycle()?.goalSettingOpen ?? false);

  /** Sum of all goal weights (AC-3). */
  readonly totalWeight = computed(() =>
    (this.goalsValue() as IGoalInput[]).reduce(
      (acc, g) => acc + (Number(g?.weight) || 0),
      0,
    ),
  );

  readonly totalIs100 = computed(() =>
    weightsTotalCorrect(
      (this.goalsValue() as IGoalInput[]).map((g) => Number(g?.weight) || 0),
    ),
  );

  /** Stacked-bar segments derived from current weights (§8). */
  readonly segments = computed(() =>
    (this.goalsValue() as IGoalInput[]).map((g, index) => {
      const weight = Number(g?.weight) || 0;
      return {
        index,
        width: Math.max(0, Math.min(100, weight)),
        color: WEIGHT_BAR_COLORS[index % WEIGHT_BAR_COLORS.length],
        title: `${g?.title || 'Goal ' + (index + 1)}: ${weight}%`,
      };
    }),
  );

  /** AC-2/AC-3: enabled only when the form is valid AND weights total 100%. */
  readonly canSave = computed(
    () =>
      this.windowOpen() &&
      !this.saving() &&
      this.totalIs100() &&
      this.form.valid &&
      this.goals.length > 0,
  );

  get goals(): FormArray {
    return this.form.get('goals') as FormArray;
  }

  ngOnInit(): void {
    this.employeeId = this.route.snapshot.paramMap.get('employeeId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.goalService.getActiveCycle().subscribe({
      next: (cycle) => {
        this.cycle.set(cycle);
        this.loadGoals(cycle.id);
      },
      error: () => {
        this.loadError.set('Unable to load the active appraisal cycle.');
        this.loading.set(false);
      },
    });
  }

  private loadGoals(cycleId: string): void {
    this.goalService.getEmployeeGoals(cycleId, this.employeeId).subscribe({
      next: (goals) => {
        this.hydrate(goals);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load existing goals.');
        this.loading.set(false);
      },
    });
  }

  private hydrate(goals: IGoal[]): void {
    this.goals.clear();
    if (goals.length === 0 && this.windowOpen()) {
      // Start managers with one empty row to fill in (AC-1).
      this.goals.push(this.newGoalGroup());
    } else {
      goals.forEach((g) => this.goals.push(this.newGoalGroup(g)));
    }
    if (!this.windowOpen()) {
      this.form.disable({ emitEvent: false });
    }
  }

  private newGoalGroup(goal?: IGoal): FormGroup {
    return this.fb.group({
      id: [goal?.id ?? null],
      title: [
        goal?.title ?? '',
        [Validators.required, Validators.maxLength(GOAL_TITLE_MAX)],
      ],
      description: [
        goal?.description ?? '',
        [Validators.maxLength(GOAL_DESCRIPTION_MAX)],
      ],
      category: [goal?.category ?? this.categories[0], [Validators.required]],
      weight: [
        goal?.weight ?? 0,
        [
          Validators.required,
          Validators.min(0),
          Validators.max(100),
          this.weightStepValidator,
        ],
      ],
      targetValue: [goal?.targetValue ?? '', [Validators.required]],
      measurementUnit: [goal?.measurementUnit ?? '', [Validators.required]],
      dueDate: [goal?.dueDate ?? '', [Validators.required]],
      parentGoalId: [goal?.parentGoalId ?? null],
    });
  }

  /** BR-3: weight must be a multiple of 5. */
  private readonly weightStepValidator = (control: {
    value: unknown;
  }): { [k: string]: boolean } | null => {
    const value = Number(control.value);
    if (Number.isNaN(value)) {
      return { weightStep: true };
    }
    return value % GOAL_WEIGHT_STEP === 0 ? null : { weightStep: true };
  };

  addGoal(): void {
    if (!this.windowOpen() || this.goals.length >= this.maxGoals) {
      return;
    }
    this.goals.push(this.newGoalGroup());
  }

  removeGoal(index: number): void {
    if (!this.windowOpen()) {
      return;
    }
    this.goals.removeAt(index);
  }

  colorFor(index: number): string {
    return WEIGHT_BAR_COLORS[index % WEIGHT_BAR_COLORS.length];
  }

  /** Show a field error once it has been touched/dirtied and is invalid. */
  showError(group: unknown, controlName: string): boolean {
    const ctrl = (group as FormGroup).get(controlName);
    return !!ctrl && ctrl.invalid && (ctrl.dirty || ctrl.touched);
  }

  save(): void {
    if (!this.canSave()) {
      this.form.markAllAsTouched();
      if (!this.totalIs100()) {
        this.toastr.error('Goal weights must total 100%');
      }
      return;
    }
    const cycle = this.cycle();
    if (!cycle) {
      return;
    }
    this.saving.set(true);
    const goals = (this.goals.value as IGoalInput[]).map((g) => ({
      ...g,
      id: g.id ?? undefined,
    }));
    this.goalService
      .saveGoals(cycle.id, this.employeeId, { goals })
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.toastr.success('Goals saved and the employee was notified.');
          this.hydrate(saved);
        },
        error: () => {
          this.saving.set(false);
          this.toastr.error('Could not save goals. Please try again.');
        },
      });
  }
}
