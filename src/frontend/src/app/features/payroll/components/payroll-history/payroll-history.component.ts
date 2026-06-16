import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';

import { AuditService } from '../../services/audit.service';
import {
  IPayrollHistoryRun,
} from '../../models/audit.models';
import {
  PayrollRunStatus,
  PAY_MONTHS,
  RUN_STATUS_BADGE,
  RUN_STATUS_LABELS,
} from '../../models/payroll-run.models';

type SortKey = 'period' | 'status' | 'employees' | 'net' | 'finalized';

/**
 * US-PAY-012 (AC-1, §8): the Payroll History page — a Notion-style database table
 * of all payroll runs with the audit-facing columns: Pay Period, Status (colour
 * badge), Employee Count, Total Net, Initiated By, Approved By, Finalized Date.
 *
 * Sortable (header clicks toggle direction) and filterable by YEAR and STATUS
 * (AC-1). Filtering is client-side over the loaded list for instant feedback; the
 * service also accepts year/status server params (the FE re-derives the available
 * years from the returned data). Row click → the run's detail page (which carries
 * the per-run audit timeline + summary, AC-2).
 *
 * Mobile (§8): the table collapses to stacked cards so the history list + run
 * summary stay viewable; the full audit timeline + diff live on the detail/audit
 * pages.
 */
@Component({
  selector: 'app-payroll-history',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div
        class="mb-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between"
      >
        <div>
          <nav class="mb-1 text-xs text-neutral-500" aria-label="Breadcrumb">
            <a routerLink="/payroll" class="hover:text-neutral-700">Payroll</a>
            <span class="px-1">/</span>
            <span class="text-neutral-700">History</span>
          </nav>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Payroll History
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            A complete history of every payroll run, for compliance and audit.
          </p>
        </div>
        <a
          routerLink="/payroll/audit"
          class="self-start rounded-lg border border-neutral-200 bg-white px-4 py-2 text-sm font-medium text-neutral-700 shadow-sm transition hover:bg-neutral-50 sm:self-auto"
          data-test="audit-link"
        >
          View audit trail →
        </a>
      </div>

      <!-- Filters: year + status (AC-1) -->
      @if (!loading() && !error() && runs().length > 0) {
        <div class="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center">
          <!-- Year pills -->
          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              class="filter-pill"
              [class]="pillClass(yearFilter() === null)"
              (click)="setYear(null)"
            >
              All years
            </button>
            @for (y of years(); track y) {
              <button
                type="button"
                class="filter-pill"
                [class]="pillClass(yearFilter() === y)"
                (click)="setYear(y)"
              >
                {{ y }}
              </button>
            }
          </div>
          <!-- Status select -->
          <div class="sm:ml-auto">
            <label class="sr-only" for="history-status">Status</label>
            <select
              id="history-status"
              class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm text-neutral-700 focus:border-neutral-400 focus:outline-none"
              [value]="statusFilter() ?? ''"
              (change)="onStatusChange($event)"
            >
              <option value="">All statuses</option>
              @for (s of statusOptions; track s) {
                <option [value]="s">{{ statusLabels[s] }}</option>
              }
            </select>
          </div>
        </div>
      }

      <!-- Loading / error / empty -->
      @if (loading()) {
        <div class="space-y-2">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="h-14 animate-pulse rounded-lg bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (runs().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-300 bg-white px-6 py-16 text-center"
          data-test="empty"
        >
          <p class="text-sm font-medium text-neutral-700">No payroll history</p>
          <p class="mt-1 text-sm text-neutral-500">
            Run your first monthly payroll to start building history.
          </p>
        </div>
      } @else {
        <!-- Table (desktop) -->
        <div
          @fadeIn
          class="hidden overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm md:block"
        >
          <table class="w-full text-left text-sm">
            <thead
              class="border-b border-neutral-200 bg-neutral-50 text-xs font-medium uppercase tracking-wide text-neutral-500"
            >
              <tr>
                <th class="px-4 py-2.5">
                  <button type="button" class="th-sort" (click)="sortBy('period')">
                    Pay period {{ sortIndicator('period') }}
                  </button>
                </th>
                <th class="px-4 py-2.5">
                  <button type="button" class="th-sort" (click)="sortBy('status')">
                    Status {{ sortIndicator('status') }}
                  </button>
                </th>
                <th class="px-4 py-2.5">
                  <button
                    type="button"
                    class="th-sort"
                    (click)="sortBy('employees')"
                  >
                    Employees {{ sortIndicator('employees') }}
                  </button>
                </th>
                <th class="px-4 py-2.5 text-right">
                  <button type="button" class="th-sort" (click)="sortBy('net')">
                    Total net {{ sortIndicator('net') }}
                  </button>
                </th>
                <th class="px-4 py-2.5">Initiated by</th>
                <th class="px-4 py-2.5">Approved by</th>
                <th class="px-4 py-2.5">
                  <button
                    type="button"
                    class="th-sort"
                    (click)="sortBy('finalized')"
                  >
                    Finalized {{ sortIndicator('finalized') }}
                  </button>
                </th>
              </tr>
            </thead>
            <tbody class="divide-y divide-neutral-100">
              @for (r of visibleRuns(); track r.runId) {
                <tr
                  class="cursor-pointer transition hover:bg-neutral-50"
                  [routerLink]="['/payroll', 'runs', r.runId]"
                  data-test="history-row"
                >
                  <td class="px-4 py-3 font-medium text-neutral-900">
                    {{ periodLabel(r) }}
                  </td>
                  <td class="px-4 py-3">
                    <span
                      class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                      [class]="statusBadge[r.status]"
                    >
                      {{ statusLabels[r.status] }}
                    </span>
                  </td>
                  <td class="px-4 py-3 text-neutral-700">
                    {{ r.employeeCount | number }}
                  </td>
                  <td
                    class="px-4 py-3 text-right font-medium tabular-nums text-neutral-900"
                  >
                    {{ r.totalNet | number: '1.2-2' }}
                  </td>
                  <td class="px-4 py-3 text-neutral-600">
                    {{ r.initiatedBy || '—' }}
                  </td>
                  <td class="px-4 py-3 text-neutral-600">
                    {{ r.approvedBy || '—' }}
                  </td>
                  <td class="px-4 py-3 text-neutral-600">
                    {{ r.finalizedAt ? (r.finalizedAt | date: 'mediumDate') : '—' }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
          @if (visibleRuns().length === 0) {
            <div class="px-4 py-10 text-center text-sm text-neutral-500">
              No runs match the current filters.
            </div>
          }
        </div>

        <!-- Cards (mobile) -->
        <div @fadeIn class="space-y-3 md:hidden">
          @for (r of visibleRuns(); track r.runId) {
            <a
              [routerLink]="['/payroll', 'runs', r.runId]"
              class="block rounded-xl border border-neutral-200 bg-white p-4 shadow-sm transition hover:bg-neutral-50"
            >
              <div class="flex items-center justify-between">
                <span class="font-medium text-neutral-900">{{
                  periodLabel(r)
                }}</span>
                <span
                  class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="statusBadge[r.status]"
                >
                  {{ statusLabels[r.status] }}
                </span>
              </div>
              <div class="mt-3 grid grid-cols-2 gap-2 text-xs text-neutral-500">
                <div>
                  <span class="block text-neutral-400">Employees</span>
                  {{ r.employeeCount | number }}
                </div>
                <div class="text-right">
                  <span class="block text-neutral-400">Total net</span>
                  <span class="font-medium text-neutral-900">{{
                    r.totalNet | number: '1.2-2'
                  }}</span>
                </div>
                <div>
                  <span class="block text-neutral-400">Approved by</span>
                  {{ r.approvedBy || '—' }}
                </div>
                <div class="text-right">
                  <span class="block text-neutral-400">Finalized</span>
                  {{ r.finalizedAt ? (r.finalizedAt | date: 'mediumDate') : '—' }}
                </div>
              </div>
            </a>
          }
          @if (visibleRuns().length === 0) {
            <p class="py-8 text-center text-sm text-neutral-500">
              No runs match the current filters.
            </p>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      .filter-pill {
        border-radius: 9999px;
        padding: 0.25rem 0.75rem;
        font-size: 0.75rem;
        font-weight: 500;
        transition: background-color 0.2s;
      }
      .th-sort {
        display: inline-flex;
        align-items: center;
        gap: 0.25rem;
      }
      .th-sort:hover {
        color: rgb(64 64 64);
      }
    `,
  ],
})
export class PayrollHistoryComponent implements OnInit {
  private readonly auditService = inject(AuditService);

  readonly statusBadge = RUN_STATUS_BADGE;
  readonly statusLabels = RUN_STATUS_LABELS;
  readonly statusOptions: PayrollRunStatus[] = [
    'Queued',
    'Processing',
    'ReviewPending',
    'AwaitingApproval',
    'Approved',
    'Finalized',
    'Rejected',
    'Cancelled',
  ];

  readonly runs = signal<IPayrollHistoryRun[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly yearFilter = signal<number | null>(null);
  readonly statusFilter = signal<PayrollRunStatus | null>(null);
  readonly sortKey = signal<SortKey>('period');
  readonly sortAsc = signal(false);

  /** Distinct years present in the data, newest-first (AC-1 year filter). */
  readonly years = computed(() => {
    const set = new Set(this.runs().map((r) => r.payYear));
    return Array.from(set).sort((a, b) => b - a);
  });

  /** Filtered (year + status) + sorted runs for display. */
  readonly visibleRuns = computed(() => {
    const year = this.yearFilter();
    const status = this.statusFilter();
    let list = this.runs().filter(
      (r) =>
        (year === null || r.payYear === year) &&
        (status === null || r.status === status),
    );
    const key = this.sortKey();
    const dir = this.sortAsc() ? 1 : -1;
    return [...list].sort((a, b) => this.compare(a, b, key) * dir);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.auditService.getHistory().subscribe({
      next: (page) => {
        this.runs.set(page.items);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load payroll history.');
        this.loading.set(false);
      },
    });
  }

  setYear(year: number | null): void {
    this.yearFilter.set(year);
  }

  onStatusChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.statusFilter.set(value ? (value as PayrollRunStatus) : null);
  }

  sortBy(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortAsc.update((a) => !a);
    } else {
      this.sortKey.set(key);
      this.sortAsc.set(true);
    }
  }

  sortIndicator(key: SortKey): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.sortAsc() ? '↑' : '↓';
  }

  pillClass(active: boolean): string {
    return active
      ? 'bg-neutral-900 text-white'
      : 'bg-white text-neutral-600 ring-1 ring-inset ring-neutral-200 hover:bg-neutral-50';
  }

  periodLabel(r: IPayrollHistoryRun): string {
    const month = PAY_MONTHS.find((m) => m.value === r.payMonth);
    return `${month ? month.label : r.payMonth} ${r.payYear}`;
  }

  private compare(
    a: IPayrollHistoryRun,
    b: IPayrollHistoryRun,
    key: SortKey,
  ): number {
    switch (key) {
      case 'period':
        return a.payYear - b.payYear || a.payMonth - b.payMonth;
      case 'status':
        return a.status.localeCompare(b.status);
      case 'employees':
        return a.employeeCount - b.employeeCount;
      case 'net':
        return a.totalNet - b.totalNet;
      case 'finalized':
        return (
          new Date(a.finalizedAt ?? 0).getTime() -
          new Date(b.finalizedAt ?? 0).getTime()
        );
    }
  }
}
