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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { trigger, transition, style, animate } from '@angular/animations';
import { PayrollRunService } from '../../services/payroll-run.service';
import {
  IPayrollRun,
  IPayrollRunProgress,
  PayrollRunStatus,
  PAY_MONTHS,
  RUN_STATUS_BADGE,
  RUN_STATUS_LABELS,
  RUN_STEPPER,
} from '../../models/payroll-run.models';

/**
 * US-PAY-003 (§8): Payroll run detail — the in-progress view and completion summary
 * for one run.
 *
 * - Horizontal status stepper: Queued > Processing > Review > Approved > Finalized.
 *   Cancelled is shown as a terminal off-path banner, not a stepper node.
 * - While Queued/Processing: a live progress bar ("Processing 1,247 / 5,000
 *   employees…") fed by `streamProgress` (FR-6). POLLING by default; the service's
 *   `streamProgress` is the single SignalR swap point. The subscription is torn
 *   down on destroy (takeUntil) and when the stream completes (run finished).
 * - On completion (ReviewPending and beyond): a run summary card with total
 *   gross/deductions/net and employee/skipped counts (FR-8), plus a "View details"
 *   link to the payslip list — STUBBED here; full payslip viewing is US-PAY-004/005.
 *
 * Mobile: initiation + progress are available; the detailed payslip list is
 * deferred to desktop (§8).
 */
@Component({
  selector: 'app-payroll-run-detail',
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
    <div class="mx-auto max-w-4xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Breadcrumb -->
      <nav class="mb-1 text-xs text-neutral-500" aria-label="Breadcrumb">
        <a routerLink="/payroll" class="hover:text-neutral-700">Payroll</a>
        <span class="px-1">/</span>
        <a routerLink="/payroll/runs" class="hover:text-neutral-700"
          >Payroll runs</a
        >
        <span class="px-1">/</span>
        <span class="text-neutral-700">Run</span>
      </nav>

      <!-- Loading / error -->
      @if (loading()) {
        <div class="mt-4 space-y-3">
          <div class="h-9 w-64 animate-pulse rounded-lg bg-neutral-100"></div>
          <div class="h-20 animate-pulse rounded-xl bg-neutral-100"></div>
          <div class="h-40 animate-pulse rounded-xl bg-neutral-100"></div>
        </div>
      } @else if (error()) {
        <div
          class="mt-4 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (run(); as r) {
        <!-- Title -->
        <div
          @fadeIn
          class="mb-6 mt-1 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <div>
            <h1
              class="text-2xl font-semibold tracking-tight text-neutral-900"
            >
              {{ periodLabel(r) }}
            </h1>
            <p class="mt-1 text-sm text-neutral-500">
              Initiated by {{ r.initiatedByName || '—' }} on
              {{ r.initiatedAt | date: 'medium' }}
            </p>
          </div>
          <span
            class="inline-flex h-fit items-center self-start rounded-full px-3 py-1 text-xs font-medium ring-1 ring-inset"
            [class]="statusBadge[r.status]"
          >
            {{ statusLabels[r.status] }}
          </span>
        </div>

        <!-- Cancelled banner (off-path terminal state) -->
        @if (r.status === 'Cancelled') {
          <div
            class="mb-6 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
            role="alert"
          >
            This payroll run was cancelled.
          </div>
        } @else {
          <!-- Status stepper -->
          <ol
            class="mb-8 flex items-center"
            aria-label="Payroll run progress"
          >
            @for (step of stepper; track step.status; let i = $index) {
              <li class="flex flex-1 items-center" [class.flex-none]="isLast(i)">
                <div class="flex flex-col items-center gap-1.5">
                  <span
                    class="flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold ring-1 ring-inset transition"
                    [class]="stepClass(step.status)"
                    [attr.aria-current]="
                      isCurrentStep(step.status) ? 'step' : null
                    "
                  >
                    @if (isCompleteStep(step.status)) {
                      ✓
                    } @else {
                      {{ i + 1 }}
                    }
                  </span>
                  <span
                    class="text-[11px] font-medium"
                    [class.text-neutral-900]="isCurrentStep(step.status)"
                    [class.text-neutral-400]="!isCurrentStep(step.status)"
                  >
                    {{ step.label }}
                  </span>
                </div>
                @if (!isLast(i)) {
                  <span
                    class="mx-1 h-0.5 flex-1 rounded transition"
                    [class.bg-emerald-400]="stepIndex() > i"
                    [class.bg-neutral-200]="stepIndex() <= i"
                  ></span>
                }
              </li>
            }
          </ol>

          <!-- In-progress view: live progress bar (FR-6) -->
          @if (isProcessing()) {
            <div
              @fadeIn
              class="mb-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            >
              <div class="mb-2 flex items-center justify-between">
                <p class="text-sm font-medium text-neutral-900">
                  Processing
                  {{ processed() | number }} / {{ total() | number }}
                  employees…
                </p>
                <span class="text-sm font-medium text-neutral-500"
                  >{{ percent() }}%</span
                >
              </div>
              <div
                class="h-2.5 w-full overflow-hidden rounded-full bg-neutral-100"
                role="progressbar"
                [attr.aria-valuenow]="percent()"
                aria-valuemin="0"
                aria-valuemax="100"
              >
                <div
                  class="h-full rounded-full transition-[width] duration-500 ease-out"
                  [style.width.%]="percent()"
                  [style.background-color]="'var(--brand-primary)'"
                ></div>
              </div>
              @if (skipped() > 0) {
                <p class="mt-2 text-xs text-amber-600">
                  {{ skipped() | number }} skipped (no salary structure).
                </p>
              }
            </div>
          }

          <!-- Completion summary card (FR-8) -->
          @if (isComplete()) {
            <div
              @fadeIn
              class="mb-6 rounded-xl border border-neutral-200 bg-white p-6 shadow-sm"
            >
              <h2 class="mb-4 text-sm font-semibold text-neutral-900">
                Run summary
              </h2>
              <div class="grid grid-cols-2 gap-4 sm:grid-cols-3">
                <div class="rounded-lg bg-neutral-50 p-3">
                  <p class="text-xs text-neutral-500">Total gross</p>
                  <p class="mt-1 text-lg font-semibold text-neutral-900">
                    {{ r.totalGross | number: '1.2-2' }}
                  </p>
                </div>
                <div class="rounded-lg bg-neutral-50 p-3">
                  <p class="text-xs text-neutral-500">Total deductions</p>
                  <p class="mt-1 text-lg font-semibold text-rose-700">
                    {{ r.totalDeductions | number: '1.2-2' }}
                  </p>
                </div>
                <div class="rounded-lg bg-emerald-50 p-3">
                  <p class="text-xs text-emerald-700">Total net</p>
                  <p class="mt-1 text-lg font-semibold text-emerald-700">
                    {{ r.totalNet | number: '1.2-2' }}
                  </p>
                </div>
                <div class="rounded-lg bg-neutral-50 p-3">
                  <p class="text-xs text-neutral-500">Employees paid</p>
                  <p class="mt-1 text-lg font-semibold text-neutral-900">
                    {{ r.processedEmployees | number }}
                  </p>
                </div>
                <div class="rounded-lg bg-neutral-50 p-3">
                  <p class="text-xs text-neutral-500">Skipped</p>
                  <p class="mt-1 text-lg font-semibold text-amber-700">
                    {{ r.skippedEmployees | number }}
                  </p>
                </div>
                <div class="rounded-lg bg-neutral-50 p-3">
                  <p class="text-xs text-neutral-500">Total employees</p>
                  <p class="mt-1 text-lg font-semibold text-neutral-900">
                    {{ r.totalEmployees | number }}
                  </p>
                </div>
              </div>

              <!-- View details (payslip list) — stub for US-PAY-004/005 -->
              <div class="mt-5 border-t border-neutral-100 pt-4">
                <button
                  type="button"
                  class="text-sm font-medium text-neutral-400"
                  disabled
                  title="Payslip viewing arrives in a later story"
                >
                  View details →
                </button>
                <p class="mt-1 text-xs text-neutral-400">
                  Per-employee payslips are coming soon.
                </p>
              </div>
            </div>
          }
        }
      }
    </div>
  `,
})
export class PayrollRunDetailComponent implements OnInit, OnDestroy {
  private readonly runsService = inject(PayrollRunService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy$ = new Subject<void>();

  readonly statusBadge = RUN_STATUS_BADGE;
  readonly statusLabels = RUN_STATUS_LABELS;
  readonly stepper = RUN_STEPPER;

  readonly run = signal<IPayrollRun | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** Live progress overrides the run's counts while the stream is active (FR-6). */
  readonly progress = signal<IPayrollRunProgress | null>(null);

  private runId = '';

  // ─── Derived state ─────────────────────────────────────────

  readonly status = computed<PayrollRunStatus | null>(
    () => this.progress()?.status ?? this.run()?.status ?? null,
  );

  readonly isProcessing = computed(() => {
    const s = this.status();
    return s === 'Queued' || s === 'Processing';
  });

  /** ReviewPending and beyond have computed totals (FR-8). */
  readonly isComplete = computed(() => {
    const s = this.status();
    return (
      s === 'ReviewPending' ||
      s === 'Approved' ||
      s === 'Finalized'
    );
  });

  readonly processed = computed(
    () => this.progress()?.processedEmployees ?? this.run()?.processedEmployees ?? 0,
  );
  readonly total = computed(
    () => this.progress()?.totalEmployees ?? this.run()?.totalEmployees ?? 0,
  );
  readonly skipped = computed(
    () => this.progress()?.skippedEmployees ?? this.run()?.skippedEmployees ?? 0,
  );

  readonly percent = computed(() => {
    const t = this.total();
    if (t <= 0) {
      return 0;
    }
    return Math.min(100, Math.round((this.processed() / t) * 100));
  });

  /** 0-based index of the current step in RUN_STEPPER (Cancelled → -1). */
  readonly stepIndex = computed(() => {
    const s = this.status();
    if (!s) {
      return -1;
    }
    return this.stepper.findIndex((step) => step.status === s);
  });

  ngOnInit(): void {
    this.runId = this.route.snapshot.paramMap.get('id') ?? '';
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.runsService.getRun(this.runId).subscribe({
      next: (r) => {
        this.run.set(r);
        this.loading.set(false);
        // If the run is still active, start streaming progress (FR-6).
        if (r.status === 'Queued' || r.status === 'Processing') {
          this.startProgress();
        }
      },
      error: () => {
        this.error.set('Could not load this payroll run.');
        this.loading.set(false);
      },
    });
  }

  /**
   * Subscribe to progress updates (FR-6). `streamProgress` completes when the run
   * leaves the active states; on completion we refetch the run so the summary card
   * (totals) reflects the finished run.
   */
  private startProgress(): void {
    this.runsService
      .streamProgress(this.runId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (p) => this.progress.set(p),
        complete: () => this.refreshOnComplete(),
      });
  }

  private refreshOnComplete(): void {
    this.runsService.getRun(this.runId).subscribe({
      next: (r) => this.run.set(r),
    });
  }

  // ─── Stepper helpers ───────────────────────────────────────

  isLast(i: number): boolean {
    return i === this.stepper.length - 1;
  }

  isCurrentStep(status: PayrollRunStatus): boolean {
    return this.status() === status;
  }

  isCompleteStep(status: PayrollRunStatus): boolean {
    const idx = this.stepper.findIndex((s) => s.status === status);
    return idx > -1 && idx < this.stepIndex();
  }

  stepClass(status: PayrollRunStatus): string {
    const idx = this.stepper.findIndex((s) => s.status === status);
    const current = this.stepIndex();
    if (idx < current) {
      return 'bg-emerald-500 text-white ring-emerald-500';
    }
    if (idx === current) {
      return 'bg-neutral-900 text-white ring-neutral-900';
    }
    return 'bg-white text-neutral-400 ring-neutral-200';
  }

  periodLabel(r: IPayrollRun): string {
    const month = PAY_MONTHS.find((m) => m.value === r.payMonth);
    return `${month ? month.label : r.payMonth} ${r.payYear}`;
  }
}
