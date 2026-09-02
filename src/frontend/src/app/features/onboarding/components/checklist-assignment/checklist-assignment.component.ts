import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  effect,
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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { OnboardingChecklistService } from '../../services/onboarding-checklist.service';
import {
  RESPONSIBLE_ROLES,
  ResponsibleRole,
} from '../../models/onboarding-template.models';
import {
  AssignMode,
  CHECKLIST_STATUS_LABELS,
  ChecklistTaskStatus,
  IApplicableTemplate,
  IAssignChecklistRequest,
  IAssignedChecklist,
  IChecklistPreview,
  IChecklistTask,
  IResolvedChecklistTaskRequest,
  countResponsibleParties,
  notificationToast,
  sortTasksByDueDate,
} from '../../models/onboarding-checklist.models';

/** Reactive group describing one previewed/editable task row. */
type TaskGroup = FormGroup<{
  id: FormControl<string | null>;
  templateTaskId: FormControl<string | null>;
  title: FormControl<string>;
  description: FormControl<string | null>;
  category: FormControl<string | null>;
  responsibleRole: FormControl<ResponsibleRole | null>;
  responsibleUserId: FormControl<string | null>;
  responsibleName: FormControl<string | null>;
  dueDate: FormControl<string>;
  status: FormControl<ChecklistTaskStatus>;
  isMandatory: FormControl<boolean>;
  sortOrder: FormControl<number>;
}>;

/**
 * US-ONB-002: Assign an onboarding checklist to a new hire (AC-1..AC-5).
 *
 * Routed at `onboarding/assign/:employeeId` (routed input() employeeId with a
 * route-snapshot fallback). Onboarding-side only — never touches Core HR employee
 * screens; it is keyed purely by employeeId.
 *
 * Flow: searchable dropdown of APPLICABLE active templates (server-filtered by
 * dept/job-title + universal, AC-1) → preview the task instances with
 * server-calculated due dates (FR-2/BR-4) → inline-edit due date + responsible
 * party (UI/UX §8) → confirm. If the employee already has an active checklist,
 * a Replace/Merge dialog gates the assign (AC-3). On success a toast reports the
 * notifiedCount from the API response (AC-2/AC-5).
 *
 * Standalone, signals, OnPush. Material-free, Tailwind + native controls. The
 * timeline collapses to a vertical due-date-sorted list under 768px (UI/UX §8).
 */
@Component({
  selector: 'app-checklist-assignment',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
    trigger('overlay', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-4xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6 flex items-center gap-3">
        <a
          routerLink="/onboarding/templates"
          class="rounded-lg p-2 text-neutral-500 transition hover:bg-neutral-100"
          aria-label="Back to onboarding"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            class="h-5 w-5"
            viewBox="0 0 20 20"
            fill="currentColor"
            aria-hidden="true"
          >
            <path
              fill-rule="evenodd"
              d="M12.79 5.23a.75.75 0 0 1 0 1.06L9.06 10l3.73 3.71a.75.75 0 1 1-1.06 1.06l-4.25-4.24a.75.75 0 0 1 0-1.06l4.25-4.24a.75.75 0 0 1 1.06 0Z"
              clip-rule="evenodd"
            />
          </svg>
        </a>
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Assign onboarding checklist
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Pick a template, review the schedule, then assign it to the new hire.
          </p>
        </div>
      </div>

      @if (alreadyAssigned()) {
        <div
          @fadeIn
          class="mb-5 flex items-start gap-2 rounded-xl border border-amber-200 bg-amber-50 p-4"
          role="status"
        >
          <span class="text-amber-700">
            This employee already has an active onboarding checklist
            (<strong>{{ alreadyAssigned()!.templateName }}</strong>). Assigning
            again will prompt you to replace or merge (AC-3).
          </span>
        </div>
      }

      <!-- Template picker (AC-1 / FR-1) -->
      <section class="mb-5 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm">
        <label for="tpl-search" class="lbl">Applicable templates</label>
        <input
          id="tpl-search"
          type="text"
          class="field"
          [value]="search()"
          (input)="onSearch($event)"
          placeholder="Search templates by name…"
          role="combobox"
          aria-expanded="true"
          aria-controls="tpl-listbox"
          autocomplete="off"
        />
        <p class="hint">
          Only active templates matching this employee's department / job title
          (plus universal) are listed.
        </p>

        @if (loadingTemplates()) {
          <div class="mt-3 space-y-2" aria-busy="true">
            @for (n of [1, 2, 3]; track n) {
              <div class="h-12 animate-pulse rounded-lg bg-neutral-100"></div>
            }
          </div>
        } @else if (filteredTemplates().length === 0) {
          <p class="mt-3 text-sm text-neutral-500" role="status">
            No applicable templates found.
          </p>
        } @else {
          <ul
            id="tpl-listbox"
            role="listbox"
            class="mt-3 space-y-2"
            aria-label="Applicable templates"
          >
            @for (t of filteredTemplates(); track t.id) {
              <li>
                <button
                  type="button"
                  role="option"
                  [attr.aria-selected]="selectedTemplate()?.id === t.id"
                  class="tpl-option"
                  [class.tpl-selected]="selectedTemplate()?.id === t.id"
                  (click)="selectTemplate(t)"
                >
                  <span class="min-w-0">
                    <span class="block truncate font-medium text-neutral-900">{{
                      t.templateName
                    }}</span>
                    @if (t.description) {
                      <span class="block truncate text-xs text-neutral-500">{{
                        t.description
                      }}</span>
                    }
                  </span>
                  <span class="flex shrink-0 items-center gap-2">
                    @if (t.matchReason) {
                      <span class="tag">{{ t.matchReason }}</span>
                    }
                    <span class="text-xs text-neutral-400"
                      >{{ t.taskCount }} task{{ t.taskCount === 1 ? '' : 's' }}</span
                    >
                  </span>
                </button>
              </li>
            }
          </ul>
        }
      </section>

      <!-- Preview + inline edit (UI/UX §8) -->
      @if (loadingPreview()) {
        <div class="space-y-3" aria-busy="true">
          <div class="h-20 animate-pulse rounded-xl bg-neutral-100"></div>
          <div class="h-20 animate-pulse rounded-xl bg-neutral-100"></div>
        </div>
      } @else if (preview()) {
        <form [formGroup]="form" (ngSubmit)="openConfirm()" @fadeIn novalidate>
          <section class="mb-3 flex flex-wrap items-end justify-between gap-3">
            <div>
              <h2 class="text-sm font-semibold text-neutral-700">
                Task schedule
                <span class="ml-1 text-neutral-400">({{ taskGroups().length }})</span>
              </h2>
              <p class="hint">
                Due dates are calculated from
                <strong>{{ preview()!.startDate }}</strong> (joining date or today
                if past). You can adjust any date or owner before assigning.
              </p>
            </div>
            <button type="button" class="add-btn" (click)="addTask()">
              + Add ad-hoc task
            </button>
          </section>

          <!-- Timeline (desktop ≥768px) -->
          <ol class="timeline hidden md:block" role="list">
            @for (group of taskGroups(); track group; let i = $index) {
              <li class="timeline-item" [formGroup]="group" @fadeIn>
                <span class="timeline-dot" aria-hidden="true"></span>
                <div class="task-card">
                  <ng-container
                    [ngTemplateOutlet]="taskBody"
                    [ngTemplateOutletContext]="{ group: group, i: i }"
                  ></ng-container>
                </div>
              </li>
            }
          </ol>

          <!-- Vertical list (mobile <768px) sorted by due date -->
          <div class="space-y-3 md:hidden">
            @for (row of mobileRows(); track row.group; let i = $index) {
              <div class="task-card" [formGroup]="row.group" @fadeIn>
                <ng-container
                  [ngTemplateOutlet]="taskBody"
                  [ngTemplateOutletContext]="{ group: row.group, i: row.index }"
                ></ng-container>
              </div>
            }
          </div>

          <!-- Reusable task editor body -->
          <ng-template #taskBody let-group="group" let-i="i">
            <div [formGroup]="group">
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <input
                    type="text"
                    class="title-input"
                    formControlName="title"
                    [attr.aria-label]="'Task ' + (i + 1) + ' title'"
                    [attr.aria-invalid]="taskError(group, 'title')"
                  />
                  @if (taskError(group, 'title')) {
                    <p class="err" role="alert">Title must be 3–200 characters.</p>
                  }
                </div>
                <div class="flex shrink-0 items-center gap-2">
                  <span class="chip" [attr.data-status]="group.controls.status.value">
                    {{ statusLabel(group.controls.status.value) }}
                  </span>
                  @if (group.controls.isMandatory.value) {
                    <span class="chip chip-mandatory" title="Mandatory (BR-3)"
                      >Required</span
                    >
                  } @else {
                    <button
                      type="button"
                      class="rounded-lg p-1.5 text-neutral-400 transition hover:bg-rose-50 hover:text-rose-600"
                      (click)="removeTaskById(group)"
                      [attr.aria-label]="'Remove task ' + (i + 1)"
                    >
                      <svg
                        xmlns="http://www.w3.org/2000/svg"
                        class="h-4 w-4"
                        viewBox="0 0 20 20"
                        fill="currentColor"
                        aria-hidden="true"
                      >
                        <path
                          fill-rule="evenodd"
                          d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5Z"
                          clip-rule="evenodd"
                        />
                      </svg>
                    </button>
                  }
                </div>
              </div>

              <div class="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
                <div>
                  <label class="lbl" [attr.for]="'due-' + group.controls.sortOrder.value"
                    >Due date <span class="text-rose-500">*</span></label
                  >
                  <input
                    [id]="'due-' + group.controls.sortOrder.value"
                    type="date"
                    class="field"
                    formControlName="dueDate"
                    [attr.aria-invalid]="taskError(group, 'dueDate')"
                  />
                  @if (taskError(group, 'dueDate')) {
                    <p class="err" role="alert">A due date is required.</p>
                  }
                </div>
                <div>
                  <label class="lbl" [attr.for]="'role-' + group.controls.sortOrder.value"
                    >Responsible party</label
                  >
                  <select
                    [id]="'role-' + group.controls.sortOrder.value"
                    class="field"
                    formControlName="responsibleRole"
                  >
                    <option [ngValue]="null">— Unassigned —</option>
                    @for (r of roles; track r) {
                      <option [ngValue]="r">{{ r }}</option>
                    }
                  </select>
                </div>
              </div>
            </div>
          </ng-template>

          <!-- Actions -->
          <div class="mt-6 flex items-center justify-end gap-3">
            <a routerLink="/onboarding/templates" class="btn-ghost">Cancel</a>
            <button
              type="submit"
              class="btn-primary"
              [disabled]="assigning() || taskGroups().length === 0"
            >
              Review &amp; assign
            </button>
          </div>
        </form>
      }
    </div>

    <!-- AC-3: Replace / Merge dialog -->
    @if (showModeDialog()) {
      <div
        class="dialog-backdrop"
        @overlay
        (click)="cancelMode()"
        aria-hidden="true"
      ></div>
      <div
        class="dialog"
        @fadeIn
        role="dialog"
        aria-modal="true"
        aria-labelledby="mode-title"
      >
        <h2 id="mode-title" class="text-base font-semibold text-neutral-900">
          Employee already has a checklist
        </h2>
        <p class="mt-2 text-sm text-neutral-600">
          This employee already has an active onboarding checklist. Do you want to
          replace it or add these as additional tasks?
        </p>
        <div class="mt-5 flex flex-col gap-2 sm:flex-row sm:justify-end">
          <button type="button" class="btn-ghost" (click)="cancelMode()">
            Cancel
          </button>
          <button type="button" class="btn-outline" (click)="confirmMode('merge')">
            Merge (add tasks)
          </button>
          <button type="button" class="btn-primary" (click)="confirmMode('replace')">
            Replace
          </button>
        </div>
      </div>
    }

    <!-- Confirmation summary dialog (UI/UX §8) -->
    @if (showConfirmDialog()) {
      <div
        class="dialog-backdrop"
        @overlay
        (click)="cancelConfirm()"
        aria-hidden="true"
      ></div>
      <div
        class="dialog"
        @fadeIn
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
      >
        <h2 id="confirm-title" class="text-base font-semibold text-neutral-900">
          Confirm assignment
        </h2>
        <p class="mt-2 text-sm text-neutral-600">
          You're about to assign
          <strong>{{ taskGroups().length }}</strong>
          task{{ taskGroups().length === 1 ? '' : 's' }} across
          <strong>{{ responsiblePartyCount() }}</strong>
          responsible part{{ responsiblePartyCount() === 1 ? 'y' : 'ies' }}.
          @if (chosenMode()) {
            Mode: <strong>{{ chosenMode() }}</strong>.
          }
        </p>
        <div class="mt-5 flex flex-col gap-2 sm:flex-row sm:justify-end">
          <button type="button" class="btn-ghost" (click)="cancelConfirm()">
            Back
          </button>
          <button
            type="button"
            class="btn-primary"
            [disabled]="assigning()"
            (click)="assign()"
          >
            {{ assigning() ? 'Assigning…' : 'Assign checklist' }}
          </button>
        </div>
      </div>
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .lbl {
        display: block;
        margin-bottom: 0.25rem;
        font-size: 0.75rem;
        font-weight: 600;
        color: #525252;
      }
      .field {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.625rem;
        font-size: 0.875rem;
        color: #171717;
        transition: border-color 200ms ease, box-shadow 200ms ease;
      }
      .field:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 2px #e0e7ff;
      }
      .title-input {
        width: 100%;
        border: none;
        background: transparent;
        font-size: 0.9375rem;
        font-weight: 600;
        color: #171717;
        padding: 0;
      }
      .title-input:focus {
        outline: none;
        box-shadow: 0 1px 0 0 #818cf8;
      }
      .hint {
        margin-top: 0.25rem;
        font-size: 0.6875rem;
        color: #a3a3a3;
      }
      .err {
        margin-top: 0.25rem;
        font-size: 0.75rem;
        color: #e11d48;
      }
      .tpl-option {
        display: flex;
        width: 100%;
        align-items: center;
        justify-content: space-between;
        gap: 0.75rem;
        border-radius: 0.625rem;
        border: 1px solid #f0f0f0;
        background: #fff;
        padding: 0.625rem 0.875rem;
        text-align: left;
        transition: all 200ms ease;
      }
      .tpl-option:hover {
        background: #fafafa;
        border-color: #e5e5e5;
      }
      .tpl-selected {
        border-color: #818cf8;
        background: #eef2ff;
      }
      .tag {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        background: #eef2ff;
        padding: 0.0625rem 0.5rem;
        font-size: 0.625rem;
        font-weight: 600;
        color: #4338ca;
        text-transform: capitalize;
      }
      .timeline {
        position: relative;
        margin-left: 0.5rem;
        padding-left: 1.25rem;
        border-left: 2px solid #ececec;
      }
      .timeline-item {
        position: relative;
        margin-bottom: 0.75rem;
      }
      .timeline-dot {
        position: absolute;
        left: -1.6875rem;
        top: 1rem;
        height: 0.625rem;
        width: 0.625rem;
        border-radius: 9999px;
        background: #818cf8;
        box-shadow: 0 0 0 3px #e0e7ff;
      }
      .task-card {
        border-radius: 0.75rem;
        border: 1px solid #f0f0f0;
        background: #fff;
        padding: 1rem;
        box-shadow: 0 1px 2px rgb(0 0 0 / 0.04);
        transition: box-shadow 200ms ease;
      }
      .task-card:hover {
        box-shadow: 0 2px 8px rgb(0 0 0 / 0.06);
      }
      .chip {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        padding: 0.125rem 0.5rem;
        font-size: 0.625rem;
        font-weight: 600;
        background: #f5f5f5;
        color: #525252;
        white-space: nowrap;
      }
      .chip[data-status='completed'] {
        background: #ecfdf5;
        color: #047857;
      }
      .chip[data-status='in_progress'] {
        background: #eff6ff;
        color: #1d4ed8;
      }
      .chip[data-status='cancelled'] {
        background: #fef2f2;
        color: #b91c1c;
      }
      .chip-mandatory {
        background: #fef3c7;
        color: #b45309;
      }
      .add-btn {
        border-radius: 0.5rem;
        border: 1px dashed #d4d4d4;
        background: #fafafa;
        padding: 0.5rem 1rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #525252;
        transition: all 200ms ease;
      }
      .add-btn:hover {
        background: #f5f3ff;
        border-color: #c7d2fe;
        color: #4338ca;
      }
      .btn-primary {
        border-radius: 0.5rem;
        background: #4f46e5;
        padding: 0.5rem 1.25rem;
        font-size: 0.875rem;
        font-weight: 600;
        color: #fff;
        transition: background 200ms ease;
      }
      .btn-primary:hover:not(:disabled) {
        background: #4338ca;
      }
      .btn-primary:disabled {
        opacity: 0.6;
      }
      .btn-ghost {
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: #525252;
      }
      .btn-ghost:hover {
        background: #f5f5f5;
      }
      .btn-outline {
        border-radius: 0.5rem;
        border: 1px solid #c7d2fe;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 600;
        color: #4338ca;
        background: #fff;
        transition: background 200ms ease;
      }
      .btn-outline:hover {
        background: #eef2ff;
      }
      .dialog-backdrop {
        position: fixed;
        inset: 0;
        background: rgb(0 0 0 / 0.4);
        z-index: 40;
      }
      .dialog {
        position: fixed;
        left: 50%;
        top: 50%;
        z-index: 50;
        width: calc(100% - 2rem);
        max-width: 28rem;
        transform: translate(-50%, -50%);
        border-radius: 0.875rem;
        background: #fff;
        padding: 1.5rem;
        box-shadow: 0 12px 40px rgb(0 0 0 / 0.2);
      }
    `,
  ],
})
export class ChecklistAssignmentComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(OnboardingChecklistService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly roles = RESPONSIBLE_ROLES;

  /** Routed input (withComponentInputBinding); falls back to the route snapshot. */
  readonly employeeId = input<string>('');

  private readonly resolvedEmployeeId = computed(
    () =>
      this.employeeId() ||
      this.route.snapshot.paramMap.get('employeeId') ||
      '',
  );

  readonly templates = signal<IApplicableTemplate[]>([]);
  readonly loadingTemplates = signal(true);
  readonly selectedTemplate = signal<IApplicableTemplate | null>(null);

  readonly search = signal('');
  readonly preview = signal<IChecklistPreview | null>(null);
  readonly loadingPreview = signal(false);

  /** The existing active checklist (AC-3), looked up once on load. */
  readonly alreadyAssigned = signal<IAssignedChecklist | null>(null);

  readonly assigning = signal(false);
  readonly showModeDialog = signal(false);
  readonly showConfirmDialog = signal(false);
  readonly chosenMode = signal<AssignMode | null>(null);

  /** Bumped on structural task-array changes so OnPush recomputes the rows. */
  private readonly taskRev = signal(0);

  readonly form = this.fb.group({
    tasks: this.fb.array<TaskGroup>([]),
  });

  get tasks(): FormArray<TaskGroup> {
    return this.form.controls.tasks;
  }

  readonly taskGroups = computed<TaskGroup[]>(() => {
    this.taskRev();
    return this.tasks.controls;
  });

  /** Mobile rows: same groups, sorted by due date (UI/UX §8). */
  readonly mobileRows = computed<{ group: TaskGroup; index: number }[]>(() => {
    this.taskRev();
    return this.tasks.controls
      .map((group, index) => ({ group, index }))
      .sort((a, b) => {
        const da = a.group.controls.dueDate.value;
        const db = b.group.controls.dueDate.value;
        if (da !== db) {
          return da < db ? -1 : 1;
        }
        return a.index - b.index;
      });
  });

  readonly responsiblePartyCount = computed(() => {
    this.taskRev();
    return countResponsibleParties(this.snapshotTasks());
  });

  constructor() {
    // Load applicable templates + existing checklist whenever the employee id
    // resolves (input or snapshot). effect keeps it reactive to routed input.
    effect(() => {
      const id = this.resolvedEmployeeId();
      if (!id) {
        return;
      }
      this.loadTemplates(id);
      this.loadExisting(id);
    });
  }

  // ─── Loading ──────────────────────────────────────────────

  private loadTemplates(employeeId: string): void {
    this.loadingTemplates.set(true);
    this.service.getApplicableTemplates(employeeId).subscribe({
      next: (list) => {
        this.templates.set(list ?? []);
        this.loadingTemplates.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loadingTemplates.set(false);
        this.toastr.error(OnboardingChecklistService.parseErrorMessage(err));
      },
    });
  }

  private loadExisting(employeeId: string): void {
    this.service.getByEmployee(employeeId).subscribe({
      next: (existing) => this.alreadyAssigned.set(existing ?? null),
      // Non-fatal — a 404/204 just means no existing checklist.
      error: () => this.alreadyAssigned.set(null),
    });
  }

  // ─── Template selection + preview (AC-1 / FR-2) ───────────

  readonly filteredTemplates = computed<IApplicableTemplate[]>(() => {
    const q = this.search().trim().toLowerCase();
    const all = this.templates();
    if (!q) {
      return all;
    }
    return all.filter(
      (t) =>
        t.templateName.toLowerCase().includes(q) ||
        (t.description ?? '').toLowerCase().includes(q),
    );
  });

  onSearch(event: Event): void {
    this.search.set((event.target as HTMLInputElement).value);
  }

  selectTemplate(t: IApplicableTemplate): void {
    this.selectedTemplate.set(t);
    this.loadingPreview.set(true);
    this.preview.set(null);
    this.service.preview(this.resolvedEmployeeId(), t.id).subscribe({
      next: (p) => {
        this.preview.set(p);
        this.buildTaskArray(p.tasks);
        this.loadingPreview.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loadingPreview.set(false);
        this.toastr.error(OnboardingChecklistService.parseErrorMessage(err));
      },
    });
  }

  // ─── Task array mutations ─────────────────────────────────

  private buildTaskArray(tasks: IChecklistTask[]): void {
    this.tasks.clear();
    for (const t of sortTasksByDueDate(tasks)) {
      this.tasks.push(this.buildTaskGroup(t));
    }
    this.taskRev.update((n) => n + 1);
  }

  addTask(): void {
    const startDate = this.preview()?.startDate ?? this.today();
    this.tasks.push(
      this.buildTaskGroup({
        title: '',
        dueDate: startDate,
        dueOffsetDays: 0,
        status: 'pending',
        isMandatory: false,
        sortOrder: this.tasks.length,
      }),
    );
    this.taskRev.update((n) => n + 1);
  }

  removeTaskById(group: TaskGroup): void {
    const idx = this.tasks.controls.indexOf(group);
    if (idx >= 0) {
      this.tasks.removeAt(idx);
      this.taskRev.update((n) => n + 1);
    }
  }

  // ─── Assign flow (AC-2 / AC-3 / AC-5) ─────────────────────

  /** Submit handler: validate, then gate on the AC-3 replace/merge prompt. */
  openConfirm(): void {
    this.tasks.markAllAsTouched();
    if (this.tasks.length === 0) {
      this.toastr.error('Add at least one task before assigning.');
      return;
    }
    if (this.form.invalid) {
      this.toastr.error('Please fix the highlighted fields.');
      return;
    }
    // AC-3: if an active checklist already exists, ask replace vs merge first.
    if (this.alreadyAssigned()) {
      this.showModeDialog.set(true);
      return;
    }
    this.chosenMode.set(null);
    this.showConfirmDialog.set(true);
  }

  confirmMode(mode: AssignMode): void {
    this.chosenMode.set(mode);
    this.showModeDialog.set(false);
    this.showConfirmDialog.set(true);
  }

  cancelMode(): void {
    this.showModeDialog.set(false);
  }

  cancelConfirm(): void {
    this.showConfirmDialog.set(false);
  }

  assign(): void {
    const template = this.selectedTemplate();
    if (!template) {
      return;
    }
    this.assigning.set(true);
    this.service.assign(this.toRequest(template.id)).subscribe({
      next: (result) => {
        this.assigning.set(false);
        this.showConfirmDialog.set(false);
        const n = result.notifiedCount ?? 0;
        this.toastr.success(notificationToast(n));
        this.router.navigate(['/onboarding/templates']);
      },
      error: (err: HttpErrorResponse) => {
        this.assigning.set(false);
        this.toastr.error(OnboardingChecklistService.parseErrorMessage(err));
      },
    });
  }

  // ─── View helpers ─────────────────────────────────────────

  taskError(group: TaskGroup, field: 'title' | 'dueDate'): boolean {
    const c = group.get(field);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  statusLabel(status: ChecklistTaskStatus): string {
    return CHECKLIST_STATUS_LABELS[status];
  }

  // ─── Internal ─────────────────────────────────────────────

  private buildTaskGroup(task: IChecklistTask): TaskGroup {
    return this.fb.group({
      id: this.fb.control<string | null>(task.id ?? null),
      templateTaskId: this.fb.control<string | null>(task.templateTaskId ?? null),
      title: this.fb.nonNullable.control(task.title ?? '', [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(200),
      ]),
      description: this.fb.control<string | null>(task.description ?? null),
      category: this.fb.control<string | null>(task.category ?? null),
      responsibleRole: this.fb.control<ResponsibleRole | null>(
        task.responsibleRole ?? null,
      ),
      responsibleUserId: this.fb.control<string | null>(
        task.responsibleUserId ?? null,
      ),
      responsibleName: this.fb.control<string | null>(task.responsibleName ?? null),
      dueDate: this.fb.nonNullable.control(task.dueDate ?? this.today(), [
        Validators.required,
      ]),
      status: this.fb.nonNullable.control<ChecklistTaskStatus>(
        task.status ?? 'pending',
      ),
      isMandatory: this.fb.nonNullable.control(task.isMandatory ?? false),
      sortOrder: this.fb.nonNullable.control(task.sortOrder ?? 0),
    }) as TaskGroup;
  }

  /** Snapshot the FormArray as plain task objects (for the party count). */
  private snapshotTasks(): IChecklistTask[] {
    return this.tasks.controls.map((g) => g.getRawValue() as IChecklistTask);
  }

  /**
   * Build the assign payload; sortOrder follows the on-screen order (FR-3).
   *
   * BUG-441: the on-screen list is the AUTHORITATIVE task set, so it goes in `resolvedTasks` — the
   * server creates exactly these rows and skips template expansion. It must NOT go in
   * `additionalTasks`, which means "extras on top of the template": that made the server create
   * `template.Tasks` *plus* everything echoed here, i.e. every template task twice, all at
   * `startDate + 0` because an ad-hoc row can only carry an offset — throwing away the officer's
   * inline due-date edits (FR-6). `additionalTasks` is therefore omitted entirely, not sent empty:
   * the two fields are mutually exclusive server-side and sending both is a deliberate 400.
   */
  private toRequest(templateId: string): IAssignChecklistRequest {
    const resolvedTasks: IResolvedChecklistTaskRequest[] = this.tasks.controls.map(
      (g, i) => {
        const v = g.getRawValue();
        return {
          templateTaskId: v.templateTaskId ?? null,
          title: v.title.trim(),
          description: v.description?.trim() || null,
          category: v.category?.trim() || null,
          responsibleRole: v.responsibleRole ?? null,
          // Concrete date straight off the form control — the officer's edit, used verbatim.
          dueDate: v.dueDate,
          isMandatory: v.isMandatory,
          sortOrder: i,
        };
      },
    );
    return {
      employeeId: this.resolvedEmployeeId(),
      templateId,
      overrideStartDate: this.preview()?.startDate ?? null,
      mode: this.chosenMode(),
      resolvedTasks,
    };
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
