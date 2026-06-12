import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  input,
  output,
  OnInit,
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
import { JobTitleService } from '../../services/job-title.service';
import {
  IJobTitle,
  ICreateJobTitleRequest,
  IUpdateJobTitleRequest,
  IJobTitleErrorResponse,
} from '../../models/job-title.models';

/**
 * US-CHR-005 AC-2: Job title create/edit form as a slide-over panel.
 *
 * Fields: Title Name (required, max 150, unique within tenant),
 * Description (optional textarea), Grade (disabled placeholder),
 * Active status toggle.
 *
 * The Grade field is intentionally rendered as a disabled placeholder until
 * the Grade entity is implemented.
 * TODO(US-CHR-005): Add grade dropdown/picker once Grade entity exists.
 */
@Component({
  selector: 'app-job-title-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-container">
      <!-- Header -->
      <div class="form-header">
        <h2 class="form-title">
          {{ jobTitle() ? 'Edit Job Title' : 'Add Job Title' }}
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
        <!-- Title Name (FR-2: unique within tenant) -->
        <div class="form-section">
          <label class="label-notion" for="jt-name">
            Title Name <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="jt-name"
            type="text"
            formControlName="titleName"
            class="input-notion"
            placeholder="e.g. Software Engineer"
            maxlength="150"
            autocomplete="off"
          />
          @if (form.get('titleName')?.invalid && form.get('titleName')?.touched) {
            <p class="field-error">
              @if (form.get('titleName')?.hasError('required')) {
                Title name is required.
              } @else if (form.get('titleName')?.hasError('maxlength')) {
                Title name cannot exceed 150 characters.
              }
            </p>
          }
          @if (duplicateNameError()) {
            <p class="field-error">{{ duplicateNameError() }}</p>
          }
        </div>

        <!-- Description -->
        <div class="form-section">
          <label class="label-notion" for="jt-description">
            Description
          </label>
          <textarea
            id="jt-description"
            formControlName="description"
            class="input-notion textarea-notion"
            rows="3"
            placeholder="Brief description of this job title"
          ></textarea>
        </div>

        <!-- Grade (disabled placeholder) -->
        <div class="form-section">
          <label class="label-notion">
            Salary Grade
          </label>
          <p class="field-hint">
            <!-- TODO(US-CHR-005): Replace with grade dropdown once Grade entity is available -->
            Grade linking will be available once salary grade management is implemented.
          </p>
          <div class="grade-placeholder">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 text-neutral-300" aria-hidden="true">
              <path fill-rule="evenodd" d="M10 2a.75.75 0 0 1 .75.75v7.5a.75.75 0 0 1-1.5 0v-7.5A.75.75 0 0 1 10 2ZM5.404 4.343a.75.75 0 0 1 0 1.06 6.5 6.5 0 1 0 9.192 0 .75.75 0 1 1 1.06-1.06 8 8 0 1 1-11.313 0 .75.75 0 0 1 1.06 0Z" clip-rule="evenodd" />
            </svg>
            <span class="text-sm text-neutral-400">
              @if (jobTitle()?.gradeName) {
                {{ jobTitle()!.gradeName }}
              } @else {
                Not linked
              }
            </span>
          </div>
        </div>

        <!-- Active Toggle -->
        <div class="form-section">
          <div class="toggle-row">
            <div class="toggle-label-block">
              <label class="label-notion mb-0" for="jt-active">
                Active
              </label>
              <p class="field-hint">
                Inactive job titles are hidden from assignment dropdowns (BR-3).
              </p>
            </div>
            <label class="toggle-switch" for="jt-active">
              <input
                id="jt-active"
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
              {{ jobTitle() ? 'Save Changes' : 'Create Job Title' }}
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

    .grade-placeholder {
      @apply flex items-center gap-2 rounded-lg border border-dashed border-neutral-200
        bg-neutral-50 px-3.5 py-2.5;
    }

    /* --- Toggle switch ---------------------- */

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

    /* --- Buttons ----------------------------- */

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
export class JobTitleFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly jobTitleService = inject(JobTitleService);
  private readonly toastr = inject(ToastrService);

  /** Job title to edit. null = create mode. */
  readonly jobTitle = input<IJobTitle | null>(null);

  /** Emitted on successful create/update */
  readonly saved = output<void>();

  /** Emitted when the user cancels */
  readonly cancelled = output<void>();

  readonly isSaving = signal(false);
  readonly duplicateNameError = signal('');

  form!: FormGroup;

  ngOnInit(): void {
    const jt = this.jobTitle();

    this.form = this.fb.group({
      titleName: [
        jt?.titleName ?? '',
        [Validators.required, Validators.maxLength(150)],
      ],
      description: [jt?.description ?? ''],
      isActive: [jt?.isActive ?? true],
    });
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) return;

    this.isSaving.set(true);
    this.duplicateNameError.set('');

    const formValue = this.form.value;
    const jt = this.jobTitle();

    if (jt) {
      // Edit mode
      const request: IUpdateJobTitleRequest = {
        titleName: formValue.titleName.trim(),
        description: formValue.description?.trim() || null,
        isActive: formValue.isActive,
      };

      this.jobTitleService
        .updateJobTitle(jt.jobTitleId, request)
        .subscribe({
          next: () => {
            this.isSaving.set(false);
            this.toastr.success(`"${request.titleName}" updated successfully.`);
            this.saved.emit();
          },
          error: (err: HttpErrorResponse) => {
            this.isSaving.set(false);
            this.handleError(err);
          },
        });
    } else {
      // Create mode
      const request: ICreateJobTitleRequest = {
        titleName: formValue.titleName.trim(),
        description: formValue.description?.trim() || null,
        isActive: formValue.isActive,
      };

      this.jobTitleService.createJobTitle(request).subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toastr.success(`"${request.titleName}" created successfully.`);
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
    const body = err.error as IJobTitleErrorResponse | undefined;

    if (body?.code === 'duplicate_name') {
      // AC-3: duplicate job title name within tenant
      this.duplicateNameError.set(
        body.message || 'A job title with this name already exists.'
      );
    } else {
      this.toastr.error(body?.message || 'Failed to save job title.');
    }
  }
}
