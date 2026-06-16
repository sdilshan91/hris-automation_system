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
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';

import { PayrollReportService } from '../../services/payroll-report.service';
import { DepartmentService } from '../../../core-hr/departments/services/department.service';
import { IDepartment } from '../../../core-hr/departments/models/department.models';
import {
  IBankAdvicePreview,
  IReportFilters,
  IReportResult,
  IReportTypeMeta,
  PayrollReportType,
  ReportExportFormat,
  REPORT_TYPES,
  defaultReportPeriod,
  periodLabel,
  reportHasChart,
  reportTypeName,
} from '../../models/payroll-report.models';

/** Export formats offered in the toolbar dropdown (FR-2). */
const EXPORT_FORMATS: { format: ReportExportFormat; label: string }[] = [
  { format: 'csv', label: 'CSV (.csv)' },
  { format: 'xlsx', label: 'Excel (.xlsx)' },
  { format: 'pdf', label: 'PDF (.pdf)' },
];

/** A single derived bar for the department-wise preview chart. */
interface IChartBar {
  label: string;
  value: number;
}

/**
 * US-PAY-009 (§8, AC-1/AC-2/AC-4): the Payroll Reports page.
 *
 * Notion-style two-pane layout:
 *  - LEFT: a sidebar listing the available report types (FR-1) — selecting one sets
 *    the active report.
 *  - RIGHT: a filter/config panel (pay period + department, FR-3) and a preview area.
 *
 * Preview area, by report type:
 *  - Generic reports → a data table rendered from the BE's `{ columns, rows.cells,
 *    totalRow }` shape (and, for chart-bearing reports, a department-wise horizontal
 *    bar chart DERIVED from the rows in pure SVG/CSS — NO charting library).
 *  - Bank Advice → a table with MASKED account numbers (BR-2) + a "Download Full
 *    File" button (AC-2).
 *
 * Toolbar export buttons (CSV / Excel / PDF) trigger a blob download bypassing the
 * JSON envelope (FR-2, AC-4). On mobile, charts + export/download stay available, but
 * detailed tables defer to desktop with a note (§8).
 */
@Component({
  selector: 'app-payroll-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Payroll Reports</h1>
          <p class="text-sm text-neutral-500 mt-1">Generate, preview and export payroll reports.</p>
        </div>
        <a routerLink="/payroll/analytics" class="btn-secondary text-sm" data-test="analytics-link">Analytics</a>
      </div>

      <div class="lg:grid lg:grid-cols-[16rem_1fr] lg:gap-6">
        <!-- ─── Sidebar: report types (§8, FR-1) ────────────────── -->
        <aside class="mb-6 lg:mb-0" data-test="report-sidebar">
          <nav class="card-notion !p-2 space-y-0.5" role="tablist" aria-label="Report types">
            @for (rt of reportTypes(); track rt.id) {
              <button type="button" role="tab" class="sidebar-item"
                [class.sidebar-active]="activeType() === rt.id"
                [attr.aria-selected]="activeType() === rt.id"
                (click)="selectType(rt.id)" [attr.data-test]="'rt-' + rt.id">
                <span class="font-medium">{{ rt.name }}</span>
                <span class="block text-xs text-neutral-400 mt-0.5">{{ rt.description }}</span>
              </button>
            }
          </nav>
        </aside>

        <!-- ─── Main: filters + preview ─────────────────────────── -->
        <section class="min-w-0">
          <!-- Filter / config panel (FR-3) -->
          <div class="card-notion mb-5" data-test="filter-panel">
            <div class="flex flex-col sm:flex-row sm:items-end gap-4">
              <label class="block flex-1">
                <span class="field-label">Pay period</span>
                <input type="month" class="field" [ngModel]="period()"
                  (ngModelChange)="setPeriod($event)" data-test="period-input" />
              </label>
              <label class="block flex-1">
                <span class="field-label">Department</span>
                <select class="field" [ngModel]="departmentId()"
                  (ngModelChange)="setDepartment($event)" data-test="department-select">
                  <option [ngValue]="null">All departments</option>
                  @for (d of departments(); track d.departmentId) {
                    <option [ngValue]="d.departmentId">{{ d.name }}</option>
                  }
                </select>
              </label>
              <button type="button" class="btn-primary" (click)="generate()"
                [disabled]="isLoading()" data-test="generate-btn"
                [style.background-color]="'var(--brand-primary)'">
                {{ isLoading() ? 'Generating…' : 'Generate' }}
              </button>
            </div>
          </div>

          <!-- Toolbar: title + export menu (FR-2, AC-4) -->
          <div class="flex items-center justify-between mb-3">
            <div>
              <h2 class="text-base font-semibold text-neutral-900">{{ activeName() }}</h2>
              <p class="text-xs text-neutral-400">{{ periodText() }}</p>
            </div>
            @if (!isBankAdvice()) {
              <div class="relative">
                <button type="button" class="btn-secondary text-sm" (click)="toggleExportMenu()"
                  [disabled]="!hasReport() || isExporting()"
                  [attr.aria-expanded]="exportMenuOpen()" data-test="export-btn">
                  {{ isExporting() ? 'Exporting…' : 'Export' }}
                </button>
                @if (exportMenuOpen()) {
                  <div class="export-menu" role="menu" data-test="export-menu">
                    @for (ef of exportFormats; track ef.format) {
                      <button type="button" role="menuitem" class="export-item"
                        (click)="exportAs(ef.format)" [attr.data-test]="'export-' + ef.format">
                        {{ ef.label }}
                      </button>
                    }
                  </div>
                }
              </div>
            }
          </div>

          <!-- ─── Preview area ──────────────────────────────────── -->
          @if (isLoading()) {
            <div class="card-notion space-y-3" aria-busy="true" data-test="preview-skeleton">
              <div class="skeleton-line h-4 w-40"></div>
              @for (_ of [1,2,3,4,5]; track $index) { <div class="skeleton-line h-10 w-full"></div> }
            </div>
          } @else if (isBankAdvice()) {
            <!-- Bank advice preview (AC-2 / BR-2) -->
            @if (bankAdvice(); as ba) {
              <div class="card-notion" data-test="bank-advice">
                <div class="flex items-center justify-between mb-4">
                  <p class="text-sm text-neutral-500">
                    {{ ba.employeeCount }} employees · total
                    <span class="font-medium text-neutral-700 font-mono">{{ ba.totalNetAmount | number:'1.2-2' }}</span>
                  </p>
                  <button type="button" class="btn-primary text-sm" (click)="downloadBankAdvice()"
                    [disabled]="isExporting()" data-test="download-full-btn"
                    [style.background-color]="'var(--brand-primary)'">
                    {{ isExporting() ? 'Preparing…' : 'Download Full File' }}
                  </button>
                </div>
                <p class="text-xs text-amber-600 mb-3" data-test="mask-note">
                  Account numbers are masked in this preview. The downloaded file contains full numbers.
                </p>
                @if (ba.lines.length === 0) {
                  <p class="empty-note" data-test="bank-empty">No payable employees for this period.</p>
                } @else {
                  <!-- Desktop table -->
                  <div class="hidden md:block overflow-x-auto">
                    <table class="w-full text-sm" aria-label="Bank advice preview">
                      <thead>
                        <tr class="border-b border-neutral-100">
                          <th class="th">Employee No</th>
                          <th class="th">Name</th>
                          <th class="th">Bank</th>
                          <th class="th">Branch</th>
                          <th class="th">Account</th>
                          <th class="th text-right">Net Amount</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (line of ba.lines; track line.employeeNo) {
                          <tr class="row" data-test="bank-row">
                            <td class="td text-neutral-500">{{ line.employeeNo }}</td>
                            <td class="td font-medium text-neutral-900">{{ line.employeeName }}</td>
                            <td class="td text-neutral-600">{{ line.bankName }}</td>
                            <td class="td text-neutral-600">{{ line.branchCode }}</td>
                            <td class="td font-mono text-neutral-500" data-test="masked-account">
                              {{ line.accountNumber }}
                            </td>
                            <td class="td text-right font-mono tabular-nums">{{ line.netAmount | number:'1.2-2' }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                  <!-- Mobile: defer detail table, but masked account still shown compactly -->
                  <div class="md:hidden" data-test="bank-mobile-note">
                    <p class="text-xs text-neutral-400 mb-3">View the full table on a larger screen.</p>
                    <ul class="space-y-2">
                      @for (line of ba.lines; track line.employeeNo) {
                        <li class="rounded-lg border border-neutral-100 p-3">
                          <div class="flex justify-between">
                            <span class="font-medium text-neutral-900">{{ line.employeeName }}</span>
                            <span class="font-mono tabular-nums text-sm">{{ line.netAmount | number:'1.2-2' }}</span>
                          </div>
                          <p class="text-xs text-neutral-400 mt-0.5 font-mono">{{ line.accountNumber }}</p>
                        </li>
                      }
                    </ul>
                  </div>
                }
              </div>
            } @else {
              <div class="card-notion text-center py-12" data-test="bank-prompt">
                <p class="text-sm text-neutral-500">Choose a period and click Generate to preview the bank advice.</p>
              </div>
            }
          } @else if (report(); as r) {
            <!-- Generic report: optional derived chart + table -->
            @if (sortedBars().length > 0) {
              <div class="card-notion mb-5" data-test="report-chart">
                <h3 class="card-title">{{ r.columns[0] }} breakdown</h3>
                <ul class="space-y-3">
                  @for (bar of sortedBars(); track bar.label) {
                    <li>
                      <div class="flex items-center justify-between text-xs mb-1">
                        <span class="text-neutral-600 truncate">{{ bar.label }}</span>
                        <span class="font-medium text-neutral-700 font-mono tabular-nums">
                          {{ bar.value | number:'1.0-0' }}
                        </span>
                      </div>
                      <div class="h-2.5 rounded-full bg-neutral-100 overflow-hidden">
                        <div class="h-full rounded-full transition-all duration-500"
                          [style.width.%]="barWidth(bar.value)"
                          [style.background-color]="'var(--brand-primary)'"></div>
                      </div>
                    </li>
                  }
                </ul>
              </div>
            }

            <!-- Data table (desktop) -->
            <div class="card-notion !p-0 hidden md:block overflow-x-auto" data-test="report-table">
              @if (r.rows.length === 0) {
                <p class="empty-note" data-test="table-empty">No data for this period.</p>
              } @else {
                <table class="w-full text-sm" [attr.aria-label]="activeName()">
                  <thead>
                    <tr class="border-b border-neutral-100">
                      @for (col of r.columns; track $index) {
                        <th class="th" [class.text-right]="isNumericColumn($index)">{{ col }}</th>
                      }
                    </tr>
                  </thead>
                  <tbody>
                    @for (row of r.rows; track $index) {
                      <tr class="row" data-test="report-row">
                        @for (val of row.cells; track $index) {
                          <td class="td" [class.text-right]="isNumericColumn($index)"
                            [class.font-mono]="isNumericColumn($index)"
                            [class.tabular-nums]="isNumericColumn($index)">
                            {{ val }}
                          </td>
                        }
                      </tr>
                    }
                  </tbody>
                  @if (r.totalRow; as total) {
                    <tfoot>
                      <tr class="border-t border-neutral-200 font-medium" data-test="report-total">
                        @for (val of total.cells; track $index) {
                          <td class="td" [class.text-right]="isNumericColumn($index)"
                            [class.font-mono]="isNumericColumn($index)"
                            [class.tabular-nums]="isNumericColumn($index)">
                            {{ val }}
                          </td>
                        }
                      </tr>
                    </tfoot>
                  }
                </table>
              }
              @if (r.note) {
                <p class="text-xs text-neutral-400 px-4 py-3" data-test="report-note">{{ r.note }}</p>
              }
            </div>
            <!-- Mobile: defer table -->
            <div class="card-notion md:hidden text-center py-8" data-test="table-mobile-note">
              <p class="text-sm text-neutral-500">The detailed table is best viewed on a larger screen.</p>
              <p class="text-xs text-neutral-400 mt-1">Charts and export remain available here.</p>
            </div>
          } @else {
            <div class="card-notion text-center py-12" data-test="report-prompt">
              <p class="text-sm text-neutral-500">Choose a period and click Generate to preview the report.</p>
            </div>
          }
        </section>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-7xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .card-title { @apply text-sm font-medium text-neutral-700 mb-4; }
    .empty-note { @apply text-sm text-neutral-400 py-10 text-center; }

    .sidebar-item {
      @apply block w-full text-left rounded-lg px-3 py-2.5 text-sm text-neutral-700
        transition-colors hover:bg-neutral-50;
    }
    .sidebar-active { @apply bg-neutral-100 text-neutral-900; }

    .field-label { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .field {
      @apply block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-900 transition-colors focus:border-neutral-400 focus:outline-none;
    }

    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .td { @apply py-3 px-4 align-middle text-neutral-700; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60; }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg px-4 py-2 text-sm font-medium
        text-white transition-all duration-200 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .export-menu {
      @apply absolute right-0 mt-1 w-40 rounded-lg border border-neutral-200 bg-white shadow-md
        z-10 py-1 overflow-hidden;
    }
    .export-item { @apply block w-full text-left px-3 py-2 text-sm text-neutral-700 hover:bg-neutral-50; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class PayrollReportsComponent implements OnInit {
  private readonly reportService = inject(PayrollReportService);
  private readonly departmentService = inject(DepartmentService);
  private readonly toastr = inject(ToastrService);

  readonly exportFormats = EXPORT_FORMATS;

  // ─── State ──────────────────────────────────────────────────
  readonly reportTypes = signal<IReportTypeMeta[]>(REPORT_TYPES);
  readonly activeType = signal<PayrollReportType>('PayrollSummary');
  readonly period = signal(defaultReportPeriod());
  readonly departmentId = signal<string | null>(null);
  readonly departments = signal<IDepartment[]>([]);

  readonly report = signal<IReportResult | null>(null);
  readonly bankAdvice = signal<IBankAdvicePreview | null>(null);
  readonly isLoading = signal(false);
  readonly isExporting = signal(false);
  readonly exportMenuOpen = signal(false);

  readonly isBankAdvice = computed(() => this.activeType() === 'BankAdvice');
  readonly activeName = computed(() => reportTypeName(this.activeType()));
  readonly periodText = computed(() => periodLabel(this.period()));
  readonly hasReport = computed(() => this.report() !== null);

  /**
   * Department-wise bars DERIVED from the report rows (the BE result is a generic
   * table). Only built for chart-bearing report types; uses the first column as the
   * label and the last numeric-parseable column as the value, sorted by value desc.
   */
  readonly sortedBars = computed<IChartBar[]>(() => {
    const r = this.report();
    if (!r || !reportHasChart(r.reportType) || r.rows.length === 0) {
      return [];
    }
    const valueCol = this.lastNumericColumnIndex(r);
    if (valueCol < 0) {
      return [];
    }
    const bars: IChartBar[] = [];
    for (const row of r.rows) {
      const value = parseNumeric(row.cells[valueCol]);
      if (value === null) {
        continue;
      }
      bars.push({ label: row.cells[0] ?? '', value });
    }
    return bars.sort((a, b) => b.value - a.value);
  });

  private readonly maxBar = computed(() =>
    this.sortedBars().reduce((m, b) => Math.max(m, b.value), 0),
  );

  ngOnInit(): void {
    this.reportService.listReportTypes().subscribe({
      next: (list) => {
        if (list.length > 0) {
          this.reportTypes.set(list);
        }
      },
      error: () => {
        // Fall back to the static REPORT_TYPES already seeded in the signal.
      },
    });

    this.departmentService.getDepartments().subscribe({
      next: (list) => this.departments.set(list ?? []),
      error: () => this.departments.set([]),
    });
  }

  // ─── Filters ────────────────────────────────────────────────
  selectType(type: PayrollReportType): void {
    if (this.activeType() === type) {
      return;
    }
    this.activeType.set(type);
    this.exportMenuOpen.set(false);
    // Selecting a new report clears the stale preview; the user re-generates.
    this.report.set(null);
    this.bankAdvice.set(null);
  }

  setPeriod(value: string): void {
    this.period.set(value);
  }

  setDepartment(value: string | null): void {
    this.departmentId.set(value);
  }

  private currentFilters(): IReportFilters {
    return { period: this.period(), departmentId: this.departmentId() };
  }

  // ─── Generate ───────────────────────────────────────────────
  generate(): void {
    if (!this.period()) {
      this.toastr.error('Choose a pay period first.');
      return;
    }
    this.exportMenuOpen.set(false);
    this.isLoading.set(true);

    if (this.isBankAdvice()) {
      this.reportService.getBankAdvicePreview(this.currentFilters()).subscribe({
        next: (preview) => {
          this.bankAdvice.set(preview);
          this.isLoading.set(false);
        },
        error: () => {
          this.bankAdvice.set(null);
          this.isLoading.set(false);
          this.toastr.error('Failed to load the bank advice preview.');
        },
      });
      return;
    }

    this.reportService.getReport(this.activeType(), this.currentFilters()).subscribe({
      next: (result) => {
        this.report.set(result);
        this.isLoading.set(false);
      },
      error: () => {
        this.report.set(null);
        this.isLoading.set(false);
        this.toastr.error('Failed to generate the report.');
      },
    });
  }

  // ─── Export (FR-2, AC-4) ────────────────────────────────────
  toggleExportMenu(): void {
    this.exportMenuOpen.update((v) => !v);
  }

  exportAs(format: ReportExportFormat): void {
    this.exportMenuOpen.set(false);
    if (!this.hasReport()) {
      return;
    }
    this.isExporting.set(true);
    this.reportService.exportReport(this.activeType(), this.currentFilters(), format).subscribe({
      next: (resp) => {
        this.saveDownload(resp, `${this.activeType()}-${this.period()}.${format}`);
        this.isExporting.set(false);
        this.toastr.success('Export downloaded.');
      },
      error: () => {
        this.isExporting.set(false);
        this.toastr.error('Export failed.');
      },
    });
  }

  // ─── Bank advice full download (BR-2, AC-2) ─────────────────
  downloadBankAdvice(): void {
    this.isExporting.set(true);
    this.reportService.downloadBankAdvice(this.currentFilters(), 'csv').subscribe({
      next: (resp) => {
        this.saveDownload(resp, `bank-advice-${this.period()}.csv`);
        this.isExporting.set(false);
        this.toastr.success('Bank advice file downloaded.');
      },
      error: () => {
        this.isExporting.set(false);
        this.toastr.error('Download failed.');
      },
    });
  }

  // ─── View helpers ───────────────────────────────────────────
  barWidth(value: number): number {
    const max = this.maxBar();
    if (max <= 0) {
      return 0;
    }
    return Math.max(2, (Math.max(0, value) / max) * 100);
  }

  /**
   * Whether a column index renders as numeric (right-aligned, monospace). Detected
   * from the current report's first data row: a cell that parses as a number marks
   * its column numeric. Defaults to false when there is no row to sample.
   */
  isNumericColumn(index: number): boolean {
    const r = this.report();
    const sample = r?.rows[0]?.cells[index];
    return sample !== undefined && parseNumeric(sample) !== null;
  }

  /** The index of the last numeric-parseable column (for the derived chart). -1 if none. */
  private lastNumericColumnIndex(r: IReportResult): number {
    const sample = r.rows[0]?.cells ?? [];
    for (let i = sample.length - 1; i >= 1; i--) {
      if (parseNumeric(sample[i]) !== null) {
        return i;
      }
    }
    return -1;
  }

  /** Save a blob response, deriving the filename from Content-Disposition. */
  private saveDownload(resp: HttpResponse<Blob>, fallbackName: string): void {
    const blob = resp.body;
    if (!blob) {
      return;
    }
    const filename = filenameFromDisposition(resp.headers.get('Content-Disposition')) ?? fallbackName;
    downloadBlob(blob, filename);
  }
}

/** Parse a display-formatted numeric cell (strips thousands separators + currency). */
function parseNumeric(cell: string | undefined | null): number | null {
  if (cell === null || cell === undefined) {
    return null;
  }
  const cleaned = cell.replace(/[^0-9.-]/g, '');
  if (cleaned === '' || cleaned === '-' || cleaned === '.') {
    return null;
  }
  const n = Number(cleaned);
  return Number.isFinite(n) ? n : null;
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
  if (typeof document === 'undefined' || typeof URL === 'undefined' || !URL.createObjectURL) {
    return;
  }
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
