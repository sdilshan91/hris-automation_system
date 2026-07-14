import {
  Component,
  ChangeDetectionStrategy,
  inject,
  output,
  signal,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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

import { ReconciliationService } from '../../services/reconciliation.service';
import { EmployeeService } from '../../../core-hr/employees/services/employee.service';
import { IEmployee } from '../../../core-hr/employees/models/employee.models';
import {
  ILeaveEncashmentRequest,
  ILeaveEncashmentResult,
  RECON_PAY_MONTHS,
} from '../../models/reconciliation.models';
import { TrappedDialogDirective } from '../../../../shared/directives';

/**
 * US-PAY-010 (AC-3/FR-5): "Trigger leave encashment" right slide-over drawer — same
 * Notion drawer pattern as the salary component-form / adjustment-form (full-screen on
 * mobile). HR selects an employee (typeahead, reused Core HR
 * `EmployeeService.searchActiveEmployees`), enters the eligible day count, an optional
 * leave type, and a target pay period; on submit the encashment is created as an earning
 * adjustment on the NEXT run for that period (the drawer states this up front).
 *
 * Leave-type eligibility (BR-6) is enforced server-side; here the leave type is a free
 * text id (optional — null means an HR override with a manual day count). The result's
 * `periodDeferred` flag is surfaced as a warning when the period was pushed forward.
 */
@Component({
  selector: 'app-leave-encashment-form',
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
        aria-label="Trigger leave encashment"
        appTrappedDialog
        (dismiss)="close.emit()"
      >
        <!-- Header -->
        <header
          class="sticky top-0 z-10 flex items-center justify-between border-b border-neutral-200 bg-white px-5 py-4"
        >
          <div>
            <h2 class="text-lg font-semibold tracking-tight text-neutral-900">
              Leave encashment
            </h2>
            <p class="mt-0.5 text-xs text-neutral-500">
              Added as an earning to the next payroll run.
            </p>
          </div>
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
          <!-- Employee typeahead (FR-5) -->
          <div class="relative">
            <label class="form-label" for="encashEmployeeSearch">Employee *</label>
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
                id="encashEmployeeSearch"
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
            <!-- Eligible days -->
            <div>
              <label class="form-label" for="eligibleDays">Eligible days *</label>
              <input
                id="eligibleDays"
                type="number"
                step="0.5"
                min="0.5"
                formControlName="eligibleDays"
                class="form-input"
                placeholder="e.g. 5"
              />
              @if (showError('eligibleDays')) {
                <p class="form-error">Enter the number of days (greater than zero).</p>
              }
            </div>
            <!-- Leave type (optional) -->
            <div>
              <label class="form-label" for="leaveTypeId">Leave type</label>
              <input
                id="leaveTypeId"
                type="text"
                formControlName="leaveTypeId"
                class="form-input"
                placeholder="Optional"
              />
              <p class="mt-1 text-xs text-neutral-400">
                Leave blank for an HR override.
              </p>
            </div>
          </div>

          <!-- Target period (FR-5) -->
          <div>
            <label class="form-label">Target pay period *</label>
            <div class="grid grid-cols-2 gap-4">
              <select
                formControlName="payMonth"
                class="form-input"
                aria-label="Pay month"
              >
                @for (m of months; track m.value) {
                  <option [ngValue]="m.value">{{ m.label }}</option>
                }
              </select>
              <select
                formControlName="payYear"
                class="form-input"
                aria-label="Pay year"
              >
                @for (y of years; track y) {
                  <option [ngValue]="y">{{ y }}</option>
                }
              </select>
            </div>
          </div>

          <!-- Taxable flag -->
          <label class="flex items-center gap-3 text-sm text-neutral-800">
            <input
              type="checkbox"
              formControlName="isTaxable"
              class="h-4 w-4 rounded border-neutral-300"
            />
            Taxable
          </label>

          <p
            class="rounded-lg border border-neutral-200 bg-white px-3 py-2 text-xs text-neutral-500"
          >
            The encashment is calculated as eligible days × daily basic rate and added as
            an earning to the next available payroll run for the selected period.
          </p>
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
            {{ saving() ? 'Submitting…' : 'Trigger encashment' }}
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
export class LeaveEncashmentFormComponent implements OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly reconciliation = inject(ReconciliationService);
  private readonly employees = inject(EmployeeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<string>();

  readonly close = output<void>();
  readonly saved = output<ILeaveEncashmentResult>();

  readonly months = RECON_PAY_MONTHS;
  readonly years = this.buildYears();

  readonly saving = signal(false);
  readonly searching = signal(false);
  readonly searchTerm = signal('');
  readonly results = signal<IEmployee[]>([]);
  readonly selectedEmployee = signal<IEmployee | null>(null);
  private readonly employeeTouched = signal(false);

  readonly form = this.fb.nonNullable.group({
    eligibleDays: [
      null as number | null,
      [Validators.required, Validators.min(0.5)],
    ],
    leaveTypeId: [''],
    payMonth: [this.defaultMonth(), Validators.required],
    payYear: [this.defaultYear(), Validators.required],
    isTaxable: [true],
  });

  constructor() {
    // Debounced typeahead search — min 2 chars (reuses Core HR directory search).
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

  showError(controlName: string): boolean {
    const ctrl = this.form.get(controlName);
    return !!ctrl && ctrl.invalid && (ctrl.dirty || ctrl.touched);
  }

  // ─── Save ────────────────────────────────────────────────────

  save(): void {
    this.employeeTouched.set(true);
    const employee = this.selectedEmployee();
    if (this.form.invalid || !employee) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const leaveTypeId = raw.leaveTypeId.trim();
    const request: ILeaveEncashmentRequest = {
      employeeId: employee.employeeId,
      eligibleDays: raw.eligibleDays as number,
      leaveTypeId: leaveTypeId.length > 0 ? leaveTypeId : null,
      payMonth: raw.payMonth,
      payYear: raw.payYear,
      isTaxable: raw.isTaxable,
    };

    this.saving.set(true);
    this.reconciliation.triggerLeaveEncashment(request).subscribe({
      next: (result) => {
        this.saving.set(false);
        if (result.periodDeferred) {
          this.toastr.warning(
            'Encashment was deferred to a later run — the requested period is locked.',
          );
        } else {
          this.toastr.success('Leave encashment added to the next run.');
        }
        this.saved.emit(result);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        const msg =
          (err.error as { message?: string })?.message ??
          'Could not trigger the leave encashment.';
        this.toastr.error(msg);
      },
    });
  }

  // ─── Defaults ────────────────────────────────────────────────

  private defaultMonth(): number {
    return new Date().getMonth() + 1;
  }

  private defaultYear(): number {
    return new Date().getFullYear();
  }

  /** Year options: previous year through two years out. */
  private buildYears(): number[] {
    const current = new Date().getFullYear();
    const out: number[] = [];
    for (let y = current - 1; y <= current + 2; y++) {
      out.push(y);
    }
    return out;
  }
}
