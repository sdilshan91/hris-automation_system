import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { PayrollService } from '../../services/payroll.service';
import {
  ISalaryComponent,
  ISalaryComponentRequest,
  SalaryComponentType,
  CalculationMethod,
  COMPONENT_TYPE_OPTIONS,
  CALCULATION_METHOD_OPTIONS,
  CALCULATION_METHOD_LABELS,
  IFormulaTestResult,
} from '../../models/payroll.models';

/**
 * US-PAY-001: Create / edit a salary component, rendered as a Notion-style right
 * slide-over drawer (full-screen on mobile, §8). Reactive form with cross-field
 * validation: the formula textarea + Test button (FR-4) only show when
 * calculationMethod === 'Formula'; defaultValue is required for the non-formula
 * methods. Tenant primary color (var(--brand-primary)) on the primary action.
 *
 * On success emits `saved` with the persisted component; the parent table reloads.
 */
@Component({
  selector: 'app-component-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <!-- Backdrop -->
    <div
      @backdrop
      class="fixed inset-0 z-40 bg-neutral-900/30"
      (click)="close.emit()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap: backdrop clicks pass through, panel stays interactive -->
    <div class="fixed inset-0 z-50 flex justify-end pointer-events-none">
      <section
        @drawer
        class="pointer-events-auto flex h-full w-full sm:max-w-lg flex-col bg-neutral-50 shadow-2xl"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="isEdit() ? 'Edit salary component' : 'Create salary component'"
      >
        <!-- Header -->
        <header
          class="sticky top-0 z-10 flex items-center justify-between border-b border-neutral-200 bg-white px-5 py-4"
        >
          <h2 class="text-lg font-semibold tracking-tight text-neutral-900">
            {{ isEdit() ? 'Edit component' : 'New component' }}
          </h2>
          <button
            type="button"
            class="rounded-lg p-2 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            aria-label="Close"
            (click)="close.emit()"
          >
            &#10005;
          </button>
        </header>

        <!-- Body -->
        <form [formGroup]="form" class="flex-1 overflow-y-auto px-5 py-5 space-y-6">
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label class="form-label" for="name">Name *</label>
              <input
                id="name"
                type="text"
                formControlName="name"
                maxlength="100"
                class="form-input"
                placeholder="e.g. Basic Salary"
              />
              @if (showError('name')) {
                <p class="form-error">Name is required.</p>
              }
            </div>
            <div>
              <label class="form-label" for="code">Code *</label>
              <input
                id="code"
                type="text"
                formControlName="code"
                maxlength="50"
                class="form-input uppercase"
                placeholder="e.g. BASIC"
              />
              @if (showError('code')) {
                <p class="form-error">Code is required (unique per tenant).</p>
              }
            </div>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label class="form-label" for="type">Type *</label>
              <select id="type" formControlName="type" class="form-input">
                @for (t of typeOptions; track t) {
                  <option [ngValue]="t">{{ t }}</option>
                }
              </select>
            </div>
            <div>
              <label class="form-label" for="calculationMethod">
                Calculation method *
              </label>
              <select
                id="calculationMethod"
                formControlName="calculationMethod"
                class="form-input"
              >
                @for (m of methodOptions; track m) {
                  <option [ngValue]="m">{{ methodLabels[m] }}</option>
                }
              </select>
            </div>
          </div>

          <!-- Default value (hidden for Formula) -->
          @if (!isFormula()) {
            <div>
              <label class="form-label" for="defaultValue">
                {{ isPercentage() ? 'Default percentage *' : 'Default amount *' }}
              </label>
              <div class="relative">
                <input
                  id="defaultValue"
                  type="number"
                  step="0.01"
                  min="0"
                  formControlName="defaultValue"
                  class="form-input"
                  [class.pr-8]="isPercentage()"
                  placeholder="0.00"
                />
                @if (isPercentage()) {
                  <span
                    class="pointer-events-none absolute inset-y-0 right-3 flex items-center text-sm text-neutral-400"
                    >%</span
                  >
                }
              </div>
              @if (showError('defaultValue')) {
                <p class="form-error">A value is required for this method.</p>
              }
            </div>
          }

          <!-- Formula editor + Test (FR-4) -->
          @if (isFormula()) {
            <div class="space-y-3">
              <div>
                <label class="form-label" for="formulaExpression">
                  Formula *
                </label>
                <textarea
                  id="formulaExpression"
                  formControlName="formulaExpression"
                  rows="3"
                  class="form-input font-mono text-xs"
                  placeholder="e.g. basic * 0.12"
                  spellcheck="false"
                ></textarea>
                @if (showError('formulaExpression')) {
                  <p class="form-error">A formula expression is required.</p>
                }
                <p class="mt-1 text-xs text-neutral-500">
                  Variables: <code>basic</code>, <code>gross</code>. Operators:
                  <code>+ - * / ( )</code>.
                </p>
              </div>

              <!-- Sample values + Test -->
              <div class="rounded-lg border border-neutral-200 bg-white p-3">
                <p class="mb-2 text-xs font-semibold text-neutral-700">
                  Test against sample values
                </p>
                <div class="grid grid-cols-2 gap-3">
                  <div>
                    <label class="form-label" for="sampleBasic">basic</label>
                    <input
                      id="sampleBasic"
                      type="number"
                      class="form-input"
                      [value]="sampleBasic()"
                      (input)="sampleBasic.set(toNumber($event))"
                    />
                  </div>
                  <div>
                    <label class="form-label" for="sampleGross">gross</label>
                    <input
                      id="sampleGross"
                      type="number"
                      class="form-input"
                      [value]="sampleGross()"
                      (input)="sampleGross.set(toNumber($event))"
                    />
                  </div>
                </div>
                <button
                  type="button"
                  class="btn-secondary mt-3 w-full"
                  [disabled]="testing() || !form.get('formulaExpression')?.value"
                  (click)="testFormula()"
                >
                  {{ testing() ? 'Testing…' : 'Test formula' }}
                </button>
                @if (testResult(); as r) {
                  @if (r.valid) {
                    <p
                      class="mt-2 rounded-md bg-emerald-50 px-3 py-2 text-xs text-emerald-700"
                      role="status"
                    >
                      Result: <strong>{{ r.result }}</strong>
                    </p>
                  } @else {
                    <p
                      class="mt-2 rounded-md bg-rose-50 px-3 py-2 text-xs text-rose-700"
                      role="alert"
                    >
                      {{ r.error || 'Invalid formula.' }}
                    </p>
                  }
                }
              </div>
            </div>
          }

          <!-- Flags -->
          <div class="space-y-3">
            <label class="flex items-center gap-3 text-sm text-neutral-800">
              <input
                type="checkbox"
                formControlName="isTaxable"
                class="h-4 w-4 rounded border-neutral-300"
              />
              Taxable
            </label>
            <label class="flex items-center gap-3 text-sm text-neutral-800">
              <input
                type="checkbox"
                formControlName="isStatutory"
                class="h-4 w-4 rounded border-neutral-300"
              />
              Statutory (e.g. EPF, ETF, PAYE)
            </label>
          </div>
        </form>

        <!-- Footer -->
        <footer
          class="flex items-center justify-end gap-3 border-t border-neutral-200 bg-white px-5 py-4"
        >
          <button type="button" class="btn-secondary" (click)="close.emit()">
            Cancel
          </button>
          <button
            type="button"
            class="btn-primary"
            [style.background-color]="'var(--brand-primary)'"
            [disabled]="saving()"
            (click)="save()"
          >
            {{ saving() ? 'Saving…' : isEdit() ? 'Save changes' : 'Create' }}
          </button>
        </footer>
      </section>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .form-label {
        display: block;
        margin-bottom: 0.25rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #404040;
      }
      .form-input {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #171717;
        transition: border-color 150ms ease, box-shadow 150ms ease;
      }
      .form-input:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 3px #e0e7ff;
      }
      .form-error {
        margin-top: 0.25rem;
        font-size: 0.75rem;
        color: #dc2626;
      }
      .btn-primary,
      .btn-secondary {
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        transition: background-color 150ms ease, color 150ms ease;
      }
      .btn-primary {
        color: #fff;
      }
      .btn-primary:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
      .btn-secondary {
        background: #fff;
        color: #404040;
        border: 1px solid #e5e5e5;
      }
      .btn-secondary:hover:not(:disabled) {
        background: #f5f5f5;
      }
      .btn-secondary:disabled {
        opacity: 0.6;
        cursor: not-allowed;
      }
    `,
  ],
})
export class ComponentFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly payroll = inject(PayrollService);
  private readonly toastr = inject(ToastrService);

  /** When set the drawer is in edit mode; null = create. */
  readonly component = input<ISalaryComponent | null>(null);

  readonly close = output<void>();
  readonly saved = output<ISalaryComponent>();

  readonly typeOptions = COMPONENT_TYPE_OPTIONS;
  readonly methodOptions = CALCULATION_METHOD_OPTIONS;
  readonly methodLabels = CALCULATION_METHOD_LABELS;

  readonly saving = signal(false);
  readonly testing = signal(false);
  readonly testResult = signal<IFormulaTestResult | null>(null);
  readonly sampleBasic = signal(1000);
  readonly sampleGross = signal(1500);

  readonly isEdit = computed(() => this.component() !== null);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    code: ['', [Validators.required, Validators.maxLength(50)]],
    type: ['Earning' as SalaryComponentType, Validators.required],
    calculationMethod: [
      'Fixed' as CalculationMethod,
      Validators.required,
    ],
    defaultValue: [null as number | null],
    formulaExpression: [null as string | null],
    isTaxable: [true],
    isStatutory: [false],
  });

  /** Live signal mirror of calculationMethod so the template reacts. */
  private readonly method = signal<CalculationMethod>('Fixed');
  readonly isFormula = computed(() => this.method() === 'Formula');
  readonly isPercentage = computed(
    () =>
      this.method() === 'PercentageOfBasic' ||
      this.method() === 'PercentageOfGross',
  );

  constructor() {
    // Patch the form when an edit target arrives.
    effect(() => {
      const c = this.component();
      if (c) {
        this.form.patchValue({
          name: c.name,
          code: c.code,
          type: c.type,
          calculationMethod: c.calculationMethod,
          defaultValue: c.defaultValue,
          formulaExpression: c.formulaExpression,
          isTaxable: c.isTaxable,
          isStatutory: c.isStatutory,
        });
        this.method.set(c.calculationMethod);
        this.applyMethodValidators(c.calculationMethod);
      }
    });

    // Swap defaultValue / formula validators when the method changes.
    this.form.controls.calculationMethod.valueChanges.subscribe((m) => {
      this.method.set(m);
      this.testResult.set(null);
      this.applyMethodValidators(m);
    });

    this.applyMethodValidators('Fixed');
  }

  private applyMethodValidators(method: CalculationMethod): void {
    const valueCtrl = this.form.controls.defaultValue;
    const formulaCtrl = this.form.controls.formulaExpression;
    if (method === 'Formula') {
      valueCtrl.clearValidators();
      formulaCtrl.setValidators([Validators.required]);
    } else {
      formulaCtrl.clearValidators();
      valueCtrl.setValidators([Validators.required]);
    }
    valueCtrl.updateValueAndValidity({ emitEvent: false });
    formulaCtrl.updateValueAndValidity({ emitEvent: false });
  }

  showError(controlName: string): boolean {
    const ctrl = this.form.get(controlName);
    return !!ctrl && ctrl.invalid && (ctrl.dirty || ctrl.touched);
  }

  toNumber(event: Event): number {
    const v = Number((event.target as HTMLInputElement).value);
    return Number.isFinite(v) ? v : 0;
  }

  testFormula(): void {
    const expression = this.form.controls.formulaExpression.value;
    if (!expression) {
      return;
    }
    this.testing.set(true);
    this.testResult.set(null);
    this.payroll
      .testFormula({
        expression,
        sampleValues: { basic: this.sampleBasic(), gross: this.sampleGross() },
      })
      .subscribe({
        next: (r) => {
          this.testResult.set(r);
          this.testing.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.testResult.set({
            valid: false,
            error:
              (err.error as { message?: string })?.message ??
              'Could not evaluate the formula.',
          });
          this.testing.set(false);
        },
      });
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const isFormula = raw.calculationMethod === 'Formula';
    const request: ISalaryComponentRequest = {
      name: raw.name.trim(),
      code: raw.code.trim().toUpperCase(),
      type: raw.type,
      calculationMethod: raw.calculationMethod,
      defaultValue: isFormula ? null : raw.defaultValue,
      formulaExpression: isFormula ? raw.formulaExpression : null,
      isTaxable: raw.isTaxable,
      isStatutory: raw.isStatutory,
    };

    this.saving.set(true);
    const editing = this.component();
    const op$ = editing
      ? this.payroll.updateComponent(editing.id, request)
      : this.payroll.createComponent(request);

    op$.subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.toastr.success(
          editing ? 'Component updated.' : 'Component created.',
        );
        this.saved.emit(saved);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const msg =
          (err.error as { message?: string })?.message ??
          'Could not save the component.';
        this.toastr.error(msg);
      },
    });
  }
}
