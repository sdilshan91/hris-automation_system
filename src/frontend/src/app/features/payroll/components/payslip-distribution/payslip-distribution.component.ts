import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { PayslipEmailService } from '../../services/payslip-email.service';
import { PayslipService } from '../../services/payslip.service';
import { PayrollRunStatus } from '../../models/payroll-run.models';
import {
  EmailDeliveryStatus,
  EMAIL_STATUS_BADGE,
  EMAIL_STATUS_LABELS,
  IEmployeeDistribution,
  IPayslipDistributionStatus,
  distributionPercent,
  recipientsByStatus,
} from '../../models/payslip-email.models';

/**
 * US-PAY-011 (§8): Bulk payslip email distribution, embedded in the payroll
 * run-detail page (same self-contained `[runId]` child pattern as payslip-list).
 *
 * Responsibilities:
 * - A primary "Send Payslips" action (email icon), enabled ONLY when the run is
 *   Finalized AND payslip PDFs have been generated (AC-1). PDF readiness is derived
 *   from the existing payslip generation-status endpoint (reused, not re-fetched
 *   server-side).
 * - A confirmation dialog before sending ("You are about to send payslip emails to
 *   {count} employees… Continue?"). If a distribution has already run for this run,
 *   the dialog additionally requires explicit acknowledgement of the duplicate send
 *   (FR-7/BR-5), and the confirm flag is passed to the backend.
 * - A live progress bar while sending (sent / total), fed by
 *   `streamDistributionStatus` (POLLING; the single SignalR swap point). The
 *   subscription is torn down on destroy and when the stream completes.
 * - A distribution summary card after completion: green/red/amber counts for
 *   Sent / Failed / Skipped (BR-6) with expandable per-employee lists. "Re-send All
 *   Failed" + per-employee re-send for failures (FR-4).
 *
 * Mobile (§8): HR can initiate the send and view the status here; the same layout
 * stacks responsively.
 */
@Component({
  selector: 'app-payslip-distribution',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('180ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <section class="mt-6" @fadeIn>
      <div
        class="rounded-xl border border-neutral-200 bg-white p-6 shadow-sm"
      >
        <div
          class="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between"
        >
          <div>
            <h2 class="text-sm font-semibold text-neutral-900">
              Payslip email distribution
            </h2>
            <p class="mt-1 text-xs text-neutral-500">
              Email each employee their payslip PDF after this run is finalized.
            </p>
          </div>

          <!-- Send Payslips primary action (AC-1) -->
          <button
            type="button"
            class="inline-flex items-center gap-1.5 rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
            [style.background-color]="'var(--brand-primary)'"
            [disabled]="!canSend() || sending()"
            [attr.aria-disabled]="!canSend() || sending()"
            (click)="openConfirm()"
          >
            @if (sending()) {
              <span class="btn-spinner btn-spinner--light"></span>
            } @else {
              <span aria-hidden="true">✉</span>
            }
            {{ status()?.hasSent ? 'Re-send payslips' : 'Send payslips' }}
          </button>
        </div>

        <!-- Why the action is disabled (AC-1 preconditions) -->
        @if (!canSend() && !sending()) {
          <p class="mt-3 text-xs text-amber-600">
            @if (runStatus() !== 'Finalized') {
              Payslip emails can be sent once this payroll run is finalized.
            } @else if (!hasGeneratedPdfs()) {
              Generate payslip PDFs before sending them by email.
            }
          </p>
        }

        <!-- Live progress bar while sending (§8: "sent / total") -->
        @if (status(); as s) {
          @if (s.isSending || sending()) {
            <div
              @fadeIn
              class="mt-5 rounded-lg border border-neutral-200 bg-neutral-50 p-4"
            >
              <div class="mb-2 flex items-center justify-between">
                <p class="text-sm font-medium text-neutral-900">
                  Sending {{ s.emailsSent | number }} /
                  {{ s.totalEmployees | number }} emails…
                </p>
                <span class="text-sm font-medium text-neutral-500"
                  >{{ percent() }}%</span
                >
              </div>
              <div
                class="h-2.5 w-full overflow-hidden rounded-full bg-neutral-200"
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
            </div>
          } @else if (isDistributed()) {
            <!-- Distribution summary card after completion (BR-6, §8) -->
            <div @fadeIn class="mt-5 border-t border-neutral-100 pt-5">
              <div class="grid grid-cols-3 gap-3">
                <button
                  type="button"
                  class="rounded-lg bg-emerald-50 p-3 text-left transition hover:ring-1 hover:ring-emerald-300"
                  [attr.aria-expanded]="expanded() === 'Sent'"
                  (click)="toggle('Sent')"
                >
                  <p class="text-xs text-emerald-700">Sent</p>
                  <p class="mt-1 text-lg font-semibold text-emerald-700">
                    {{ s.emailsSent | number }}
                  </p>
                </button>
                <button
                  type="button"
                  class="rounded-lg bg-rose-50 p-3 text-left transition hover:ring-1 hover:ring-rose-300"
                  [attr.aria-expanded]="expanded() === 'Failed'"
                  (click)="toggle('Failed')"
                >
                  <p class="text-xs text-rose-700">Failed</p>
                  <p class="mt-1 text-lg font-semibold text-rose-700">
                    {{ s.emailsFailed | number }}
                  </p>
                </button>
                <button
                  type="button"
                  class="rounded-lg bg-amber-50 p-3 text-left transition hover:ring-1 hover:ring-amber-300"
                  [attr.aria-expanded]="expanded() === 'Skipped'"
                  (click)="toggle('Skipped')"
                >
                  <p class="text-xs text-amber-700">Skipped</p>
                  <p class="mt-1 text-lg font-semibold text-amber-700">
                    {{ s.emailsSkipped | number }}
                  </p>
                </button>
              </div>

              <!-- Re-send All Failed (FR-4) -->
              @if (s.emailsFailed > 0) {
                <div class="mt-3 flex justify-end">
                  <button
                    type="button"
                    class="inline-flex items-center gap-1.5 rounded-lg border border-rose-200 bg-white px-3 py-2 text-sm font-medium text-rose-700 shadow-sm transition hover:bg-rose-50 disabled:opacity-50"
                    [disabled]="sending() || resendingAll()"
                    (click)="resendAllFailed()"
                  >
                    @if (resendingAll()) {
                      <span class="btn-spinner"></span>
                    }
                    Re-send all failed ({{ s.emailsFailed | number }})
                  </button>
                </div>
              }

              <!-- Expandable per-status list (§8) -->
              @if (expanded(); as st) {
                <div
                  @fadeIn
                  class="mt-4 overflow-hidden rounded-lg border border-neutral-200"
                >
                  @if (expandedRows().length === 0) {
                    <p
                      class="px-4 py-6 text-center text-sm text-neutral-500"
                    >
                      No {{ statusLabels[st].toLowerCase() }} deliveries.
                    </p>
                  } @else {
                    <ul class="divide-y divide-neutral-100">
                      @for (r of expandedRows(); track r.employeeId) {
                        <li
                          class="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
                        >
                          <div class="min-w-0">
                            <p
                              class="truncate text-sm font-medium text-neutral-900"
                            >
                              {{ r.employeeName }}
                            </p>
                            <p class="truncate text-xs text-neutral-500">
                              {{ r.employeeNo }}
                              @if (r.recipientEmail) {
                                · {{ r.recipientEmail }}
                              }
                            </p>
                            @if (r.status === 'Failed' && r.failureReason) {
                              <p class="mt-0.5 text-xs text-rose-600">
                                {{ r.failureReason }}
                              </p>
                            }
                            @if (r.status === 'Skipped') {
                              <p class="mt-0.5 text-xs text-amber-600">
                                No email address on file.
                              </p>
                            }
                          </div>
                          <div class="flex items-center gap-2">
                            <span
                              class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                              [class]="statusBadge[r.status]"
                            >
                              {{ statusLabels[r.status] }}
                            </span>
                            <!-- Per-employee re-send for failures (FR-4) -->
                            @if (r.status === 'Failed') {
                              <button
                                type="button"
                                class="rounded-md px-2 py-1 text-xs font-medium text-neutral-700 transition hover:bg-neutral-100 disabled:opacity-40"
                                [disabled]="
                                  sending() || resendingId() === r.employeeId
                                "
                                (click)="resendOne(r)"
                              >
                                {{
                                  resendingId() === r.employeeId
                                    ? 'Re-sending…'
                                    : 'Re-send'
                                }}
                              </button>
                            }
                          </div>
                        </li>
                      }
                    </ul>
                  }
                </div>
              }
            </div>
          }
        }
      </div>

      <!-- Confirmation dialog (§8 / FR-7 / BR-5) -->
      @if (confirmOpen()) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center p-4"
          role="dialog"
          aria-modal="true"
          aria-labelledby="send-confirm-title"
        >
          <div
            @backdrop
            class="absolute inset-0 bg-neutral-900/40"
            (click)="closeConfirm()"
          ></div>
          <div
            @fadeIn
            class="relative z-10 w-full max-w-md rounded-xl bg-white p-6 shadow-xl"
          >
            <h3
              id="send-confirm-title"
              class="text-base font-semibold text-neutral-900"
            >
              Send payslip emails
            </h3>
            <p class="mt-2 text-sm text-neutral-600">
              You are about to send payslip emails to
              {{ recipientCount() | number }}
              {{ recipientCount() === 1 ? 'employee' : 'employees' }}. This
              action cannot be undone. Continue?
            </p>

            <!-- Already-sent: require explicit confirmation (FR-7/BR-5) -->
            @if (status()?.hasSent) {
              <div
                class="mt-3 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2.5 text-sm text-amber-700"
                role="alert"
              >
                <p class="font-medium">Payslips were already sent for this run.</p>
                <label class="mt-2 flex items-start gap-2">
                  <input
                    type="checkbox"
                    class="mt-0.5"
                    [checked]="resendAck()"
                    (change)="resendAck.set($any($event.target).checked)"
                  />
                  <span
                    >I understand employees will receive a duplicate email and
                    want to re-send.</span
                  >
                </label>
              </div>
            }

            <div class="mt-5 flex items-center justify-end gap-2">
              <button
                type="button"
                class="rounded-lg px-3 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
                (click)="closeConfirm()"
              >
                Cancel
              </button>
              <button
                type="button"
                class="inline-flex items-center gap-1.5 rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="!canConfirm()"
                (click)="confirmSend()"
              >
                Send emails
              </button>
            </div>
          </div>
        </div>
      }
    </section>
  `,
  styles: [
    `
      .btn-spinner {
        display: inline-block;
        width: 0.85rem;
        height: 0.85rem;
        border: 2px solid rgba(0, 0, 0, 0.15);
        border-top-color: rgba(0, 0, 0, 0.55);
        border-radius: 9999px;
        animation: btn-spin 0.6s linear infinite;
      }
      .btn-spinner--light {
        border-color: rgba(255, 255, 255, 0.4);
        border-top-color: #fff;
      }
      @keyframes btn-spin {
        to {
          transform: rotate(360deg);
        }
      }
    `,
  ],
})
export class PayslipDistributionComponent implements OnInit, OnDestroy {
  private readonly emailService = inject(PayslipEmailService);
  private readonly payslipService = inject(PayslipService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** The run whose payslips we distribute. */
  readonly runId = input.required<string>();
  /** The run's status — sending is only allowed once Finalized (AC-1/BR-1). */
  readonly runStatus = input.required<PayrollRunStatus>();
  /** Employee count in the run, shown in the confirmation dialog (§8). */
  readonly employeeCount = input<number>(0);

  readonly statusBadge = EMAIL_STATUS_BADGE;
  readonly statusLabels = EMAIL_STATUS_LABELS;

  /** Aggregate distribution status (progress + summary), null until loaded. */
  readonly status = signal<IPayslipDistributionStatus | null>(null);
  /** Whether at least one payslip PDF has been generated (AC-1 precondition). */
  readonly hasGeneratedPdfs = signal(false);

  /** True while a send/re-send request is in flight or the job is streaming. */
  readonly sending = signal(false);
  readonly resendingAll = signal(false);
  readonly resendingId = signal<string | null>(null);

  /** The confirmation dialog open state + the explicit re-send acknowledgement. */
  readonly confirmOpen = signal(false);
  readonly resendAck = signal(false);

  /** Which summary list (Sent/Failed/Skipped) is expanded (null = none). */
  readonly expanded = signal<EmailDeliveryStatus | null>(null);

  // ─── Derived state ─────────────────────────────────────────

  /** Send enabled ONLY for a Finalized run with generated PDFs (AC-1). */
  readonly canSend = computed(
    () => this.runStatus() === 'Finalized' && this.hasGeneratedPdfs(),
  );

  /** Progress percentage for the bar (§8). */
  readonly percent = computed(() => distributionPercent(this.status()));

  /** A distribution has run (the summary card should show) once there's a result. */
  readonly isDistributed = computed(() => {
    const s = this.status();
    return !!s && s.hasSent && !s.isSending;
  });

  /** Recipient count for the confirmation copy: prefer the live status total. */
  readonly recipientCount = computed(
    () => this.status()?.totalEmployees || this.employeeCount(),
  );

  /** Rows of the currently-expanded status list (§8). */
  readonly expandedRows = computed<IEmployeeDistribution[]>(() => {
    const st = this.expanded();
    return st ? recipientsByStatus(this.status(), st) : [];
  });

  /** The confirm button is enabled unless a re-send needs the unticked ack. */
  readonly canConfirm = computed(() => {
    if (this.sending()) {
      return false;
    }
    return this.status()?.hasSent ? this.resendAck() : true;
  });

  ngOnInit(): void {
    this.loadStatus();
    this.loadPdfReadiness();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Load the current distribution status (progress + summary). Best-effort. */
  loadStatus(): void {
    this.emailService
      .getDistributionStatus(this.runId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (s) => {
          this.status.set(s);
          // Resume the progress stream if a job is already running.
          if (s.isSending) {
            this.stream();
          }
        },
        error: () => this.status.set(null),
      });
  }

  /**
   * Derive PDF readiness (AC-1) from the existing payslip generation status — Send
   * is gated on at least one payslip PDF having been generated. Best-effort: a
   * failure leaves Send disabled with the "generate PDFs first" hint.
   */
  private loadPdfReadiness(): void {
    this.payslipService
      .getGenerationStatus(this.runId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (g) => this.hasGeneratedPdfs.set(g.generatedCount > 0),
        error: () => this.hasGeneratedPdfs.set(false),
      });
  }

  // ─── Send flow (AC-1 / FR-7 / BR-5) ────────────────────────

  /** Open the confirmation dialog (resets the re-send acknowledgement). */
  openConfirm(): void {
    if (!this.canSend()) {
      return;
    }
    this.resendAck.set(false);
    this.confirmOpen.set(true);
  }

  closeConfirm(): void {
    this.confirmOpen.set(false);
  }

  /**
   * Confirm + enqueue the distribution (AC-1). Passes `confirm: true` when the run
   * has already been sent (FR-7/BR-5 duplicate guard) so the backend allows it.
   *
   * The backend returns the 202 ACCEPTED dto (queued count), NOT the status — so on
   * acceptance the FE starts polling `streamDistributionStatus` to drive the
   * progress bar + summary.
   */
  confirmSend(): void {
    if (!this.canConfirm()) {
      return;
    }
    const confirmFlag = !!this.status()?.hasSent;
    this.confirmOpen.set(false);
    this.sending.set(true);
    this.emailService
      .sendPayslips(this.runId(), confirmFlag)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => this.stream(),
        error: () => {
          this.sending.set(false);
          this.toastr.error('Could not start sending payslip emails.');
        },
      });
  }

  /**
   * Poll distribution status while the job runs (§8). On completion, the final
   * snapshot (with the per-employee summary, BR-6) is already in `status`.
   */
  private stream(): void {
    this.emailService
      .streamDistributionStatus(this.runId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (s) => this.status.set(s),
        error: () => {
          this.sending.set(false);
          this.resendingAll.set(false);
          this.resendingId.set(null);
          this.toastr.error('Lost track of the email distribution.');
        },
        complete: () => {
          this.sending.set(false);
          this.resendingAll.set(false);
          this.resendingId.set(null);
          this.toastr.success('Payslip email distribution finished.');
        },
      });
  }

  // ─── Re-send (FR-4) ────────────────────────────────────────

  /** Re-send to every failed delivery (FR-4 "Re-send All Failed"). */
  resendAllFailed(): void {
    if (this.resendingAll()) {
      return;
    }
    this.resendingAll.set(true);
    this.emailService
      .resendAllFailed(this.runId())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => this.stream(),
        error: () => {
          this.resendingAll.set(false);
          this.toastr.error('Could not re-send the failed payslips.');
        },
      });
  }

  /** Re-send a single failed delivery (FR-4 per-employee re-send). */
  resendOne(r: IEmployeeDistribution): void {
    if (this.resendingId()) {
      return;
    }
    this.resendingId.set(r.employeeId);
    this.emailService
      .resendSelective(this.runId(), [r.employeeId])
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => this.stream(),
        error: () => {
          this.resendingId.set(null);
          this.toastr.error('Could not re-send this payslip.');
        },
      });
  }

  // ─── Summary list expand/collapse (§8) ─────────────────────

  toggle(status: EmailDeliveryStatus): void {
    this.expanded.update((cur) => (cur === status ? null : status));
  }
}
