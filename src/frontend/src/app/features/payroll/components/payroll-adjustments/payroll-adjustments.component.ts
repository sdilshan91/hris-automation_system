import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { AdjustmentService } from '../../services/adjustment.service';
import { AdjustmentFormComponent } from '../adjustment-form/adjustment-form.component';
import { AdjustmentBulkUploadComponent } from '../adjustment-bulk-upload/adjustment-bulk-upload.component';
import {
  IAdjustment,
  IAdjustmentFilters,
  AdjustmentStatus,
  AdjustmentType,
  ADJUSTMENT_TYPE_OPTIONS,
  ADJUSTMENT_TYPE_BADGE,
  ADJUSTMENT_TYPE_LABELS,
  ADJUSTMENT_STATUS_BADGE,
  ADJUSTMENT_STATUS_LABELS,
  periodLabel,
} from '../../models/adjustment.models';

/** Which list tab is active (§8 Applied moved to its own tab to reduce clutter). */
type AdjustmentTab = 'active' | 'applied';

/** Sortable table columns (§8). */
type SortKey = 'employee' | 'type' | 'amount' | 'period' | 'status' | 'created';
type SortDir = 'asc' | 'desc';

/**
 * US-PAY-007 (§8): Payroll Adjustments page. A Notion-style database table view of
 * adjustments with filters (Status, Type, Period, Employee — FR-1/§8), sortable
 * columns, and inline status badges. Two tabs split Active (Pending/Cancelled) from
 * Applied so finalized adjustments don't clutter the working view (§8); Applied
 * rows are also dimmed. "New Adjustment" opens the slide-over form (FR-1) and a
 * collapsible bulk-upload card hosts the CSV drag-and-drop (FR-2). A Pending
 * adjustment can be cancelled (FR-6).
 *
 * Lazy-loaded under the existing `payroll` parent route, which already applies
 * roleGuard(['Tenant Admin','HR Officer']) + Payroll.View; the backend enforces the
 * actual permission + tenant RLS (AC-5).
 */
@Component({
  selector: 'app-payroll-adjustments',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    AdjustmentFormComponent,
    AdjustmentBulkUploadComponent,
  ],
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
    <div class="page-container max-w-6xl mx-auto px-4 py-6">
      <!-- Header -->
      <div class="flex items-start justify-between mb-6">
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Payroll Adjustments
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Ad-hoc bonuses, deductions, reimbursements, and corrections for the next payroll run.
          </p>
        </div>
        <button
          type="button"
          class="btn-primary"
          [style.background-color]="'var(--brand-primary)'"
          (click)="openForm()"
        >
          New adjustment
        </button>
      </div>

      <!-- Bulk upload (collapsible, FR-2) -->
      <div class="mb-6 rounded-xl border border-neutral-200 bg-white">
        <button
          type="button"
          class="flex w-full items-center justify-between px-5 py-3 text-left"
          (click)="bulkOpen.set(!bulkOpen())"
          [attr.aria-expanded]="bulkOpen()"
        >
          <span class="text-sm font-medium text-neutral-800">Bulk upload (CSV)</span>
          <span class="text-neutral-400">{{ bulkOpen() ? '▲' : '▼' }}</span>
        </button>
        @if (bulkOpen()) {
          <div class="border-t border-neutral-100 px-5 py-4">
            <app-adjustment-bulk-upload (committed)="reload()" />
          </div>
        }
      </div>

      <!-- Tabs -->
      <div class="mb-4 flex items-center gap-1 border-b border-neutral-200">
        <button
          type="button"
          class="tab"
          [class.tab-active]="tab() === 'active'"
          (click)="setTab('active')"
        >
          Active
        </button>
        <button
          type="button"
          class="tab"
          [class.tab-active]="tab() === 'applied'"
          (click)="setTab('applied')"
        >
          Applied
        </button>
      </div>

      <!-- Filters (§8) -->
      <div class="mb-4 grid grid-cols-1 gap-3 sm:grid-cols-4">
        <div>
          <label class="filter-label" for="f-status">Status</label>
          <select
            id="f-status"
            class="filter-input"
            [ngModel]="filterStatus()"
            (ngModelChange)="filterStatus.set($event); reload()"
          >
            <option [ngValue]="null">All</option>
            @for (s of statusFilterOptions(); track s) {
              <option [ngValue]="s">{{ statusLabels[s] }}</option>
            }
          </select>
        </div>
        <div>
          <label class="filter-label" for="f-type">Type</label>
          <select
            id="f-type"
            class="filter-input"
            [ngModel]="filterType()"
            (ngModelChange)="filterType.set($event); reload()"
          >
            <option [ngValue]="null">All</option>
            @for (t of typeOptions; track t) {
              <option [ngValue]="t">{{ typeLabels[t] }}</option>
            }
          </select>
        </div>
        <div>
          <label class="filter-label" for="f-period">Period</label>
          <input
            id="f-period"
            type="month"
            class="filter-input"
            [ngModel]="filterPeriod()"
            (ngModelChange)="filterPeriod.set($event); reload()"
          />
        </div>
        <div>
          <label class="filter-label" for="f-emp">Employee</label>
          <input
            id="f-emp"
            type="text"
            class="filter-input"
            placeholder="Name or no…"
            [ngModel]="filterEmployee()"
            (ngModelChange)="filterEmployee.set($event)"
            (keyup.enter)="reload()"
          />
        </div>
      </div>

      <!-- Table -->
      @if (loading()) {
        <div class="rounded-xl border border-neutral-200 bg-white p-10 text-center text-sm text-neutral-400">
          Loading adjustments…
        </div>
      } @else if (sortedRows().length === 0) {
        <div class="rounded-xl border border-neutral-200 bg-white p-12 text-center" @fadeIn>
          <h3 class="text-base font-semibold text-neutral-900 mb-1">No adjustments</h3>
          <p class="text-sm text-neutral-500">
            {{ tab() === 'applied' ? 'Nothing has been applied to a payroll run yet.' : 'Create an adjustment or upload a CSV to get started.' }}
          </p>
        </div>
      } @else {
        <div class="rounded-xl border border-neutral-200 bg-white overflow-hidden" @fadeIn>
          <div class="overflow-x-auto">
            <table class="w-full text-sm">
              <thead class="bg-neutral-50 text-neutral-500">
                <tr>
                  <th class="th"><button type="button" class="sort-btn" (click)="sort('employee')">Employee {{ sortIcon('employee') }}</button></th>
                  <th class="th"><button type="button" class="sort-btn" (click)="sort('type')">Type {{ sortIcon('type') }}</button></th>
                  <th class="th text-right"><button type="button" class="sort-btn" (click)="sort('amount')">Amount {{ sortIcon('amount') }}</button></th>
                  <th class="th"><button type="button" class="sort-btn" (click)="sort('period')">Period {{ sortIcon('period') }}</button></th>
                  <th class="th"><button type="button" class="sort-btn" (click)="sort('status')">Status {{ sortIcon('status') }}</button></th>
                  <th class="th">Flags</th>
                  <th class="th text-right">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-neutral-100">
                @for (a of sortedRows(); track a.id) {
                  <tr [class.opacity-50]="a.status === 'Applied' || a.status === 'Cancelled'">
                    <td class="td">
                      <p class="font-medium text-neutral-900">{{ a.employeeName }}</p>
                      <p class="text-xs text-neutral-400">{{ a.employeeNo }}</p>
                    </td>
                    <td class="td">
                      <span class="badge" [ngClass]="typeBadge[a.type]">{{ typeLabels[a.type] }}</span>
                    </td>
                    <td class="td text-right font-mono tabular-nums text-neutral-800">
                      {{ a.amount | number: '1.2-2' }}
                    </td>
                    <td class="td text-neutral-600">{{ period(a) }}</td>
                    <td class="td">
                      <span class="badge" [ngClass]="statusBadge[a.status]">{{ statusLabels[a.status] }}</span>
                    </td>
                    <td class="td">
                      <div class="flex flex-wrap gap-1">
                        @if (a.isTaxable) {
                          <span class="chip">Taxable</span>
                        }
                        @if (a.isRecurring) {
                          <span class="chip">Recurring</span>
                        }
                        @if (a.hasDocument) {
                          <button type="button" class="chip chip-link" (click)="downloadDocument(a)">Document</button>
                        }
                      </div>
                    </td>
                    <td class="td text-right">
                      @if (a.status === 'Pending') {
                        <button
                          type="button"
                          class="text-xs font-medium text-rose-600 hover:underline disabled:opacity-50"
                          [disabled]="cancellingId() === a.id"
                          (click)="cancel(a)"
                        >
                          {{ cancellingId() === a.id ? 'Cancelling…' : 'Cancel' }}
                        </button>
                      } @else {
                        <span class="text-xs text-neutral-300">—</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </div>

    <!-- New adjustment slide-over (FR-1) -->
    @if (formOpen()) {
      <app-adjustment-form (close)="formOpen.set(false)" (saved)="onSaved()" />
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .btn-primary {
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: #fff;
        transition: opacity 150ms ease;
      }
      .tab {
        padding: 0.5rem 0.875rem;
        font-size: 0.875rem;
        font-weight: 500;
        color: #737373;
        border-bottom: 2px solid transparent;
        margin-bottom: -1px;
        transition: color 150ms ease, border-color 150ms ease;
      }
      .tab:hover {
        color: #404040;
      }
      .tab-active {
        color: #171717;
        border-bottom-color: var(--brand-primary, #4f46e5);
      }
      .filter-label {
        display: block;
        margin-bottom: 0.25rem;
        font-size: 0.75rem;
        font-weight: 500;
        color: #737373;
      }
      .filter-input {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.4rem 0.6rem;
        font-size: 0.8125rem;
        color: #171717;
      }
      .th {
        padding: 0.625rem 1rem;
        text-align: left;
        font-weight: 500;
        white-space: nowrap;
      }
      .td {
        padding: 0.625rem 1rem;
        vertical-align: top;
      }
      .sort-btn {
        font-weight: 500;
        color: inherit;
      }
      .sort-btn:hover {
        color: #404040;
      }
      .badge {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        padding: 0.125rem 0.5rem;
        font-size: 0.75rem;
        font-weight: 500;
        box-shadow: inset 0 0 0 1px var(--tw-ring-color, transparent);
      }
      .chip {
        display: inline-flex;
        align-items: center;
        border-radius: 0.375rem;
        background: #f5f5f5;
        padding: 0.0625rem 0.375rem;
        font-size: 0.6875rem;
        color: #525252;
      }
      .chip-link {
        color: var(--brand-primary, #4f46e5);
        cursor: pointer;
      }
      .chip-link:hover {
        text-decoration: underline;
      }
    `,
  ],
})
export class PayrollAdjustmentsComponent implements OnInit {
  private readonly service = inject(AdjustmentService);
  private readonly toastr = inject(ToastrService);

  readonly typeOptions = ADJUSTMENT_TYPE_OPTIONS;
  readonly typeBadge = ADJUSTMENT_TYPE_BADGE;
  readonly typeLabels = ADJUSTMENT_TYPE_LABELS;
  readonly statusBadge = ADJUSTMENT_STATUS_BADGE;
  readonly statusLabels = ADJUSTMENT_STATUS_LABELS;

  readonly loading = signal(true);
  readonly rows = signal<IAdjustment[]>([]);
  readonly tab = signal<AdjustmentTab>('active');
  readonly formOpen = signal(false);
  readonly bulkOpen = signal(false);
  readonly cancellingId = signal<string | null>(null);

  // Filters (§8)
  readonly filterStatus = signal<AdjustmentStatus | null>(null);
  readonly filterType = signal<AdjustmentType | null>(null);
  /** `YYYY-MM` from the <input type="month">. */
  readonly filterPeriod = signal<string | null>(null);
  readonly filterEmployee = signal<string>('');

  // Sort (§8)
  readonly sortKey = signal<SortKey>('created');
  readonly sortDir = signal<SortDir>('desc');

  /**
   * Status options offered in the filter depend on the tab: the Applied tab is, by
   * definition, only Applied; the Active tab offers Pending / Cancelled.
   */
  readonly statusFilterOptions = computed<AdjustmentStatus[]>(() =>
    this.tab() === 'applied' ? ['Applied'] : ['Pending', 'Cancelled'],
  );

  /**
   * Rows for the current tab. Applied tab shows Applied only; Active shows
   * everything else (Pending + Cancelled). Server-side filters already apply; this
   * is the tab partition layered on top.
   */
  readonly tabRows = computed(() => {
    const applied = this.tab() === 'applied';
    return this.rows().filter((r) =>
      applied ? r.status === 'Applied' : r.status !== 'Applied',
    );
  });

  /** Tab rows with the active column sort applied (§8 sortable columns). */
  readonly sortedRows = computed(() => {
    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...this.tabRows()].sort((a, b) => this.compare(a, b, key) * dir);
  });

  ngOnInit(): void {
    this.reload();
  }

  setTab(tab: AdjustmentTab): void {
    if (this.tab() === tab) {
      return;
    }
    this.tab.set(tab);
    // A status filter from the other tab no longer applies.
    this.filterStatus.set(null);
    this.reload();
  }

  /** Load adjustments with the current server-side filters (§8). */
  reload(): void {
    const filters: IAdjustmentFilters = {
      status: this.filterStatus(),
      type: this.filterType(),
      period: this.filterPeriod() || null,
      employeeId: null,
    };
    this.loading.set(true);
    this.service.listAdjustments(filters).subscribe({
      next: (rows) => {
        this.rows.set(this.applyEmployeeTextFilter(rows));
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.toastr.error('Could not load adjustments.');
      },
    });
  }

  /**
   * The Employee filter is a free-text box (name or no); the list API filters by
   * employeeId, so we additionally narrow client-side by the typed text. Empty =
   * no narrowing.
   */
  private applyEmployeeTextFilter(rows: IAdjustment[]): IAdjustment[] {
    const term = this.filterEmployee().trim().toLowerCase();
    if (!term) {
      return rows;
    }
    return rows.filter(
      (r) =>
        r.employeeName.toLowerCase().includes(term) ||
        r.employeeNo.toLowerCase().includes(term),
    );
  }

  openForm(): void {
    this.formOpen.set(true);
  }

  onSaved(): void {
    this.formOpen.set(false);
    this.reload();
  }

  cancel(a: IAdjustment): void {
    this.cancellingId.set(a.id);
    this.service.cancelAdjustment(a.id).subscribe({
      next: (updated) => {
        this.cancellingId.set(null);
        this.rows.update((rows) =>
          rows.map((r) => (r.id === updated.id ? updated : r)),
        );
        this.toastr.success('Adjustment cancelled.');
      },
      error: () => {
        this.cancellingId.set(null);
        this.toastr.error('Could not cancel the adjustment.');
      },
    });
  }

  downloadDocument(a: IAdjustment): void {
    this.service.downloadDocument(a.id).subscribe({
      next: (resp: HttpResponse<Blob>) => {
        const blob = resp.body;
        if (!blob) {
          return;
        }
        const filename =
          filenameFromDisposition(resp.headers.get('Content-Disposition')) ??
          `adjustment-${a.id}`;
        downloadBlob(blob, filename);
      },
      error: () => this.toastr.error('Could not download the document.'),
    });
  }

  // ─── Sorting ─────────────────────────────────────────────────

  sort(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortKey.set(key);
      this.sortDir.set('asc');
    }
  }

  sortIcon(key: SortKey): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.sortDir() === 'asc' ? '↑' : '↓';
  }

  private compare(a: IAdjustment, b: IAdjustment, key: SortKey): number {
    switch (key) {
      case 'employee':
        return a.employeeName.localeCompare(b.employeeName);
      case 'type':
        return a.type.localeCompare(b.type);
      case 'amount':
        return a.amount - b.amount;
      case 'period':
        return (
          a.applicablePayYear * 12 +
          a.applicablePayMonth -
          (b.applicablePayYear * 12 + b.applicablePayMonth)
        );
      case 'status':
        return a.status.localeCompare(b.status);
      case 'created':
        return a.createdAt.localeCompare(b.createdAt);
    }
  }

  period(a: IAdjustment): string {
    return periodLabel(a.applicablePayMonth, a.applicablePayYear);
  }
}

/** Parse a filename from a Content-Disposition header, if present. */
function filenameFromDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (utf8?.[1]) {
    return decodeURIComponent(utf8[1]);
  }
  const quoted = /filename="?([^";]+)"?/i.exec(header);
  return quoted?.[1] ?? null;
}

/** Trigger a browser download for a blob with the given filename. */
function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
