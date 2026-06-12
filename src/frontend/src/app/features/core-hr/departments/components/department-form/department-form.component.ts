import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  input,
  output,
  OnInit,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { DepartmentService } from '../../services/department.service';
import {
  IDepartment,
  ICreateDepartmentRequest,
  IUpdateDepartmentRequest,
  IDepartmentErrorResponse,
} from '../../models/department.models';

/**
 * US-CHR-004 AC-1: Department create/edit form as a slide-over panel.
 *
 * Fields: Department Name (required, max 150), Description (optional),
 * Parent Department (optional dropdown with hierarchy indentation),
 * Active status toggle.
 *
 * The Manager field is intentionally omitted until US-CHR-001 (Employees)
 * is implemented.
 * TODO(US-CHR-001): Add manager employee picker once Employee entity exists.
 */
@Component({
  selector: 'app-department-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-container">
      <!-- Header -->
      <div class="form-header">
        <h2 class="form-title">
          {{ department() ? 'Edit Department' : 'Add Department' }}
        </h2>
        <button
          type="button"
          class="close-btn"
          (click)="cancelled.emit()"
          aria-label="Close panel"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
            <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
          </svg>
        </button>
      </div>

      <!-- Form body -->
      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="form-body">
        <!-- Department Name (FR-2: unique within tenant) -->
        <div class="form-section">
          <label class="label-notion" for="dept-name">
            Department Name <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="dept-name"
            type="text"
            formControlName="name"
            class="input-notion"
            placeholder="e.g. Engineering"
            maxlength="150"
            autocomplete="off"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <p class="field-error">
              @if (form.get('name')?.hasError('required')) {
                Department name is required.
              } @else if (form.get('name')?.hasError('maxlength')) {
                Name cannot exceed 150 characters.
              }
            </p>
          }
          @if (duplicateNameError()) {
            <p class="field-error">{{ duplicateNameError() }}</p>
          }
        </div>

        <!-- Description -->
        <div class="form-section">
          <label class="label-notion" for="dept-description">
            Description
          </label>
          <textarea
            id="dept-description"
            formControlName="description"
            class="input-notion textarea-notion"
            rows="3"
            placeholder="Brief description of this department"
          ></textarea>
        </div>

        <!-- Parent Department (FR-3: self-referencing FK) -->
        <div class="form-section">
          <label class="label-notion" for="dept-parent">
            Parent Department
          </label>
          <p class="field-hint">
            Leave empty for a root-level department.
          </p>
          <select
            id="dept-parent"
            formControlName="parentDepartmentId"
            class="input-notion select-input"
          >
            <option [ngValue]="null">None (Root Department)</option>
            @for (opt of parentOptions(); track opt.department.departmentId) {
              <option [ngValue]="opt.department.departmentId">
                {{ opt.indent }}{{ opt.department.name }}
              </option>
            }
          </select>
        </div>

        <!-- Manager (read-only placeholder) -->
        <div class="form-section">
          <label class="label-notion">
            Department Manager
          </label>
          <p class="field-hint">
            <!-- TODO(US-CHR-001): Replace with employee picker once Employees feature is available -->
            Manager assignment will be available once employee management is implemented.
          </p>
          <div class="manager-placeholder">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-neutral-300" aria-hidden="true">
              <path fill-rule="evenodd" d="M15 8A7 7 0 1 1 1 8a7 7 0 0 1 14 0Zm-5-2a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm-2 9c-2.841 0-4.263-.722-5.004-1.483-.173-.177-.18-.454-.023-.644A4.504 4.504 0 0 1 6.5 10.5h3a4.504 4.504 0 0 1 3.527 2.373c.157.19.15.467-.023.644C12.263 14.278 10.841 15 8 15Z" clip-rule="evenodd" />
            </svg>
            <span class="text-sm text-neutral-400">
              @if (department()?.managerName) {
                {{ department()!.managerName }}
              } @else {
                Not assigned
              }
            </span>
          </div>
        </div>

        <!-- Active Toggle -->
        <div class="form-section">
          <div class="toggle-row">
            <div class="toggle-label-block">
              <label class="label-notion mb-0" for="dept-active">
                Active
              </label>
              <p class="field-hint">
                Inactive departments are hidden from assignment dropdowns (BR-5).
              </p>
            </div>
            <label class="toggle-switch" for="dept-active">
              <input
                id="dept-active"
                type="checkbox"
                formControlName="isActive"
                class="toggle-input"
              />
              <span class="toggle-slider"></span>
            </label>
          </div>
        </div>

        <!-- Form actions -->
        <div class="form-actions">
          <button
            type="button"
            class="btn-secondary"
            (click)="cancelled.emit()"
          >
            Cancel
          </button>
          <button
            type="submit"
            class="btn-primary"
            [disabled]="isSaving() || form.invalid || form.pristine"
          >
            @if (isSaving()) {
              <span class="btn-spinner"></span>
              Saving...
            } @else {
              {{ department() ? 'Save Changes' : 'Create Department' }}
            }
          </button>
        </div>
      </form>
    </div>
  `,
  styles: [`
    :host {
      display: block;
      height: 100%;
    }

    .form-container {
      @apply flex flex-col h-full;
    }

    .form-header {
      @apply flex items-center justify-between px-6 py-4 border-b border-neutral-100;
    }

    .form-title {
      @apply text-lg font-semibold text-neutral-900;
    }

    .close-btn {
      @apply w-8 h-8 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150;
    }

    .form-body {
      @apply flex-1 px-6 py-5 space-y-5 overflow-y-auto;
    }

    .form-section {
      @apply space-y-1.5;
    }

    .field-hint {
      @apply text-xs text-neutral-400;
    }

    .field-error {
      @apply text-xs text-red-600 mt-1;
    }

    .textarea-notion {
      @apply resize-y min-h-[5rem];
    }

    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    .manager-placeholder {
      @apply flex items-center gap-2 rounded-lg border border-dashed border-neutral-200
        bg-neutral-50 px-3.5 py-2.5;
    }

    /* ─── Toggle switch ─────────────────────────── */

    .toggle-row {
      @apply flex items-start justify-between gap-4;
    }

    .toggle-label-block {
      @apply flex-1;
    }

    .toggle-switch {
      @apply relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer
        rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out;
      background-color: theme('colors.neutral.200');
    }

    .toggle-input {
      @apply sr-only;
    }

    .toggle-input:checked + .toggle-slider {
      transform: translateX(1.25rem);
    }

    .toggle-switch:has(.toggle-input:checked) {
      background-color: theme('colors.brand.600');
    }

    .toggle-slider {
      @apply pointer-events-none inline-block h-5 w-5 transform rounded-full
        bg-white shadow ring-0 transition duration-200 ease-in-out;
    }

    /* ─── Buttons ───────────────────────────────── */

    .form-actions {
      @apply flex justify-end gap-3 pt-4 border-t border-neutral-100 mt-auto;
    }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `],
})
export class DepartmentFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly departmentService = inject(DepartmentService);
  private readonly toastr = inject(ToastrService);

  /** Department to edit. null = create mode. */
  readonly department = input<IDepartment | null>(null);

  /** All active departments for parent selection */
  readonly allDepartments = input<IDepartment[]>([]);

  /** Emitted on successful create/update */
  readonly saved = output<void>();

  /** Emitted when the user cancels */
  readonly cancelled = output<void>();

  readonly isSaving = signal(false);
  readonly duplicateNameError = signal('');

  form!: FormGroup;

  /**
   * Build a flat list of parent options with indentation to convey hierarchy.
   * Excludes the department being edited (and its descendants) to prevent
   * circular references (FR-5).
   */
  readonly parentOptions = computed(() => {
    const departments = this.allDepartments();
    const editingId = this.department()?.departmentId ?? null;

    // Build a children-map
    const childrenMap = new Map<string | null, IDepartment[]>();
    for (const dept of departments) {
      const parentId = dept.parentDepartmentId;
      if (!childrenMap.has(parentId)) {
        childrenMap.set(parentId, []);
      }
      childrenMap.get(parentId)!.push(dept);
    }

    // Collect IDs to exclude (editing dept + its descendants)
    const excludeIds = new Set<string>();
    if (editingId) {
      const collectDescendants = (id: string): void => {
        excludeIds.add(id);
        const children = childrenMap.get(id) ?? [];
        for (const child of children) {
          collectDescendants(child.departmentId);
        }
      };
      collectDescendants(editingId);
    }

    // Walk the tree depth-first, building indented options
    const options: { department: IDepartment; indent: string }[] = [];
    const walk = (parentId: string | null, level: number): void => {
      const children = childrenMap.get(parentId) ?? [];
      for (const child of children) {
        if (excludeIds.has(child.departmentId)) continue;
        options.push({
          department: child,
          indent: '  '.repeat(level),
        });
        walk(child.departmentId, level + 1);
      }
    };
    walk(null, 0);

    return options;
  });

  ngOnInit(): void {
    const dept = this.department();

    this.form = this.fb.group({
      name: [
        dept?.name ?? '',
        [Validators.required, Validators.maxLength(150)],
      ],
      description: [dept?.description ?? ''],
      parentDepartmentId: [dept?.parentDepartmentId ?? null],
      isActive: [dept?.isActive ?? true],
    });
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) return;

    this.isSaving.set(true);
    this.duplicateNameError.set('');

    const formValue = this.form.value;
    const dept = this.department();

    if (dept) {
      // Edit mode
      const request: IUpdateDepartmentRequest = {
        name: formValue.name.trim(),
        description: formValue.description?.trim() || null,
        parentDepartmentId: formValue.parentDepartmentId || null,
        isActive: formValue.isActive,
      };

      this.departmentService
        .updateDepartment(dept.departmentId, request)
        .subscribe({
          next: () => {
            this.isSaving.set(false);
            this.toastr.success(`"${request.name}" updated successfully.`);
            this.saved.emit();
          },
          error: (err: HttpErrorResponse) => {
            this.isSaving.set(false);
            this.handleError(err);
          },
        });
    } else {
      // Create mode
      const request: ICreateDepartmentRequest = {
        name: formValue.name.trim(),
        description: formValue.description?.trim() || null,
        parentDepartmentId: formValue.parentDepartmentId || null,
        isActive: formValue.isActive,
      };

      this.departmentService.createDepartment(request).subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toastr.success(`"${request.name}" created successfully.`);
          this.saved.emit();
        },
        error: (err: HttpErrorResponse) => {
          this.isSaving.set(false);
          this.handleError(err);
        },
      });
    }
  }

  private handleError(err: HttpErrorResponse): void {
    const body = err.error as IDepartmentErrorResponse | undefined;

    if (body?.code === 'duplicate_name') {
      // AC-3: duplicate department name within tenant
      this.duplicateNameError.set(
        body.message || 'A department with this name already exists.'
      );
    } else if (body?.code === 'circular_reference') {
      // FR-5: circular parent-child reference
      this.toastr.error(
        body.message ||
          'This parent assignment would create a circular reference.'
      );
    } else {
      this.toastr.error(body?.message || 'Failed to save department.');
    }
  }
}
