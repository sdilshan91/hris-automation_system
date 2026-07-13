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
import { HttpErrorResponse } from '@angular/common/http';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Observable, Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../core/auth/auth.service';
import { PayrollRunService } from '../../services/payroll-run.service';
import { PayrollApprovalService } from '../../services/payroll-approval.service';
import { PayslipListComponent } from '../payslip-list/payslip-list.component';
import { PayslipDistributionComponent } from '../payslip-distribution/payslip-distribution.component';
import { AuditTrailComponent } from '../audit-trail/audit-trail.component';
import {
  IPayrollRun,
  IPayrollRunProgress,
  PayrollRunStatus,
  PAY_MONTHS,
  RUN_STATUS_BADGE,
  RUN_STATUS_LABELS,
  RUN_STEPPER,
  runActionErrorMessage,
} from '../../models/payroll-run.models';
import {
  APPROVAL_ACTION_BADGE,
  APPROVAL_ACTION_LABELS,
  IApprovalHistoryEntry,
  IApprovalSummary,
  VARIANCE_BAND_CLASS,
  VarianceBand,
  varianceBand,
} from '../../models/approval.models';

/** US-PAY-008: which action the sticky comments field is gating. */
type CommentAction = 'reject' | 'return' | null;

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
 *   gross/deductions/net and employee/skipped counts (FR-8), plus a "View payslips"
 *   toggle that reveals the embedded PayslipListComponent (US-PAY-004 §8): the
 *   searchable payslip table, the Generate/Regenerate action + status bar, the PDF
 *   preview modal, and per-employee / bulk-ZIP downloads.
 *
 * `canGeneratePayslips` (BR-1/AC-5): payslips may be generated on a ReviewPending,
 * Approved, or Finalized run, but REGENERATED only on a non-finalized run — the
 * child handles the generate-vs-regenerate label; here we just gate the action on
 * non-Finalized so finalized runs are view/download only.
 *
 * Mobile (§8): individual payslip view + download are supported; the bulk ZIP
 * download is hidden on mobile by the child component.
 *
 * US-PAY-008 (approval workflow) extends this page with:
 * - Status-driven action buttons in a sticky bottom action bar: "Submit for
 *   Approval" (ReviewPending), "Approve"/"Reject"/"Return to HR" (AwaitingApproval,
 *   gated by Payroll.Approve), "Finalize" (Approved). Reject + Return require a
 *   comments field.
 * - An approval review layout once AwaitingApproval: left = the payroll summary
 *   card (totals + statutory + month-over-month variance, colour-coded green/amber/
 *   red) + exceptions; right = the existing embedded payslip-list for drill-down
 *   (FR-5, desktop-only on mobile by the child).
 * - An approval-history timeline at the bottom (who/when/action/comments, FR-7).
 * - Rejected is shown as an off-path banner (like Cancelled), not a stepper node.
 */
@Component({
  selector: 'app-payroll-run-detail',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    RouterLink,
    PayslipListComponent,
    PayslipDistributionComponent,
    AuditTrailComponent,
  ],
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
          <div class="flex flex-wrap items-center gap-2 self-start">
            <!-- ISSUE-154: Re-run (ReviewPending only) + Cancel (any pre-Finalized,
                 non-Cancelled). Both gated by Payroll.Run; the server is the final
                 authority and re-validates. -->
            @if (canRerun()) {
              <button
                type="button"
                class="rounded-lg border border-neutral-200 px-3 py-1.5 text-xs font-medium text-neutral-700 transition hover:bg-neutral-50 disabled:opacity-50"
                [disabled]="acting()"
                (click)="startRerun()"
              >
                Re-run
              </button>
            }
            @if (canCancel()) {
              <button
                type="button"
                class="rounded-lg border border-rose-200 px-3 py-1.5 text-xs font-medium text-rose-700 transition hover:bg-rose-50 disabled:opacity-50"
                [disabled]="acting()"
                (click)="startCancel()"
              >
                Cancel run
              </button>
            }
            <span
              class="inline-flex h-fit items-center rounded-full px-3 py-1 text-xs font-medium ring-1 ring-inset"
              [class]="statusBadge[r.status]"
            >
              {{ statusLabels[r.status] }}
            </span>
          </div>
        </div>

        <!-- ISSUE-154: inline confirm for Cancel / Re-run (mirrors the reject/return
             inline-confirm idiom). Cancel removes slips; Re-run replaces them. -->
        @if (confirmAction(); as ca) {
          <div
            @fadeIn
            class="mb-6 rounded-xl border px-4 py-3 text-sm"
            [class.border-rose-200]="ca === 'cancel'"
            [class.bg-rose-50]="ca === 'cancel'"
            [class.border-amber-200]="ca === 'rerun'"
            [class.bg-amber-50]="ca === 'rerun'"
            role="alert"
          >
            <p
              class="font-medium"
              [class.text-rose-700]="ca === 'cancel'"
              [class.text-amber-800]="ca === 'rerun'"
            >
              {{
                ca === 'cancel'
                  ? 'Cancel this payroll run? All generated payslips will be removed. This cannot be undone.'
                  : 'Re-run this payroll run? Its payslips will be replaced and payroll reprocessed.'
              }}
            </p>
            <div class="mt-3 flex flex-wrap items-center gap-2">
              <button
                type="button"
                class="rounded-lg px-3 py-1.5 text-xs font-medium text-neutral-600 transition hover:bg-white/60 disabled:opacity-50"
                [disabled]="acting()"
                (click)="dismissConfirm()"
              >
                Keep run
              </button>
              @if (ca === 'cancel') {
                <button
                  type="button"
                  class="rounded-lg bg-rose-600 px-3 py-1.5 text-xs font-medium text-white shadow-sm transition hover:bg-rose-700 disabled:opacity-50"
                  [disabled]="acting()"
                  (click)="confirmCancel()"
                >
                  {{ acting() ? 'Cancelling…' : 'Confirm cancel' }}
                </button>
              } @else {
                <button
                  type="button"
                  class="rounded-lg bg-amber-600 px-3 py-1.5 text-xs font-medium text-white shadow-sm transition hover:bg-amber-700 disabled:opacity-50"
                  [disabled]="acting()"
                  (click)="confirmRerun()"
                >
                  {{ acting() ? 'Starting…' : 'Confirm re-run' }}
                </button>
              }
            </div>
          </div>
        }

        <!-- Cancelled banner (off-path terminal state) -->
        @if (r.status === 'Cancelled') {
          <div
            class="mb-6 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
            role="alert"
          >
            This payroll run was cancelled.
          </div>
        } @else if (r.status === 'Rejected') {
          <!-- Rejected banner (off-path; HR corrects + re-submits, BR-3/BR-4) -->
          <div
            class="mb-6 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
            role="alert"
          >
            <p class="font-medium">This payroll run was rejected.</p>
            @if (lastComment(); as c) {
              <p class="mt-1 text-rose-600">Reason: {{ c }}</p>
            }
            <p class="mt-1 text-rose-600">
              Make the necessary adjustments, then re-submit for approval.
            </p>
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

              <!-- Approval review extras (US-PAY-008 FR-4): statutory subtotal +
                   month-over-month variance vs previous month, colour-coded. -->
              @if (approvalSummary(); as sum) {
                <div
                  class="mt-4 grid grid-cols-2 gap-4 border-t border-neutral-100 pt-4 sm:grid-cols-3"
                >
                  <div class="rounded-lg bg-indigo-50 p-3">
                    <p class="text-xs text-indigo-700">Total statutory</p>
                    <p class="mt-1 text-lg font-semibold text-indigo-700">
                      {{ sum.totalStatutory | number: '1.2-2' }}
                    </p>
                  </div>
                  <div class="rounded-lg bg-neutral-50 p-3">
                    <p class="text-xs text-neutral-500">
                      Previous month net
                    </p>
                    <p class="mt-1 text-lg font-semibold text-neutral-900">
                      {{
                        sum.previousMonthTotalNet === null
                          ? '—'
                          : (sum.previousMonthTotalNet | number: '1.2-2')
                      }}
                    </p>
                  </div>
                  <div class="rounded-lg bg-neutral-50 p-3">
                    <p class="text-xs text-neutral-500">Variance (net)</p>
                    <p
                      class="mt-1 text-lg font-semibold"
                      [class]="varianceClass()"
                    >
                      {{ varianceLabel() }}
                    </p>
                  </div>
                </div>
                @if (varianceBandValue() === 'high') {
                  <p
                    class="mt-2 text-xs font-medium text-rose-700"
                    role="alert"
                  >
                    Net pay increased more than 15% from last month — please
                    review before approving.
                  </p>
                } @else if (varianceBandValue() === 'elevated') {
                  <p class="mt-2 text-xs font-medium text-amber-700">
                    Net pay increased more than 5% from last month.
                  </p>
                }
              }

              <!-- Exceptions / warnings (FR-4) -->
              @if (exceptions().length > 0) {
                <div class="mt-4 border-t border-neutral-100 pt-4">
                  <h3 class="mb-2 text-xs font-semibold text-neutral-900">
                    Exceptions ({{ exceptions().length }})
                  </h3>
                  <ul class="space-y-1.5">
                    @for (ex of exceptions(); track $index) {
                      <li
                        class="flex items-start gap-2 rounded-lg px-3 py-2 text-sm"
                        [class.bg-rose-50]="ex.severity === 'Error'"
                        [class.text-rose-700]="ex.severity === 'Error'"
                        [class.bg-amber-50]="ex.severity !== 'Error'"
                        [class.text-amber-700]="ex.severity !== 'Error'"
                      >
                        <span aria-hidden="true">{{
                          ex.severity === 'Error' ? '⛔' : '⚠'
                        }}</span>
                        <span>
                          {{ ex.message }}
                          @if (ex.employeeName) {
                            <span class="font-medium"
                              >— {{ ex.employeeName }}</span
                            >
                          }
                        </span>
                      </li>
                    }
                  </ul>
                </div>
              }

              <!-- View payslips (US-PAY-004 §8): reveals the embedded list -->
              <div class="mt-5 border-t border-neutral-100 pt-4">
                <button
                  type="button"
                  class="text-sm font-medium text-neutral-700 transition hover:text-neutral-900"
                  [attr.aria-expanded]="showPayslips()"
                  (click)="togglePayslips()"
                >
                  {{ showPayslips() ? 'Hide payslips' : 'View payslips' }}
                  {{ showPayslips() ? '↑' : '→' }}
                </button>
              </div>
            </div>

            <!-- Embedded payslip list (US-PAY-004) -->
            @if (showPayslips()) {
              <app-payslip-list
                [runId]="r.id"
                [canGenerate]="canGeneratePayslips()"
              />
            }

            <!-- Bulk payslip email distribution (US-PAY-011): Send Payslips,
                 progress, and the per-employee delivery summary. Self-gates on a
                 Finalized run with generated PDFs (AC-1). -->
            <app-payslip-distribution
              [runId]="r.id"
              [runStatus]="r.status"
              [employeeCount]="r.processedEmployees"
            />
          }
        }

        <!-- Approval history timeline (US-PAY-008 FR-7) -->
        @if (history().length > 0) {
          <div
            @fadeIn
            class="mt-2 rounded-xl border border-neutral-200 bg-white p-6 shadow-sm"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-900">
              Approval history
            </h2>
            <ol class="relative space-y-5 border-l border-neutral-200 pl-5">
              @for (h of history(); track h.id) {
                <li class="relative">
                  <span
                    class="absolute -left-[1.4rem] top-1 h-2.5 w-2.5 rounded-full ring-4 ring-white"
                    [class.bg-emerald-500]="h.action === 'Approved'"
                    [class.bg-rose-500]="h.action === 'Rejected'"
                    [class.bg-amber-500]="h.action === 'Returned'"
                    [class.bg-sky-500]="
                      h.action === 'Submitted' || h.action === 'Escalated'
                    "
                    aria-hidden="true"
                  ></span>
                  <div
                    class="flex flex-wrap items-center gap-x-2 gap-y-1"
                  >
                    <span
                      class="inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium ring-1 ring-inset"
                      [class]="actionBadge[h.action]"
                    >
                      {{ actionLabels[h.action] }}
                    </span>
                    <span class="text-sm font-medium text-neutral-900">
                      {{ h.actorName || '—' }}
                    </span>
                    <span class="text-xs text-neutral-400">
                      {{ h.actedAt | date: 'medium' }}
                    </span>
                  </div>
                  @if (h.comments) {
                    <p class="mt-1 text-sm text-neutral-600">
                      “{{ h.comments }}”
                    </p>
                  }
                </li>
              }
            </ol>
          </div>
        }

        <!-- US-PAY-012 (AC-2, FR-6/FR-8): the per-run Audit Trail — the full
             timeline of every action on this run (config, run events, approvals,
             payslip generation, email) with an expandable before/after diff per
             change. Reuses the AuditTrailComponent in per-run mode (filter
             bar/export hidden; the standalone /payroll/audit page is where
             cross-run filtering + export live). -->
        <div class="mb-24 mt-2 rounded-xl border border-neutral-200 bg-white p-6 shadow-sm">
          <app-audit-trail [runId]="r.id" />
        </div>

        <!-- Sticky bottom action bar (US-PAY-008 §8) -->
        @if (hasActions()) {
          <div
            class="fixed inset-x-0 bottom-0 z-20 border-t border-neutral-200 bg-white/95 backdrop-blur"
          >
            <div class="mx-auto max-w-4xl px-4 py-3 sm:px-6 lg:px-8">
              <!-- Comments field for reject / return (required) -->
              @if (commentAction()) {
                <div class="mb-3">
                  <label
                    class="mb-1 block text-xs font-medium text-neutral-700"
                    [attr.for]="'approval-comments'"
                  >
                    {{
                      commentAction() === 'reject'
                        ? 'Rejection reason'
                        : 'Comments for HR'
                    }}
                    <span class="text-rose-500">*</span>
                  </label>
                  <textarea
                    id="approval-comments"
                    rows="2"
                    [formControl]="comments"
                    class="w-full resize-none rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-900 transition focus:border-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-200"
                    [placeholder]="
                      commentAction() === 'reject'
                        ? 'Explain why this run is being rejected…'
                        : 'What should HR adjust before re-submitting?'
                    "
                  ></textarea>
                  @if (comments.touched && comments.invalid) {
                    <p class="mt-1 text-xs text-rose-600">
                      A comment is required.
                    </p>
                  }
                </div>
              }

              <div class="flex flex-wrap items-center justify-end gap-2">
                @if (commentAction()) {
                  <button
                    type="button"
                    class="rounded-lg px-3 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
                    [disabled]="acting()"
                    (click)="cancelComment()"
                  >
                    Cancel
                  </button>
                }

                @switch (status()) {
                  @case ('ReviewPending') {
                    <button
                      type="button"
                      class="rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                      [style.background-color]="'var(--brand-primary)'"
                      [disabled]="acting()"
                      (click)="submitForApproval()"
                    >
                      {{ acting() ? 'Submitting…' : 'Submit for Approval' }}
                    </button>
                  }
                  @case ('AwaitingApproval') {
                    @if (canApprove()) {
                      @if (commentAction() === 'reject') {
                        <button
                          type="button"
                          class="rounded-lg bg-rose-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-rose-700 disabled:opacity-50"
                          [disabled]="acting()"
                          (click)="confirmReject()"
                        >
                          {{ acting() ? 'Rejecting…' : 'Confirm rejection' }}
                        </button>
                      } @else if (commentAction() === 'return') {
                        <button
                          type="button"
                          class="rounded-lg bg-amber-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-amber-700 disabled:opacity-50"
                          [disabled]="acting()"
                          (click)="confirmReturn()"
                        >
                          {{ acting() ? 'Returning…' : 'Confirm return' }}
                        </button>
                      } @else {
                        <button
                          type="button"
                          class="rounded-lg px-3 py-2 text-sm font-medium text-amber-700 transition hover:bg-amber-50 disabled:opacity-50"
                          [disabled]="acting()"
                          (click)="startReturn()"
                        >
                          Return to HR
                        </button>
                        <button
                          type="button"
                          class="rounded-lg px-3 py-2 text-sm font-medium text-rose-700 transition hover:bg-rose-50 disabled:opacity-50"
                          [disabled]="acting()"
                          (click)="startReject()"
                        >
                          Reject
                        </button>
                        <button
                          type="button"
                          class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-emerald-700 disabled:opacity-50"
                          [disabled]="acting()"
                          (click)="approve()"
                        >
                          {{ acting() ? 'Approving…' : 'Approve' }}
                        </button>
                      }
                    } @else {
                      <span class="text-xs text-neutral-500">
                        You do not have permission to approve this run.
                      </span>
                    }
                  }
                  @case ('Approved') {
                    <button
                      type="button"
                      class="rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-emerald-700 disabled:opacity-50"
                      [disabled]="acting()"
                      (click)="finalize()"
                    >
                      {{ acting() ? 'Finalizing…' : 'Finalize' }}
                    </button>
                  }
                }
              </div>
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class PayrollRunDetailComponent implements OnInit, OnDestroy {
  private readonly runsService = inject(PayrollRunService);
  private readonly approvalService = inject(PayrollApprovalService);
  private readonly auth = inject(AuthService);
  private readonly toastr = inject(ToastrService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy$ = new Subject<void>();

  readonly statusBadge = RUN_STATUS_BADGE;
  readonly statusLabels = RUN_STATUS_LABELS;
  readonly stepper = RUN_STEPPER;
  readonly actionBadge = APPROVAL_ACTION_BADGE;
  readonly actionLabels = APPROVAL_ACTION_LABELS;

  readonly run = signal<IPayrollRun | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  /** Live progress overrides the run's counts while the stream is active (FR-6). */
  readonly progress = signal<IPayrollRunProgress | null>(null);

  /** Whether the embedded payslip list (US-PAY-004) is revealed. */
  readonly showPayslips = signal(false);

  // ─── US-PAY-008 approval state ─────────────────────────────

  /** The approval review summary (FR-4); null until loaded / not applicable. */
  readonly approvalSummary = signal<IApprovalSummary | null>(null);
  /** The approval audit-trail timeline (FR-7), newest-first. */
  readonly history = signal<IApprovalHistoryEntry[]>([]);
  /** True while an approval action (submit/approve/...) is in flight. */
  readonly acting = signal(false);
  /** Which action the sticky comments field is gating (null = none). */
  readonly commentAction = signal<CommentAction>(null);
  /** Required comments for reject/return (AC-3, FR-9). */
  readonly comments = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, Validators.minLength(3)],
  });

  private runId = '';

  // ─── Derived state ─────────────────────────────────────────

  readonly status = computed<PayrollRunStatus | null>(
    () => this.progress()?.status ?? this.run()?.status ?? null,
  );

  readonly isProcessing = computed(() => {
    const s = this.status();
    return s === 'Queued' || s === 'Processing';
  });

  /**
   * ReviewPending and beyond have computed totals (FR-8). Includes the US-PAY-008
   * AwaitingApproval state so the summary card + approval review extras show while
   * an approver reviews the run.
   */
  readonly isComplete = computed(() => {
    const s = this.status();
    return (
      s === 'ReviewPending' ||
      s === 'AwaitingApproval' ||
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

  /**
   * Generation is allowed on a ReviewPending/Approved/Finalized run (BR-1), but the
   * child only offers "Regenerate" on a non-finalized run (AC-5). We pass
   * `canGenerate = run is NOT Finalized` so a finalized run is view/download only;
   * the child still shows downloads regardless.
   */
  readonly canGeneratePayslips = computed(() => this.status() !== 'Finalized');

  /** 0-based index of the current step in RUN_STEPPER (Cancelled/Rejected → -1). */
  readonly stepIndex = computed(() => {
    const s = this.status();
    if (!s) {
      return -1;
    }
    return this.stepper.findIndex((step) => step.status === s);
  });

  // ─── US-PAY-008 approval derived state ─────────────────────

  /** Exceptions from the approval summary (FR-4); [] when none / not loaded. */
  readonly exceptions = computed(
    () => this.approvalSummary()?.exceptions ?? [],
  );

  /** Variance band for the §8 colour-coded month-over-month highlight. */
  readonly varianceBandValue = computed<VarianceBand>(() =>
    varianceBand(this.approvalSummary()?.variancePercentage ?? null),
  );

  /** Tailwind text colour for the variance figure (§8). */
  readonly varianceClass = computed(
    () => VARIANCE_BAND_CLASS[this.varianceBandValue()],
  );

  /** Signed, formatted variance label (e.g. "+7.3%"); "—" when no baseline. */
  readonly varianceLabel = computed(() => {
    const v = this.approvalSummary()?.variancePercentage;
    if (v === null || v === undefined) {
      return '—';
    }
    const sign = v > 0 ? '+' : '';
    return `${sign}${v.toFixed(1)}%`;
  });

  /**
   * Whether the current user may act as an approver on this run (AC-2/AC-3 gate).
   * The backend is the real authority (maker-checker BR-5 + Payroll.Approve); this
   * is the FE permission gate so non-approvers don't see approve/reject controls.
   */
  readonly canApprove = computed(() =>
    this.auth.hasPermission('Payroll.Approve'),
  );

  // ─── ISSUE-154 cancel / re-run state ───────────────────────

  /** Which run-action the inline confirm panel is gating (null = none). */
  readonly confirmAction = signal<'cancel' | 'rerun' | null>(null);

  /**
   * Whether the current user may run Cancel/Re-run (Payroll.Run). The backend
   * re-enforces the permission + the state guard; this hides the controls from
   * users who can't act.
   */
  private readonly canRunActions = computed(() =>
    this.auth.hasPermission('Payroll.Run'),
  );

  /**
   * Cancel is offered on any pre-Finalized, non-Cancelled run (mirrors the BE
   * guard: a finalized or already-cancelled run can't be cancelled).
   */
  readonly canCancel = computed(() => {
    const s = this.status();
    return (
      this.canRunActions() && !!s && s !== 'Finalized' && s !== 'Cancelled'
    );
  });

  /** Re-run is offered only on a ReviewPending run (mirrors the BE guard). */
  readonly canRerun = computed(
    () => this.canRunActions() && this.status() === 'ReviewPending',
  );

  /** Whether the sticky action bar should render at all for the current status. */
  readonly hasActions = computed(() => {
    const s = this.status();
    return (
      s === 'ReviewPending' ||
      s === 'AwaitingApproval' ||
      s === 'Approved'
    );
  });

  /** The most recent comment in the history (shown on the Rejected banner). */
  readonly lastComment = computed(
    () => this.history().find((h) => !!h.comments)?.comments ?? null,
  );

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
        this.loadApprovalData(r.status);
      },
      error: () => {
        this.error.set('Could not load this payroll run.');
        this.loading.set(false);
      },
    });
  }

  /**
   * US-PAY-008: load the approval-history timeline (always — it carries the audit
   * trail for any status) and, once the run has reached review/approval, the
   * approval review summary (FR-4 totals + variance + exceptions). Both are
   * best-effort: a failure leaves the page usable (the run itself already loaded).
   */
  private loadApprovalData(status: PayrollRunStatus): void {
    this.approvalService.getApprovalHistory(this.runId).subscribe({
      next: (h) => this.history.set(h),
      error: () => this.history.set([]),
    });

    const wantsSummary =
      status === 'ReviewPending' ||
      status === 'AwaitingApproval' ||
      status === 'Approved' ||
      status === 'Finalized';
    if (wantsSummary) {
      this.approvalService.getApprovalSummary(this.runId).subscribe({
        next: (s) => this.approvalSummary.set(s),
        error: () => this.approvalSummary.set(null),
      });
    }
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
      next: (r) => {
        this.run.set(r);
        this.loadApprovalData(r.status);
      },
    });
  }

  togglePayslips(): void {
    this.showPayslips.update((v) => !v);
  }

  // ─── US-PAY-008 approval actions ───────────────────────────

  /** Begin a "Reject" — reveals the required comments field (AC-3). */
  startReject(): void {
    this.commentAction.set('reject');
    this.comments.reset('');
  }

  /** Begin a "Return to HR" — reveals the required comments field (FR-9). */
  startReturn(): void {
    this.commentAction.set('return');
    this.comments.reset('');
  }

  /** Dismiss the comments field without acting. */
  cancelComment(): void {
    this.commentAction.set(null);
    this.comments.reset('');
  }

  /** Submit a ReviewPending run for approval (AC-1). */
  submitForApproval(): void {
    this.runAction(
      this.approvalService.submit(this.runId),
      'Submitted for approval.',
    );
  }

  /** Approve the current step (AC-2). */
  approve(): void {
    this.runAction(
      this.approvalService.approve(this.runId),
      'Payroll run approved.',
    );
  }

  /** Confirm a rejection with the required reason (AC-3). */
  confirmReject(): void {
    if (!this.requireComment()) {
      return;
    }
    this.runAction(
      this.approvalService.reject(this.runId, {
        comments: this.comments.value.trim(),
      }),
      'Payroll run rejected. HR has been notified.',
    );
  }

  /** Confirm a return-to-HR with the required comments (FR-9). */
  confirmReturn(): void {
    if (!this.requireComment()) {
      return;
    }
    this.runAction(
      this.approvalService.return(this.runId, {
        comments: this.comments.value.trim(),
      }),
      'Payroll run returned to HR.',
    );
  }

  /** Finalize an Approved run (AC-5) — locks payslips (FR-8). */
  finalize(): void {
    this.runAction(
      this.approvalService.finalize(this.runId),
      'Payroll run finalized.',
    );
  }

  /** Validate the comments field for reject/return; marks touched on failure. */
  private requireComment(): boolean {
    if (this.comments.invalid) {
      this.comments.markAsTouched();
      return false;
    }
    return true;
  }

  /**
   * Run an approval action observable: flips the `acting` flag, on success updates
   * the run + reloads approval data + clears any comments field, and toasts. A
   * failure surfaces a toast and re-enables the bar (the backend owns the real
   * state-machine + maker-checker enforcement, BR-5).
   */
  private runAction(
    action$: Observable<IPayrollRun>,
    successMessage: string,
  ): void {
    this.acting.set(true);
    action$.pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        // The approval action endpoints return a result DTO (status + step info),
        // not the full run — refetch the run + approval data so the summary card,
        // totals, history timeline and action bar all reflect the new state.
        this.progress.set(null);
        this.commentAction.set(null);
        this.comments.reset('');
        this.acting.set(false);
        this.toastr.success(successMessage);
        this.load();
      },
      error: () => {
        this.acting.set(false);
        this.toastr.error('That action could not be completed.');
      },
    });
  }

  // ─── ISSUE-154 cancel / re-run actions ─────────────────────

  /** Reveal the inline confirm for a Cancel (it removes the run's slips). */
  startCancel(): void {
    this.confirmAction.set('cancel');
  }

  /** Reveal the inline confirm for a Re-run (it replaces the run's slips). */
  startRerun(): void {
    this.confirmAction.set('rerun');
  }

  /** Dismiss the inline confirm without acting. */
  dismissConfirm(): void {
    this.confirmAction.set(null);
  }

  /** Confirm the cancel — removes slips and moves the run to Cancelled. */
  confirmCancel(): void {
    this.runRunAction(
      this.runsService.cancelRun(this.runId),
      'Payroll run cancelled. Generated payslips were removed.',
    );
  }

  /** Confirm the re-run — replaces slips and reprocesses payroll (202 Accepted). */
  confirmRerun(): void {
    this.runRunAction(
      this.runsService.rerunRun(this.runId),
      'Re-run started. Payslips are being reprocessed.',
    );
  }

  /**
   * Run a Cancel/Re-run observable: flips `acting`, on success clears the confirm
   * panel + refetches the run (so status/stepper/summary reflect the new state) +
   * toasts. On error, maps the 409 ApiResponse `code` to a friendly message (the
   * backend owns the real state guard + Payroll.Run enforcement).
   */
  private runRunAction(
    action$: Observable<IPayrollRun>,
    successMessage: string,
  ): void {
    this.acting.set(true);
    action$.pipe(takeUntil(this.destroy$)).subscribe({
      next: () => {
        this.confirmAction.set(null);
        this.progress.set(null);
        this.acting.set(false);
        this.toastr.success(successMessage);
        this.load();
      },
      error: (err: unknown) => {
        this.acting.set(false);
        const code =
          err instanceof HttpErrorResponse
            ? (err.error?.code as string | undefined)
            : undefined;
        this.toastr.error(runActionErrorMessage(code));
      },
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
