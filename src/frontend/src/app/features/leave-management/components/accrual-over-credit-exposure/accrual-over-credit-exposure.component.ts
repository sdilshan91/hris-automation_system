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
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AccrualOverCreditExposureService } from '../../services/accrual-over-credit-exposure.service';
import {
  IAccrualOverCreditExposureRow,
  AccrualExposureExportFormat,
} from '../../models/accrual-over-credit-exposure.models';

type LoadStatus = 'loading' | 'loaded' | 'error';

/**
 * BUG-291: Accrual over-credit exposure screen (READ-ONLY remediation tooling).
 *
 * A legacy leave-accrual bug credited a FULL YEAR on the first accrual run for
 * some Monthly/Quarterly leave types. The fix is forward-only, so affected
 * employees still hold over-credited balances that flow into encashment and
 * final settlement (real money). The decision is NOT to auto-correct — HR/Finance
 * work this list case-by-case. This screen lets a human read the exposure and act
 * deliberately: pick an as-of date, read who is most over-credited (sorted
 * descending), and export the population for offline work.
 *
 * It is proportionate remediation tooling for a specific defect — no dashboards,
 * no charts.
 */
@Component({
  selector: 'app-accrual-over-credit-exposure',
  standalone: true,
  imports: [CommonModule, FormsModule],
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
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-start sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Accrual Over-Credit Exposure
          </h1>
          <p class="text-sm text-neutral-500 mt-1">
            Employees whose leave balances were over-credited by the accrual defect (BUG-291).
          </p>
        </div>
        <div class="flex items-center gap-2">
          <button
            type="button"
            class="btn-secondary text-sm"
            [disabled]="status() !== 'loaded' || !hasRows() || isExporting()"
            (click)="download('csv')"
            aria-label="Download exposure as CSV"
          >
            Download CSV
          </button>
          <button
            type="button"
            class="btn-secondary text-sm"
            [disabled]="status() !== 'loaded' || !hasRows() || isExporting()"
            (click)="download('xlsx')"
            aria-label="Download exposure as Excel"
          >
            Download Excel
          </button>
        </div>
      </div>

      <!-- Warning banner: nothing is auto-corrected -->
      <div
        class="bg-amber-50 border border-amber-200 rounded-lg p-4 mb-4 flex items-start gap-2.5"
        role="note"
      >
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
          class="w-5 h-5 text-amber-500 flex-shrink-0 mt-0.5" aria-hidden="true">
          <path fill-rule="evenodd" d="M8.485 2.495c.673-1.167 2.357-1.167 3.03 0l6.28 10.875c.673 1.167-.17 2.625-1.516 2.625H3.72c-1.347 0-2.189-1.458-1.515-2.625L8.485 2.495ZM10 5a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 10 5Zm0 9a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/>
        </svg>
        <div class="text-sm text-amber-800">
          <p class="font-medium">These balances are NOT corrected automatically.</p>
          <p class="mt-0.5">
            Reducing an employee's leave balance is a case-by-case HR/Finance decision. The figures
            below are a snapshot <span class="font-medium">as of the chosen date</span> — changing
            the date re-queries and can change every number.
          </p>
        </div>
      </div>

      <!-- As-of date control -->
      <div class="card-notion mb-4">
        <div class="flex flex-col sm:flex-row sm:items-end gap-3">
          <div>
            <label class="label-sm" for="asOfDate">As-of date</label>
            <input
              id="asOfDate"
              type="date"
              class="input-sm"
              [ngModel]="asOfDate()"
              (ngModelChange)="onDateChange($event)"
              [max]="today"
              aria-label="As-of date for the exposure figures"
            />
          </div>
          <p class="text-xs text-neutral-500 sm:pb-2.5">
            Over-credit is measured against what each employee's Monthly/Quarterly accrual should
            have granted by this date.
          </p>
        </div>
      </div>

      <!-- Loading state (distinct from empty) -->
      @if (status() === 'loading') {
        <div class="card-notion" aria-live="polite" aria-busy="true">
          <div class="space-y-3">
            @for (_ of [1,2,3,4]; track $index) {
              <div class="skeleton-line w-full h-10"></div>
            }
          </div>
        </div>
      }

      <!-- Error state (distinct from empty) -->
      @if (status() === 'error') {
        <div @fadeIn class="card-notion text-center py-16" role="alert">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">Couldn't load the exposure</h3>
          <p class="text-sm text-neutral-500 mb-4">{{ errorMessage() }}</p>
          <button type="button" class="btn-primary" (click)="load()">Try again</button>
        </div>
      }

      <!-- Empty state (loaded, but no affected employees) -->
      @if (status() === 'loaded' && !hasRows()) {
        <div @fadeIn class="card-notion text-center py-16">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
            class="w-12 h-12 mx-auto text-green-400 mb-4" aria-hidden="true">
            <path fill-rule="evenodd" d="M2.25 12c0-5.385 4.365-9.75 9.75-9.75s9.75 4.365 9.75 9.75-4.365 9.75-9.75 9.75S2.25 17.385 2.25 12Zm13.36-1.814a.75.75 0 1 0-1.22-.872l-3.236 4.53L9.53 12.22a.75.75 0 0 0-1.06 1.06l2.25 2.25a.75.75 0 0 0 1.14-.094l3.75-5.25Z" clip-rule="evenodd"/>
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No affected employees</h3>
          <p class="text-sm text-neutral-500">
            No employee is over-credited as of {{ asOfDate() }}. Nothing to remediate for this date.
          </p>
        </div>
      }

      <!-- Results -->
      @if (status() === 'loaded' && hasRows()) {
        <p class="text-sm text-neutral-500 mb-3" @fadeIn>
          <span class="font-medium text-neutral-800">{{ sortedRows().length }}</span>
          affected {{ sortedRows().length === 1 ? 'record' : 'records' }} as of
          <span class="font-medium text-neutral-800">{{ asOfDate() }}</span>,
          most over-credited first.
        </p>

        <!-- Desktop table -->
        <div class="hidden md:block card-notion overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="Accrual over-credit exposure">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Employee</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Leave Type</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Frequency</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Year</th>
                <th class="text-right py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Credited</th>
                <th class="text-right py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Should&nbsp;have</th>
                <th class="text-right py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Over-credited</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (row of sortedRows(); track row.employeeId + '|' + row.leaveTypeId) {
                <tr class="border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors">
                  <td class="py-3 px-3">
                    <div class="font-medium text-neutral-900">{{ row.employeeName }}</div>
                    <div class="text-xs text-neutral-400">{{ row.employeeNo }}</div>
                  </td>
                  <td class="py-3 px-3 text-neutral-600">{{ row.leaveTypeName }}</td>
                  <td class="py-3 px-3 text-neutral-600">{{ row.accrualFrequency }}</td>
                  <td class="py-3 px-3 text-center text-neutral-600">{{ row.leaveYear }}</td>
                  <td class="py-3 px-3 text-right tabular-nums text-neutral-700">{{ row.creditedDays | number:'1.0-2' }}</td>
                  <td class="py-3 px-3 text-right tabular-nums text-neutral-500">{{ row.shouldHaveAccruedDays | number:'1.0-2' }}</td>
                  <td class="py-3 px-3 text-right">
                    <span class="over-credit-figure">+{{ row.overCreditedDays | number:'1.0-2' }}</span>
                  </td>
                  <td class="py-3 px-3 text-center">
                    @if (row.isEmployeeActive) {
                      <span class="badge badge-active">Active</span>
                    } @else {
                      <span class="badge badge-inactive">Terminated</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile card view -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (row of sortedRows(); track row.employeeId + '|' + row.leaveTypeId) {
            <div class="card-notion">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <div class="font-medium text-neutral-900">{{ row.employeeName }}</div>
                  <div class="text-xs text-neutral-400">{{ row.employeeNo }}</div>
                </div>
                @if (row.isEmployeeActive) {
                  <span class="badge badge-active">Active</span>
                } @else {
                  <span class="badge badge-inactive">Terminated</span>
                }
              </div>
              <div class="text-xs text-neutral-500 mt-1">
                {{ row.leaveTypeName }} · {{ row.accrualFrequency }} · {{ row.leaveYear }}
              </div>
              <div class="grid grid-cols-3 gap-2 mt-3 text-center">
                <div>
                  <div class="text-[11px] text-neutral-400 uppercase tracking-wide">Credited</div>
                  <div class="tabular-nums text-neutral-700">{{ row.creditedDays | number:'1.0-2' }}</div>
                </div>
                <div>
                  <div class="text-[11px] text-neutral-400 uppercase tracking-wide">Should have</div>
                  <div class="tabular-nums text-neutral-500">{{ row.shouldHaveAccruedDays | number:'1.0-2' }}</div>
                </div>
                <div>
                  <div class="text-[11px] text-neutral-400 uppercase tracking-wide">Over</div>
                  <div><span class="over-credit-figure">+{{ row.overCreditedDays | number:'1.0-2' }}</span></div>
                </div>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .page-container { @apply max-w-7xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-sm {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2
        text-sm text-neutral-900 placeholder-neutral-400
        transition-all duration-150
        focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-400;
    }

    .over-credit-figure {
      @apply inline-flex items-center justify-center rounded-md px-2 py-0.5
        text-sm font-semibold tabular-nums bg-red-50 text-red-700;
    }

    .badge {
      @apply inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium;
    }
    .badge-active { @apply bg-green-50 text-green-700; }
    .badge-inactive { @apply bg-neutral-100 text-neutral-500; }

    .skeleton-line {
      @apply rounded bg-neutral-200;
      animation: shimmer 1.5s ease-in-out infinite;
    }
    @keyframes shimmer {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.4; }
    }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class AccrualOverCreditExposureComponent implements OnInit, OnDestroy {
  private readonly exposureService = inject(AccrualOverCreditExposureService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** Today as an ISO calendar date (YYYY-MM-DD) — used as the input max + default. */
  readonly today = new Date().toISOString().slice(0, 10);

  readonly asOfDate = signal<string>(this.today);
  readonly status = signal<LoadStatus>('loading');
  readonly errorMessage = signal<string>('');
  readonly isExporting = signal(false);

  private readonly rows = signal<IAccrualOverCreditExposureRow[]>([]);

  /** Rows sorted by over-credited days descending — the money figure a human acts on. */
  readonly sortedRows = computed(() =>
    [...this.rows()].sort((a, b) => b.overCreditedDays - a.overCreditedDays),
  );

  readonly hasRows = computed(() => this.rows().length > 0);

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onDateChange(value: string): void {
    if (!value || value === this.asOfDate()) {
      return;
    }
    this.asOfDate.set(value);
    this.load();
  }

  load(): void {
    this.status.set('loading');
    this.exposureService
      .getExposure(this.asOfDate())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (report) => {
          this.rows.set(report?.rows ?? []);
          this.status.set('loaded');
        },
        error: (err: HttpErrorResponse) => {
          this.rows.set([]);
          this.errorMessage.set(AccrualOverCreditExposureService.parseError(err));
          this.status.set('error');
        },
      });
  }

  download(format: AccrualExposureExportFormat): void {
    if (this.isExporting()) {
      return;
    }
    this.isExporting.set(true);
    this.exposureService
      .exportExposure(this.asOfDate(), format)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ blob, filename }) => {
          this.isExporting.set(false);
          this.triggerDownload(blob, filename);
        },
        error: (err: HttpErrorResponse) => {
          this.isExporting.set(false);
          this.toastr.error(AccrualOverCreditExposureService.parseError(err));
        },
      });
  }

  /** Standard blob → anchor click → revoke download. */
  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
