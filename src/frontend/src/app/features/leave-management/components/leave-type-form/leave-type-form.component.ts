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
import { LeaveTypeService } from '../../services/leave-type.service';
import {
  ILeaveType,
  ICreateLeaveTypeRequest,
  ACCRUAL_FREQUENCY_OPTIONS,
  GENDER_OPTIONS,
  LEAVE_TYPE_COLORS,
  getContrastTextColor,
} from '../../models/leave-type.models';

/**
 * US-LV-001 AC-1/AC-2: Leave type create/edit form as a slide-over panel.
 *
 * Fields grouped into sections (UI/UX notes):
 *   - Basic Info: name, code, color, description
 *   - Entitlement Rules: annual entitlement, accrual frequency
 *   - Carry-Forward: carry-forward limit, expiry months
 *   - Document Rules: documents required toggle + day threshold (AC-5)
 *   - Advanced: probation eligible, encashable + max days, half-day, hourly,
 *     gender, max consecutive days, negative balance + limit
 *
 * Mobile: Advanced section collapses into an accordion.
 */
@Component({
  selector: 'app-leave-type-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-container">
      <!-- Header -->
      <div class="form-header">
        <h2 class="form-title">
          {{ leaveType() ? 'Edit Leave Type' : 'Add Leave Type' }}
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

        <!-- ===== Section: Basic Info ===== -->
        <div class="section-header">Basic Info</div>

        <!-- Name -->
        <div class="form-section">
          <label class="label-notion" for="lt-name">
            Name <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="lt-name"
            type="text"
            formControlName="name"
            class="input-notion"
            placeholder="e.g. Annual Leave"
            maxlength="100"
            autocomplete="off"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <p class="field-error" role="alert">
              @if (form.get('name')?.hasError('required')) {
                Leave type name is required.
              } @else if (form.get('name')?.hasError('maxlength')) {
                Name cannot exceed 100 characters.
              }
            </p>
          }
          @if (duplicateNameError()) {
            <p class="field-error" role="alert">{{ duplicateNameError() }}</p>
          }
        </div>

        <!-- Code -->
        <div class="form-section">
          <label class="label-notion" for="lt-code">
            Code <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="lt-code"
            type="text"
            formControlName="code"
            class="input-notion font-mono text-sm"
            placeholder="e.g. AL"
            maxlength="20"
            autocomplete="off"
          />
          @if (form.get('code')?.invalid && form.get('code')?.touched) {
            <p class="field-error" role="alert">
              @if (form.get('code')?.hasError('required')) {
                Code is required.
              } @else if (form.get('code')?.hasError('maxlength')) {
                Code cannot exceed 20 characters.
              } @else if (form.get('code')?.hasError('pattern')) {
                Code must be alphanumeric (letters, numbers, hyphens, underscores).
              }
            </p>
          }
        </div>

        <!-- Color Picker -->
        <div class="form-section">
          <label class="label-notion">
            Color <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <div class="color-palette" role="radiogroup" aria-label="Select leave type color">
            @for (c of colorOptions; track c) {
              <button
                type="button"
                class="color-swatch"
                [style.background-color]="c"
                [class.color-swatch-selected]="form.get('color')?.value === c"
                (click)="selectColor(c)"
                [attr.aria-pressed]="form.get('color')?.value === c"
                [attr.aria-label]="'Color ' + c"
              >
                @if (form.get('color')?.value === c) {
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5" [style.color]="getContrastColor(c)" aria-hidden="true">
                    <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd" />
                  </svg>
                }
              </button>
            }
            <!-- Custom color input -->
            <div class="relative">
              <input
                type="color"
                class="color-custom-input"
                [value]="form.get('color')?.value || '#2563eb'"
                (input)="selectColor($any($event.target).value)"
                aria-label="Custom color"
                title="Pick a custom color"
              />
            </div>
          </div>
          @if (form.get('color')?.value) {
            <div class="mt-2 flex items-center gap-2">
              <span
                class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium"
                [style.background-color]="form.get('color')?.value"
                [style.color]="getContrastColor(form.get('color')?.value)"
              >
                Preview Tag
              </span>
              <span class="text-xs text-neutral-400 font-mono">{{ form.get('color')?.value }}</span>
            </div>
          }
        </div>

        <!-- Description -->
        <div class="form-section">
          <label class="label-notion" for="lt-desc">Description</label>
          <textarea
            id="lt-desc"
            formControlName="description"
            class="input-notion"
            placeholder="Optional description for this leave type..."
            rows="2"
            maxlength="500"
          ></textarea>
        </div>

        <!-- ===== Section: Entitlement Rules ===== -->
        <div class="section-header">Entitlement Rules</div>

        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <!-- Annual Entitlement -->
          <div class="form-section">
            <label class="label-notion" for="lt-entitlement">
              Annual Entitlement (days) <span class="text-red-500" aria-hidden="true">*</span>
            </label>
            <input
              id="lt-entitlement"
              type="number"
              formControlName="annualEntitlement"
              class="input-notion"
              placeholder="0"
              min="0"
              step="0.5"
            />
            @if (form.get('annualEntitlement')?.invalid && form.get('annualEntitlement')?.touched) {
              <p class="field-error" role="alert">
                @if (form.get('annualEntitlement')?.hasError('required')) {
                  Annual entitlement is required.
                } @else if (form.get('annualEntitlement')?.hasError('min')) {
                  Must be 0 or greater.
                }
              </p>
            }
          </div>

          <!-- Accrual Frequency -->
          <div class="form-section">
            <label class="label-notion" for="lt-accrual">
              Accrual Frequency <span class="text-red-500" aria-hidden="true">*</span>
            </label>
            <select
              id="lt-accrual"
              formControlName="accrualFrequency"
              class="input-notion select-input"
            >
              @for (opt of accrualOptions; track opt.value) {
                <option [value]="opt.value">{{ opt.label }}</option>
              }
            </select>
            @if (form.get('accrualFrequency')?.invalid && form.get('accrualFrequency')?.touched) {
              <p class="field-error" role="alert">Accrual frequency is required.</p>
            }
          </div>
        </div>

        <!-- ===== Section: Carry-Forward ===== -->
        <div class="section-header">Carry-Forward</div>

        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <!-- Carry-Forward Limit -->
          <div class="form-section">
            <label class="label-notion" for="lt-carry-limit">
              Carry-Forward Limit (days)
            </label>
            <input
              id="lt-carry-limit"
              type="number"
              formControlName="carryForwardLimit"
              class="input-notion"
              placeholder="0"
              min="0"
              step="0.5"
            />
            <p class="field-hint">0 = no carry-forward allowed.</p>
          </div>

          <!-- Carry-Forward Expiry -->
          <div class="form-section">
            <label class="label-notion" for="lt-carry-expiry">
              Expiry (months)
            </label>
            <input
              id="lt-carry-expiry"
              type="number"
              formControlName="carryForwardExpiryMonths"
              class="input-notion"
              placeholder="0"
              min="0"
              step="1"
            />
            <p class="field-hint">0 = never expires.</p>
          </div>
        </div>

        <!-- ===== Section: Document Rules (AC-5) ===== -->
        <div class="section-header">Document Rules</div>

        <div class="form-section">
          <div class="toggle-row">
            <div class="toggle-label-block">
              <label class="label-notion mb-0">Documents Required</label>
              <p class="field-hint">
                Require supporting documents (e.g., medical certificate).
              </p>
            </div>
            <button
              type="button"
              role="switch"
              [attr.aria-checked]="form.get('documentsRequired')?.value"
              class="toggle-switch"
              [class.toggle-switch-on]="form.get('documentsRequired')?.value"
              (click)="toggleControl('documentsRequired')"
            >
              <span
                class="toggle-knob"
                [class.toggle-knob-on]="form.get('documentsRequired')?.value"
              ></span>
            </button>
          </div>
        </div>

        @if (form.get('documentsRequired')?.value) {
          <div class="form-section pl-4 border-l-2 border-neutral-100">
            <label class="label-notion" for="lt-doc-threshold">
              Day Threshold
            </label>
            <input
              id="lt-doc-threshold"
              type="number"
              formControlName="documentDayThreshold"
              class="input-notion"
              placeholder="e.g. 2"
              min="1"
              step="1"
            />
            <p class="field-hint">
              Documents required when leave exceeds this many days.
            </p>
          </div>
        }

        <!-- ===== Section: Advanced (collapsible on mobile) ===== -->
        <div class="form-section">
          <button
            type="button"
            class="section-header-collapsible"
            (click)="advancedExpanded.set(!advancedExpanded())"
            [attr.aria-expanded]="advancedExpanded()"
            aria-controls="advanced-section"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="w-4 h-4 transition-transform duration-200"
              [class.rotate-90]="advancedExpanded()"
              aria-hidden="true"
            >
              <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z" clip-rule="evenodd" />
            </svg>
            Advanced
          </button>
        </div>

        @if (advancedExpanded()) {
          <div id="advanced-section" class="space-y-4">
            <!-- Probation Eligible -->
            <div class="form-section">
              <div class="toggle-row">
                <label class="label-notion mb-0">Probation Eligible</label>
                <button
                  type="button"
                  role="switch"
                  [attr.aria-checked]="form.get('probationEligible')?.value"
                  class="toggle-switch"
                  [class.toggle-switch-on]="form.get('probationEligible')?.value"
                  (click)="toggleControl('probationEligible')"
                >
                  <span
                    class="toggle-knob"
                    [class.toggle-knob-on]="form.get('probationEligible')?.value"
                  ></span>
                </button>
              </div>
            </div>

            <!-- Encashable -->
            <div class="form-section">
              <div class="toggle-row">
                <label class="label-notion mb-0">Encashable</label>
                <button
                  type="button"
                  role="switch"
                  [attr.aria-checked]="form.get('encashable')?.value"
                  class="toggle-switch"
                  [class.toggle-switch-on]="form.get('encashable')?.value"
                  (click)="toggleControl('encashable')"
                >
                  <span
                    class="toggle-knob"
                    [class.toggle-knob-on]="form.get('encashable')?.value"
                  ></span>
                </button>
              </div>
            </div>

            @if (form.get('encashable')?.value) {
              <div class="form-section pl-4 border-l-2 border-neutral-100">
                <label class="label-notion" for="lt-max-encash">
                  Max Encashable Days
                </label>
                <input
                  id="lt-max-encash"
                  type="number"
                  formControlName="maxEncashDays"
                  class="input-notion"
                  placeholder="e.g. 10"
                  min="0"
                  step="0.5"
                />
              </div>
            }

            <!-- Half-Day / Hourly -->
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div class="form-section">
                <div class="toggle-row">
                  <label class="label-notion mb-0">Half-Day Allowed</label>
                  <button
                    type="button"
                    role="switch"
                    [attr.aria-checked]="form.get('halfDayAllowed')?.value"
                    class="toggle-switch"
                    [class.toggle-switch-on]="form.get('halfDayAllowed')?.value"
                    (click)="toggleControl('halfDayAllowed')"
                  >
                    <span
                      class="toggle-knob"
                      [class.toggle-knob-on]="form.get('halfDayAllowed')?.value"
                    ></span>
                  </button>
                </div>
              </div>

              <div class="form-section">
                <div class="toggle-row">
                  <label class="label-notion mb-0">Hourly Allowed</label>
                  <button
                    type="button"
                    role="switch"
                    [attr.aria-checked]="form.get('hourlyAllowed')?.value"
                    class="toggle-switch"
                    [class.toggle-switch-on]="form.get('hourlyAllowed')?.value"
                    (click)="toggleControl('hourlyAllowed')"
                  >
                    <span
                      class="toggle-knob"
                      [class.toggle-knob-on]="form.get('hourlyAllowed')?.value"
                    ></span>
                  </button>
                </div>
              </div>
            </div>

            <!-- Gender Applicability -->
            <div class="form-section">
              <label class="label-notion" for="lt-gender">
                Gender Applicability
              </label>
              <select
                id="lt-gender"
                formControlName="gender"
                class="input-notion select-input"
              >
                @for (opt of genderOptions; track opt.value) {
                  <option [value]="opt.value">{{ opt.label }}</option>
                }
              </select>
            </div>

            <!-- Max Consecutive Days -->
            <div class="form-section">
              <label class="label-notion" for="lt-max-consecutive">
                Max Consecutive Days
              </label>
              <input
                id="lt-max-consecutive"
                type="number"
                formControlName="maxConsecutiveDays"
                class="input-notion"
                placeholder="No limit"
                min="1"
                step="1"
              />
              <p class="field-hint">Leave blank for no limit.</p>
            </div>

            <!-- Negative Balance -->
            <div class="form-section">
              <div class="toggle-row">
                <label class="label-notion mb-0">Negative Balance Allowed</label>
                <button
                  type="button"
                  role="switch"
                  [attr.aria-checked]="form.get('negativeBalanceAllowed')?.value"
                  class="toggle-switch"
                  [class.toggle-switch-on]="form.get('negativeBalanceAllowed')?.value"
                  (click)="toggleControl('negativeBalanceAllowed')"
                >
                  <span
                    class="toggle-knob"
                    [class.toggle-knob-on]="form.get('negativeBalanceAllowed')?.value"
                  ></span>
                </button>
              </div>
            </div>

            @if (form.get('negativeBalanceAllowed')?.value) {
              <div class="form-section pl-4 border-l-2 border-neutral-100">
                <label class="label-notion" for="lt-neg-limit">
                  Negative Balance Limit (days)
                </label>
                <input
                  id="lt-neg-limit"
                  type="number"
                  formControlName="negativeBalanceLimit"
                  class="input-notion"
                  placeholder="e.g. 5"
                  min="0"
                  step="0.5"
                />
              </div>
            }
          </div>
        }

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
              {{ leaveType() ? 'Save Changes' : 'Create Leave Type' }}
            }
          </button>
        </div>
      </form>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }

    .form-container { @apply flex flex-col h-full; }

    .form-header {
      @apply flex items-center justify-between px-6 py-4 border-b border-neutral-100;
    }
    .form-title { @apply text-lg font-semibold text-neutral-900; }

    .close-btn {
      @apply w-8 h-8 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150;
    }

    .form-body { @apply flex-1 px-6 py-5 space-y-4 overflow-y-auto; }

    .form-section { @apply space-y-1.5; }

    .section-header {
      @apply text-xs font-semibold uppercase tracking-wider text-neutral-400
        pt-4 pb-1 border-b border-neutral-100 mb-2;
    }
    .section-header-collapsible {
      @apply flex items-center gap-2 text-xs font-semibold uppercase tracking-wider
        text-neutral-400 pt-4 pb-1 border-b border-neutral-100 w-full text-left
        hover:text-neutral-600 transition-colors duration-150;
    }

    .field-error { @apply text-xs text-red-600 mt-1; }
    .field-hint { @apply text-xs text-neutral-400; }

    /* --- Color picker --- */
    .color-palette { @apply flex flex-wrap items-center gap-2; }

    .color-swatch {
      @apply w-7 h-7 rounded-full cursor-pointer flex items-center justify-center
        ring-2 ring-transparent transition-all duration-150
        hover:ring-neutral-300 hover:scale-110;
    }
    .color-swatch-selected {
      @apply ring-neutral-900 scale-110;
    }
    .color-custom-input {
      @apply w-7 h-7 rounded-full cursor-pointer border-2 border-dashed
        border-neutral-300 bg-transparent;
      -webkit-appearance: none;
      padding: 0;
    }
    .color-custom-input::-webkit-color-swatch-wrapper { padding: 0; }
    .color-custom-input::-webkit-color-swatch {
      border: none;
      border-radius: 50%;
    }

    /* --- Select input --- */
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    /* --- Toggle switch --- */
    .toggle-row { @apply flex items-center justify-between gap-4; }
    .toggle-label-block { @apply flex-1; }

    .toggle-switch {
      @apply relative inline-flex h-5 w-9 items-center rounded-full flex-shrink-0
        transition-colors duration-200 bg-neutral-200 cursor-pointer
        focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600;
    }
    .toggle-switch-on { @apply bg-brand-600; }

    .toggle-knob {
      @apply inline-block h-3.5 w-3.5 rounded-full bg-white shadow-sm
        transition-transform duration-200 translate-x-1;
    }
    .toggle-knob-on { @apply translate-x-[18px]; }

    /* --- Buttons --- */
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
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class LeaveTypeFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly toastr = inject(ToastrService);

  /** Leave type to edit. null = create mode. */
  readonly leaveType = input<ILeaveType | null>(null);

  /** Emitted on successful create/update */
  readonly saved = output<void>();

  /** Emitted when the user cancels */
  readonly cancelled = output<void>();

  readonly isSaving = signal(false);
  readonly duplicateNameError = signal('');
  readonly advancedExpanded = signal(false);

  readonly colorOptions = LEAVE_TYPE_COLORS;
  readonly accrualOptions = ACCRUAL_FREQUENCY_OPTIONS;
  readonly genderOptions = GENDER_OPTIONS;

  form!: FormGroup;

  ngOnInit(): void {
    const lt = this.leaveType();

    this.form = this.fb.group({
      name: [lt?.name ?? '', [Validators.required, Validators.maxLength(100)]],
      code: [
        lt?.code ?? '',
        [Validators.required, Validators.maxLength(20), Validators.pattern(/^[A-Za-z0-9_-]+$/)],
      ],
      color: [lt?.color ?? '#2563eb', [Validators.required]],
      description: [lt?.description ?? ''],
      annualEntitlement: [lt?.annualEntitlement ?? 0, [Validators.required, Validators.min(0)]],
      accrualFrequency: [lt?.accrualFrequency ?? 'yearly', [Validators.required]],
      carryForwardLimit: [lt?.carryForwardLimit ?? 0, [Validators.min(0)]],
      carryForwardExpiryMonths: [lt?.carryForwardExpiryMonths ?? 0, [Validators.min(0)]],
      probationEligible: [lt?.probationEligible ?? false],
      documentsRequired: [lt?.documentsRequired ?? false],
      documentDayThreshold: [lt?.documentDayThreshold ?? null],
      encashable: [lt?.encashable ?? false],
      maxEncashDays: [lt?.maxEncashDays ?? null],
      halfDayAllowed: [lt?.halfDayAllowed ?? true],
      hourlyAllowed: [lt?.hourlyAllowed ?? false],
      gender: [lt?.gender ?? 'all'],
      maxConsecutiveDays: [lt?.maxConsecutiveDays ?? null],
      negativeBalanceAllowed: [lt?.negativeBalanceAllowed ?? false],
      negativeBalanceLimit: [lt?.negativeBalanceLimit ?? null],
    });

    // Auto-expand advanced section in edit mode if any advanced fields differ from defaults
    if (lt) {
      if (
        lt.probationEligible || lt.encashable || !lt.halfDayAllowed ||
        lt.hourlyAllowed || lt.gender !== 'all' ||
        lt.maxConsecutiveDays != null || lt.negativeBalanceAllowed
      ) {
        this.advancedExpanded.set(true);
      }
    }
  }

  selectColor(color: string): void {
    this.form.get('color')?.setValue(color);
    this.form.markAsDirty();
  }

  getContrastColor(hex: string): string {
    return getContrastTextColor(hex);
  }

  toggleControl(controlName: string): void {
    const ctrl = this.form.get(controlName);
    if (ctrl) {
      ctrl.setValue(!ctrl.value);
      this.form.markAsDirty();
    }
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) return;

    this.isSaving.set(true);
    this.duplicateNameError.set('');

    const fv = this.form.value;
    const lt = this.leaveType();

    const payload: ICreateLeaveTypeRequest = {
      name: fv.name.trim(),
      code: fv.code.trim(),
      color: fv.color,
      description: fv.description?.trim() || null,
      annualEntitlement: fv.annualEntitlement,
      accrualFrequency: fv.accrualFrequency,
      carryForwardLimit: fv.carryForwardLimit ?? 0,
      carryForwardExpiryMonths: fv.carryForwardExpiryMonths ?? 0,
      probationEligible: fv.probationEligible,
      documentsRequired: fv.documentsRequired,
      documentDayThreshold: fv.documentsRequired ? (fv.documentDayThreshold ?? null) : null,
      encashable: fv.encashable,
      maxEncashDays: fv.encashable ? (fv.maxEncashDays ?? null) : null,
      halfDayAllowed: fv.halfDayAllowed,
      hourlyAllowed: fv.hourlyAllowed,
      gender: fv.gender,
      maxConsecutiveDays: fv.maxConsecutiveDays || null,
      negativeBalanceAllowed: fv.negativeBalanceAllowed,
      negativeBalanceLimit: fv.negativeBalanceAllowed
        ? (fv.negativeBalanceLimit ?? null)
        : null,
    };

    if (lt) {
      this.leaveTypeService
        .updateLeaveType(lt.leaveTypeId, payload)
        .subscribe({
          next: () => {
            this.isSaving.set(false);
            this.toastr.success(`"${payload.name}" updated successfully.`);
            this.saved.emit();
          },
          error: (err: HttpErrorResponse) => {
            this.isSaving.set(false);
            this.handleError(err);
          },
        });
    } else {
      this.leaveTypeService.createLeaveType(payload).subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toastr.success(`"${payload.name}" created successfully.`);
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
    const body = LeaveTypeService.parseError(err);

    if (body?.code === 'duplicate_name') {
      this.duplicateNameError.set(
        body.message || 'A leave type with this name already exists.'
      );
    } else {
      this.toastr.error(body?.message || 'Failed to save leave type.');
    }
  }
}
