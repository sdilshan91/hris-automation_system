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
import { BenefitService } from '../../services/benefit.service';
import {
  IBenefitPlan,
  ICreateBenefitPlan,
  IUpdateBenefitPlan,
  IBenefitErrorResponse,
  BENEFIT_TYPES,
  BenefitType,
} from '../../models/benefit.models';

/**
 * US-TRN-002 AC-1/AC-4: Benefit-plan create/edit form as a slide-over panel.
 *
 * Fields: Name (required, max 200), Type (required), Description, Coverage
 * details, Employer/Employee cost (>= 0), Currency, Effective from (required)
 * / to (>= from), optional enrollment window (opens/closes, close >= open).
 * Creation always yields a Draft plan; status transitions are handled from the
 * list, not here.
 */
@Component({
  selector: 'app-plan-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-container">
      <!-- Header -->
      <div class="form-header">
        <h2 class="form-title">
          {{ plan() ? 'Edit Benefit Plan' : 'Add Benefit Plan' }}
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
        <!-- Name -->
        <div class="form-section">
          <label class="label-notion" for="plan-name">
            Name <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="plan-name"
            type="text"
            formControlName="name"
            class="input-notion"
            placeholder="e.g. Gold Health Plan"
            maxlength="200"
            autocomplete="off"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <p class="field-error">
              @if (form.get('name')?.hasError('required')) {
                Name is required.
              } @else if (form.get('name')?.hasError('maxlength')) {
                Name cannot exceed 200 characters.
              }
            </p>
          }
        </div>

        <!-- Type -->
        <div class="form-section">
          <label class="label-notion" for="plan-type">
            Type <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <select id="plan-type" formControlName="type" class="input-notion">
            @for (t of types; track t) {
              <option [value]="t">{{ t }}</option>
            }
          </select>
        </div>

        <!-- Description -->
        <div class="form-section">
          <label class="label-notion" for="plan-description">Description</label>
          <textarea
            id="plan-description"
            formControlName="description"
            class="input-notion textarea-notion"
            rows="2"
            placeholder="Short summary of the plan"
          ></textarea>
        </div>

        <!-- Coverage details -->
        <div class="form-section">
          <label class="label-notion" for="plan-coverage">Coverage details</label>
          <textarea
            id="plan-coverage"
            formControlName="coverageDetails"
            class="input-notion textarea-notion"
            rows="3"
            placeholder="What the plan covers (limits, exclusions, tiers)"
          ></textarea>
        </div>

        <!-- Costs + currency -->
        <div class="grid grid-cols-3 gap-3">
          <div class="form-section">
            <label class="label-notion" for="plan-employer-cost">Employer cost</label>
            <input
              id="plan-employer-cost"
              type="number"
              min="0"
              step="0.01"
              formControlName="employerCost"
              class="input-notion"
              placeholder="0.00"
            />
            @if (form.get('employerCost')?.hasError('min') && form.get('employerCost')?.touched) {
              <p class="field-error">Cost cannot be negative.</p>
            }
          </div>
          <div class="form-section">
            <label class="label-notion" for="plan-employee-cost">Employee cost</label>
            <input
              id="plan-employee-cost"
              type="number"
              min="0"
              step="0.01"
              formControlName="employeeCost"
              class="input-notion"
              placeholder="0.00"
            />
            @if (form.get('employeeCost')?.hasError('min') && form.get('employeeCost')?.touched) {
              <p class="field-error">Cost cannot be negative.</p>
            }
          </div>
          <div class="form-section">
            <label class="label-notion" for="plan-currency">Currency</label>
            <input
              id="plan-currency"
              type="text"
              formControlName="currency"
              class="input-notion uppercase"
              placeholder="USD"
              maxlength="8"
              autocomplete="off"
            />
            <p class="field-hint">Blank uses the tenant default.</p>
          </div>
        </div>

        <!-- Effective window -->
        <div class="grid grid-cols-2 gap-3">
          <div class="form-section">
            <label class="label-notion" for="plan-effective-from">
              Effective from <span class="text-red-500" aria-hidden="true">*</span>
            </label>
            <input
              id="plan-effective-from"
              type="date"
              formControlName="effectiveFrom"
              class="input-notion"
            />
            @if (form.get('effectiveFrom')?.hasError('required') && form.get('effectiveFrom')?.touched) {
              <p class="field-error">Effective-from date is required.</p>
            }
          </div>
          <div class="form-section">
            <label class="label-notion" for="plan-effective-to">Effective to</label>
            <input
              id="plan-effective-to"
              type="date"
              formControlName="effectiveTo"
              class="input-notion"
            />
            @if (form.hasError('effectiveToBeforeFrom') && form.get('effectiveTo')?.touched) {
              <p class="field-error">End date cannot be before the start date.</p>
            }
            <p class="field-hint">Blank = open-ended.</p>
          </div>
        </div>

        <!-- Optional enrollment window -->
        <div class="grid grid-cols-2 gap-3">
          <div class="form-section">
            <label class="label-notion" for="plan-enroll-opens">Enrollment opens</label>
            <input
              id="plan-enroll-opens"
              type="date"
              formControlName="enrollmentOpensAt"
              class="input-notion"
            />
          </div>
          <div class="form-section">
            <label class="label-notion" for="plan-enroll-closes">Enrollment closes</label>
            <input
              id="plan-enroll-closes"
              type="date"
              formControlName="enrollmentClosesAt"
              class="input-notion"
            />
            @if (form.hasError('enrollmentCloseBeforeOpen') && form.get('enrollmentClosesAt')?.touched) {
              <p class="field-error">Close date cannot be before the open date.</p>
            }
          </div>
        </div>
        <p class="field-hint -mt-2">
          Enrollment window is optional (used when the plan is offered — US-TRN-003).
        </p>

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
              {{ plan() ? 'Save Changes' : 'Create Plan' }}
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
      @apply resize-y min-h-[4rem];
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
export class PlanFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly benefitService = inject(BenefitService);
  private readonly toastr = inject(ToastrService);

  /** Plan to edit. null = create mode. */
  readonly plan = input<IBenefitPlan | null>(null);

  /** Emitted on successful create/update. */
  readonly saved = output<void>();

  /** Emitted when the user cancels. */
  readonly cancelled = output<void>();

  readonly isSaving = signal(false);

  readonly types = BENEFIT_TYPES;

  form!: FormGroup;

  ngOnInit(): void {
    const p = this.plan();

    this.form = this.fb.group(
      {
        name: [p?.name ?? '', [Validators.required, Validators.maxLength(200)]],
        type: [p?.type ?? 'Health', [Validators.required]],
        description: [p?.description ?? ''],
        coverageDetails: [p?.coverageDetails ?? ''],
        employerCost: [p?.employerCost ?? null, [Validators.min(0)]],
        employeeCost: [p?.employeeCost ?? null, [Validators.min(0)]],
        currency: [p?.currency ?? ''],
        effectiveFrom: [p?.effectiveFrom ?? '', [Validators.required]],
        effectiveTo: [p?.effectiveTo ?? ''],
        enrollmentOpensAt: [p?.enrollmentOpensAt ?? ''],
        enrollmentClosesAt: [p?.enrollmentClosesAt ?? ''],
      },
      {
        validators: [
          effectiveToAfterFromValidator,
          enrollmentCloseAfterOpenValidator,
        ],
      }
    );
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) return;

    this.isSaving.set(true);

    const request = this.buildRequest();
    const p = this.plan();

    const request$ = p
      ? this.benefitService.updatePlan(p.id, request as IUpdateBenefitPlan)
      : this.benefitService.createPlan(request);

    request$.subscribe({
      next: () => {
        this.isSaving.set(false);
        this.toastr.success(
          `"${request.name}" ${p ? 'updated' : 'created'} successfully.`
        );
        this.saved.emit();
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        const body = err.error as IBenefitErrorResponse | undefined;
        this.toastr.error(body?.message || 'Failed to save benefit plan.');
      },
    });
  }

  private buildRequest(): ICreateBenefitPlan {
    const v = this.form.value;
    return {
      name: (v.name ?? '').trim(),
      type: v.type as BenefitType,
      description: v.description?.trim() || null,
      coverageDetails: v.coverageDetails?.trim() || null,
      employerCost:
        v.employerCost === null || v.employerCost === ''
          ? null
          : Number(v.employerCost),
      employeeCost:
        v.employeeCost === null || v.employeeCost === ''
          ? null
          : Number(v.employeeCost),
      currency: v.currency?.trim().toUpperCase() || null,
      effectiveFrom: v.effectiveFrom,
      effectiveTo: v.effectiveTo || null,
      enrollmentOpensAt: v.enrollmentOpensAt || null,
      enrollmentClosesAt: v.enrollmentClosesAt || null,
    };
  }
}

/** Cross-field: EffectiveTo must be >= EffectiveFrom when both are set (AC-1). */
export function effectiveToAfterFromValidator(
  control: AbstractControl
): ValidationErrors | null {
  const from = control.get('effectiveFrom')?.value;
  const to = control.get('effectiveTo')?.value;
  if (from && to && to < from) {
    return { effectiveToBeforeFrom: true };
  }
  return null;
}

/** Cross-field: EnrollmentClosesAt must be >= EnrollmentOpensAt when both are set. */
export function enrollmentCloseAfterOpenValidator(
  control: AbstractControl
): ValidationErrors | null {
  const opens = control.get('enrollmentOpensAt')?.value;
  const closes = control.get('enrollmentClosesAt')?.value;
  if (opens && closes && closes < opens) {
    return { enrollmentCloseBeforeOpen: true };
  }
  return null;
}
