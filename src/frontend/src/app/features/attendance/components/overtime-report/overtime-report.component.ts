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
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  IOvertimeReportRow,
  IOvertimeReportResult,
  formatWorkMinutes,
} from '../../models/attendance.models';

/** Sortable report columns (AC-5, §8). */
type SortKey =
  | 'employeeName'
  | 'approvedMinutes'
  | 'pendingMinutes'
  | 'rejectedMinutes'
  | 'recordCount';
type SortDir = 'asc' | 'desc';

/**
 * US-ATT-006 (AC-5): HR monthly overtime report.
 *
 * A Notion-style sortable table of overtime aggregated per employee for the selected
 * month — approved / pending / rejected minutes and record count — with a month selector
 * and a client-side CSV export (§8). Role-gated to HR / Tenant Admin via the route guard.
 */
@Component({
  selector: 'app-overtime-report',
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
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Overtime Report</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Approved, pending and rejected overtime by employee for the selected month.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <label class="sr-only" for="report-month">Month</label>
          <input id="report-month" type="month" class="field" [ngModel]="month()"
            (ngModelChange)="onMonthChange($event)" data-test="month-selector" />
          <button type="button" class="btn-secondary text-sm" (click)="exportCsv()"
            [disabled]="rows().length === 0" data-test="export-csv">Export CSV</button>
        </div>
      </div>

      @if (isLoading()) {
        <div class="card-notion space-y-3" aria-busy="true" data-test="skeleton">
          @for (_ of [1,2,3,4]; track $index) {
            <div class="skeleton-line h-10 w-full"></div>
          }
        </div>
      } @else if (rows().length === 0) {
        <div class="card-notion text-center py-16" @fadeIn data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No overtime this month</h3>
          <p class="text-sm text-neutral-500">No overtime records were found for {{ month() }}.</p>
        </div>
      } @else {
        <div class="card-notion !p-0 overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="Monthly overtime report">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th"><button type="button" class="th-btn" (click)="sortBy('employeeName')"
                  data-test="sort-employeeName">Employee {{ sortIndicator('employeeName') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('approvedMinutes')"
                  data-test="sort-approvedMinutes">Approved {{ sortIndicator('approvedMinutes') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('pendingMinutes')"
                  data-test="sort-pendingMinutes">Pending {{ sortIndicator('pendingMinutes') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('rejectedMinutes')"
                  data-test="sort-rejectedMinutes">Rejected {{ sortIndicator('rejectedMinutes') }}</button></th>
                <th class="th text-right"><button type="button" class="th-btn" (click)="sortBy('recordCount')"
                  data-test="sort-recordCount">Records {{ sortIndicator('recordCount') }}</button></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.employeeId) {
                <tr class="row" data-test="report-row">
                  <td class="td font-medium text-neutral-900">{{ row.employeeName }}</td>
                  <td class="td text-right text-green-700">{{ minutes(row.approvedMinutes) }}</td>
                  <td class="td text-right text-amber-700">{{ minutes(row.pendingMinutes) }}</td>
                  <td class="td text-right text-red-700">{{ minutes(row.rejectedMinutes) }}</td>
                  <td class="td text-right text-neutral-600">{{ row.recordCount }}</td>
                </tr>
              }
            </tbody>
            @if (totals(); as t) {
              <tfoot>
                <tr class="border-t border-neutral-200 font-semibold" data-test="report-totals">
                  <td class="td text-neutral-900">Total</td>
                  <td class="td text-right text-green-700">{{ minutes(t.approvedMinutes) }}</td>
                  <td class="td text-right text-amber-700">{{ minutes(t.pendingMinutes) }}</td>
                  <td class="td text-right text-red-700">{{ minutes(t.rejectedMinutes) }}</td>
                  <td class="td text-right text-neutral-700">{{ t.recordCount }}</td>
                </tr>
              </tfoot>
            }
          </table>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-5xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .th-btn { @apply inline-flex items-center gap-1 hover:text-neutral-600 transition-colors; }
    .td { @apply py-3 px-4 align-middle; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60; }
    .field {
      @apply block rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class OvertimeReportComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // ─── State ──────────────────────────────────────────────────
  readonly month = signal(currentMonthIso());
  readonly isLoading = signal(true);
  readonly result = signal<IOvertimeReportResult | null>(null);
  readonly sortKey = signal<SortKey>('approvedMinutes');
  readonly sortDir = signal<SortDir>('desc');

  // ─── Computed ───────────────────────────────────────────────
  readonly totals = computed(() => this.result()?.totals ?? null);

  readonly rows = computed<IOvertimeReportRow[]>(() => {
    const items = this.result()?.items ?? [];
    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    return [...items].sort((a, b) => {
      const av = a[key];
      const bv = b[key];
      if (typeof av === 'string' && typeof bv === 'string') {
        return av.localeCompare(bv) * dir;
      }
      return ((av as number) - (bv as number)) * dir;
    });
  });

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getOvertimeReport(this.month())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.result.set(res);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.result.set(null);
          this.toastr.error('Failed to load the overtime report.');
        },
      });
  }

  onMonthChange(value: string): void {
    if (!value) {
      return;
    }
    this.month.set(value);
    this.load();
  }

  sortBy(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortDir.update((d) => (d === 'asc' ? 'desc' : 'asc'));
    } else {
      this.sortKey.set(key);
      this.sortDir.set(key === 'employeeName' ? 'asc' : 'desc');
    }
  }

  sortIndicator(key: SortKey): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.sortDir() === 'asc' ? '↑' : '↓';
  }

  minutes(m: number): string {
    return formatWorkMinutes(m);
  }

  /**
   * AC-5 (§8): export the currently sorted report rows to a CSV download, client-side.
   * Minutes are emitted as raw integers so the export is machine-friendly.
   */
  exportCsv(): void {
    const rows = this.rows();
    if (rows.length === 0) {
      return;
    }
    const header = [
      'Employee',
      'Approved Minutes',
      'Pending Minutes',
      'Rejected Minutes',
      'Record Count',
    ];
    const lines = rows.map((r) =>
      [
        csvCell(r.employeeName),
        r.approvedMinutes,
        r.pendingMinutes,
        r.rejectedMinutes,
        r.recordCount,
      ].join(','),
    );
    const csv = [header.join(','), ...lines].join('\r\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `overtime-report-${this.month()}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    this.toastr.success('Report exported.');
  }
}

/** Current month as `yyyy-MM` in the browser's local timezone. */
function currentMonthIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  return `${y}-${m}`;
}

/** Quote a CSV cell when it contains a comma, quote or newline. */
function csvCell(value: string): string {
  const v = value ?? '';
  if (/[",\r\n]/.test(v)) {
    return `"${v.replace(/"/g, '""')}"`;
  }
  return v;
}
