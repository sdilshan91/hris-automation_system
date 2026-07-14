import {
  Component,
  ChangeDetectionStrategy,
  inject,
  output,
  signal,
  computed,
  OnDestroy,
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
import { Subject } from 'rxjs';
import {
  debounceTime,
  distinctUntilChanged,
  switchMap,
  takeUntil,
} from 'rxjs/operators';
import { AdjustmentService } from '../../services/adjustment.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { IEmployee } from '../../../core-hr/employees/models/employee.models';
import {
  IAdjustment,
  IAdjustmentRequest,
  AdjustmentType,
  ADJUSTMENT_TYPE_OPTIONS,
  ADJUSTMENT_TYPE_LABELS,
  MONTH_LABELS,
  periodLabel,
  recurringPeriods,
} from '../../models/adjustment.models';
import { TrappedDialogDirective } from '../../../../shared/directives';

/** Allowed supporting-document MIME types (NFR-5). */
const ALLOWED_DOC_TYPES = ['application/pdf', 'image/jpeg', 'image/png'];
/** Max supporting-document size in bytes (NFR-5: 5MB). */
const MAX_DOC_BYTES = 5 * 1024 * 1024;

/**
 * US-PAY-007 (§8, FR-1): "New Adjustment" right slide-over drawer — same Notion
 * drawer pattern as the salary component-form (full-screen on mobile). Reactive
 * form with: employee typeahead select, type dropdown (Bonus|Deduction|
 * Reimbursement|Correction), amount, period selector (month/year), description,
 * is_taxable + is_recurring toggles (recurring reveals a recurrence-end period and
 * a preview of every affected future period, BR-6), and an optional
 * supporting-document upload validated client-side for type + size (NFR-5).
 *
 * On save it creates the adjustment, then (if a document was attached) uploads it
 * to `/:id/document` (AC-3) and emits `saved`; the parent table reloads.
 */
@Component({
  selector: 'app-adjustment-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, TrappedDialogDirective],
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
        aria-label="New payroll adjustment"
        appTrappedDialog
        (dismiss)="close.emit()"
      >
        <!-- Header -->
        <header
          class="sticky top-0 z-10 flex items-center justify-between border-b border-neutral-200 bg-white px-5 py-4"
        >
          <h2 class="text-lg font-semibold tracking-tight text-neutral-900">
            New adjustment
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
          <!-- Employee typeahead (FR-1, §8) -->
          <div class="relative">
            <label class="form-label" for="employeeSearch">Employee *</label>
            @if (selectedEmployee(); as emp) {
              <div
                class="flex items-center justify-between rounded-lg border border-neutral-200 bg-white px-3 py-2"
              >
                <div>
                  <p class="text-sm font-medium text-neutral-900">
                    {{ emp.firstName }} {{ emp.lastName }}
                  </p>
                  <p class="text-xs text-neutral-400">{{ emp.employeeNo }}</p>
                </div>
                <button
                  type="button"
                  class="text-xs text-neutral-500 hover:text-neutral-800"
                  (click)="clearEmployee()"
                >
                  Change
                </button>
              </div>
            } @else {
              <input
                id="employeeSearch"
                type="text"
                class="form-input"
                placeholder="Search by name or employee no…"
                autocomplete="off"
                [value]="searchTerm()"
                (input)="onSearch($event)"
                role="combobox"
                aria-autocomplete="list"
                [attr.aria-expanded]="results().length > 0"
              />
              @if (searching()) {
                <p class="mt-1 text-xs text-neutral-400">Searching…</p>
              }
              @if (results().length > 0) {
                <ul
                  class="absolute z-20 mt-1 max-h-56 w-full overflow-auto rounded-lg border border-neutral-200 bg-white shadow-lg"
                  role="listbox"
                >
                  @for (emp of results(); track emp.employeeId) {
                    <li
                      class="cursor-pointer px-3 py-2 text-sm hover:bg-neutral-50"
                      role="option"
                      [attr.aria-selected]="false"
                      (click)="selectEmployee(emp)"
                    >
                      <span class="font-medium text-neutral-900"
                        >{{ emp.firstName }} {{ emp.lastName }}</span
                      >
                      <span class="ml-2 text-xs text-neutral-400">{{ emp.employeeNo }}</span>
                    </li>
                  }
                </ul>
              }
              @if (showEmployeeError()) {
                <p class="form-error">Select an employee.</p>
              }
            }
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <!-- Type -->
            <div>
              <label class="form-label" for="type">Type *</label>
              <select id="type" formControlName="type" class="form-input">
                @for (t of typeOptions; track t) {
                  <option [ngValue]="t">{{ typeLabels[t] }}</option>
                }
              </select>
            </div>
            <!-- Amount -->
            <div>
              <label class="form-label" for="amount">Amount *</label>
              <input
                id="amount"
                type="number"
                step="0.01"
                min="0.01"
                formControlName="amount"
                class="form-input"
                placeholder="0.00"
              />
              @if (showError('amount')) {
                <p class="form-error">Enter an amount greater than zero.</p>
              }
            </div>
          </div>

          <!-- Period selector (FR-1) -->
          <div>
            <label class="form-label">Applicable period *</label>
            <div class="grid grid-cols-2 gap-4">
              <select
                formControlName="applicablePayMonth"
                class="form-input"
                aria-label="Applicable month"
              >
                @for (m of months; track m.value) {
                  <option [ngValue]="m.value">{{ m.label }}</option>
                }
              </select>
              <select
                formControlName="applicablePayYear"
                class="form-input"
                aria-label="Applicable year"
              >
                @for (y of years; track y) {
                  <option [ngValue]="y">{{ y }}</option>
                }
              </select>
            </div>
          </div>

          <!-- Description (FR-1) -->
          <div>
            <label class="form-label" for="description">Description / reason *</label>
            <textarea
              id="description"
              formControlName="description"
              rows="2"
              maxlength="500"
              class="form-input"
              placeholder="e.g. Q2 performance bonus"
            ></textarea>
            @if (showError('description')) {
              <p class="form-error">A description is required.</p>
            }
          </div>

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
                formControlName="isRecurring"
                class="h-4 w-4 rounded border-neutral-300"
              />
              Recurring
            </label>
          </div>

          <!-- Recurrence end + preview (BR-6) -->
          @if (isRecurring()) {
            <div class="rounded-lg border border-neutral-200 bg-white p-4 space-y-3">
              <label class="form-label">Repeat until *</label>
              <div class="grid grid-cols-2 gap-4">
                <select
                  formControlName="recurrenceEndMonth"
                  class="form-input"
                  aria-label="Recurrence end month"
                >
                  @for (m of months; track m.value) {
                    <option [ngValue]="m.value">{{ m.label }}</option>
                  }
                </select>
                <select
                  formControlName="recurrenceEndYear"
                  class="form-input"
                  aria-label="Recurrence end year"
                >
                  @for (y of years; track y) {
                    <option [ngValue]="y">{{ y }}</option>
                  }
                </select>
              </div>

              @if (recurringPreview().length > 0) {
                <div>
                  <p class="text-xs font-semibold text-neutral-700">
                    Affects {{ recurringPreview().length }} period(s):
                  </p>
                  <div class="mt-2 flex flex-wrap gap-1.5">
                    @for (p of recurringPreview(); track p.year * 12 + p.month) {
                      <span
                        class="inline-flex items-center rounded-md bg-neutral-100 px-2 py-0.5 text-xs text-neutral-600"
                        >{{ periodFor(p.month, p.year) }}</span
                      >
                    }
                  </div>
                </div>
              } @else {
                <p class="text-xs text-rose-600" role="alert">
                  The end period must be on or after the applicable period.
                </p>
              }
            </div>
          }

          <!-- Supporting document upload (AC-3, NFR-5) -->
          <div>
            <label class="form-label" for="document">Supporting document</label>
            <input
              id="document"
              type="file"
              accept=".pdf,.jpg,.jpeg,.png"
              class="block w-full text-sm text-neutral-600 file:mr-3 file:rounded-lg file:border-0 file:bg-neutral-100 file:px-3 file:py-2 file:text-sm file:font-medium file:text-neutral-700 hover:file:bg-neutral-200"
              (change)="onFileSelected($event)"
            />
            <p class="mt-1 text-xs text-neutral-400">PDF, JPG, or PNG up to 5MB.</p>
            @if (documentFile(); as f) {
              <p class="mt-1 text-xs text-emerald-700" role="status">
                {{ f.name }} ({{ fileSizeKb(f) }} KB)
              </p>
            }
            @if (documentError()) {
              <p class="form-error" role="alert">{{ documentError() }}</p>
            }
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
            {{ saving() ? 'Saving…' : 'Create adjustment' }}
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
export class AdjustmentFormComponent implements OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly adjustments = inject(AdjustmentService);
  private readonly employees = inject(EmployeeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<string>();

  readonly close = output<void>();
  readonly saved = output<IAdjustment>();

  readonly typeOptions = ADJUSTMENT_TYPE_OPTIONS;
  readonly typeLabels = ADJUSTMENT_TYPE_LABELS;
  readonly months = MONTH_LABELS.map((label, i) => ({ value: i + 1, label }));
  readonly years = this.buildYears();

  readonly saving = signal(false);
  readonly searching = signal(false);
  readonly searchTerm = signal('');
  readonly results = signal<IEmployee[]>([]);
  readonly selectedEmployee = signal<IEmployee | null>(null);
  readonly documentFile = signal<File | null>(null);
  readonly documentError = signal<string | null>(null);
  private readonly employeeTouched = signal(false);

  /** Live signal mirror of is_recurring so the template + preview react. */
  private readonly recurring = signal(false);
  readonly isRecurring = computed(() => this.recurring());

  /** Live mirrors of the four period controls so the preview recomputes reactively. */
  private readonly startMonth = signal(this.defaultMonth());
  private readonly startYear = signal(this.defaultYear());
  private readonly endMonth = signal(this.defaultMonth());
  private readonly endYear = signal(this.defaultYear());

  /** BR-6: every future period a recurring adjustment will affect. */
  readonly recurringPreview = computed(() =>
    this.recurring()
      ? recurringPeriods(
          this.startMonth(),
          this.startYear(),
          this.endMonth(),
          this.endYear(),
        )
      : [],
  );

  readonly form = this.fb.nonNullable.group({
    type: ['Bonus' as AdjustmentType, Validators.required],
    amount: [
      null as number | null,
      [Validators.required, Validators.min(0.01)],
    ],
    applicablePayMonth: [this.defaultMonth(), Validators.required],
    applicablePayYear: [this.defaultYear(), Validators.required],
    description: ['', [Validators.required, Validators.maxLength(500)]],
    isTaxable: [false],
    isRecurring: [false],
    recurrenceEndMonth: [this.defaultMonth()],
    recurrenceEndYear: [this.defaultYear()],
  });

  constructor() {
    // Debounced typeahead search (FR-1, §8) — min 2 chars.
    this.search$
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((term) => {
          this.searching.set(true);
          return this.employees.searchActiveEmployees(term, 8);
        }),
        takeUntil(this.destroy$),
      )
      .subscribe({
        next: (page) => {
          this.results.set(page.items ?? []);
          this.searching.set(false);
        },
        error: () => {
          this.results.set([]);
          this.searching.set(false);
        },
      });

    // Mirror reactive form controls into signals for the recurrence preview.
    this.form.controls.isRecurring.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((v) => this.recurring.set(!!v));
    this.form.controls.applicablePayMonth.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((v) => this.startMonth.set(v));
    this.form.controls.applicablePayYear.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((v) => this.startYear.set(v));
    this.form.controls.recurrenceEndMonth.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((v) => this.endMonth.set(v));
    this.form.controls.recurrenceEndYear.valueChanges
      .pipe(takeUntil(this.destroy$))
      .subscribe((v) => this.endYear.set(v));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Employee typeahead ──────────────────────────────────────

  onSearch(event: Event): void {
    const term = (event.target as HTMLInputElement).value;
    this.searchTerm.set(term);
    if (term.trim().length < 2) {
      this.results.set([]);
      return;
    }
    this.search$.next(term.trim());
  }

  selectEmployee(emp: IEmployee): void {
    this.selectedEmployee.set(emp);
    this.results.set([]);
    this.searchTerm.set('');
    this.employeeTouched.set(true);
  }

  clearEmployee(): void {
    this.selectedEmployee.set(null);
    this.employeeTouched.set(true);
  }

  showEmployeeError(): boolean {
    return this.employeeTouched() && this.selectedEmployee() === null;
  }

  // ─── Supporting document (NFR-5) ─────────────────────────────

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.documentError.set(null);
    if (!file) {
      this.documentFile.set(null);
      return;
    }
    if (!ALLOWED_DOC_TYPES.includes(file.type)) {
      this.documentFile.set(null);
      this.documentError.set('Only PDF, JPG, or PNG files are allowed.');
      input.value = '';
      return;
    }
    if (file.size > MAX_DOC_BYTES) {
      this.documentFile.set(null);
      this.documentError.set('File exceeds the 5MB limit.');
      input.value = '';
      return;
    }
    this.documentFile.set(file);
  }

  fileSizeKb(file: File): number {
    return Math.round(file.size / 1024);
  }

  // ─── Save ────────────────────────────────────────────────────

  save(): void {
    this.employeeTouched.set(true);
    const employee = this.selectedEmployee();
    if (this.form.invalid || !employee) {
      this.form.markAllAsTouched();
      return;
    }
    // A recurring adjustment needs a valid (non-empty) period range (BR-6).
    if (this.form.controls.isRecurring.value && this.recurringPreview().length === 0) {
      this.toastr.error('Fix the recurrence end period.');
      return;
    }

    const raw = this.form.getRawValue();
    const recurring = raw.isRecurring;
    const request: IAdjustmentRequest = {
      employeeId: employee.employeeId,
      type: raw.type,
      amount: raw.amount as number,
      description: raw.description.trim(),
      applicablePayMonth: raw.applicablePayMonth,
      applicablePayYear: raw.applicablePayYear,
      isTaxable: raw.isTaxable,
      isRecurring: recurring,
      recurrenceEndMonth: recurring ? raw.recurrenceEndMonth : null,
      recurrenceEndYear: recurring ? raw.recurrenceEndYear : null,
    };

    this.saving.set(true);
    this.adjustments.createAdjustment(request).subscribe({
      next: (created) => {
        const file = this.documentFile();
        if (file) {
          this.adjustments.uploadDocument(created.id, file).subscribe({
            next: (withDoc) => this.finish(withDoc),
            error: () => {
              // The record exists; only the document failed (AC-3 best-effort).
              this.saving.set(false);
              this.toastr.warning(
                'Adjustment created, but the document upload failed.',
              );
              this.saved.emit(created);
            },
          });
        } else {
          this.finish(created);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const msg =
          (err.error as { message?: string })?.message ??
          'Could not create the adjustment.';
        this.toastr.error(msg);
      },
    });
  }

  private finish(adjustment: IAdjustment): void {
    this.saving.set(false);
    this.toastr.success('Adjustment created.');
    this.saved.emit(adjustment);
  }

  showError(controlName: string): boolean {
    const ctrl = this.form.get(controlName);
    return !!ctrl && ctrl.invalid && (ctrl.dirty || ctrl.touched);
  }

  periodFor(month: number, year: number): string {
    return periodLabel(month, year);
  }

  // ─── Defaults ────────────────────────────────────────────────

  private defaultMonth(): number {
    return new Date().getMonth() + 1;
  }

  private defaultYear(): number {
    return new Date().getFullYear();
  }

  /** Year options: previous year through three years out (covers arrears + future recurrence). */
  private buildYears(): number[] {
    const current = new Date().getFullYear();
    const out: number[] = [];
    for (let y = current - 1; y <= current + 3; y++) {
      out.push(y);
    }
    return out;
  }
}
