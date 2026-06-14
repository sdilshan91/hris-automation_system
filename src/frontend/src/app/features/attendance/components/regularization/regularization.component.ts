import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  ICreateRegularizationRequest,
  IRegularization,
  RegularizationType,
  REGULARIZATION_STATUS_CLASSES,
  regularizationStatusLabel,
  regularizationTypeLabel,
  typeRequiresClockIn,
  typeRequiresClockOut,
  todayLocalIso,
} from '../../models/attendance.models';

/**
 * US-ATT-003 (FR-5): cross-field validators on the regularization form.
 *
 * 1. Future-date block (BR-4): the regularized date cannot be after today.
 * 2. Conditional required times (FR-1, §7): clock-in/out are required only for the
 *    relevant type (MISSED_CLOCK_IN/BOTH need in; MISSED_CLOCK_OUT/BOTH need out).
 * 3. Time order (FR-5): when BOTH times are present, clock-in must be before clock-out.
 *
 * Times are `HH:mm` strings for a single calendar day, so a lexical compare is a
 * correct chronological compare.
 */
function regularizationValidator(group: AbstractControl): ValidationErrors | null {
  const errors: ValidationErrors = {};

  const date = group.get('date')?.value as string;
  if (date && date > todayLocalIso()) {
    errors['futureDate'] = true;
  }

  const type = group.get('regularizationType')?.value as RegularizationType | '';
  const clockIn = group.get('requestedClockIn')?.value as string;
  const clockOut = group.get('requestedClockOut')?.value as string;

  if (type && typeRequiresClockIn(type) && !clockIn) {
    errors['clockInRequired'] = true;
  }
  if (type && typeRequiresClockOut(type) && !clockOut) {
    errors['clockOutRequired'] = true;
  }

  // FR-5: clock-in before clock-out when both supplied (single calendar day).
  if (clockIn && clockOut && clockIn >= clockOut) {
    errors['timeOrder'] = true;
  }

  return Object.keys(errors).length ? errors : null;
}

/**
 * US-ATT-003: Attendance Regularization (Forgot Clock-In/Out).
 *
 * Smart component combining (§8):
 *  - A list of the employee's regularization requests with a Notion-style status pill
 *    (Pending/Approved/Rejected/Cancelled) — the success state after submit (§8).
 *  - A right-sliding drawer holding the reactive regularization form (full-screen on
 *    mobile). Fields per §7: date, type, conditional times, reason.
 *  - A static approval-chain placeholder below the form ("Pending line manager
 *    approval") — the real workflow engine (US-ATT-004 / S34) is not built yet.
 *
 * Validation (FR-5, BR-4, BR-7): reason min 10 chars with a live counter; date not in
 * the future; conditional required times; clock-in before clock-out. The backend is
 * the source of truth — AC-3 (lookback), AC-4 (duplicate pending) and AC-5 (locked
 * payroll period) arrive as a `{ message, code }` body and are shown inline verbatim.
 *
 * Role-gated to Employee (and Manager/HR/Admin) via the route guard.
 */
@Component({
  selector: 'app-regularization',
  standalone: true,
  imports: [CommonModule, DatePipe, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [style({ opacity: 0 }), animate('200ms ease-out', style({ opacity: 1 }))]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('260ms cubic-bezier(0.4, 0, 0.2, 1)', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms cubic-bezier(0.4, 0, 1, 1)', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Attendance Regularization</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Forgot to clock in or out? Request a correction and your manager will review it.
          </p>
        </div>
        <button type="button" class="btn-primary text-sm" (click)="openForm()">
          + Request Regularization
        </button>
      </div>

      @if (isLoading()) {
        <div class="card-notion" aria-live="polite" aria-busy="true">
          <div class="space-y-3">
            @for (_ of [1,2,3]; track $index) {
              <div class="skeleton-line w-full h-12"></div>
            }
          </div>
        </div>
      } @else if (requests().length === 0) {
        <div @fadeIn class="card-notion text-center py-16">
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No regularization requests yet</h3>
          <p class="text-sm text-neutral-500 mb-4">
            When you request an attendance correction, it appears here with its status.
          </p>
          <button type="button" class="btn-primary" (click)="openForm()">
            + Request Regularization
          </button>
        </div>
      } @else {
        <!-- Desktop table -->
        <div class="hidden md:block card-notion overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" aria-label="My regularization requests">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Date</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Type</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Reason</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Requested</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (req of requests(); track req.regularizationId) {
                <tr class="border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors">
                  <td class="py-3 px-3 font-medium text-neutral-900">{{ req.date | date: 'mediumDate' }}</td>
                  <td class="py-3 px-3 text-neutral-600">{{ typeLabel(req.regularizationType) }}</td>
                  <td class="py-3 px-3 text-neutral-500 max-w-xs truncate" [title]="req.reason">{{ req.reason }}</td>
                  <td class="py-3 px-3 text-neutral-500 text-xs">{{ req.createdAt | date: 'short' }}</td>
                  <td class="py-3 px-3 text-center">
                    <span class="status-badge" [class]="badgeClass(req)">{{ statusLabel(req.status) }}</span>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile cards -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (req of requests(); track req.regularizationId) {
            <div class="card-notion">
              <div class="flex items-center justify-between mb-2">
                <span class="font-medium text-neutral-900">{{ req.date | date: 'mediumDate' }}</span>
                <span class="status-badge" [class]="badgeClass(req)">{{ statusLabel(req.status) }}</span>
              </div>
              <p class="text-sm text-neutral-600">{{ typeLabel(req.regularizationType) }}</p>
              <p class="text-xs text-neutral-500 mt-1 line-clamp-2">{{ req.reason }}</p>
            </div>
          }
        </div>
      }
    </div>

    <!-- ─── Regularization drawer (slides in from the right, §8) ─────────── -->
    @if (formOpen()) {
      <div class="drawer-backdrop" @backdrop (click)="closeForm()" aria-hidden="true"></div>
      <div class="drawer-wrap" role="dialog" aria-modal="true" aria-labelledby="reg-drawer-title">
        <div class="drawer-panel" @drawer>
          <!-- Drawer header -->
          <div class="flex items-start justify-between gap-3 px-5 py-4 border-b border-neutral-100">
            <div>
              <h2 id="reg-drawer-title" class="text-lg font-semibold text-neutral-900">Request Regularization</h2>
              <p class="text-sm text-neutral-500 mt-0.5">Correct a missed clock-in or clock-out.</p>
            </div>
            <button type="button" class="icon-btn" (click)="closeForm()" aria-label="Close">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                class="w-5 h-5" aria-hidden="true">
                <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
              </svg>
            </button>
          </div>

          <!-- Drawer body (scrollable) -->
          <form [formGroup]="form" (ngSubmit)="submit()" class="drawer-body">
            <!-- Date -->
            <div>
              <label class="label-sm" for="reg-date">Date</label>
              <input id="reg-date" type="date" class="input-field" formControlName="date"
                [max]="maxDate" aria-label="Date to regularize" />
              @if (showError('date') && form.get('date')?.hasError('required')) {
                <p class="error-text">Please select a date.</p>
              }
              @if (form.errors?.['futureDate']) {
                <p class="error-text">The date cannot be in the future.</p>
              }
            </div>

            <!-- Type -->
            <div>
              <label class="label-sm" for="reg-type">What did you miss?</label>
              <select id="reg-type" class="input-field select-input" formControlName="regularizationType"
                aria-label="Regularization type">
                <option value="">Select...</option>
                <option value="MISSED_CLOCK_IN">Missed clock-in</option>
                <option value="MISSED_CLOCK_OUT">Missed clock-out</option>
                <option value="MISSED_BOTH">Missed both</option>
              </select>
              @if (showError('regularizationType')) {
                <p class="error-text">Please select what you missed.</p>
              }
            </div>

            <!-- Conditional times -->
            @if (requiresClockIn() || requiresClockOut()) {
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
                @if (requiresClockIn()) {
                  <div>
                    <label class="label-sm" for="reg-clock-in">Clock-in time</label>
                    <input id="reg-clock-in" type="time" class="input-field"
                      formControlName="requestedClockIn" aria-label="Requested clock-in time" />
                    @if (form.errors?.['clockInRequired'] && touched('requestedClockIn')) {
                      <p class="error-text">Clock-in time is required.</p>
                    }
                  </div>
                }
                @if (requiresClockOut()) {
                  <div>
                    <label class="label-sm" for="reg-clock-out">Clock-out time</label>
                    <input id="reg-clock-out" type="time" class="input-field"
                      formControlName="requestedClockOut" aria-label="Requested clock-out time" />
                    @if (form.errors?.['clockOutRequired'] && touched('requestedClockOut')) {
                      <p class="error-text">Clock-out time is required.</p>
                    }
                  </div>
                }
              </div>
              @if (form.errors?.['timeOrder']) {
                <p class="error-text">Clock-in time must be before clock-out time.</p>
              }
            }

            <!-- Reason -->
            <div>
              <div class="flex items-center justify-between">
                <label class="label-sm" for="reg-reason">Reason</label>
                <span class="text-xs" [class.text-red-500]="reasonBelowMin()"
                  [class.text-neutral-400]="!reasonBelowMin()">
                  {{ reasonLength() }}/{{ minReason }}
                </span>
              </div>
              <textarea id="reg-reason" class="input-field" rows="3" formControlName="reason"
                placeholder="Explain why you missed clocking in/out (min 10 characters)"
                aria-label="Reason"></textarea>
              @if (reasonBelowMin() && touched('reason')) {
                <p class="error-text">Reason must be at least {{ minReason }} characters.</p>
              }
            </div>

            <!-- Approval chain placeholder (§8). Workflow engine not built (US-ATT-004 / S34). -->
            <div class="approval-chain" aria-label="Approval chain">
              <p class="text-xs font-medium text-neutral-400 uppercase tracking-wide mb-2">Approval chain</p>
              <div class="flex items-center gap-2.5">
                <span class="chain-dot"></span>
                <span class="text-sm text-neutral-600">Pending line manager approval</span>
              </div>
              <p class="text-xs text-neutral-400 mt-2">
                Your manager will be notified once you submit this request.
              </p>
            </div>

            <!-- Inline server error (AC-3 lookback, AC-4 duplicate, AC-5 locked period) -->
            @if (serverError(); as msg) {
              <div class="error-banner" role="alert">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                  class="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" aria-hidden="true">
                  <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-7 4a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm-1-9a.75.75 0 0 0-.75.75v3.5a.75.75 0 0 0 1.5 0v-3.5A.75.75 0 0 0 10 5Z" clip-rule="evenodd"/>
                </svg>
                <p class="text-sm text-red-700 flex-1">{{ msg }}</p>
              </div>
            }
          </form>

          <!-- Drawer footer -->
          <div class="drawer-footer">
            <button type="button" class="btn-secondary" (click)="closeForm()" [disabled]="isSubmitting()">
              Cancel
            </button>
            <button type="button" class="btn-primary" (click)="submit()" [disabled]="isSubmitting()">
              @if (isSubmitting()) {
                <span class="btn-spinner"></span> Submitting...
              } @else {
                Submit request
              }
            </button>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-4xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .status-badge {
      @apply inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset;
    }

    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-field {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2
        text-sm text-neutral-900 placeholder-neutral-400 transition-all duration-150
        focus:outline-none focus:ring-2 focus:ring-brand-500/20 focus:border-brand-400;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }
    .error-text { @apply text-xs text-red-600 mt-1; }

    .approval-chain { @apply rounded-xl bg-neutral-50 border border-neutral-100 p-4; }
    .chain-dot { @apply w-2 h-2 rounded-full bg-amber-400; }

    .error-banner {
      @apply bg-red-50 border border-red-100 rounded-lg p-3 flex items-start gap-2.5;
    }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50 disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .icon-btn {
      @apply inline-flex items-center justify-center w-8 h-8 rounded-lg text-neutral-400
        transition-colors duration-150 hover:bg-neutral-100 hover:text-neutral-600;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* ─── Drawer (right slide-in; full-screen on mobile, §8) ─────────── */
    .drawer-backdrop { @apply fixed inset-0 z-40 bg-neutral-900/30 backdrop-blur-[1px]; }
    .drawer-wrap { @apply fixed inset-0 z-50 flex justify-end pointer-events-none; }
    .drawer-panel {
      @apply pointer-events-auto bg-white shadow-xl flex flex-col h-full
        w-full sm:max-w-md sm:w-full;
    }
    .drawer-body { @apply flex-1 overflow-y-auto px-5 py-5 space-y-5; }
    .drawer-footer {
      @apply flex items-center justify-end gap-3 px-5 py-4 border-t border-neutral-100 bg-white;
    }
  `],
})
export class RegularizationComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly attendanceService = inject(AttendanceService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  /** Reason minimum length (BR-7). */
  readonly minReason = 10;
  /** Max selectable date (today, BR-4) for the picker. */
  readonly maxDate = todayLocalIso();

  // ─── State ────────────────────────────────────────────────
  readonly requests = signal<IRegularization[]>([]);
  readonly isLoading = signal(true);
  readonly isSubmitting = signal(false);
  readonly formOpen = signal(false);
  readonly serverError = signal<string | null>(null);

  /** Mirror of form values so computeds (type/reason) react to changes. */
  private readonly formValue = signal<{
    regularizationType: RegularizationType | '';
    reason: string;
  }>({ regularizationType: '', reason: '' });

  readonly form: FormGroup = this.fb.group(
    {
      date: ['', Validators.required],
      regularizationType: ['', Validators.required],
      requestedClockIn: [''],
      requestedClockOut: [''],
      reason: ['', [Validators.required, Validators.minLength(this.minReason)]],
    },
    { validators: [regularizationValidator] },
  );

  // ─── Computed ─────────────────────────────────────────────

  readonly requiresClockIn = computed(() => {
    const t = this.formValue().regularizationType;
    return t === 'MISSED_CLOCK_IN' || t === 'MISSED_BOTH';
  });

  readonly requiresClockOut = computed(() => {
    const t = this.formValue().regularizationType;
    return t === 'MISSED_CLOCK_OUT' || t === 'MISSED_BOTH';
  });

  readonly reasonLength = computed(() => this.formValue().reason.trim().length);
  readonly reasonBelowMin = computed(() => this.reasonLength() < this.minReason);

  // ─── Lifecycle ────────────────────────────────────────────

  ngOnInit(): void {
    this.form.valueChanges.pipe(takeUntil(this.destroy$)).subscribe((v) => {
      this.formValue.set({
        regularizationType: (v.regularizationType ?? '') as RegularizationType | '',
        reason: v.reason ?? '',
      });
    });
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ─────────────────────────────────────────

  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .listRegularizations()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (requests) => {
          this.requests.set(requests);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load your regularization requests.');
        },
      });
  }

  // ─── Display helpers ──────────────────────────────────────

  typeLabel(type: RegularizationType): string {
    return regularizationTypeLabel(type);
  }

  statusLabel(status: IRegularization['status']): string {
    return regularizationStatusLabel(status);
  }

  badgeClass(req: IRegularization): string {
    return REGULARIZATION_STATUS_CLASSES[req.status] ?? REGULARIZATION_STATUS_CLASSES.PENDING;
  }

  showError(controlName: string): boolean {
    const control = this.form.get(controlName);
    return !!control && control.invalid && (control.touched || control.dirty);
  }

  touched(controlName: string): boolean {
    const control = this.form.get(controlName);
    return !!control && (control.touched || control.dirty);
  }

  // ─── Form open/close ──────────────────────────────────────

  /** Open the drawer, pre-populating the date with today (§8). */
  openForm(): void {
    this.serverError.set(null);
    this.form.reset({
      date: todayLocalIso(),
      regularizationType: '',
      requestedClockIn: '',
      requestedClockOut: '',
      reason: '',
    });
    this.formOpen.set(true);
  }

  closeForm(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.formOpen.set(false);
    this.serverError.set(null);
  }

  // ─── Submit (AC-1, AC-2; AC-3/AC-4/AC-5 rejections) ───────

  submit(): void {
    this.serverError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toastr.warning('Please fix the highlighted fields.');
      return;
    }

    const request = this.buildRequest();
    this.isSubmitting.set(true);
    this.attendanceService
      .submitRegularization(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (created) => this.onSubmitted(created),
        error: (err: HttpErrorResponse) => this.onSubmitError(err),
      });
  }

  /** Build the create payload, nulling out times the type does not require (§7). */
  private buildRequest(): ICreateRegularizationRequest {
    const raw = this.form.getRawValue();
    const type = raw.regularizationType as RegularizationType;
    return {
      date: raw.date,
      regularizationType: type,
      requestedClockIn: typeRequiresClockIn(type) ? (raw.requestedClockIn || null) : null,
      requestedClockOut: typeRequiresClockOut(type) ? (raw.requestedClockOut || null) : null,
      reason: raw.reason.trim(),
    };
  }

  /** Success (AC-1/AC-2): prepend the new Pending record, close drawer, toast. */
  private onSubmitted(created: IRegularization): void {
    this.isSubmitting.set(false);
    this.requests.set([created, ...this.requests()]);
    this.formOpen.set(false);
    this.toastr.success(
      `Regularization for ${created.date} submitted — pending approval.`,
      'Request submitted',
    );
  }

  /**
   * AC-3/AC-4/AC-5: backend rejection. Show the server `message` verbatim inline,
   * keeping the drawer open so the employee sees the context and can adjust.
   */
  private onSubmitError(err: HttpErrorResponse): void {
    this.isSubmitting.set(false);
    const parsed = AttendanceService.parseRegularizationError(err);
    this.serverError.set(parsed?.message ?? 'An unexpected error occurred. Please try again.');
  }
}
