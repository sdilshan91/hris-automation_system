import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import {
  IEntitlementRule,
  ICreateEntitlementRuleRequest,
  ILookupItem,
  EMPLOYMENT_TYPE_OPTIONS,
} from '../../models/leave-entitlement.models';

/**
 * US-LV-002 AC-1: Slide-over form for creating/editing an entitlement rule.
 *
 * Dimensions: leave type, department, job title, employment type,
 *   tenure min/max months, entitlement days, priority, effective dates.
 *
 * "Job level" is omitted (no backend entity).
 */
@Component({
  selector: 'app-entitlement-rule-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('slideOver', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ opacity: 1, transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('overlayFade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
  ],
  template: `
    <!-- Overlay -->
    <div
      class="fixed inset-0 z-40 bg-black/30 backdrop-blur-sm"
      @overlayFade
      (click)="onClose()"
      role="presentation"
    ></div>

    <!-- Slide-over panel -->
    <div
      class="fixed inset-y-0 right-0 z-50 w-full sm:w-[480px] bg-white shadow-xl flex flex-col"
      @slideOver
      role="dialog"
      aria-modal="true"
      [attr.aria-label]="rule() ? 'Edit entitlement rule' : 'Create entitlement rule'"
    >
      <!-- Header -->
      <div class="flex items-center justify-between px-6 py-4 border-b border-neutral-100">
        <h2 class="text-lg font-semibold text-neutral-900">
          {{ rule() ? 'Edit Entitlement Rule' : 'Create Entitlement Rule' }}
        </h2>
        <button
          type="button"
          class="w-8 h-8 rounded-lg flex items-center justify-center text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100 transition-colors"
          (click)="onClose()"
          aria-label="Close form"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
            <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
          </svg>
        </button>
      </div>

      <!-- Form body -->
      <div class="flex-1 overflow-y-auto px-6 py-5">
        <form [formGroup]="form" (ngSubmit)="onSubmit()" id="rule-form">
          <div class="space-y-5">
            <!-- Leave Type (required) -->
            <div class="form-field">
              <label class="label-notion" for="rf-leaveType">Leave Type <span class="text-red-500">*</span></label>
              <select id="rf-leaveType" formControlName="leaveTypeId" class="input-notion select-input" aria-required="true">
                <option value="">Select leave type...</option>
                @for (lt of leaveTypes(); track lt.id) {
                  <option [value]="lt.id">{{ lt.name }}</option>
                }
              </select>
              @if (form.get('leaveTypeId')?.touched && form.get('leaveTypeId')?.hasError('required')) {
                <p class="field-error" role="alert">Leave type is required.</p>
              }
            </div>

            <!-- Department (optional dimension) -->
            <div class="form-field">
              <label class="label-notion" for="rf-department">Department</label>
              <select id="rf-department" formControlName="departmentId" class="input-notion select-input">
                <option value="">All departments</option>
                @for (dept of departments(); track dept.id) {
                  <option [value]="dept.id">{{ dept.name }}</option>
                }
              </select>
            </div>

            <!-- Job Title (optional dimension) -->
            <div class="form-field">
              <label class="label-notion" for="rf-jobTitle">Job Title</label>
              <select id="rf-jobTitle" formControlName="jobTitleId" class="input-notion select-input">
                <option value="">All job titles</option>
                @for (jt of jobTitles(); track jt.id) {
                  <option [value]="jt.id">{{ jt.name }}</option>
                }
              </select>
            </div>

            <!-- Employment Type (optional dimension) -->
            <div class="form-field">
              <label class="label-notion" for="rf-employmentType">Employment Type</label>
              <select id="rf-employmentType" formControlName="employmentType" class="input-notion select-input">
                <option value="">All types</option>
                @for (et of employmentTypeOptions; track et.value) {
                  <option [value]="et.value">{{ et.label }}</option>
                }
              </select>
            </div>

            <!-- Tenure bracket -->
            <div class="grid grid-cols-2 gap-3">
              <div class="form-field">
                <label class="label-notion" for="rf-tenureMin">Tenure Min (months)</label>
                <input id="rf-tenureMin" type="number" formControlName="tenureMinMonths" class="input-notion"
                  min="0" placeholder="e.g. 0" />
              </div>
              <div class="form-field">
                <label class="label-notion" for="rf-tenureMax">Tenure Max (months)</label>
                <input id="rf-tenureMax" type="number" formControlName="tenureMaxMonths" class="input-notion"
                  min="0" placeholder="e.g. 60" />
              </div>
            </div>

            <!-- Entitlement Days (required) -->
            <div class="form-field">
              <label class="label-notion" for="rf-days">Entitlement Days <span class="text-red-500">*</span></label>
              <input id="rf-days" type="number" formControlName="entitlementDays" class="input-notion"
                min="0" step="0.5" aria-required="true" />
              @if (form.get('entitlementDays')?.touched && form.get('entitlementDays')?.hasError('required')) {
                <p class="field-error" role="alert">Entitlement days is required.</p>
              }
              @if (form.get('entitlementDays')?.touched && form.get('entitlementDays')?.hasError('min')) {
                <p class="field-error" role="alert">Entitlement days cannot be negative.</p>
              }
            </div>

            <!-- Priority -->
            <div class="form-field">
              <label class="label-notion" for="rf-priority">Priority <span class="text-red-500">*</span></label>
              <input id="rf-priority" type="number" formControlName="priority" class="input-notion"
                min="1" aria-required="true" />
              <p class="text-xs text-neutral-400 mt-1">Higher number = more specific. See the help tooltip on the rules page.</p>
              @if (form.get('priority')?.touched && form.get('priority')?.hasError('required')) {
                <p class="field-error" role="alert">Priority is required.</p>
              }
              @if (form.get('priority')?.touched && form.get('priority')?.hasError('min')) {
                <p class="field-error" role="alert">Priority must be at least 1.</p>
              }
            </div>

            <!-- Effective From (required) -->
            <div class="form-field">
              <label class="label-notion" for="rf-effectiveFrom">Effective From <span class="text-red-500">*</span></label>
              <input id="rf-effectiveFrom" type="date" formControlName="effectiveFrom" class="input-notion" aria-required="true" />
              @if (form.get('effectiveFrom')?.touched && form.get('effectiveFrom')?.hasError('required')) {
                <p class="field-error" role="alert">Effective from date is required.</p>
              }
            </div>

            <!-- Effective To (optional) -->
            <div class="form-field">
              <label class="label-notion" for="rf-effectiveTo">Effective To</label>
              <input id="rf-effectiveTo" type="date" formControlName="effectiveTo" class="input-notion" />
              <p class="text-xs text-neutral-400 mt-1">Leave blank for open-ended rules.</p>
            </div>
          </div>
        </form>
      </div>

      <!-- Footer -->
      <div class="px-6 py-4 border-t border-neutral-100 flex items-center justify-end gap-3">
        <button type="button" class="btn-secondary" (click)="onClose()">Cancel</button>
        <button type="submit" form="rule-form" class="btn-primary" [disabled]="isSaving()">
          @if (isSaving()) {
            <span class="btn-spinner"></span> Saving...
          } @else {
            {{ rule() ? 'Update Rule' : 'Create Rule' }}
          }
        </button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .form-field { @apply space-y-1.5; }
    .label-notion {
      @apply block text-sm font-medium text-neutral-700;
    }
    .input-notion {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2.5
        text-sm text-neutral-900 placeholder-neutral-400
        transition-all duration-150
        focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-400;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }
    .field-error { @apply text-xs text-red-500 mt-1; }
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
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class EntitlementRuleFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);

  /** Existing rule for editing, or null for create mode */
  readonly rule = input<IEntitlementRule | null>(null);
  /** Leave type lookup items */
  readonly leaveTypes = input<ILookupItem[]>([]);
  /** Department lookup items */
  readonly departments = input<ILookupItem[]>([]);
  /** Job title lookup items */
  readonly jobTitles = input<ILookupItem[]>([]);

  readonly save = output<ICreateEntitlementRuleRequest>();
  readonly close = output<void>();

  readonly isSaving = signal(false);
  readonly employmentTypeOptions = EMPLOYMENT_TYPE_OPTIONS;

  form!: FormGroup;

  ngOnInit(): void {
    this.form = this.fb.group({
      leaveTypeId: ['', [Validators.required]],
      departmentId: [''],
      jobTitleId: [''],
      employmentType: [''],
      tenureMinMonths: [null as number | null],
      tenureMaxMonths: [null as number | null],
      entitlementDays: [null as number | null, [Validators.required, Validators.min(0)]],
      priority: [1, [Validators.required, Validators.min(1)]],
      effectiveFrom: ['', [Validators.required]],
      effectiveTo: [''],
    });

    const existing = this.rule();
    if (existing) {
      this.form.patchValue({
        leaveTypeId: existing.leaveTypeId,
        departmentId: existing.departmentId ?? '',
        jobTitleId: existing.jobTitleId ?? '',
        employmentType: existing.employmentType ?? '',
        tenureMinMonths: existing.tenureMinMonths,
        tenureMaxMonths: existing.tenureMaxMonths,
        entitlementDays: existing.entitlementDays,
        priority: existing.priority,
        effectiveFrom: existing.effectiveFrom,
        effectiveTo: existing.effectiveTo ?? '',
      });
    }
  }

  onSubmit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const v = this.form.value;
    const request: ICreateEntitlementRuleRequest = {
      leaveTypeId: v.leaveTypeId,
      departmentId: v.departmentId || null,
      jobTitleId: v.jobTitleId || null,
      employmentType: v.employmentType || null,
      tenureMinMonths: v.tenureMinMonths ?? null,
      tenureMaxMonths: v.tenureMaxMonths ?? null,
      entitlementDays: v.entitlementDays,
      priority: v.priority,
      effectiveFrom: v.effectiveFrom,
      effectiveTo: v.effectiveTo || null,
    };
    this.save.emit(request);
  }

  onClose(): void {
    this.close.emit();
  }
}
