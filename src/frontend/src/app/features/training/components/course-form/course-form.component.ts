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
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { TrainingService } from '../../services/training.service';
import {
  ICourse,
  ICreateCourse,
  IUpdateCourse,
  ITrainingErrorResponse,
  TRAINING_MODES,
  TrainingMode,
} from '../../models/training.models';

/**
 * US-TRN-001 AC-1: Course create/edit form as a slide-over panel.
 *
 * Fields: Title (required, max 200), Description, Mode (required),
 * Capacity (>= 0, blank = unlimited), Location, Instructor, Start/End dates
 * (End >= Start), Duration hours (>= 0). Creation always yields a Draft course;
 * status transitions are handled from the list, not here.
 */
@Component({
  selector: 'app-course-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-container">
      <!-- Header -->
      <div class="form-header">
        <h2 class="form-title">
          {{ course() ? 'Edit Course' : 'Add Course' }}
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
        <!-- Title -->
        <div class="form-section">
          <label class="label-notion" for="course-title">
            Title <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="course-title"
            type="text"
            formControlName="title"
            class="input-notion"
            placeholder="e.g. Workplace Safety Fundamentals"
            maxlength="200"
            autocomplete="off"
          />
          @if (form.get('title')?.invalid && form.get('title')?.touched) {
            <p class="field-error">
              @if (form.get('title')?.hasError('required')) {
                Title is required.
              } @else if (form.get('title')?.hasError('maxlength')) {
                Title cannot exceed 200 characters.
              }
            </p>
          }
        </div>

        <!-- Description -->
        <div class="form-section">
          <label class="label-notion" for="course-description">Description</label>
          <textarea
            id="course-description"
            formControlName="description"
            class="input-notion textarea-notion"
            rows="3"
            placeholder="What this course covers"
          ></textarea>
        </div>

        <!-- Mode -->
        <div class="form-section">
          <label class="label-notion" for="course-mode">
            Mode <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <select id="course-mode" formControlName="mode" class="input-notion">
            @for (m of modes; track m) {
              <option [value]="m">{{ modeLabel(m) }}</option>
            }
          </select>
        </div>

        <!-- Capacity + Duration -->
        <div class="grid grid-cols-2 gap-3">
          <div class="form-section">
            <label class="label-notion" for="course-capacity">Capacity</label>
            <input
              id="course-capacity"
              type="number"
              min="0"
              formControlName="capacity"
              class="input-notion"
              placeholder="Unlimited"
            />
            @if (form.get('capacity')?.hasError('min') && form.get('capacity')?.touched) {
              <p class="field-error">Capacity cannot be negative.</p>
            }
            <p class="field-hint">Leave blank for unlimited (never waitlists).</p>
          </div>
          <div class="form-section">
            <label class="label-notion" for="course-duration">Duration (hours)</label>
            <input
              id="course-duration"
              type="number"
              min="0"
              step="0.5"
              formControlName="durationHours"
              class="input-notion"
              placeholder="e.g. 8"
            />
            @if (form.get('durationHours')?.hasError('min') && form.get('durationHours')?.touched) {
              <p class="field-error">Duration cannot be negative.</p>
            }
          </div>
        </div>

        <!-- Start + End dates -->
        <div class="grid grid-cols-2 gap-3">
          <div class="form-section">
            <label class="label-notion" for="course-start">Start date</label>
            <input
              id="course-start"
              type="date"
              formControlName="startDate"
              class="input-notion"
            />
          </div>
          <div class="form-section">
            <label class="label-notion" for="course-end">End date</label>
            <input
              id="course-end"
              type="date"
              formControlName="endDate"
              class="input-notion"
            />
            @if (form.hasError('endBeforeStart') && form.get('endDate')?.touched) {
              <p class="field-error">End date cannot be before the start date.</p>
            }
          </div>
        </div>

        <!-- Location + Instructor -->
        <div class="form-section">
          <label class="label-notion" for="course-location">Location</label>
          <input
            id="course-location"
            type="text"
            formControlName="location"
            class="input-notion"
            placeholder="e.g. Training Room B / Zoom"
            autocomplete="off"
          />
        </div>
        <div class="form-section">
          <label class="label-notion" for="course-instructor">Instructor</label>
          <input
            id="course-instructor"
            type="text"
            formControlName="instructor"
            class="input-notion"
            placeholder="e.g. Jane Doe"
            autocomplete="off"
          />
        </div>

        <!-- Form actions -->
        <div class="form-actions">
          <button type="button" class="btn-secondary" (click)="cancelled.emit()">
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
              {{ course() ? 'Save Changes' : 'Create Course' }}
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
export class CourseFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly trainingService = inject(TrainingService);
  private readonly toastr = inject(ToastrService);

  /** Course to edit. null = create mode. */
  readonly course = input<ICourse | null>(null);

  /** Emitted on successful create/update. */
  readonly saved = output<void>();

  /** Emitted when the user cancels. */
  readonly cancelled = output<void>();

  readonly isSaving = signal(false);

  readonly modes = TRAINING_MODES;

  form!: FormGroup;

  ngOnInit(): void {
    const c = this.course();

    this.form = this.fb.group(
      {
        title: [
          c?.title ?? '',
          [Validators.required, Validators.maxLength(200)],
        ],
        description: [c?.description ?? ''],
        mode: [c?.mode ?? 'InPerson', [Validators.required]],
        capacity: [c?.capacity ?? null, [Validators.min(0)]],
        location: [c?.location ?? ''],
        instructor: [c?.instructor ?? ''],
        startDate: [c?.startDate ?? ''],
        endDate: [c?.endDate ?? ''],
        durationHours: [c?.durationHours ?? null, [Validators.min(0)]],
      },
      { validators: [endAfterStartValidator] }
    );
  }

  modeLabel(mode: TrainingMode): string {
    switch (mode) {
      case 'InPerson':
        return 'In person';
      case 'Online':
        return 'Online';
      case 'Hybrid':
        return 'Hybrid';
    }
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) return;

    this.isSaving.set(true);

    const request = this.buildRequest();
    const c = this.course();

    const request$ = c
      ? this.trainingService.updateCourse(c.id, request as IUpdateCourse)
      : this.trainingService.createCourse(request);

    request$.subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toastr.success(
          `"${request.title}" ${c ? 'updated' : 'created'} successfully.`
        );
        this.saved.emit();
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        const body = err.error as ITrainingErrorResponse | undefined;
        this.toastr.error(body?.message || 'Failed to save course.');
      },
    });
  }

  private buildRequest(): ICreateCourse {
    const v = this.form.value;
    return {
      title: (v.title ?? '').trim(),
      description: v.description?.trim() || null,
      mode: v.mode,
      capacity: v.capacity === null || v.capacity === '' ? null : Number(v.capacity),
      location: v.location?.trim() || null,
      instructor: v.instructor?.trim() || null,
      startDate: v.startDate || null,
      endDate: v.endDate || null,
      durationHours:
        v.durationHours === null || v.durationHours === ''
          ? null
          : Number(v.durationHours),
    };
  }
}

/** Cross-field: End date must be >= Start date when both are set (AC-1). */
export function endAfterStartValidator(
  control: AbstractControl
): ValidationErrors | null {
  const start = control.get('startDate')?.value;
  const end = control.get('endDate')?.value;
  if (start && end && end < start) {
    return { endBeforeStart: true };
  }
  return null;
}
