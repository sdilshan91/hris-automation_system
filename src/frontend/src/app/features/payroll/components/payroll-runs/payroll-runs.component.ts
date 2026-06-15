import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { PayrollRunService } from '../../services/payroll-run.service';
import { NewPayrollRunComponent } from '../new-payroll-run/new-payroll-run.component';
import {
  IPayrollRun,
  PayrollRunStatus,
  PAY_MONTHS,
  RUN_STATUS_BADGE,
  RUN_STATUS_LABELS,
} from '../../models/payroll-run.models';

type SortKey = 'period' | 'status' | 'employees' | 'net' | 'date';

/**
 * US-PAY-003 (§8): Payroll Runs page — a Notion-style table listing all runs with
 * columns Period, Status (color-coded badge), Employees, Total Net, Initiated By,
 * Date. Sortable (header clicks) and filterable (status pills). The "New Payroll
 * Run" button opens the right slide-over modal; on a successful initiate the user
 * is routed to the run's detail view to watch progress.
 *
 * Mobile: the table collapses to stacked cards (§8 — initiation + progress are
 * available on mobile; the detailed payslip list is deferred to desktop in the
 * detail view).
 */
@Component({
  selector: 'app-payroll-runs',
  standalone: true,
  imports: [CommonModule, RouterLink, NewPayrollRunComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '220ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
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
            <span class="text-neutral-700">Payroll runs</span>
          </nav>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Payroll runs
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Monthly payroll runs for your organisation.
          </p>
        </div>
        <button
          type="button"
          class="self-start rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 sm:self-auto"
          [style.background-color]="'var(--brand-primary)'"
          (click)="openNew()"
        >
          + New payroll run
        </button>
      </div>

      <!-- Status filter pills -->
      @if (!loading() && !error() && runs().length > 0) {
        <div class="mb-4 flex flex-wrap gap-2">
          <button
            type="button"
            class="rounded-full px-3 py-1 text-xs font-medium ring-1 ring-inset transition"
            [class]="
              statusFilter() === null
                ? 'bg-neutral-900 text-white ring-neutral-900'
                : 'bg-white text-neutral-600 ring-neutral-200 hover:bg-neutral-50'
            "
            (click)="setFilter(null)"
          >
            All
          </button>
          @for (s of statusOptions; track s) {
            <button
              type="button"
              class="rounded-full px-3 py-1 text-xs font-medium ring-1 ring-inset transition"
              [class]="
                statusFilter() === s
                  ? 'bg-neutral-900 text-white ring-neutral-900'
                  : 'bg-white text-neutral-600 ring-neutral-200 hover:bg-neutral-50'
              "
              (click)="setFilter(s)"
            >
              {{ statusLabels[s] }}
            </button>
          }
        </div>
      }

      <!-- Loading -->
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
        >
          <p class="text-sm font-medium text-neutral-700">No payroll runs yet</p>
          <p class="mt-1 text-sm text-neutral-500">
            Start your first monthly payroll run to calculate salaries.
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
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 hover:text-neutral-700"
                    (click)="sortBy('period')"
                  >
                    Period {{ sortIndicator('period') }}
                  </button>
                </th>
                <th class="px-4 py-2.5">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 hover:text-neutral-700"
                    (click)="sortBy('status')"
                  >
                    Status {{ sortIndicator('status') }}
                  </button>
                </th>
                <th class="px-4 py-2.5">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 hover:text-neutral-700"
                    (click)="sortBy('employees')"
                  >
                    Employees {{ sortIndicator('employees') }}
                  </button>
                </th>
                <th class="px-4 py-2.5 text-right">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 hover:text-neutral-700"
                    (click)="sortBy('net')"
                  >
                    Total net {{ sortIndicator('net') }}
                  </button>
                </th>
                <th class="px-4 py-2.5">Initiated by</th>
                <th class="px-4 py-2.5">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1 hover:text-neutral-700"
                    (click)="sortBy('date')"
                  >
                    Date {{ sortIndicator('date') }}
                  </button>
                </th>
              </tr>
            </thead>
            <tbody class="divide-y divide-neutral-100">
              @for (r of visibleRuns(); track r.id) {
                <tr
                  class="cursor-pointer transition hover:bg-neutral-50"
                  [routerLink]="['/payroll', 'runs', r.id]"
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
                    {{ r.processedEmployees | number }} /
                    {{ r.totalEmployees | number }}
                    @if (r.skippedEmployees > 0) {
                      <span class="text-xs text-amber-600"
                        >({{ r.skippedEmployees }} skipped)</span
                      >
                    }
                  </td>
                  <td class="px-4 py-3 text-right font-medium text-neutral-900">
                    {{ r.totalNet | number: '1.2-2' }}
                  </td>
                  <td class="px-4 py-3 text-neutral-600">
                    {{ r.initiatedByName || '—' }}
                  </td>
                  <td class="px-4 py-3 text-neutral-600">
                    {{ r.initiatedAt | date: 'mediumDate' }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Cards (mobile) -->
        <div @fadeIn class="space-y-3 md:hidden">
          @for (r of visibleRuns(); track r.id) {
            <a
              [routerLink]="['/payroll', 'runs', r.id]"
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
              <div
                class="mt-3 grid grid-cols-2 gap-2 text-xs text-neutral-500"
              >
                <div>
                  <span class="block text-neutral-400">Employees</span>
                  {{ r.processedEmployees | number }} /
                  {{ r.totalEmployees | number }}
                </div>
                <div class="text-right">
                  <span class="block text-neutral-400">Total net</span>
                  <span class="font-medium text-neutral-900">{{
                    r.totalNet | number: '1.2-2'
                  }}</span>
                </div>
              </div>
            </a>
          }
        </div>
      }
    </div>

    <!-- New run modal -->
    @if (newOpen()) {
      <app-new-payroll-run
        (created)="onCreated($event)"
        (close)="newOpen.set(false)"
      />
    }
  `,
})
export class PayrollRunsComponent implements OnInit {
  private readonly runsService = inject(PayrollRunService);
  private readonly router = inject(Router);

  readonly statusBadge = RUN_STATUS_BADGE;
  readonly statusLabels = RUN_STATUS_LABELS;
  readonly statusOptions: PayrollRunStatus[] = [
    'Queued',
    'Processing',
    'ReviewPending',
    'Approved',
    'Finalized',
    'Cancelled',
  ];

  readonly runs = signal<IPayrollRun[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly newOpen = signal(false);

  readonly statusFilter = signal<PayrollRunStatus | null>(null);
  readonly sortKey = signal<SortKey>('date');
  readonly sortAsc = signal(false);

  /** Filtered + sorted runs for display. */
  readonly visibleRuns = computed(() => {
    const filter = this.statusFilter();
    const list = filter
      ? this.runs().filter((r) => r.status === filter)
      : [...this.runs()];
    const key = this.sortKey();
    const dir = this.sortAsc() ? 1 : -1;
    return list.sort((a, b) => this.compare(a, b, key) * dir);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.runsService.listRuns().subscribe({
      next: (list) => {
        this.runs.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load payroll runs.');
        this.loading.set(false);
      },
    });
  }

  setFilter(status: PayrollRunStatus | null): void {
    this.statusFilter.set(status);
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

  periodLabel(r: IPayrollRun): string {
    const month = PAY_MONTHS.find((m) => m.value === r.payMonth);
    return `${month ? month.label : r.payMonth} ${r.payYear}`;
  }

  openNew(): void {
    this.newOpen.set(true);
  }

  onCreated(run: IPayrollRun): void {
    this.newOpen.set(false);
    this.router.navigate(['/payroll', 'runs', run.id]);
  }

  private compare(a: IPayrollRun, b: IPayrollRun, key: SortKey): number {
    switch (key) {
      case 'period':
        return a.payYear - b.payYear || a.payMonth - b.payMonth;
      case 'status':
        return a.status.localeCompare(b.status);
      case 'employees':
        return a.totalEmployees - b.totalEmployees;
      case 'net':
        return a.totalNet - b.totalNet;
      case 'date':
        return (
          new Date(a.initiatedAt).getTime() -
          new Date(b.initiatedAt).getTime()
        );
    }
  }
}
