import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { AdjustmentService } from '../../services/adjustment.service';
import {
  ADJUSTMENT_TYPE_OPTIONS,
  IBulkAdjustmentPreview,
  IBulkAdjustmentPreviewRow,
  MONTH_LABELS,
} from '../../models/adjustment.models';

/**
 * CSV MIME types browsers commonly report. The primary gate is the `.csv`
 * extension; a recognized CSV mime is accepted too, but a generic type
 * (`text/plain`, empty) alone is NOT enough — the name must end in `.csv`.
 */
const ALLOWED_CSV_TYPES = ['text/csv', 'application/vnd.ms-excel', 'application/csv'];

/** Allowed adjustment_type values (lower-cased for client-side row validation). */
const ALLOWED_TYPES_LOWER = ADJUSTMENT_TYPE_OPTIONS.map((t) => t.toLowerCase());

/** Years offered in the period selector — a small fixed window (no Date.now()). */
const YEAR_OPTIONS = [2025, 2026, 2027];

/**
 * US-PAY-007 (§8, FR-2): Bulk adjustment upload — a Notion-style card with a period
 * selector, a drag-and-drop CSV drop zone, a client-side template download, and a
 * validation preview table that must be reviewed before committing. The backend has
 * NO dry-run mode, so the preview is built by parsing the CSV in the browser; Commit
 * is only enabled once a target period is set AND at least one parsed row is valid,
 * and it POSTs the file + payMonth/payYear to the bulk endpoint (which commits).
 *
 * Desktop-only (§8): a message replaces the drop zone on small screens, mirroring
 * the bulk-salary-assignment grid.
 */
@Component({
  selector: 'app-adjustment-bulk-upload',
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
    <div class="space-y-4">
      <!-- Mobile: not available (§8) -->
      <div class="md:hidden rounded-xl border border-neutral-200 bg-white p-6 text-center" @fadeIn>
        <h3 class="text-base font-semibold text-neutral-900 mb-1">Desktop only</h3>
        <p class="text-sm text-neutral-500">
          Bulk CSV upload needs a larger screen. Add adjustments individually on mobile,
          or open this page on a desktop to upload a CSV.
        </p>
      </div>

      <!-- Desktop -->
      <div class="hidden md:block space-y-4" @fadeIn>
        <div class="flex items-center justify-between">
          <p class="text-sm text-neutral-500">
            Upload a CSV of adjustments for review before they are created.
          </p>
          <button
            type="button"
            class="text-sm font-medium text-brand-600 hover:underline"
            (click)="downloadTemplate()"
          >
            Download template
          </button>
        </div>

        <!-- Period selector (required before commit) -->
        <div class="flex flex-wrap items-end gap-3">
          <label class="flex flex-col gap-1">
            <span class="text-xs font-medium text-neutral-500">Pay month</span>
            <select
              class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm"
              [ngModel]="payMonth()"
              (ngModelChange)="payMonth.set($event)"
              aria-label="Pay month"
            >
              @for (m of monthOptions; track m) {
                <option [ngValue]="m">{{ monthLabels[m - 1] }}</option>
              }
            </select>
          </label>
          <label class="flex flex-col gap-1">
            <span class="text-xs font-medium text-neutral-500">Pay year</span>
            <select
              class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm"
              [ngModel]="payYear()"
              (ngModelChange)="payYear.set($event)"
              aria-label="Pay year"
            >
              @for (y of yearOptions; track y) {
                <option [ngValue]="y">{{ y }}</option>
              }
            </select>
          </label>
          <p class="pb-1.5 text-xs text-neutral-400">
            All rows in this upload apply to the selected period.
          </p>
        </div>

        <!-- Drop zone -->
        <div
          class="rounded-xl border-2 border-dashed px-6 py-10 text-center transition-colors"
          [class.border-neutral-200]="!dragging()"
          [class.border-brand-400]="dragging()"
          [class.bg-brand-50]="dragging()"
          (dragover)="onDragOver($event)"
          (dragleave)="onDragLeave($event)"
          (drop)="onDrop($event)"
        >
          <p class="text-sm text-neutral-600">
            Drag &amp; drop a CSV here, or
            <label class="cursor-pointer font-medium text-brand-600 hover:underline">
              browse
              <input
                type="file"
                accept=".csv,text/csv"
                class="hidden"
                (change)="onFileSelected($event)"
              />
            </label>
          </p>
          <p class="mt-1 text-xs text-neutral-400">
            Columns: employee_no, adjustment_type, amount, description, is_taxable
          </p>
          @if (fileName()) {
            <p class="mt-3 text-xs text-neutral-600">
              Selected: <span class="font-medium">{{ fileName() }}</span>
            </p>
          }
          @if (fileError()) {
            <p class="mt-2 text-xs text-rose-600" role="alert">{{ fileError() }}</p>
          }
        </div>

        @if (previewing()) {
          <p class="text-sm text-neutral-400">Reading CSV…</p>
        }

        <!-- Validation preview (FR-2) -->
        @if (preview(); as p) {
          <div class="rounded-xl border border-neutral-200 bg-white overflow-hidden">
            <div class="flex items-center justify-between border-b border-neutral-100 px-4 py-3">
              <p class="text-sm font-medium text-neutral-900">
                {{ p.validCount }} valid, {{ p.invalidCount }} invalid
                <span class="text-neutral-400">of {{ p.totalRows }} rows</span>
              </p>
            </div>
            <div class="overflow-x-auto">
              <table class="w-full text-sm">
                <thead class="bg-neutral-50 text-neutral-500">
                  <tr>
                    <th class="px-4 py-2 text-left font-medium w-12">#</th>
                    <th class="px-4 py-2 text-left font-medium">Employee No</th>
                    <th class="px-4 py-2 text-left font-medium">Type</th>
                    <th class="px-4 py-2 text-right font-medium">Amount</th>
                    <th class="px-4 py-2 text-left font-medium">Status</th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-neutral-100">
                  @for (row of p.rows; track row.rowNumber) {
                    <tr [class.bg-rose-50]="!row.valid">
                      <td class="px-4 py-2 text-neutral-400">{{ row.rowNumber }}</td>
                      <td class="px-4 py-2 text-neutral-700">{{ row.employeeNo }}</td>
                      <td class="px-4 py-2 text-neutral-700">{{ row.type }}</td>
                      <td class="px-4 py-2 text-right font-mono tabular-nums text-neutral-700">
                        {{ row.amount != null ? (row.amount | number: '1.2-2') : '—' }}
                      </td>
                      <td class="px-4 py-2">
                        @if (row.valid) {
                          <span class="badge bg-emerald-50 text-emerald-700 ring-emerald-600/20">Valid</span>
                        } @else {
                          <span class="badge bg-rose-50 text-rose-700 ring-rose-600/20" [title]="row.error || ''">
                            {{ row.error || 'Invalid' }}
                          </span>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <div class="flex items-center justify-end gap-2 border-t border-neutral-100 px-4 py-3">
              <button type="button" class="btn-secondary" (click)="reset()">Clear</button>
              <button
                type="button"
                class="btn-primary"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="!canCommit()"
                (click)="commit()"
              >
                {{ committing() ? 'Creating…' : 'Create ' + p.validCount + ' adjustment(s)' }}
              </button>
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
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
      .btn-primary,
      .btn-secondary {
        border-radius: 0.5rem;
        padding: 0.45rem 0.9rem;
        font-size: 0.8125rem;
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
      .btn-secondary:hover {
        background: #f5f5f5;
      }
    `,
  ],
})
export class AdjustmentBulkUploadComponent {
  private readonly adjustments = inject(AdjustmentService);
  private readonly toastr = inject(ToastrService);

  /** Emitted after a successful commit so the parent reloads the table. */
  readonly committed = output<void>();

  /** Period selector options + labels. Defaults are hardcoded (no Date.now()). */
  readonly monthOptions = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  readonly monthLabels = MONTH_LABELS;
  readonly yearOptions = YEAR_OPTIONS;

  readonly dragging = signal(false);
  readonly previewing = signal(false);
  readonly committing = signal(false);
  readonly fileName = signal<string | null>(null);
  readonly fileError = signal<string | null>(null);
  readonly preview = signal<IBulkAdjustmentPreview | null>(null);
  private readonly file = signal<File | null>(null);

  /** Target period for every row in this upload (sensible hardcoded default). */
  readonly payMonth = signal<number>(6);
  readonly payYear = signal<number>(2026);

  readonly hasValidRows = computed(() => (this.preview()?.validCount ?? 0) > 0);

  /** Commit is allowed once a period is set AND there is >=1 valid parsed row. */
  readonly canCommit = computed(
    () =>
      !this.committing() &&
      this.payMonth() > 0 &&
      this.payYear() > 0 &&
      this.hasValidRows(),
  );

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0] ?? null;
    this.accept(file);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.accept(input.files?.[0] ?? null);
    input.value = '';
  }

  /** Validate the picked file is a CSV, then parse it client-side for preview (FR-2). */
  private accept(file: File | null): void {
    this.fileError.set(null);
    this.preview.set(null);
    if (!file) {
      return;
    }
    const isCsv =
      ALLOWED_CSV_TYPES.includes(file.type) ||
      file.name.toLowerCase().endsWith('.csv');
    if (!isCsv) {
      this.fileError.set('Please choose a .csv file.');
      this.fileName.set(null);
      this.file.set(null);
      return;
    }
    this.file.set(file);
    this.fileName.set(file.name);
    this.parsePreview(file);
  }

  /**
   * Read + parse the CSV in the browser to build the preview (the backend has no
   * dry-run). Each data row is validated: employee_no non-empty, adjustment_type in
   * the allowed set, amount a positive number.
   */
  private parsePreview(file: File): void {
    this.previewing.set(true);
    const reader = new FileReader();
    reader.onload = () => {
      this.previewing.set(false);
      try {
        this.preview.set(buildPreview(String(reader.result ?? '')));
      } catch {
        this.fileError.set(
          'Could not read the CSV. Check the format and try again.',
        );
      }
    };
    reader.onerror = () => {
      this.previewing.set(false);
      this.fileError.set('Could not read the CSV. Check the format and try again.');
    };
    reader.readAsText(file);
  }

  commit(): void {
    const file = this.file();
    if (!file || !this.canCommit()) {
      return;
    }
    this.committing.set(true);
    this.adjustments.bulkUpload(file, this.payMonth(), this.payYear()).subscribe({
      next: (res) => {
        this.committing.set(false);
        this.toastr.success(
          `${res.succeededCount} succeeded, ${res.failedCount} failed.`,
        );
        this.reset();
        this.committed.emit();
      },
      error: () => {
        this.committing.set(false);
        this.toastr.error('Bulk upload failed.');
      },
    });
  }

  downloadTemplate(): void {
    const csv = this.adjustments.bulkTemplateCsv();
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8' });
    downloadBlob(blob, 'adjustments-template.csv');
  }

  reset(): void {
    this.file.set(null);
    this.fileName.set(null);
    this.fileError.set(null);
    this.preview.set(null);
  }
}

/**
 * Parse a bulk-adjustment CSV string into a preview. Expects the header row
 * `employee_no,adjustment_type,amount,description,is_taxable`; the period is NOT in
 * the CSV. Validates each data row (employee_no non-empty, type in the allowed set,
 * amount a positive number) and reports the first failure per row.
 */
function buildPreview(text: string): IBulkAdjustmentPreview {
  const lines = text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0);
  // Drop the header row if present.
  if (lines.length > 0 && /employee_no/i.test(lines[0])) {
    lines.shift();
  }

  const rows: IBulkAdjustmentPreviewRow[] = lines.map((line, i) => {
    const cells = line.split(',').map((c) => c.trim());
    const [employeeNo = '', type = '', amountRaw = '', description = '', taxableRaw = ''] =
      cells;
    const amount = amountRaw === '' ? null : Number(amountRaw);

    let error: string | null = null;
    if (!employeeNo) {
      error = 'Missing employee_no';
    } else if (!ALLOWED_TYPES_LOWER.includes(type.toLowerCase())) {
      error = 'Invalid adjustment_type';
    } else if (amount === null || Number.isNaN(amount) || amount <= 0) {
      error = 'Amount must be a positive number';
    }

    return {
      rowNumber: i + 1,
      employeeNo,
      type,
      amount: amount !== null && !Number.isNaN(amount) ? amount : null,
      description,
      isTaxable: /^(true|1|yes)$/i.test(taxableRaw),
      valid: error === null,
      error,
    };
  });

  const validCount = rows.filter((r) => r.valid).length;
  return {
    rows,
    totalRows: rows.length,
    validCount,
    invalidCount: rows.length - validCount,
  };
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
