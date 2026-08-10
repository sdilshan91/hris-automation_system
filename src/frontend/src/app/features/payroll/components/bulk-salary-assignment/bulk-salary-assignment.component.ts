import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { PayrollService } from '../../services/payroll.service';
import { EmployeeSalaryService } from '../../services/employee-salary.service';
import { ISalaryStructure } from '../../models/payroll.models';
import {
  IBulkAssignmentRequest,
  IBulkAssignmentResult,
  IBulkAssignmentRow,
  IBulkAssignmentItemResult,
} from '../../models/employee-salary.models';

/** A row in the spreadsheet-like bulk grid (FR-5, §8). */
interface IBulkGridRow {
  employeeId: string;
  annualCtc: number | null;
  /** Per-row status reflected after submit, drives the inline result chip. */
  status: 'pending' | 'success' | 'failed';
  error?: string | null;
}

/**
 * US-PAY-002 (FR-5, AC-4): Bulk salary-structure assignment. A spreadsheet-like
 * grid where HR picks one structure + effective date, then pastes employee ids
 * and CTC values (Notion table view, §8). Submitting runs the bulk assign and a
 * progress indicator shows completion with per-row success/failure.
 *
 * Desktop-only (§8): a clear message replaces the grid on small screens.
 */
@Component({
  selector: 'app-bulk-salary-assignment',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <div class="flex items-center gap-3 mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Bulk Salary Assignment</h1>
      </div>

      <!-- Mobile: not available (§8) -->
      <div class="md:hidden card-notion text-center py-12" @fadeIn>
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
          class="w-10 h-10 mx-auto text-neutral-300 mb-3" aria-hidden="true">
          <path fill-rule="evenodd" d="M2.25 5.25a3 3 0 0 1 3-3h13.5a3 3 0 0 1 3 3V15a3 3 0 0 1-3 3h-3v.257c0 .597.237 1.17.659 1.591l.621.622a.75.75 0 0 1-.53 1.28h-7.5a.75.75 0 0 1-.53-1.28l.621-.622a2.25 2.25 0 0 0 .659-1.59V18h-3a3 3 0 0 1-3-3V5.25Zm1.5 0v7.5a1.5 1.5 0 0 0 1.5 1.5h13.5a1.5 1.5 0 0 0 1.5-1.5v-7.5a1.5 1.5 0 0 0-1.5-1.5H5.25a1.5 1.5 0 0 0-1.5 1.5Z" clip-rule="evenodd"/>
        </svg>
        <h3 class="text-base font-semibold text-neutral-900 mb-1">Desktop only</h3>
        <p class="text-sm text-neutral-500 px-6">
          Bulk salary assignment uses a spreadsheet-like grid that needs a larger screen.
          Please open this page on a desktop.
        </p>
      </div>

      <!-- Desktop grid -->
      <div class="hidden md:block space-y-5" @fadeIn>
        <!-- Structure + effective date -->
        <form [formGroup]="setupForm" class="card-notion">
          <div class="grid grid-cols-2 gap-4">
            <div>
              <label class="label-notion" for="bulk-structure">Salary Structure</label>
              <select id="bulk-structure" formControlName="salaryStructureId" class="input-notion">
                <option value="">Select a structure…</option>
                @for (s of activeStructures(); track s.id) {
                  <option [value]="s.id">{{ s.name }}</option>
                }
              </select>
            </div>
            <div>
              <label class="label-notion" for="bulk-eff">Effective From</label>
              <input id="bulk-eff" type="date" formControlName="effectiveFrom" class="input-notion" />
            </div>
          </div>
        </form>

        <!-- Paste helper + grid -->
        <div class="card-notion">
          <div class="flex items-center justify-between mb-3">
            <div>
              <h3 class="text-sm font-semibold text-neutral-900">Employees</h3>
              <p class="text-xs text-neutral-400">
                Paste two columns (employee id, annual CTC) from a spreadsheet, or add rows manually.
              </p>
            </div>
            <button type="button" class="btn-secondary !py-2" (click)="addRow()">+ Add row</button>
          </div>

          <!-- Paste area -->
          <textarea
            class="input-notion mb-3 font-mono text-xs"
            rows="3"
            [value]="''"
            placeholder="e.g.  emp-001	600000"
            aria-label="Paste employee id and CTC rows"
            (paste)="onPaste($event)"
          ></textarea>

          <!-- Grid -->
          <div class="rounded-lg border border-neutral-100 overflow-hidden">
            <table class="w-full text-sm">
              <thead class="bg-neutral-50 text-neutral-500">
                <tr>
                  <th class="text-left font-medium px-4 py-2.5 w-10">#</th>
                  <th class="text-left font-medium px-4 py-2.5">Employee Id</th>
                  <th class="text-right font-medium px-4 py-2.5 w-48">Annual CTC</th>
                  <th class="text-center font-medium px-4 py-2.5 w-28">Status</th>
                  <th class="px-2 py-2.5 w-10"></th>
                </tr>
              </thead>
              <tbody class="divide-y divide-neutral-100">
                @for (row of rows(); track $index; let i = $index) {
                  <tr>
                    <td class="px-4 py-2 text-neutral-400">{{ i + 1 }}</td>
                    <td class="px-4 py-2">
                      <input
                        type="text"
                        class="input-notion !py-1.5"
                        [value]="row.employeeId"
                        [attr.aria-label]="'Employee id row ' + (i + 1)"
                        (input)="updateRow(i, 'employeeId', $any($event.target).value)"
                      />
                    </td>
                    <td class="px-4 py-2">
                      <input
                        type="number" min="0" step="0.01"
                        class="input-notion !py-1.5 text-right"
                        [value]="row.annualCtc"
                        [attr.aria-label]="'Annual CTC row ' + (i + 1)"
                        (input)="updateRow(i, 'annualCtc', $any($event.target).value)"
                      />
                    </td>
                    <td class="px-4 py-2 text-center">
                      @switch (row.status) {
                        @case ('success') {
                          <span class="badge bg-emerald-50 text-emerald-700 ring-emerald-600/20">Assigned</span>
                        }
                        @case ('failed') {
                          <span class="badge bg-rose-50 text-rose-700 ring-rose-600/20" [title]="row.error || ''">Failed</span>
                        }
                        @default {
                          <span class="text-xs text-neutral-300">—</span>
                        }
                      }
                    </td>
                    <td class="px-2 py-2 text-center">
                      <button type="button" class="text-neutral-400 hover:text-rose-500" (click)="removeRow(i)" [attr.aria-label]="'Remove row ' + (i + 1)">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                          <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                        </svg>
                      </button>
                    </td>
                  </tr>
                }
                @if (rows().length === 0) {
                  <tr>
                    <td colspan="5" class="px-4 py-8 text-center text-sm text-neutral-400">
                      No rows yet. Paste from a spreadsheet or add a row.
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>

          <!-- Progress indicator (AC-4) -->
          @if (isSubmitting() || result()) {
            <div class="mt-4">
              <div class="flex items-center justify-between text-xs text-neutral-500 mb-1">
                <span>{{ isSubmitting() ? 'Assigning…' : 'Completed' }}</span>
                <span>{{ progressDone() }} / {{ progressTotal() }}</span>
              </div>
              <div class="w-full h-2 rounded-full bg-neutral-100 overflow-hidden">
                <div
                  class="h-full bg-brand-500 transition-all duration-300"
                  role="progressbar"
                  [attr.aria-valuenow]="progressDone()"
                  [attr.aria-valuemin]="0"
                  [attr.aria-valuemax]="progressTotal()"
                  [style.width.%]="progressPercent()"
                ></div>
              </div>
              @if (result()) {
                <p class="text-xs text-neutral-500 mt-2">
                  {{ result()!.successCount }} assigned,
                  {{ result()!.failureCount }} failed.
                </p>
              }
            </div>
          }

          <div class="flex justify-end gap-2 mt-5">
            <button
              type="button"
              class="btn-primary"
              [disabled]="!canSubmit() || isSubmitting()"
              (click)="submit()"
            >
              @if (isSubmitting()) { <span class="btn-spinner"></span> Assigning… }
              @else { Assign {{ validRowCount() }} employee(s) }
            </button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      .badge { @apply inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full ring-1 ring-inset; }
      .btn-spinner {
        @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
        animation: spin 0.6s linear infinite;
      }
      @keyframes spin { to { transform: rotate(360deg); } }
    `,
  ],
})
export class BulkSalaryAssignmentComponent implements OnInit, OnDestroy {
  private readonly payrollService = inject(PayrollService);
  private readonly salaryService = inject(EmployeeSalaryService);
  private readonly toastr = inject(ToastrService);
  private readonly fb = inject(FormBuilder);
  private readonly destroy$ = new Subject<void>();

  readonly activeStructures = signal<ISalaryStructure[]>([]);
  readonly rows = signal<IBulkGridRow[]>([]);
  readonly isSubmitting = signal(false);
  readonly result = signal<IBulkAssignmentResult | null>(null);

  readonly setupForm: FormGroup = this.fb.group({
    salaryStructureId: ['', Validators.required],
    effectiveFrom: ['', Validators.required],
  });

  /** Rows that have both an employee id and a positive CTC (FR-5). */
  readonly validRowCount = computed(
    () =>
      this.rows().filter(
        (r) => r.employeeId.trim() !== '' && (r.annualCtc ?? 0) > 0,
      ).length,
  );

  readonly progressTotal = computed(() => this.result()?.totalRequested ?? this.validRowCount());
  readonly progressDone = computed(() =>
    this.result() ? this.result()!.successCount + this.result()!.failureCount : 0,
  );
  readonly progressPercent = computed(() => {
    const total = this.progressTotal();
    return total === 0 ? 0 : Math.round((this.progressDone() / total) * 100);
  });

  ngOnInit(): void {
    this.payrollService
      .listStructures()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) =>
          this.activeStructures.set(list.filter((s) => s.isActive)),
        error: () => this.toastr.error('Failed to load salary structures.'),
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  canSubmit(): boolean {
    return this.setupForm.valid && this.validRowCount() > 0;
  }

  addRow(): void {
    this.rows.update((rows) => [
      ...rows,
      { employeeId: '', annualCtc: null, status: 'pending' },
    ]);
  }

  removeRow(index: number): void {
    this.rows.update((rows) => rows.filter((_, i) => i !== index));
  }

  updateRow(
    index: number,
    field: 'employeeId' | 'annualCtc',
    value: string,
  ): void {
    this.rows.update((rows) =>
      rows.map((r, i) => {
        if (i !== index) return r;
        if (field === 'annualCtc') {
          const num = value === '' ? null : Number(value);
          return { ...r, annualCtc: Number.isNaN(num) ? null : num, status: 'pending' };
        }
        return { ...r, employeeId: value, status: 'pending' };
      }),
    );
  }

  /**
   * Parse a clipboard paste of tab/comma-separated "employeeId, CTC" lines into
   * grid rows (§8 spreadsheet-like paste). Appends to existing rows.
   */
  onPaste(event: ClipboardEvent): void {
    const text = event.clipboardData?.getData('text') ?? '';
    if (!text.trim()) return;
    event.preventDefault();
    const parsed = this.parsePaste(text);
    if (parsed.length === 0) return;
    this.rows.update((rows) => [...rows, ...parsed]);
  }

  /** Pure parser — split lines, then split each on tab or comma. */
  parsePaste(text: string): IBulkGridRow[] {
    return text
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter((line) => line !== '')
      .map((line) => {
        const cells = line.split(/[\t,]/).map((c) => c.trim());
        const ctcRaw = cells[1] ?? '';
        const ctc = ctcRaw === '' ? null : Number(ctcRaw.replace(/[^0-9.]/g, ''));
        return {
          employeeId: cells[0] ?? '',
          annualCtc: ctc != null && !Number.isNaN(ctc) ? ctc : null,
          status: 'pending' as const,
        };
      });
  }

  submit(): void {
    if (!this.canSubmit()) {
      this.setupForm.markAllAsTouched();
      return;
    }
    const rows: IBulkAssignmentRow[] = this.rows()
      .filter((r) => r.employeeId.trim() !== '' && (r.annualCtc ?? 0) > 0)
      .map((r) => ({ employeeId: r.employeeId.trim(), annualCtc: r.annualCtc! }));

    const request: IBulkAssignmentRequest = {
      salaryStructureId: this.setupForm.value.salaryStructureId,
      effectiveFrom: this.setupForm.value.effectiveFrom,
      employees: rows,   // GAP-010: the wire field is `employees`; the local stays `rows`
    };

    this.isSubmitting.set(true);
    this.result.set(null);
    this.salaryService
      .bulkAssign(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.isSubmitting.set(false);
          this.result.set(res);
          this.applyResults(res.results);
          if (res.failureCount === 0) {
            this.toastr.success(`${res.successCount} employees assigned.`);
          } else {
            this.toastr.warning(
              `${res.successCount} assigned, ${res.failureCount} failed.`,
            );
          }
        },
        error: () => {
          this.isSubmitting.set(false);
          this.toastr.error('Bulk assignment failed.');
        },
      });
  }

  /** Reflect the per-row outcome back onto the grid (AC-4). */
  private applyResults(results: IBulkAssignmentItemResult[]): void {
    const byId = new Map(results.map((r) => [r.employeeId, r]));
    this.rows.update((rows) =>
      rows.map((r) => {
        const match = byId.get(r.employeeId.trim());
        if (!match) return r;
        return {
          ...r,
          status: match.success ? 'success' : 'failed',
          error: match.error ?? null,
        };
      }),
    );
  }
}
