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
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { MyPayslipService } from '../../services/my-payslip.service';
import {
  IMyPayslipDetail,
  IMyPayslipListItem,
  hasYtd,
  payPeriodLabel,
} from '../../models/my-payslip.models';

/**
 * US-PAY-005: Employee self-service "My Payslips" view.
 *
 * Lists the authenticated employee's payslips from Finalized payroll runs (FR-2/BR-1)
 * as a Notion-style table (Pay Period | Gross Earnings | Deductions | Net Salary),
 * most-recent-first with right-aligned monospace salary amounts (§8). A year filter
 * (FR-6) is rendered as horizontal tabs. Clicking a row expands an inline detail card
 * (§8 AC-2) showing the earnings (green-tinted) and deductions (red-tinted) breakdown
 * with an optional YTD column (FR-7), plus a "Download PDF" secondary button (FR-4/AC-3)
 * that streams the pre-generated tenant-branded PDF (US-PAY-004) and anchor-clicks it.
 *
 * Authorization is enforced by the backend (`Payroll.Read.Self`); this is purely the
 * employee's own data. Mobile (AC-5): the table collapses to full-width stacked cards
 * and the breakdown table scrolls horizontally; readable at 360px.
 */
@Component({
  selector: 'app-my-payslips',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('expand', [
      transition(':enter', [
        style({ opacity: 0, height: 0 }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0, height: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">My Payslips</h1>
        <p class="text-sm text-neutral-500 mt-1">
          View and download your monthly payslips.
        </p>
      </div>

      <!-- Year filter tabs (FR-6) -->
      @if (years().length > 0) {
        <div class="year-tabs" role="tablist" aria-label="Filter payslips by year" data-test="year-tabs">
          @for (y of years(); track y) {
            <button
              type="button"
              role="tab"
              class="year-tab"
              [class.year-tab-active]="selectedYear() === y"
              [attr.aria-selected]="selectedYear() === y"
              (click)="selectYear(y)"
              [attr.data-test]="'year-' + y"
            >
              {{ y }}
            </button>
          }
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion p-0 overflow-hidden" aria-busy="true" data-test="skeleton">
          @for (_ of [1,2,3,4,5]; track $index) {
            <div class="skeleton-row">
              <div class="skeleton-line h-4 w-32"></div>
              <div class="skeleton-line h-4 w-20 ml-auto"></div>
            </div>
          }
        </div>
      } @else if (payslips().length === 0) {
        <!-- Empty state -->
        <div class="card-notion text-center py-16" @fadeIn data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No payslips yet</h3>
          <p class="text-sm text-neutral-500">
            Payslips appear here once your payroll has been finalized.
          </p>
        </div>
      } @else {
        <!-- Payslip list (§8 Notion-style table → stacked cards on mobile) -->
        <div class="card-notion p-0 overflow-hidden" @fadeIn>
          <!-- Header row (desktop only) -->
          <div class="list-head" aria-hidden="true">
            <span class="col-period">Pay Period</span>
            <span class="col-amount">Gross Earnings</span>
            <span class="col-amount">Deductions</span>
            <span class="col-amount">Net Salary</span>
            <span class="col-chevron"></span>
          </div>

          <ul class="list-none m-0 p-0" role="list">
            @for (p of payslips(); track p.payslipId) {
              <li class="list-item-wrap" [class.is-open]="expandedId() === p.payslipId">
                <button
                  type="button"
                  class="list-item"
                  (click)="toggle(p)"
                  [attr.aria-expanded]="expandedId() === p.payslipId"
                  [attr.aria-label]="'View payslip for ' + period(p)"
                  [attr.data-test]="'row-' + p.payslipId"
                >
                  <span class="col-period">
                    <span class="row-label-mobile">Pay Period</span>
                    <span class="font-medium text-neutral-900">{{ period(p) }}</span>
                  </span>
                  <span class="col-amount">
                    <span class="row-label-mobile">Gross</span>
                    <span class="amount">{{ p.grossEarnings | currency:'':'':'1.2-2' }}</span>
                  </span>
                  <span class="col-amount">
                    <span class="row-label-mobile">Deductions</span>
                    <span class="amount text-rose-600">{{ p.totalDeductions | currency:'':'':'1.2-2' }}</span>
                  </span>
                  <span class="col-amount">
                    <span class="row-label-mobile">Net Salary</span>
                    <span class="amount font-semibold text-neutral-900">{{ p.netSalary | currency:'':'':'1.2-2' }}</span>
                  </span>
                  <span class="col-chevron">
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                      class="w-5 h-5 text-neutral-400 transition-transform"
                      [class.rotate-180]="expandedId() === p.payslipId" aria-hidden="true">
                      <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 0 1 1.06.02L10 11.168l3.71-3.938a.75.75 0 1 1 1.08 1.04l-4.25 4.5a.75.75 0 0 1-1.08 0l-4.25-4.5a.75.75 0 0 1 .02-1.06Z" clip-rule="evenodd"/>
                    </svg>
                  </span>
                </button>

                <!-- Expandable detail (AC-2) -->
                @if (expandedId() === p.payslipId) {
                  <div class="detail" @expand [attr.data-test]="'detail-' + p.payslipId">
                    @if (detailLoading()) {
                      <div class="space-y-2" aria-busy="true" data-test="detail-skeleton">
                        <div class="skeleton-line h-4 w-full"></div>
                        <div class="skeleton-line h-4 w-3/4"></div>
                        <div class="skeleton-line h-4 w-1/2"></div>
                      </div>
                    } @else if (detail(); as d) {
                      <!-- Employee snapshot -->
                      @if (d.employee) {
                        <div class="detail-meta">
                          <span class="font-medium text-neutral-800">{{ d.employee.name }}</span>
                          <span class="text-neutral-400">·</span>
                          <span>{{ d.employee.employeeNo }}</span>
                          @if (d.employee.designation) {
                            <span class="text-neutral-400">·</span>
                            <span>{{ d.employee.designation }}</span>
                          }
                          @if (d.employee.department) {
                            <span class="text-neutral-400">·</span>
                            <span>{{ d.employee.department }}</span>
                          }
                        </div>
                      }

                      <!-- Breakdown tables (horizontal scroll on mobile, AC-5) -->
                      <div class="breakdown-scroll">
                        <div class="breakdown-grid">
                          <!-- Earnings (green-tinted) -->
                          <div class="breakdown-col">
                            <h4 class="breakdown-title text-emerald-700">Earnings</h4>
                            <table class="breakdown-table">
                              <thead>
                                <tr>
                                  <th scope="col">Component</th>
                                  <th scope="col" class="text-right">Amount</th>
                                  @if (showYtd()) {
                                    <th scope="col" class="text-right">YTD</th>
                                  }
                                </tr>
                              </thead>
                              <tbody>
                                @for (line of d.earnings; track line.componentName) {
                                  <tr class="row-earning">
                                    <td>{{ line.componentName }}</td>
                                    <td class="amount text-right">{{ line.amount | currency:'':'':'1.2-2' }}</td>
                                    @if (showYtd()) {
                                      <td class="amount text-right text-neutral-500">
                                        {{ line.ytdAmount != null ? (line.ytdAmount | currency:'':'':'1.2-2') : '—' }}
                                      </td>
                                    }
                                  </tr>
                                }
                                <tr class="row-total">
                                  <td>Gross Earnings</td>
                                  <td class="amount text-right">{{ d.grossEarnings | currency:'':'':'1.2-2' }}</td>
                                  @if (showYtd()) { <td></td> }
                                </tr>
                              </tbody>
                            </table>
                          </div>

                          <!-- Deductions (red-tinted) -->
                          <div class="breakdown-col">
                            <h4 class="breakdown-title text-rose-700">Deductions</h4>
                            <table class="breakdown-table">
                              <thead>
                                <tr>
                                  <th scope="col">Component</th>
                                  <th scope="col" class="text-right">Amount</th>
                                  @if (showYtd()) {
                                    <th scope="col" class="text-right">YTD</th>
                                  }
                                </tr>
                              </thead>
                              <tbody>
                                @for (line of d.deductions; track line.componentName) {
                                  <tr class="row-deduction">
                                    <td>{{ line.componentName }}</td>
                                    <td class="amount text-right">{{ line.amount | currency:'':'':'1.2-2' }}</td>
                                    @if (showYtd()) {
                                      <td class="amount text-right text-neutral-500">
                                        {{ line.ytdAmount != null ? (line.ytdAmount | currency:'':'':'1.2-2') : '—' }}
                                      </td>
                                    }
                                  </tr>
                                } @empty {
                                  <tr><td class="text-neutral-400" [attr.colspan]="showYtd() ? 3 : 2">No deductions</td></tr>
                                }
                                <tr class="row-total">
                                  <td>Total Deductions</td>
                                  <td class="amount text-right">{{ d.totalDeductions | currency:'':'':'1.2-2' }}</td>
                                  @if (showYtd()) { <td></td> }
                                </tr>
                              </tbody>
                            </table>
                          </div>
                        </div>
                      </div>

                      <!-- Net + days + download -->
                      <div class="detail-footer">
                        <div class="net-banner">
                          <span class="text-sm text-neutral-500">Net Salary</span>
                          <span class="amount text-lg font-semibold text-neutral-900">
                            {{ d.netSalary | currency:'':'':'1.2-2' }}
                          </span>
                        </div>
                        <div class="days-line">
                          Working {{ d.workingDays }} · Paid {{ d.paidDays }}
                          @if (d.lopDays > 0) { · LOP {{ d.lopDays }} }
                        </div>
                        <button
                          type="button"
                          class="btn-download"
                          (click)="download(p)"
                          [disabled]="downloadingId() === p.payslipId || !p.pdfAvailable"
                          [attr.aria-label]="'Download PDF for ' + period(p)"
                          [attr.data-test]="'download-' + p.payslipId"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                            class="w-4 h-4" aria-hidden="true">
                            <path d="M10.75 2.75a.75.75 0 0 0-1.5 0v8.614L6.295 8.235a.75.75 0 1 0-1.09 1.03l4.25 4.5a.75.75 0 0 0 1.09 0l4.25-4.5a.75.75 0 0 0-1.09-1.03l-2.955 3.129V2.75Z"/>
                            <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z"/>
                          </svg>
                          {{ downloadingId() === p.payslipId ? 'Downloading…' : 'Download PDF' }}
                        </button>
                      </div>
                    } @else if (detailError()) {
                      <p class="text-sm text-rose-600" data-test="detail-error">
                        Unable to load this payslip. Please try again.
                      </p>
                    }
                  </div>
                }
              </li>
            }
          </ul>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-4xl mx-auto p-4 sm:p-6; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm; }

    /* Year tabs */
    .year-tabs { @apply flex flex-wrap gap-1 mb-4; }
    .year-tab {
      @apply rounded-lg px-3 py-1.5 text-sm font-medium text-neutral-500
        transition-colors hover:bg-neutral-100 hover:text-neutral-800
        focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-1;
    }
    .year-tab-active { @apply bg-brand-50 text-brand-700; }

    /* List header (desktop grid) */
    .list-head {
      @apply hidden md:grid items-center gap-3 px-5 py-3 border-b border-neutral-100
        text-[11px] font-medium uppercase tracking-wider text-neutral-400;
      grid-template-columns: 1.6fr 1fr 1fr 1fr 1.5rem;
    }
    .col-amount { @apply text-right; }

    .list-item-wrap { @apply border-b border-neutral-100 last:border-b-0; }
    .list-item-wrap.is-open { @apply bg-neutral-50/40; }

    /* Row: grid on desktop, stacked card on mobile (AC-5) */
    .list-item {
      @apply w-full text-left px-5 py-4 transition-colors hover:bg-neutral-50
        focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-brand-500;
      @apply flex flex-col gap-2 md:grid md:items-center md:gap-3;
      grid-template-columns: 1.6fr 1fr 1fr 1fr 1.5rem;
    }
    .list-item .col-period,
    .list-item .col-amount {
      @apply flex items-center justify-between md:block;
    }
    .list-item .col-amount { @apply md:text-right; }
    .row-label-mobile {
      @apply md:hidden text-[11px] font-medium uppercase tracking-wider text-neutral-400;
    }
    .col-chevron { @apply hidden md:flex justify-end; }

    .amount {
      @apply font-mono tabular-nums text-sm text-neutral-700;
    }

    /* Detail card */
    .detail { @apply px-5 pb-5 pt-1 overflow-hidden; }
    .detail-meta { @apply flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-neutral-500 mb-4; }

    .breakdown-scroll { @apply -mx-1 overflow-x-auto; }
    .breakdown-grid { @apply grid grid-cols-1 lg:grid-cols-2 gap-4 min-w-[34rem] px-1; }
    .breakdown-col { @apply min-w-0; }
    .breakdown-title { @apply text-xs font-semibold uppercase tracking-wider mb-2; }
    .breakdown-table { @apply w-full border-collapse text-sm; }
    .breakdown-table th {
      @apply text-left text-[11px] font-medium uppercase tracking-wider text-neutral-400 pb-1.5 px-2;
    }
    .breakdown-table td { @apply px-2 py-1.5 text-neutral-700; }
    .row-earning { @apply bg-emerald-50/60; }
    .row-deduction { @apply bg-rose-50/60; }
    .row-total { @apply font-semibold text-neutral-900 border-t border-neutral-200; }
    .row-total td { @apply py-2; }

    .detail-footer { @apply flex flex-wrap items-center gap-3 mt-4 pt-4 border-t border-neutral-100; }
    .net-banner {
      @apply flex items-baseline gap-2 rounded-lg bg-brand-50 px-4 py-2;
    }
    .days-line { @apply text-xs text-neutral-500 flex-1; }

    .btn-download {
      @apply inline-flex items-center gap-2 rounded-lg border border-neutral-200 bg-white px-4 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-1
        disabled:opacity-50 disabled:cursor-not-allowed;
    }

    /* Skeletons */
    .skeleton-row {
      @apply flex items-center gap-3 px-5 py-4 border-b border-neutral-100 last:border-b-0;
    }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class MyPayslipsComponent implements OnInit, OnDestroy {
  private readonly service = inject(MyPayslipService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // ─── State ──────────────────────────────────────────────────
  readonly payslips = signal<IMyPayslipListItem[]>([]);
  readonly years = signal<number[]>([]);
  readonly selectedYear = signal<number | null>(null);
  readonly isLoading = signal(true);

  readonly expandedId = signal<string | null>(null);
  readonly detail = signal<IMyPayslipDetail | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal(false);
  readonly downloadingId = signal<string | null>(null);

  // ─── Computed ───────────────────────────────────────────────
  /** Show the YTD column only when the loaded detail carries YTD amounts (FR-7). */
  readonly showYtd = computed(() => hasYtd(this.detail()));

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.load(null, true);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /**
   * Load the payslip list, optionally filtered by year. On the first load
   * (`deriveYears`) the year-filter tabs are derived from the returned payslips so the
   * employee can switch between the years they have payslips for.
   */
  load(year: number | null, deriveYears = false): void {
    this.isLoading.set(true);
    this.expandedId.set(null);
    this.detail.set(null);
    this.service
      .listMyPayslips(year)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (page) => {
          const sorted = [...page.items].sort(this.byMostRecent);
          this.payslips.set(sorted);
          if (deriveYears) {
            this.deriveYears(sorted);
          }
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.payslips.set([]);
          this.toastr.error('Failed to load your payslips.');
        },
      });
  }

  selectYear(year: number): void {
    if (this.selectedYear() === year) {
      return;
    }
    this.selectedYear.set(year);
    this.load(year);
  }

  /** Expand/collapse a payslip row, lazy-loading its detail on first open (AC-2). */
  toggle(p: IMyPayslipListItem): void {
    if (this.expandedId() === p.payslipId) {
      this.expandedId.set(null);
      return;
    }
    this.expandedId.set(p.payslipId);
    this.detail.set(null);
    this.detailError.set(false);
    this.detailLoading.set(true);
    this.service
      .getMyPayslip(p.payslipId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (d) => {
          // Ignore a stale response if the user collapsed/switched rows meanwhile.
          if (this.expandedId() === p.payslipId) {
            this.detail.set(d);
          }
          this.detailLoading.set(false);
        },
        error: () => {
          this.detailLoading.set(false);
          this.detailError.set(true);
        },
      });
  }

  /** Stream the pre-generated PDF and trigger a browser download (FR-4/AC-3). */
  download(p: IMyPayslipListItem): void {
    if (this.downloadingId() || !p.pdfAvailable) {
      return;
    }
    this.downloadingId.set(p.payslipId);
    this.service
      .downloadMyPayslipPdf(p.payslipId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (resp: HttpResponse<Blob>) => {
          this.downloadingId.set(null);
          const blob = resp.body;
          if (!blob) {
            this.toastr.error('The payslip PDF is not available.');
            return;
          }
          const filename =
            filenameFromDisposition(resp.headers.get('Content-Disposition')) ??
            `payslip_${this.period(p).replace(/\s+/g, '_')}.pdf`;
          downloadBlob(blob, filename);
        },
        error: () => {
          this.downloadingId.set(null);
          this.toastr.error('Failed to download the payslip PDF.');
        },
      });
  }

  // ─── View helpers ───────────────────────────────────────────
  period(p: IMyPayslipListItem): string {
    return payPeriodLabel(p.payMonth, p.payYear);
  }

  /** Sort comparator: most-recent pay period first (AC-1). */
  private byMostRecent = (a: IMyPayslipListItem, b: IMyPayslipListItem): number => {
    if (a.payYear !== b.payYear) {
      return b.payYear - a.payYear;
    }
    return b.payMonth - a.payMonth;
  };

  /** Build the unique, descending list of years for the filter tabs (FR-6). */
  private deriveYears(items: IMyPayslipListItem[]): void {
    const unique = Array.from(new Set(items.map((i) => i.payYear))).sort(
      (a, b) => b - a,
    );
    this.years.set(unique);
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
