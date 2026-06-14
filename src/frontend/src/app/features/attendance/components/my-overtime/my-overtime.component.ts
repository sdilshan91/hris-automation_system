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
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  IOvertime,
  IOvertimePreApprovalRequest,
  OVERTIME_STATUS_CLASSES,
  WEEKLY_OVERTIME_CAP_MINUTES,
  overtimeStatusLabel,
  overtimeTypeLabel,
  formatMultiplier,
  formatWorkMinutes,
  weeklyOvertimeMinutes,
  todayLocalIso,
} from '../../models/attendance.models';

/**
 * US-ATT-006: Employee-facing "My Overtime" view.
 *
 * Shows the employee's overtime records (auto-detected + pre-approved) as Notion-style
 * rows with color-coded status pills (amber Pending / green Approved / red Rejected /
 * gray Unapproved), the multiplier, and a collapsible detail block — the daily card's
 * overtime detail surfaced as a dedicated list (§8). A weekly-progress bar shows how
 * close the employee is to the configured weekly overtime cap (BR-5, §8), and a simple
 * pre-approval form (date / expected hours / reason) submits AC-2 / FR-4 requests.
 *
 * Available to any authenticated employee — the route inherits the parent attendance guard.
 */
@Component({
  selector: 'app-my-overtime',
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
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">My Overtime</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Track your overtime hours, multipliers and approval status.
          </p>
        </div>
        <button type="button" class="btn-primary text-sm" data-test="toggle-form"
          (click)="toggleForm()">
          {{ showForm() ? 'Close' : 'Request pre-approval' }}
        </button>
      </div>

      <!-- Weekly progress bar (BR-5, §8) -->
      <div class="card-notion mb-5" data-test="weekly-progress">
        <div class="flex items-center justify-between mb-2">
          <span class="text-sm font-medium text-neutral-700">This week's overtime</span>
          <span class="text-sm text-neutral-500" data-test="weekly-summary">
            {{ formatMinutes(weeklyMinutes()) }} / {{ formatMinutes(weeklyCap) }}
          </span>
        </div>
        <div class="progress-track" role="progressbar"
          [attr.aria-valuenow]="weeklyMinutes()" aria-valuemin="0"
          [attr.aria-valuemax]="weeklyCap">
          <div class="progress-fill" [class]="progressClass()"
            [style.width.%]="weeklyPercent()" data-test="weekly-bar"></div>
        </div>
        @if (weeklyPercent() >= 80) {
          <p class="text-xs mt-2" [class.text-red-600]="weeklyPercent() >= 100"
            [class.text-amber-600]="weeklyPercent() < 100" data-test="weekly-warning">
            You are approaching your weekly overtime limit.
          </p>
        }
      </div>

      <!-- Pre-approval form (AC-2, FR-4) -->
      @if (showForm()) {
        <form class="card-notion mb-5" @expand (ngSubmit)="submit()" data-test="preapproval-form">
          <h2 class="text-base font-semibold text-neutral-800 mb-4">Overtime pre-approval</h2>
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label class="dl mb-1 block" for="ot-date">Date</label>
              <input id="ot-date" type="date" class="field" [max]="today"
                [ngModel]="formDate()" (ngModelChange)="formDate.set($event)"
                name="date" data-test="form-date" required />
            </div>
            <div>
              <label class="dl mb-1 block" for="ot-hours">Expected hours</label>
              <input id="ot-hours" type="number" class="field" min="0.5" step="0.5"
                [ngModel]="formHours()" (ngModelChange)="formHours.set($event)"
                name="hours" placeholder="e.g. 2" data-test="form-hours" required />
            </div>
          </div>
          <div class="mt-4">
            <div class="flex items-center justify-between mb-1">
              <label class="dl block" for="ot-reason">Reason</label>
              <span class="text-xs" [class.text-red-500]="reasonBelowMin() && formReason().length > 0"
                [class.text-neutral-400]="!reasonBelowMin() || formReason().length === 0">
                {{ formReason().trim().length }}/{{ minReason }}
              </span>
            </div>
            <textarea id="ot-reason" rows="3" class="field resize-none"
              [class.field-error]="reasonBelowMin() && formReason().length > 0"
              [ngModel]="formReason()" (ngModelChange)="formReason.set($event)"
              name="reason" placeholder="Why is the overtime needed? (min 10 characters)"
              data-test="form-reason"></textarea>
          </div>
          <div class="flex justify-end gap-2 mt-4">
            <button type="button" class="btn-secondary text-sm" (click)="toggleForm()"
              [disabled]="isSubmitting()" data-test="form-cancel">Cancel</button>
            <button type="submit" class="btn-primary text-sm"
              [disabled]="isSubmitting() || !canSubmit()" data-test="form-submit">
              {{ isSubmitting() ? 'Submitting…' : 'Submit request' }}
            </button>
          </div>
        </form>
      }

      <!-- Records list -->
      @if (isLoading()) {
        <div class="card-notion space-y-3" aria-busy="true" data-test="skeleton">
          @for (_ of [1,2,3]; track $index) {
            <div class="skeleton-line h-12 w-full"></div>
          }
        </div>
      } @else if (records().length === 0) {
        <div class="card-notion text-center py-16" @fadeIn data-test="empty">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No overtime yet</h3>
          <p class="text-sm text-neutral-500">
            Overtime is detected automatically when you exceed your shift hours.
          </p>
        </div>
      } @else {
        <div class="space-y-3" @fadeIn>
          @for (ot of records(); track ot.id) {
            <div class="card-notion !p-4" data-test="overtime-row">
              <button type="button" class="w-full flex items-center justify-between gap-3 text-left"
                (click)="toggleExpand(ot.id)"
                [attr.aria-expanded]="expandedId() === ot.id"
                [attr.data-test]="'expand-' + ot.id">
                <div class="min-w-0">
                  <div class="flex items-center gap-2">
                    <span class="font-medium text-neutral-900">{{ ot.date | date:'mediumDate' }}</span>
                    <span class="status-badge" [class]="statusClass(ot)"
                      [attr.data-test]="'status-' + ot.id">{{ statusLabel(ot) }}</span>
                  </div>
                  <p class="text-xs text-neutral-500 mt-0.5">
                    {{ formatMinutes(displayMinutes(ot)) }} · {{ multiplier(ot) }} · {{ typeLabel(ot) }}
                  </p>
                </div>
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                  class="w-5 h-5 text-neutral-400 flex-shrink-0 transition-transform"
                  [class.rotate-180]="expandedId() === ot.id" aria-hidden="true">
                  <path fill-rule="evenodd" d="M5.23 7.21a.75.75 0 0 1 1.06.02L10 11.168l3.71-3.938a.75.75 0 1 1 1.08 1.04l-4.25 4.5a.75.75 0 0 1-1.08 0l-4.25-4.5a.75.75 0 0 1 .02-1.06Z" clip-rule="evenodd"/>
                </svg>
              </button>
              @if (expandedId() === ot.id) {
                <dl class="grid grid-cols-2 gap-x-6 gap-y-2 text-sm mt-3 pt-3 border-t border-neutral-100"
                  @expand [attr.data-test]="'detail-' + ot.id">
                  <div><dt class="dl">Overtime</dt><dd class="text-neutral-800">{{ formatMinutes(ot.overtimeMinutes) }}</dd></div>
                  <div><dt class="dl">Approved</dt><dd class="text-neutral-800">{{ ot.approvedMinutes != null ? formatMinutes(ot.approvedMinutes) : '—' }}</dd></div>
                  <div><dt class="dl">Multiplier</dt><dd class="text-neutral-800">{{ multiplier(ot) }}</dd></div>
                  <div><dt class="dl">Type</dt><dd class="text-neutral-800">{{ typeLabel(ot) }}</dd></div>
                  <div class="col-span-2"><dt class="dl">Reason</dt><dd class="text-neutral-700 whitespace-pre-line">{{ ot.reason }}</dd></div>
                  @if (ot.managerComment) {
                    <div class="col-span-2"><dt class="dl">Manager comment</dt><dd class="text-neutral-700 whitespace-pre-line">{{ ot.managerComment }}</dd></div>
                  }
                </dl>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-4xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .dl { @apply text-[11px] font-medium text-neutral-400 uppercase tracking-wider; }
    .status-badge { @apply inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset; }

    .field {
      @apply block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .field-error { @apply border-red-300 focus:border-red-400 focus:ring-red-400; }

    .progress-track { @apply w-full h-2.5 rounded-full bg-neutral-100 overflow-hidden; }
    .progress-fill { @apply h-full rounded-full transition-all duration-300; }
    .progress-ok { @apply bg-brand-500; }
    .progress-warn { @apply bg-amber-500; }
    .progress-over { @apply bg-red-500; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2.5
        text-sm font-medium text-white transition-colors hover:bg-brand-700
        disabled:opacity-40 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class MyOvertimeComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** Pre-approval reason minimum length. */
  readonly minReason = 10;
  readonly today = todayLocalIso();
  readonly weeklyCap = WEEKLY_OVERTIME_CAP_MINUTES;

  // ─── State ──────────────────────────────────────────────────
  readonly records = signal<IOvertime[]>([]);
  readonly isLoading = signal(true);
  readonly expandedId = signal<string | null>(null);

  readonly showForm = signal(false);
  readonly isSubmitting = signal(false);
  readonly formDate = signal(todayLocalIso());
  readonly formHours = signal<number | null>(null);
  readonly formReason = signal('');

  // ─── Computed ───────────────────────────────────────────────
  readonly weeklyMinutes = computed(() => weeklyOvertimeMinutes(this.records()));
  readonly weeklyPercent = computed(() =>
    Math.min(100, Math.round((this.weeklyMinutes() / this.weeklyCap) * 100)),
  );
  readonly reasonBelowMin = computed(() => this.formReason().trim().length < this.minReason);
  readonly canSubmit = computed(() => {
    const hours = this.formHours();
    return (
      !!this.formDate() &&
      hours != null &&
      hours > 0 &&
      !this.reasonBelowMin()
    );
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
      .getMyOvertime()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (list) => {
          this.records.set(list);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load your overtime records.');
        },
      });
  }

  toggleExpand(id: string): void {
    this.expandedId.update((cur) => (cur === id ? null : id));
  }

  toggleForm(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.showForm.update((v) => !v);
  }

  submit(): void {
    if (this.isSubmitting() || !this.canSubmit()) {
      return;
    }
    const request: IOvertimePreApprovalRequest = {
      date: this.formDate(),
      expectedHours: Number(this.formHours()),
      reason: this.formReason().trim(),
    };
    this.isSubmitting.set(true);
    this.attendanceService
      .submitOvertimePreApproval(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          this.records.update((list) => [created, ...list]);
          this.showForm.set(false);
          this.formHours.set(null);
          this.formReason.set('');
          this.formDate.set(todayLocalIso());
          this.toastr.success('Overtime pre-approval submitted.');
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmitting.set(false);
          const parsed = AttendanceService.parseOvertimeActionError(err);
          this.toastr.error(parsed?.message ?? 'Failed to submit the pre-approval request.');
        },
      });
  }

  // ─── View helpers ───────────────────────────────────────────
  statusClass(ot: IOvertime): string {
    return OVERTIME_STATUS_CLASSES[ot.status];
  }

  statusLabel(ot: IOvertime): string {
    return overtimeStatusLabel(ot.status);
  }

  typeLabel(ot: IOvertime): string {
    return overtimeTypeLabel(ot.type);
  }

  multiplier(ot: IOvertime): string {
    return formatMultiplier(ot.multiplier);
  }

  /** Approved minutes when decided, else the requested overtime minutes. */
  displayMinutes(ot: IOvertime): number {
    return ot.approvedMinutes != null ? ot.approvedMinutes : ot.overtimeMinutes;
  }

  formatMinutes(minutes: number): string {
    return formatWorkMinutes(minutes);
  }

  progressClass(): string {
    const pct = this.weeklyPercent();
    if (pct >= 100) {
      return 'progress-over';
    }
    if (pct >= 80) {
      return 'progress-warn';
    }
    return 'progress-ok';
  }
}
