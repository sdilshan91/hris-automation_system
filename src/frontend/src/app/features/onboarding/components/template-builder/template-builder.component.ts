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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import {
  CdkDragDrop,
  DragDropModule,
  moveItemInArray,
} from '@angular/cdk/drag-drop';
import { ToastrService } from 'ngx-toastr';
import { OnboardingTemplateService } from '../../services/onboarding-template.service';
import {
  ICreateOnboardingTemplateRequest,
  IOnboardingLookups,
  IOnboardingTask,
  IOnboardingTemplate,
  RESPONSIBLE_ROLES,
  ResponsibleRole,
  SUGGESTED_CATEGORIES,
} from '../../models/onboarding-template.models';

/** Reactive form group describing a single task card. */
type TaskGroup = FormGroup<{
  id: FormControl<string | null>;
  title: FormControl<string>;
  description: FormControl<string | null>;
  category: FormControl<string>;
  responsibleRole: FormControl<ResponsibleRole | null>;
  responsibleUserId: FormControl<string | null>;
  dueOffsetDays: FormControl<number>;
  isMandatory: FormControl<boolean>;
}>;

/**
 * US-ONB-001: Onboarding checklist template builder (AC-1/AC-2/AC-4).
 *
 * A Notion-style card builder: template meta (name/description/department + job
 * title scoping) plus a task list grouped into collapsible category sections
 * (UI/UX §8). Tasks reorder within a category via CDK drag-and-drop (FR-1); on
 * mobile (<768px) the drag handle is replaced by up/down arrow buttons (UI/UX §8
 * / NFR-4) — both are keyboard-operable for WCAG 2.1 AA.
 *
 * Reactive Forms validators mirror the backend (name min3/max200, >=1 task,
 * due_offset>=0 — BR-2/BR-3). The `tenant_id` is NEVER sent (FR-5). Duplicate-name
 * (AC-3) is enforced server-side; the 409/400 message is surfaced verbatim.
 *
 * Reached at `/onboarding/templates/new` (blank) or via Clone (pre-filled copy).
 */
@Component({
  selector: 'app-template-builder',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, DragDropModule, RouterLink],
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
  ],
  template: `
    <div class="mx-auto max-w-4xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6 flex items-center gap-3">
        <a
          routerLink="/onboarding/templates"
          class="rounded-lg p-2 text-neutral-500 transition hover:bg-neutral-100"
          aria-label="Back to templates"
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
            {{ isClone() ? 'Clone checklist template' : 'Create checklist template' }}
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Build a reusable onboarding checklist with categorized tasks.
          </p>
        </div>
      </div>

      @if (loading()) {
        <div class="space-y-3" aria-busy="true">
          <div class="h-24 animate-pulse rounded-xl bg-neutral-100"></div>
          <div class="h-40 animate-pulse rounded-xl bg-neutral-100"></div>
        </div>
      } @else {
        <form [formGroup]="form" (ngSubmit)="save()" @fadeIn novalidate>
          <!-- Template meta card -->
          <section
            class="mb-5 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <div class="mb-4">
              <label for="t-name" class="lbl"
                >Template name <span class="text-rose-500">*</span></label
              >
              <input
                id="t-name"
                type="text"
                class="field"
                formControlName="templateName"
                placeholder="e.g. Engineering New Hire"
                [attr.aria-invalid]="showError('templateName')"
              />
              @if (showError('templateName')) {
                <p class="err" role="alert">{{ nameError() }}</p>
              }
            </div>

            <div class="mb-4">
              <label for="t-desc" class="lbl">Description</label>
              <textarea
                id="t-desc"
                rows="2"
                class="field"
                formControlName="description"
                placeholder="What is this checklist for?"
              ></textarea>
            </div>

            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <div>
                <label for="t-depts" class="lbl">Applicable departments</label>
                <select
                  id="t-depts"
                  multiple
                  class="field h-28"
                  formControlName="applicableDepartmentIds"
                >
                  @for (d of lookups().departments; track d.id) {
                    <option [value]="d.id">{{ d.name }}</option>
                  }
                </select>
                <p class="hint">Leave empty to apply to all (universal).</p>
              </div>
              <div>
                <label for="t-titles" class="lbl">Applicable job titles</label>
                <select
                  id="t-titles"
                  multiple
                  class="field h-28"
                  formControlName="applicableJobTitleIds"
                >
                  @for (j of lookups().jobTitles; track j.id) {
                    <option [value]="j.id">{{ j.name }}</option>
                  }
                </select>
                <p class="hint">Leave empty to apply to all (universal).</p>
              </div>
            </div>

            <label class="mt-4 flex items-center gap-2 text-sm text-neutral-700">
              <input
                type="checkbox"
                class="h-4 w-4 rounded border-neutral-300 text-indigo-600"
                formControlName="isActive"
              />
              Active (available to assign to new hires)
            </label>
          </section>

          <!-- Tasks -->
          <section class="mb-5">
            <div class="mb-3 flex items-center justify-between">
              <h2 class="text-sm font-semibold text-neutral-700">
                Tasks
                <span class="ml-1 text-neutral-600">({{ tasks.length }})</span>
              </h2>
            </div>

            @if (tasks.length === 0) {
              <div
                class="rounded-xl border border-dashed border-neutral-200 bg-white py-12 text-center"
              >
                <p class="text-sm font-medium text-neutral-700">No tasks yet</p>
                <p class="mt-1 text-sm text-neutral-500">
                  Add at least one task to save this template.
                </p>
              </div>
            }

            <div
              class="space-y-3"
              cdkDropList
              (cdkDropListDropped)="onDrop($event)"
            >
              @for (group of taskGroups(); track group; let i = $index) {
                <article
                  cdkDrag
                  [formGroup]="group"
                  class="task-card"
                  [attr.aria-label]="'Task ' + (i + 1)"
                >
                  <div class="flex items-start gap-3">
                    <!-- Drag handle (desktop) + reorder buttons (mobile) -->
                    <div class="flex flex-col items-center gap-1 pt-1.5">
                      <button
                        type="button"
                        cdkDragHandle
                        class="drag-handle hidden md:block"
                        aria-label="Drag to reorder"
                        title="Drag to reorder"
                      >
                        <svg
                          xmlns="http://www.w3.org/2000/svg"
                          class="h-5 w-5"
                          viewBox="0 0 20 20"
                          fill="currentColor"
                          aria-hidden="true"
                        >
                          <path
                            d="M7 4a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm0 6a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm-1 7a1 1 0 1 0 0-2 1 1 0 0 0 0 2Zm9-13a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm-1 7a1 1 0 1 0 0-2 1 1 0 0 0 0 2Zm1 5a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z"
                          />
                        </svg>
                      </button>
                      <button
                        type="button"
                        class="arrow-btn md:hidden"
                        [disabled]="i === 0"
                        (click)="moveTask(i, i - 1)"
                        [attr.aria-label]="'Move task ' + (i + 1) + ' up'"
                      >
                        ▲
                      </button>
                      <button
                        type="button"
                        class="arrow-btn md:hidden"
                        [disabled]="i === tasks.length - 1"
                        (click)="moveTask(i, i + 1)"
                        [attr.aria-label]="'Move task ' + (i + 1) + ' down'"
                      >
                        ▼
                      </button>
                    </div>

                    <div class="min-w-0 flex-1 space-y-3">
                      <div>
                        <label class="lbl" [attr.for]="'task-title-' + i"
                          >Title <span class="text-rose-500">*</span></label
                        >
                        <input
                          [id]="'task-title-' + i"
                          type="text"
                          class="field"
                          formControlName="title"
                          placeholder="e.g. Sign employment contract"
                          [attr.aria-invalid]="taskError(group, 'title')"
                        />
                        @if (taskError(group, 'title')) {
                          <p class="err" role="alert">
                            Title must be 3–200 characters.
                          </p>
                        }
                      </div>

                      <div>
                        <label class="lbl" [attr.for]="'task-desc-' + i"
                          >Description</label
                        >
                        <textarea
                          [id]="'task-desc-' + i"
                          rows="2"
                          class="field"
                          formControlName="description"
                        ></textarea>
                      </div>

                      <div class="grid grid-cols-1 gap-3 sm:grid-cols-2">
                        <div>
                          <label class="lbl" [attr.for]="'task-cat-' + i"
                            >Category</label
                          >
                          <input
                            [id]="'task-cat-' + i"
                            type="text"
                            class="field"
                            formControlName="category"
                            list="onb-categories"
                            placeholder="e.g. Documentation"
                          />
                        </div>
                        <div>
                          <label class="lbl" [attr.for]="'task-role-' + i"
                            >Responsible party</label
                          >
                          <select
                            [id]="'task-role-' + i"
                            class="field"
                            formControlName="responsibleRole"
                          >
                            <option [ngValue]="null">— Select —</option>
                            @for (r of roles; track r) {
                              <option [ngValue]="r">{{ r }}</option>
                            }
                          </select>
                        </div>
                      </div>

                      @if (group.controls.responsibleRole.value === 'Custom') {
                        <div @fadeIn>
                          <label class="lbl" [attr.for]="'task-user-' + i"
                            >Named user</label
                          >
                          <select
                            [id]="'task-user-' + i"
                            class="field"
                            formControlName="responsibleUserId"
                          >
                            <option [ngValue]="null">— Select user —</option>
                            @for (u of lookups().users; track u.id) {
                              <option [ngValue]="u.id">{{ u.name }}</option>
                            }
                          </select>
                        </div>
                      }

                      <div class="flex flex-wrap items-end gap-4">
                        <div>
                          <label class="lbl" [attr.for]="'task-due-' + i"
                            >Due offset (days)
                            <span class="text-rose-500">*</span></label
                          >
                          <input
                            [id]="'task-due-' + i"
                            type="number"
                            min="0"
                            class="field w-32"
                            formControlName="dueOffsetDays"
                            [attr.aria-invalid]="taskError(group, 'dueOffsetDays')"
                          />
                          @if (taskError(group, 'dueOffsetDays')) {
                            <p class="err" role="alert">Must be 0 or more.</p>
                          }
                        </div>
                        <label
                          class="flex items-center gap-2 pb-2 text-sm text-neutral-700"
                        >
                          <input
                            type="checkbox"
                            class="h-4 w-4 rounded border-neutral-300 text-indigo-600"
                            formControlName="isMandatory"
                          />
                          Mandatory
                        </label>
                      </div>
                    </div>

                    <button
                      type="button"
                      class="rounded-lg p-2 text-neutral-400 transition hover:bg-rose-50 hover:text-rose-600"
                      (click)="removeTask(i)"
                      [attr.aria-label]="'Remove task ' + (i + 1)"
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
                          d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4Z"
                          clip-rule="evenodd"
                        />
                      </svg>
                    </button>
                  </div>
                </article>
              }
            </div>

            <datalist id="onb-categories">
              @for (c of suggestedCategories; track c) {
                <option [value]="c"></option>
              }
            </datalist>

            <button type="button" class="add-btn mt-3" (click)="addTask()">
              + Add task
            </button>

            @if (form.touched && tasks.length === 0) {
              <p class="err mt-2" role="alert">
                At least one task is required (BR-2).
              </p>
            }
          </section>

          <!-- Actions -->
          <div class="flex items-center justify-end gap-3">
            <a routerLink="/onboarding/templates" class="btn-ghost">Cancel</a>
            <button type="submit" class="btn-primary" [disabled]="saving()">
              {{ saving() ? 'Saving…' : 'Save template' }}
            </button>
          </div>
        </form>
      }
    </div>
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
      .hint {
        margin-top: 0.25rem;
        font-size: 0.6875rem;
        color: #525252;
      }
      .err {
        margin-top: 0.25rem;
        font-size: 0.75rem;
        color: #e11d48;
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
      .drag-handle {
        cursor: grab;
        color: #a3a3a3;
        border-radius: 0.375rem;
      }
      .drag-handle:active {
        cursor: grabbing;
      }
      .arrow-btn {
        display: flex;
        height: 1.5rem;
        width: 1.5rem;
        align-items: center;
        justify-content: center;
        border-radius: 0.375rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        font-size: 0.625rem;
        color: #525252;
      }
      .arrow-btn:disabled {
        opacity: 0.4;
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
      .cdk-drag-preview {
        box-shadow: 0 8px 20px rgb(0 0 0 / 0.18);
        border-radius: 0.75rem;
      }
      .cdk-drag-placeholder {
        opacity: 0.4;
      }
      .cdk-drop-list-dragging .cdk-drag {
        transition: transform 200ms cubic-bezier(0, 0, 0.2, 1);
      }
    `,
  ],
})
export class TemplateBuilderComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(OnboardingTemplateService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly roles = RESPONSIBLE_ROLES;
  readonly suggestedCategories = SUGGESTED_CATEGORIES;

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly isClone = signal(false);
  readonly lookups = signal<IOnboardingLookups>({
    departments: [],
    jobTitles: [],
    users: [],
  });

  // Bumped on every structural change to the task FormArray so the template
  // (OnPush) recomputes the rendered group list.
  private readonly taskRev = signal(0);

  readonly form = this.fb.group({
    templateName: this.fb.nonNullable.control('', [
      Validators.required,
      Validators.minLength(3),
      Validators.maxLength(200),
    ]),
    description: this.fb.control<string | null>(null, [
      Validators.maxLength(2000),
    ]),
    applicableDepartmentIds: this.fb.nonNullable.control<string[]>([]),
    applicableJobTitleIds: this.fb.nonNullable.control<string[]>([]),
    isActive: this.fb.nonNullable.control(true),
    tasks: this.fb.array<TaskGroup>([]),
  });

  get tasks(): FormArray<TaskGroup> {
    return this.form.controls.tasks;
  }

  /** Reactive view of the task groups (re-read whenever taskRev changes). */
  readonly taskGroups = computed<TaskGroup[]>(() => {
    this.taskRev();
    return this.tasks.controls;
  });

  ngOnInit(): void {
    this.loading.set(true);
    this.service.getLookups().subscribe({
      next: (l) => this.lookups.set(l),
      error: () => {
        // Lookups are non-fatal — the builder still works with free-text roles.
        this.toastr.warning('Could not load department / user lists.');
      },
    });

    const cloneId = this.route.snapshot.queryParamMap.get('cloneFrom');
    if (cloneId) {
      this.isClone.set(true);
      this.service.clone(cloneId).subscribe({
        next: (tpl) => {
          this.patchFrom(tpl);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.loading.set(false);
          this.toastr.error(OnboardingTemplateService.parseErrorMessage(err));
          this.router.navigate(['/onboarding/templates']);
        },
      });
    } else {
      this.addTask();
      this.loading.set(false);
    }
  }

  // ─── Task array mutations ────────────────────────────────

  addTask(task?: Partial<IOnboardingTask>): void {
    this.tasks.push(this.buildTaskGroup(task));
    this.taskRev.update((n) => n + 1);
  }

  removeTask(index: number): void {
    this.tasks.removeAt(index);
    this.taskRev.update((n) => n + 1);
  }

  /** Reorder via up/down arrow buttons (mobile / keyboard alternative — NFR-4). */
  moveTask(from: number, to: number): void {
    if (to < 0 || to >= this.tasks.length) {
      return;
    }
    const controls = this.tasks.controls;
    moveItemInArray(controls, from, to);
    this.tasks.updateValueAndValidity();
    this.taskRev.update((n) => n + 1);
  }

  /** CDK drag-and-drop reorder (FR-1). */
  onDrop(event: CdkDragDrop<unknown>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }
    moveItemInArray(this.tasks.controls, event.previousIndex, event.currentIndex);
    this.tasks.updateValueAndValidity();
    this.taskRev.update((n) => n + 1);
  }

  // ─── Save (AC-2 / AC-3 / AC-4 / BR-2 / BR-3) ─────────────

  save(): void {
    this.form.markAllAsTouched();
    if (this.tasks.length === 0) {
      this.toastr.error('Add at least one task before saving.');
      return;
    }
    if (this.form.invalid) {
      this.toastr.error('Please fix the highlighted fields.');
      return;
    }

    this.saving.set(true);
    this.service.create(this.toRequest()).subscribe({
      next: () => {
        this.saving.set(false);
        this.toastr.success('Template saved.');
        this.router.navigate(['/onboarding/templates']);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        // AC-3 duplicate-name + any server validation: show the message verbatim.
        this.toastr.error(OnboardingTemplateService.parseErrorMessage(err));
      },
    });
  }

  // ─── Validation view helpers ─────────────────────────────

  showError(controlName: 'templateName'): boolean {
    const c = this.form.get(controlName);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  nameError(): string {
    const c = this.form.controls.templateName;
    if (c.hasError('required')) {
      return 'Template name is required.';
    }
    return 'Name must be 3–200 characters.';
  }

  taskError(group: TaskGroup, field: 'title' | 'dueOffsetDays'): boolean {
    const c = group.get(field);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  // ─── Internal ────────────────────────────────────────────

  private buildTaskGroup(task?: Partial<IOnboardingTask>): TaskGroup {
    return this.fb.group({
      id: this.fb.control<string | null>(task?.id ?? null),
      title: this.fb.nonNullable.control(task?.title ?? '', [
        Validators.required,
        Validators.minLength(3),
        Validators.maxLength(200),
      ]),
      description: this.fb.control<string | null>(task?.description ?? null, [
        Validators.maxLength(1000),
      ]),
      category: this.fb.nonNullable.control(task?.category ?? ''),
      responsibleRole: this.fb.control<ResponsibleRole | null>(
        task?.responsibleRole ?? null,
      ),
      responsibleUserId: this.fb.control<string | null>(
        task?.responsibleUserId ?? null,
      ),
      dueOffsetDays: this.fb.nonNullable.control(task?.dueOffsetDays ?? 0, [
        Validators.required,
        Validators.min(0),
      ]),
      isMandatory: this.fb.nonNullable.control(task?.isMandatory ?? false),
    }) as TaskGroup;
  }

  private patchFrom(tpl: IOnboardingTemplate): void {
    this.form.patchValue({
      templateName: tpl.templateName,
      description: tpl.description ?? null,
      applicableDepartmentIds: tpl.applicableDepartmentIds ?? [],
      applicableJobTitleIds: tpl.applicableJobTitleIds ?? [],
      isActive: tpl.isActive ?? true,
    });
    this.tasks.clear();
    for (const t of [...(tpl.tasks ?? [])].sort(
      (a, b) => a.sortOrder - b.sortOrder,
    )) {
      this.tasks.push(this.buildTaskGroup(t));
    }
    if (this.tasks.length === 0) {
      this.addTask();
    }
    this.taskRev.update((n) => n + 1);
  }

  /** Build the create payload; sortOrder is derived from on-screen order (FR-3). */
  private toRequest(): ICreateOnboardingTemplateRequest {
    const v = this.form.getRawValue();
    return {
      templateName: v.templateName.trim(),
      description: v.description?.trim() || null,
      applicableDepartmentIds: v.applicableDepartmentIds ?? [],
      applicableJobTitleIds: v.applicableJobTitleIds ?? [],
      isActive: v.isActive,
      tasks: v.tasks.map((t, i) => ({
        title: t.title.trim(),
        description: t.description?.trim() || null,
        category: t.category?.trim() || null,
        responsibleRole: t.responsibleRole ?? null,
        responsibleUserId:
          t.responsibleRole === 'Custom' ? t.responsibleUserId ?? null : null,
        dueOffsetDays: Number(t.dueOffsetDays),
        isMandatory: t.isMandatory,
        sortOrder: i,
      })),
    };
  }
}
