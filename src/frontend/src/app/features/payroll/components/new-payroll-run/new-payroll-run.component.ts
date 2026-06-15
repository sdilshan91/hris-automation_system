import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  output,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { PayrollRunService } from '../../services/payroll-run.service';
import {
  IPayrollRun,
  IPayrollRunValidation,
  PAY_MONTHS,
} from '../../models/payroll-run.models';

/**
 * US-PAY-003 (§8): "New Payroll Run" modal — Pay Month + Pay Year dropdowns and a
 * pre-run validation summary ("247 employees ready, 3 missing salary structure")
 * fetched from the backend. Submit initiates the run (AC-1) with an Idempotency-Key
 * header (FR-9) and emits the created run so the parent can route to its detail view.
 *
 * Right slide-over drawer (same pattern as component-form / salary assignment):
 * fixed wrap `justify-end pointer-events-none` + panel `pointer-events-auto`,
 * `@drawer` translateX, separate `@backdrop`. The validation summary refetches
 * whenever month/year changes so Submit reflects the chosen period. Submit is
 * disabled while validating, when validation says `canRun` is false (e.g. period
 * already finalized — AC-4), or while a run is being initiated.
 */
@Component({
  selector: 'app-new-payroll-run',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('180ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate(
          '240ms cubic-bezier(0.16, 1, 0.3, 1)',
          style({ transform: 'translateX(0)' }),
        ),
      ]),
      transition(':leave', [
        animate('180ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  template: `
    <div class="fixed inset-0 z-50 flex justify-end">
      <!-- Backdrop -->
      <div
        @backdrop
        class="absolute inset-0 bg-neutral-900/30"
        aria-hidden="true"
        (click)="close.emit()"
      ></div>

      <!-- Panel -->
      <div
        @drawer
        class="relative flex h-full w-full max-w-md flex-col bg-white shadow-2xl"
        role="dialog"
        aria-modal="true"
        aria-labelledby="new-run-title"
      >
        <!-- Header -->
        <div
          class="flex items-start justify-between border-b border-neutral-200 px-6 py-4"
        >
          <div>
            <h2
              id="new-run-title"
              class="text-lg font-semibold text-neutral-900"
            >
              New payroll run
            </h2>
            <p class="mt-0.5 text-sm text-neutral-500">
              Pick a pay period to validate and run.
            </p>
          </div>
          <button
            type="button"
            class="rounded-lg p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            aria-label="Close"
            (click)="close.emit()"
          >
            <svg
              class="h-5 w-5"
              viewBox="0 0 20 20"
              fill="currentColor"
              aria-hidden="true"
            >
              <path
                d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"
              />
            </svg>
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-6 py-5">
          <div class="grid grid-cols-2 gap-4">
            <label class="block">
              <span class="mb-1 block text-sm font-medium text-neutral-700"
                >Pay month</span
              >
              <select
                class="w-full rounded-lg border border-neutral-300 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
                aria-label="Pay month"
                [ngModel]="payMonth()"
                (ngModelChange)="onMonthChange($event)"
              >
                @for (m of months; track m.value) {
                  <option [ngValue]="m.value">{{ m.label }}</option>
                }
              </select>
            </label>

            <label class="block">
              <span class="mb-1 block text-sm font-medium text-neutral-700"
                >Pay year</span
              >
              <select
                class="w-full rounded-lg border border-neutral-300 bg-white px-3 py-2 text-sm text-neutral-900 focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
                aria-label="Pay year"
                [ngModel]="payYear()"
                (ngModelChange)="onYearChange($event)"
              >
                @for (y of years; track y) {
                  <option [ngValue]="y">{{ y }}</option>
                }
              </select>
            </label>
          </div>

          <!-- Pre-run validation summary (§8) -->
          <div class="mt-6">
            <h3
              class="mb-2 text-xs font-medium uppercase tracking-wide text-neutral-500"
            >
              Pre-run check
            </h3>

            @if (validating()) {
              <div class="space-y-2">
                <div class="h-16 animate-pulse rounded-lg bg-neutral-100"></div>
                <div class="h-10 animate-pulse rounded-lg bg-neutral-100"></div>
              </div>
            } @else if (validationError()) {
              <div
                class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
                role="alert"
              >
                Could not validate this period.
                <button
                  type="button"
                  class="ml-1 font-medium underline"
                  (click)="validate()"
                >
                  Retry
                </button>
              </div>
            } @else if (validation(); as v) {
              <div
                class="rounded-xl border border-neutral-200 bg-neutral-50 p-4"
              >
                <p class="text-sm text-neutral-700">
                  <strong class="text-emerald-700">{{ v.readyEmployees }}</strong>
                  {{ v.readyEmployees === 1 ? 'employee' : 'employees' }} ready
                  @if (v.missingSalaryStructure > 0) {
                    ,
                    <strong class="text-amber-700">{{
                      v.missingSalaryStructure
                    }}</strong>
                    missing salary structure
                  }
                </p>
                <p class="mt-1 text-xs text-neutral-500">
                  {{ v.totalEmployees }} active
                  {{ v.totalEmployees === 1 ? 'employee' : 'employees' }} in this
                  period.
                  @if (v.missingSalaryStructure > 0) {
                    Employees without a salary structure are skipped (AC-6).
                  }
                </p>

                @if (!v.canRun && v.blockers.length) {
                  <ul
                    class="mt-3 space-y-1 border-t border-neutral-200 pt-3 text-sm text-rose-700"
                    role="alert"
                  >
                    @for (b of v.blockers; track b) {
                      <li class="flex items-start gap-1.5">
                        <span aria-hidden="true">•</span>
                        <span>{{ b }}</span>
                      </li>
                    }
                  </ul>
                }
              </div>
            }
          </div>
        </div>

        <!-- Footer -->
        <div
          class="flex items-center justify-end gap-3 border-t border-neutral-200 px-6 py-4"
        >
          <button
            type="button"
            class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
            (click)="close.emit()"
          >
            Cancel
          </button>
          <button
            type="button"
            class="inline-flex items-center gap-2 rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
            [style.background-color]="'var(--brand-primary)'"
            [disabled]="!canSubmit()"
            (click)="submit()"
          >
            @if (submitting()) {
              <span
                class="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"
                aria-hidden="true"
              ></span>
              Starting…
            } @else {
              Run payroll
            }
          </button>
        </div>
      </div>
    </div>
  `,
})
export class NewPayrollRunComponent implements OnInit {
  private readonly runs = inject(PayrollRunService);
  private readonly toastr = inject(ToastrService);

  /** Emits the created run (status Queued) so the parent can open its detail view. */
  readonly created = output<IPayrollRun>();
  readonly close = output<void>();

  readonly months = PAY_MONTHS;
  readonly years = this.buildYears();

  readonly payMonth = signal(this.defaultMonth());
  readonly payYear = signal(new Date().getFullYear());

  readonly validation = signal<IPayrollRunValidation | null>(null);
  readonly validating = signal(false);
  readonly validationError = signal(false);
  readonly submitting = signal(false);

  /** Submit is allowed only once a clean validation says the period can run. */
  readonly canSubmit = computed(
    () =>
      !this.validating() &&
      !this.submitting() &&
      !this.validationError() &&
      this.validation()?.canRun === true,
  );

  ngOnInit(): void {
    this.validate();
  }

  onMonthChange(value: number): void {
    this.payMonth.set(value);
    this.validate();
  }

  onYearChange(value: number): void {
    this.payYear.set(value);
    this.validate();
  }

  /** Fetch the pre-run validation summary for the chosen period (§8). */
  validate(): void {
    this.validating.set(true);
    this.validationError.set(false);
    this.runs
      .validateRun({ payMonth: this.payMonth(), payYear: this.payYear() })
      .subscribe({
        next: (v) => {
          this.validation.set(v);
          this.validating.set(false);
        },
        error: () => {
          this.validation.set(null);
          this.validationError.set(true);
          this.validating.set(false);
        },
      });
  }

  /** Initiate the run (AC-1) with a fresh Idempotency-Key (FR-9). */
  submit(): void {
    if (!this.canSubmit()) {
      return;
    }
    this.submitting.set(true);
    const key = this.newIdempotencyKey();
    this.runs
      .initiateRun(
        { payMonth: this.payMonth(), payYear: this.payYear() },
        key,
      )
      .subscribe({
        next: (run) => {
          this.submitting.set(false);
          this.toastr.success('Payroll run started.');
          this.created.emit(run);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          // AC-4: a finalized period for the same month returns 409 Conflict.
          if (err.status === 409) {
            this.toastr.error(
              'A payroll run for this period is already finalized.',
            );
            this.validate();
          } else {
            this.toastr.error('Could not start the payroll run.');
          }
        },
      });
  }

  // ─── Helpers ───────────────────────────────────────────────

  private newIdempotencyKey(): string {
    const c = globalThis.crypto as Crypto | undefined;
    if (c && typeof c.randomUUID === 'function') {
      return c.randomUUID();
    }
    // Fallback for environments without crypto.randomUUID (older test runners).
    return `run-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  }

  /** Default the period to the previous month (payroll usually runs in arrears). */
  private defaultMonth(): number {
    const now = new Date();
    const m = now.getMonth(); // 0-based; previous month
    return m === 0 ? 12 : m;
  }

  private buildYears(): number[] {
    const current = new Date().getFullYear();
    return [current - 1, current, current + 1];
  }
}
