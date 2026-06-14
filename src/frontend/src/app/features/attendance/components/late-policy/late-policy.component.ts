import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  ILatePolicy,
  LATE_POLICY_PERIODS,
  latePolicyPeriodLabel,
} from '../../models/attendance.models';

/**
 * US-ATT-008 (AC-4, FR-4): HR late-arrival policy configuration.
 *
 * A Notion-style settings form that loads the tenant's {@link ILatePolicy} (GET) and
 * saves edits (PUT). Drives the deduction rules (BR-4), the per-late notification (FR-5)
 * and the chronic-lateness HR escalation threshold (FR-7). Role-gated to HR / Tenant
 * Admin via the route guard.
 */
@Component({
  selector: 'app-late-policy',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
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
      <div class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Late Arrival Policy</h1>
        <p class="text-sm text-neutral-500 mt-1">
          Configure when accumulated late arrivals trigger a deduction, notifications, and HR escalation.
        </p>
      </div>

      @if (isLoading()) {
        <div class="card-notion space-y-4" aria-busy="true" data-test="skeleton">
          @for (_ of [1,2,3,4]; track $index) {
            <div class="skeleton-line h-12 w-full"></div>
          }
        </div>
      } @else {
        <form class="card-notion space-y-6" [formGroup]="form" (ngSubmit)="onSubmit()" @fadeIn>
          <!-- Active toggle -->
          <div class="flex items-start justify-between gap-4">
            <div>
              <label class="field-label" for="isActive">Enforce this policy</label>
              <p class="field-hint">When off, late arrivals are tracked but no deductions are applied.</p>
            </div>
            <label class="toggle">
              <input id="isActive" type="checkbox" formControlName="isActive" data-test="isActive" />
              <span class="toggle-track"><span class="toggle-thumb"></span></span>
            </label>
          </div>

          <div class="grid grid-cols-1 sm:grid-cols-2 gap-5">
            <!-- Threshold count -->
            <div>
              <label class="field-label" for="thresholdCount">Late threshold</label>
              <p class="field-hint">Number of lates in the period that triggers a deduction.</p>
              <input id="thresholdCount" type="number" min="1" class="field" formControlName="thresholdCount"
                data-test="thresholdCount" />
              @if (showError('thresholdCount')) {
                <p class="field-error" data-test="thresholdCount-error">Enter a whole number of 1 or more.</p>
              }
            </div>

            <!-- Deduction days -->
            <div>
              <label class="field-label" for="deductionDays">Deduction (days)</label>
              <p class="field-hint">Day(s) deducted once the threshold is reached, e.g. 0.5.</p>
              <input id="deductionDays" type="number" min="0" step="0.5" class="field"
                formControlName="deductionDays" data-test="deductionDays" />
              @if (showError('deductionDays')) {
                <p class="field-error" data-test="deductionDays-error">Enter 0 or more.</p>
              }
            </div>

            <!-- Period -->
            <div>
              <label class="field-label" for="period">Accumulation period</label>
              <p class="field-hint">The window the threshold is counted over.</p>
              <select id="period" class="field" formControlName="period" data-test="period">
                @for (p of periods; track p) {
                  <option [value]="p">{{ periodLabel(p) }}</option>
                }
              </select>
            </div>

            <!-- Chronic threshold -->
            <div>
              <label class="field-label" for="chronicThreshold">Chronic lateness threshold</label>
              <p class="field-hint">Lates per month above this escalate to HR.</p>
              <input id="chronicThreshold" type="number" min="1" class="field"
                formControlName="chronicThreshold" data-test="chronicThreshold" />
              @if (showError('chronicThreshold')) {
                <p class="field-error" data-test="chronicThreshold-error">Enter a whole number of 1 or more.</p>
              }
            </div>
          </div>

          <!-- Notify on late -->
          <div class="flex items-start justify-between gap-4 border-t border-neutral-100 pt-5">
            <div>
              <label class="field-label" for="notificationOnLate">Notify employee on each late</label>
              <p class="field-hint">Send an in-app notification with the running monthly count.</p>
            </div>
            <label class="toggle">
              <input id="notificationOnLate" type="checkbox" formControlName="notificationOnLate"
                data-test="notificationOnLate" />
              <span class="toggle-track"><span class="toggle-thumb"></span></span>
            </label>
          </div>

          <div class="flex justify-end gap-3 border-t border-neutral-100 pt-5">
            <button type="submit" class="btn-primary text-sm"
              [disabled]="isSaving() || form.invalid" data-test="save">
              @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save policy }
            </button>
          </div>
        </form>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-3xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5 sm:p-6; }
    .field-label { @apply block text-sm font-medium text-neutral-800; }
    .field-hint { @apply mt-0.5 text-xs text-neutral-500; }
    .field-error { @apply mt-1 text-xs text-red-600; }
    .field {
      @apply mt-2 block w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm
        text-neutral-800 transition-colors focus:border-brand-500 focus:ring-1 focus:ring-brand-500 outline-none;
    }
    .skeleton-line { @apply rounded-lg bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-semibold text-white shadow-sm transition-all duration-200 hover:bg-brand-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
    /* Notion-style toggle. */
    .toggle { @apply relative inline-flex cursor-pointer items-center; }
    .toggle input { @apply sr-only; }
    .toggle-track {
      @apply block h-6 w-11 rounded-full bg-neutral-200 transition-colors duration-200;
    }
    .toggle input:checked + .toggle-track { @apply bg-brand-600; }
    .toggle input:focus-visible + .toggle-track { @apply ring-2 ring-brand-500/40; }
    .toggle-thumb {
      @apply absolute left-0.5 top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform duration-200;
    }
    .toggle input:checked ~ .toggle-track .toggle-thumb { transform: translateX(20px); }
  `],
})
export class LatePolicyComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly fb = inject(FormBuilder);
  private readonly destroy$ = new Subject<void>();

  readonly periods = LATE_POLICY_PERIODS;

  readonly isLoading = signal(true);
  readonly isSaving = signal(false);

  readonly form = this.fb.nonNullable.group({
    thresholdCount: [3, [Validators.required, Validators.min(1)]],
    deductionDays: [0.5, [Validators.required, Validators.min(0)]],
    period: ['MONTHLY' as ILatePolicy['period'], [Validators.required]],
    notificationOnLate: [true],
    chronicThreshold: [5, [Validators.required, Validators.min(1)]],
    isActive: [true],
  });

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
      .getLatePolicy()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (policy) => {
          this.form.patchValue(policy);
          this.isLoading.set(false);
        },
        error: () => {
          // Keep the form on its sensible defaults so HR can still create a policy.
          this.isLoading.set(false);
          this.toastr.error('Could not load the late-arrival policy.');
        },
      });
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.isSaving.set(true);
    const policy = this.form.getRawValue() as ILatePolicy;
    this.attendanceService
      .updateLatePolicy(policy)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (saved) => {
          this.form.patchValue(saved);
          this.isSaving.set(false);
          this.toastr.success('Late-arrival policy saved.');
        },
        error: () => {
          this.isSaving.set(false);
          this.toastr.error('Could not save the late-arrival policy.');
        },
      });
  }

  showError(control: string): boolean {
    const c = this.form.get(control);
    return !!c && c.invalid && (c.touched || c.dirty);
  }

  periodLabel(period: ILatePolicy['period']): string {
    return latePolicyPeriodLabel(period);
  }
}
