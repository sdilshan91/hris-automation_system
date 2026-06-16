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
import { PayrollApprovalService } from '../../services/payroll-approval.service';
import {
  IPayrollRun,
  PAY_MONTHS,
  RUN_STATUS_BADGE,
  RUN_STATUS_LABELS,
} from '../../models/payroll-run.models';

/**
 * US-PAY-008 (§8): "Pending Approvals" queue for approvers — the list of payroll
 * runs in AwaitingApproval that this approver can act on. A dedicated lazy route
 * (`/payroll/approvals`) gated on `Payroll.Approve`.
 *
 * Notion-style card per pending run (period, totals, submitted-by) with a badge
 * count in the header (§8). Each card links to the run detail where the approver
 * reviews + approves/rejects/returns. Mobile-friendly (the approve/reject actions
 * live on the detail page, which supports mobile; the per-employee drill-down there
 * is desktop-only).
 */
@Component({
  selector: 'app-pending-approvals',
  standalone: true,
  imports: [CommonModule, RouterLink],
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
    <div class="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <nav class="mb-1 text-xs text-neutral-500" aria-label="Breadcrumb">
          <a routerLink="/payroll" class="hover:text-neutral-700">Payroll</a>
          <span class="px-1">/</span>
          <span class="text-neutral-700">Pending approvals</span>
        </nav>
        <div class="flex items-center gap-3">
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Pending approvals
          </h1>
          @if (!loading() && !error()) {
            <span
              class="inline-flex h-6 min-w-6 items-center justify-center rounded-full bg-sky-100 px-2 text-xs font-semibold text-sky-700"
              aria-label="Pending count"
            >
              {{ count() }}
            </span>
          }
        </div>
        <p class="mt-1 text-sm text-neutral-500">
          Payroll runs awaiting your approval before they can be finalized.
        </p>
      </div>

      <!-- Loading -->
      @if (loading()) {
        <div class="grid gap-4 sm:grid-cols-2">
          @for (i of [1, 2, 3, 4]; track i) {
            <div class="h-40 animate-pulse rounded-xl bg-neutral-100"></div>
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
        <!-- Empty state -->
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-white px-6 py-12 text-center"
        >
          <p class="text-sm font-medium text-neutral-900">All caught up</p>
          <p class="mt-1 text-sm text-neutral-500">
            There are no payroll runs awaiting your approval.
          </p>
        </div>
      } @else {
        <!-- Card grid -->
        <div @fadeIn class="grid gap-4 sm:grid-cols-2">
          @for (r of runs(); track r.id) {
            <a
              [routerLink]="['/payroll/runs', r.id]"
              class="group block rounded-xl border border-neutral-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus:ring-2 focus:ring-sky-300"
            >
              <div class="mb-3 flex items-start justify-between">
                <div>
                  <p
                    class="text-base font-semibold text-neutral-900 group-hover:text-neutral-950"
                  >
                    {{ periodLabel(r) }}
                  </p>
                  <p class="mt-0.5 text-xs text-neutral-500">
                    Submitted by {{ r.initiatedByName || '—' }}
                  </p>
                </div>
                <span
                  class="inline-flex items-center rounded-full px-2.5 py-0.5 text-[11px] font-medium ring-1 ring-inset"
                  [class]="statusBadge[r.status]"
                >
                  {{ statusLabels[r.status] }}
                </span>
              </div>

              <dl class="grid grid-cols-3 gap-2 text-sm">
                <div>
                  <dt class="text-xs text-neutral-500">Employees</dt>
                  <dd class="mt-0.5 font-semibold text-neutral-900">
                    {{ r.processedEmployees | number }}
                  </dd>
                </div>
                <div>
                  <dt class="text-xs text-neutral-500">Gross</dt>
                  <dd
                    class="mt-0.5 font-semibold tabular-nums text-neutral-900"
                  >
                    {{ r.totalGross | number: '1.0-0' }}
                  </dd>
                </div>
                <div>
                  <dt class="text-xs text-neutral-500">Net</dt>
                  <dd
                    class="mt-0.5 font-semibold tabular-nums text-emerald-700"
                  >
                    {{ r.totalNet | number: '1.0-0' }}
                  </dd>
                </div>
              </dl>

              <div
                class="mt-4 flex items-center justify-between border-t border-neutral-100 pt-3"
              >
                <span class="text-xs text-neutral-400">
                  {{ r.initiatedAt | date: 'mediumDate' }}
                </span>
                <span
                  class="text-xs font-medium text-sky-600 group-hover:text-sky-700"
                >
                  Review →
                </span>
              </div>
            </a>
          }
        </div>
      }
    </div>
  `,
})
export class PendingApprovalsComponent implements OnInit {
  private readonly approvalService = inject(PayrollApprovalService);

  readonly statusBadge = RUN_STATUS_BADGE;
  readonly statusLabels = RUN_STATUS_LABELS;

  readonly runs = signal<IPayrollRun[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** Badge count for the §8 queue header. */
  readonly count = computed(() => this.runs().length);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.approvalService.listPendingApprovals().subscribe({
      next: (runs) => {
        this.runs.set(runs);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load pending approvals.');
        this.loading.set(false);
      },
    });
  }

  periodLabel(r: IPayrollRun): string {
    const month = PAY_MONTHS.find((m) => m.value === r.payMonth);
    return `${month ? month.label : r.payMonth} ${r.payYear}`;
  }
}
