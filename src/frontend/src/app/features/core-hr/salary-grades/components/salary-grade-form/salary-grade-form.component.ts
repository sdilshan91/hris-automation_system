import {
  Component,
  ChangeDetectionStrategy,
  computed,
  DestroyRef,
  inject,
  signal,
  input,
  output,
  OnInit,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
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
import { SalaryGradeService } from '../../services/salary-grade.service';
import {
  ISalaryGrade,
  ISalaryGradeRequest,
  ISalaryGradeErrorResponse,
} from '../../models/salary-grade.models';

/**
 * ISSUE-021: Salary grade create/edit form as a slide-over panel.
 *
 * Fields: Code (required), Name (required), Min / Mid (optional) / Max amounts,
 * Currency (3-char), Description (optional), Active toggle.
 *
 * Client validation: required code/name, min <= mid <= max (cross-field),
 * currency exactly 3 letters. Backend 422 (invalid_grade) / 409 (duplicate code)
 * surface via the toast + a field error.
 */
@Component({
  selector: 'app-salary-grade-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-container">
      <!-- Header -->
      <div class="form-header">
        <h2 class="form-title">
          {{ grade() ? 'Edit Salary Grade' : 'Add Salary Grade' }}
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
        <!-- Code -->
        <div class="form-section">
          <label class="label-notion" for="sg-code">
            Code <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="sg-code"
            type="text"
            formControlName="code"
            class="input-notion"
            placeholder="e.g. G1"
            maxlength="30"
            autocomplete="off"
          />
          @if (form.get('code')?.invalid && form.get('code')?.touched) {
            <p class="field-error">Code is required.</p>
          }
          @if (duplicateCodeError()) {
            <p class="field-error">{{ duplicateCodeError() }}</p>
          }
        </div>

        <!-- Name -->
        <div class="form-section">
          <label class="label-notion" for="sg-name">
            Name <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="sg-name"
            type="text"
            formControlName="name"
            class="input-notion"
            placeholder="e.g. Grade 1"
            maxlength="150"
            autocomplete="off"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <p class="field-error">Name is required.</p>
          }
        </div>

        <!-- Amounts -->
        <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div class="form-section">
            <label class="label-notion" for="sg-min">
              Min <span class="text-red-500" aria-hidden="true">*</span>
            </label>
            <input
              id="sg-min"
              type="number"
              formControlName="minAmount"
              class="input-notion"
              min="0"
              step="0.01"
            />
            @if (form.get('minAmount')?.invalid && form.get('minAmount')?.touched) {
              <p class="field-error">Min is required.</p>
            }
          </div>
          <div class="form-section">
            <label class="label-notion" for="sg-mid">Mid</label>
            <input
              id="sg-mid"
              type="number"
              formControlName="midAmount"
              class="input-notion"
              min="0"
              step="0.01"
              placeholder="Optional"
            />
          </div>
          <div class="form-section">
            <label class="label-notion" for="sg-max">
              Max <span class="text-red-500" aria-hidden="true">*</span>
            </label>
            <input
              id="sg-max"
              type="number"
              formControlName="maxAmount"
              class="input-notion"
              min="0"
              step="0.01"
            />
            @if (form.get('maxAmount')?.invalid && form.get('maxAmount')?.touched) {
              <p class="field-error">Max is required.</p>
            }
          </div>
        </div>
        @if (form.hasError('bandOrder') && (form.get('minAmount')?.touched || form.get('maxAmount')?.touched)) {
          <p class="field-error">
            Amounts must satisfy Min &le; Mid &le; Max.
          </p>
        }

        <!-- Currency -->
        <div class="form-section">
          <label class="label-notion" for="sg-currency">
            Currency <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="sg-currency"
            type="text"
            formControlName="currency"
            class="input-notion uppercase"
            placeholder="USD"
            maxlength="3"
            autocomplete="off"
          />
          @if (form.get('currency')?.invalid && form.get('currency')?.touched) {
            <p class="field-error">Currency must be a 3-letter code (e.g. USD).</p>
          }
        </div>

        <!-- Description -->
        <div class="form-section">
          <label class="label-notion" for="sg-description">Description</label>
          <textarea
            id="sg-description"
            formControlName="description"
            class="input-notion textarea-notion"
            rows="3"
            placeholder="Brief description of this grade"
          ></textarea>
        </div>

        <!-- Active Toggle — EDIT ONLY. A new grade is always created active: the create wire DTO has no
             isActive member, so rendering the toggle here would repeat on create exactly the silent no-op
             B5 fixed on update (flip it off, save, get a success toast, get an active grade anyway). -->
        @if (grade()) {
        <div class="form-section">
          <div class="toggle-row">
            <div class="toggle-label-block">
              <label class="label-notion mb-0" for="sg-active">Active</label>
              <p class="field-hint">
                Inactive grades are hidden from job-title grade pickers.
              </p>
              @if (deactivationWarning(); as warning) {
                <p class="field-hint text-amber-600" data-testid="deactivate-warning" role="status">
                  {{ warning }}
                </p>
              }
            </div>
            <label class="toggle-switch" for="sg-active">
              <input
                id="sg-active"
                type="checkbox"
                formControlName="isActive"
                class="toggle-input"
              />
              <span class="toggle-slider"></span>
            </label>
          </div>
        </div>
        }

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
              {{ grade() ? 'Save Changes' : 'Create Grade' }}
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
export class SalaryGradeFormComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);
  private readonly gradeService = inject(SalaryGradeService);
  private readonly toastr = inject(ToastrService);

  /** Grade to edit. null = create mode. */
  readonly grade = input<ISalaryGrade | null>(null);

  /** Emitted on successful create/update */
  readonly saved = output<void>();

  /** Emitted when the user cancels */
  readonly cancelled = output<void>();

  readonly isSaving = signal(false);

  /**
   * Mirrors the Active control so the warning below can be a `computed`. The component is OnPush and a
   * reactive-form `patchValue` does not mark the view dirty, so a plain method reading `form.value` renders
   * stale — the warning would only appear on some later, unrelated change detection pass.
   */
  private readonly activeToggle = signal(true);
  readonly duplicateCodeError = signal('');

  form!: FormGroup;

  ngOnInit(): void {
    const g = this.grade();

    this.form = this.fb.group(
      {
        code: [g?.code ?? '', [Validators.required, Validators.maxLength(30)]],
        name: [g?.name ?? '', [Validators.required, Validators.maxLength(150)]],
        minAmount: [g?.minAmount ?? null, [Validators.required, Validators.min(0)]],
        midAmount: [g?.midAmount ?? null, [Validators.min(0)]],
        maxAmount: [g?.maxAmount ?? null, [Validators.required, Validators.min(0)]],
        currency: [
          g?.currency ?? 'USD',
          [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)],
        ],
        description: [g?.description ?? ''],
        isActive: [g?.isActive ?? true],
      },
      { validators: SalaryGradeFormComponent.bandOrderValidator }
    );

    this.activeToggle.set(this.form.value.isActive);
    this.form.controls['isActive'].valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((value: boolean) => this.activeToggle.set(value));
  }

  /**
   * Cross-field validator: Min <= Mid (if present) <= Max. Only evaluated when
   * the relevant numeric fields are populated so it does not fight the
   * per-field required validators.
   */
  static bandOrderValidator(
    group: AbstractControl
  ): ValidationErrors | null {
    const min = group.get('minAmount')?.value;
    const mid = group.get('midAmount')?.value;
    const max = group.get('maxAmount')?.value;

    const num = (v: unknown): number | null =>
      v === null || v === undefined || v === '' ? null : Number(v);

    const minN = num(min);
    const midN = num(mid);
    const maxN = num(max);

    if (minN !== null && maxN !== null && minN > maxN) {
      return { bandOrder: true };
    }
    if (midN !== null) {
      if (minN !== null && midN < minN) return { bandOrder: true };
      if (maxN !== null && midN > maxN) return { bandOrder: true };
    }
    return null;
  }

  /**
   * B5: warn when the toggle is being turned OFF on a grade job titles still point at.
   *
   * Job titles must resolve to an ACTIVE grade (`JobTitleService.ValidateGradeAsync`), so deactivating a
   * referenced grade makes those titles fail their next save — a consequence invisible from this form.
   * It warns rather than blocks: retiring a grade part-way through a re-grade is legitimate, and refusing
   * it would leave no way out.
   */
  readonly deactivationWarning = computed<string | null>(() => {
    const g = this.grade();
    // Only when an ACTIVE grade is being switched off — creating an inactive grade, or saving one that was
    // already inactive, breaks nothing new.
    if (!g || !g.isActive || this.activeToggle()) return null;
    const count = g.referencingJobTitleCount;
    if (count <= 0) return null;
    return count === 1
      ? '1 job title uses this grade and will fail validation on its next save.'
      : `${count} job titles use this grade and will fail validation on their next save.`;
  });

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) return;

    // Deactivating a grade in use is allowed, but not by accident.
    const warning = this.deactivationWarning();
    if (warning && !window.confirm(`${warning}\n\nDeactivate it anyway?`)) {
      return;
    }

    this.isSaving.set(true);
    this.duplicateCodeError.set('');

    const request = this.buildRequest();
    const g = this.grade();

    const op$ = g
      ? this.gradeService.update(g.id, request)
      : this.gradeService.create(request);

    op$.subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toastr.success(
          g
            ? `"${request.name}" updated successfully.`
            : `"${request.name}" created successfully.`
        );
        this.saved.emit();
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        this.handleError(err);
      },
    });
  }

  private buildRequest(): ISalaryGradeRequest {
    const v = this.form.value;
    const num = (x: unknown): number | null =>
      x === null || x === undefined || x === '' ? null : Number(x);

    return {
      code: v.code.trim(),
      name: v.name.trim(),
      minAmount: num(v.minAmount) ?? 0,
      midAmount: num(v.midAmount),
      maxAmount: num(v.maxAmount) ?? 0,
      currency: (v.currency ?? '').trim().toUpperCase(),
      description: v.description?.trim() || null,
      isActive: v.isActive,
    };
  }

  private handleError(err: HttpErrorResponse): void {
    const body = err.error as ISalaryGradeErrorResponse | undefined;

    if (body?.code === 'duplicate_code' || err.status === 409) {
      this.duplicateCodeError.set(
        body?.message || 'A salary grade with this code already exists.'
      );
      return;
    }

    // 422 invalid_grade (min/mid/max band or currency rejected server-side)
    this.toastr.error(body?.message || 'Failed to save salary grade.');
  }
}
