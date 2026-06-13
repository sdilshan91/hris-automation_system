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
import { Router } from '@angular/router';
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
import { Subject, forkJoin } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { LeaveRequestService } from '../../services/leave-request.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import { ILeaveType, getContrastTextColor } from '../../models/leave-type.models';
import {
  ICreateLeaveRequest,
  ILeaveBalance,
  HALF_DAY_SESSION_OPTIONS,
  countWorkingDays,
  buildProjection,
} from '../../models/leave-request.models';

/**
 * Cross-field validator: start date must be on or before end date (AC mirror).
 * Only validates when both controls have a value.
 */
function dateRangeValidator(group: AbstractControl): ValidationErrors | null {
  const start = group.get('startDate')?.value;
  const end = group.get('endDate')?.value;
  if (!start || !end) {
    return null;
  }
  return start <= end ? null : { dateRange: true };
}

/**
 * Cross-field validator: a half-day request must be a single day and have a
 * session selected (AC-4).
 */
function halfDayValidator(group: AbstractControl): ValidationErrors | null {
  const isHalfDay = group.get('isHalfDay')?.value;
  if (!isHalfDay) {
    return null;
  }
  const start = group.get('startDate')?.value;
  const end = group.get('endDate')?.value;
  const session = group.get('halfDaySession')?.value;
  const errors: ValidationErrors = {};
  if (start && end && start !== end) {
    errors['halfDaySingleDay'] = true;
  }
  if (!session) {
    errors['halfDaySession'] = true;
  }
  return Object.keys(errors).length ? errors : null;
}

/**
 * US-LV-003: Apply-for-Leave form (FR-1).
 *
 * Reactive form with: leave type (color-coded badge + remaining balance per type),
 * start/end date pickers, half-day toggle + AM/PM session (AC-4), reason textarea,
 * and a drag-and-drop attachment area (blob upload backend DEFERRED -- see below).
 *
 * Real-time balance display (FR-2, AC-2): selecting a type + date range shows
 * current balance, requested days (weekends excluded client-side -- AC-6 holiday
 * adjustment is owned by the backend), and projected remaining, with an inline
 * insufficient-balance block.
 *
 * The backend is the source of truth; API validation errors (overlap AC-5,
 * insufficient balance AC-2, document-required AC-3) are surfaced via toast/inline.
 *
 * Role-gated to Employee (and HR/admin) via the route guard.
 */
@Component({
  selector: 'app-leave-application',
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
      <!-- Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Apply for Leave</h1>
        <p class="text-sm text-neutral-500 mt-1">
          Request time off. Your manager will review and approve it.
        </p>
      </div>

      @if (isLoading()) {
        <div class="card-notion" aria-live="polite" aria-busy="true">
          <div class="space-y-3">
            @for (_ of [1,2,3,4]; track $index) {
              <div class="skeleton-line w-full h-10"></div>
            }
          </div>
        </div>
      } @else {
        <form [formGroup]="form" (ngSubmit)="submit()" class="space-y-5 pb-24 md:pb-0">
          <!-- Leave type + balance -->
          <div class="card-notion">
            <label class="label-sm" for="leaveType">Leave type</label>
            <select id="leaveType" class="input-field select-input" formControlName="leaveTypeId"
              aria-label="Leave type">
              <option value="">Select a leave type...</option>
              @for (lt of leaveTypes(); track lt.leaveTypeId) {
                <option [value]="lt.leaveTypeId">
                  {{ lt.name }} — {{ remainingFor(lt.leaveTypeId) }} days left
                </option>
              }
            </select>
            @if (showError('leaveTypeId')) {
              <p class="error-text">Please select a leave type.</p>
            }

            <!-- Color-coded badge + per-type balance (§8) -->
            @if (selectedType(); as lt) {
              <div class="mt-3 flex flex-wrap items-center gap-2">
                <span class="type-badge"
                  [style.background-color]="lt.color"
                  [style.color]="contrastText(lt.color)">
                  {{ lt.code || lt.name }}
                </span>
                <span class="text-sm text-neutral-600">{{ lt.name }}</span>
              </div>
            }
          </div>

          <!-- Dates -->
          <div class="card-notion">
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label class="label-sm" for="startDate">Start date</label>
                <input id="startDate" type="date" class="input-field" formControlName="startDate"
                  aria-label="Start date" />
                @if (showError('startDate')) {
                  <p class="error-text">Start date is required.</p>
                }
              </div>
              <div>
                <label class="label-sm" for="endDate">End date</label>
                <input id="endDate" type="date" class="input-field" formControlName="endDate"
                  aria-label="End date" />
                @if (showError('endDate')) {
                  <p class="error-text">End date is required.</p>
                }
              </div>
            </div>
            @if (form.errors?.['dateRange']) {
              <p class="error-text">End date must be on or after the start date.</p>
            }

            <!-- Half-day toggle (AC-4) -->
            <div class="mt-4 pt-4 border-t border-neutral-100">
              <label class="flex items-center gap-2.5 cursor-pointer">
                <input type="checkbox" class="w-4 h-4 rounded border-neutral-300 text-brand-600"
                  formControlName="isHalfDay" aria-label="Half-day leave" />
                <span class="text-sm font-medium text-neutral-700">Half-day leave</span>
              </label>
              @if (form.get('isHalfDay')?.value) {
                <div class="mt-3">
                  <label class="label-sm" for="halfDaySession">Session</label>
                  <select id="halfDaySession" class="input-field select-input"
                    formControlName="halfDaySession" aria-label="Half-day session">
                    <option value="">Select session...</option>
                    @for (s of sessionOptions; track s.value) {
                      <option [value]="s.value">{{ s.label }}</option>
                    }
                  </select>
                  @if (form.errors?.['halfDaySingleDay']) {
                    <p class="error-text">A half-day request must be for a single day.</p>
                  }
                  @if (form.errors?.['halfDaySession'] && form.get('isHalfDay')?.value) {
                    <p class="error-text">Please select AM or PM.</p>
                  }
                </div>
              }
            </div>

            <!-- Days calculated chip + balance projection (FR-2, AC-2, AC-6) -->
            @if (selectedType() && requestedDays() > 0) {
              <div class="mt-4 pt-4 border-t border-neutral-100 flex flex-wrap items-center gap-3">
                <span class="days-chip">{{ requestedDays() }} day{{ requestedDays() === 1 ? '' : 's' }}</span>
                <span class="text-xs text-neutral-400">weekends excluded</span>
              </div>
              <div class="mt-3 grid grid-cols-3 gap-3 text-center">
                <div class="balance-cell">
                  <p class="balance-label">Current</p>
                  <p class="balance-value">{{ projection().remainingDays }}</p>
                </div>
                <div class="balance-cell">
                  <p class="balance-label">Requested</p>
                  <p class="balance-value">{{ projection().requestedDays }}</p>
                </div>
                <div class="balance-cell">
                  <p class="balance-label">Projected</p>
                  <p class="balance-value"
                    [class.text-red-600]="projection().insufficient"
                    [class.text-green-700]="!projection().insufficient">
                    {{ projection().projectedRemaining }}
                  </p>
                </div>
              </div>
              @if (projection().insufficient) {
                <div class="mt-3 bg-red-50 border border-red-100 rounded-lg p-3 flex items-start gap-2.5"
                  role="alert">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                    class="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" aria-hidden="true">
                    <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-7 4a1 1 0 1 1-2 0 1 1 0 0 1 2 0Zm-1-9a.75.75 0 0 0-.75.75v3.5a.75.75 0 0 0 1.5 0v-3.5A.75.75 0 0 0 10 5Z" clip-rule="evenodd"/>
                  </svg>
                  <p class="text-sm text-red-700">
                    Insufficient balance. You have {{ projection().remainingDays }} day(s) but
                    requested {{ projection().requestedDays }}. Negative balance is not allowed for this leave type.
                  </p>
                </div>
              }
            }
          </div>

          <!-- Reason -->
          <div class="card-notion">
            <label class="label-sm" for="reason">Reason</label>
            <textarea id="reason" class="input-field" rows="3" formControlName="reason"
              placeholder="Briefly describe the reason for your leave" aria-label="Reason"></textarea>
            @if (showError('reason')) {
              <p class="error-text">Please provide a reason.</p>
            }
          </div>

          <!-- Attachments (drag-and-drop, §8). Blob upload backend DEFERRED. -->
          <div class="card-notion">
            <label class="label-sm" id="attach-label">Supporting documents</label>
            @if (documentHint()) {
              <p class="text-xs text-amber-600 mb-2">{{ documentHint() }}</p>
            }
            <!--
              TODO(US-LV-003): wire actual blob upload to tenant-scoped storage
              ({tenantId}/leaves/{requestId}/) per NFR-3. For now the drop zone
              collects file names locally and the form submits attachment URLs/metadata
              the backend accepts (see attachments[] in the contract). The progress bar
              below is a UI seam for the future upload stream.
            -->
            <div class="dropzone"
              [class.dropzone-active]="dragActive()"
              (dragover)="onDragOver($event)"
              (dragleave)="onDragLeave($event)"
              (drop)="onDrop($event)"
              role="button"
              tabindex="0"
              aria-labelledby="attach-label"
              (click)="fileInput.click()"
              (keydown.enter)="fileInput.click()">
              <input #fileInput type="file" class="hidden" multiple accept=".pdf,.jpg,.jpeg,.png"
                (change)="onFilesPicked($event)" aria-hidden="true" />
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none"
                class="w-8 h-8 mx-auto text-neutral-300 mb-2" stroke="currentColor" stroke-width="1.5"
                aria-hidden="true">
                <path stroke-linecap="round" stroke-linejoin="round"
                  d="M3 16.5v2.25A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75V16.5m-13.5-9L12 3m0 0 4.5 4.5M12 3v13.5"/>
              </svg>
              <p class="text-sm text-neutral-500">
                Drag &amp; drop files here, or <span class="text-brand-600 font-medium">browse</span>
              </p>
              <p class="text-xs text-neutral-400 mt-1">PDF, JPG, PNG — up to 5MB each, max 3 files</p>
            </div>

            @if (attachments().length > 0) {
              <ul class="mt-3 space-y-2">
                @for (att of attachments(); track att; let i = $index) {
                  <li class="flex items-center justify-between bg-neutral-50 rounded-lg px-3 py-2">
                    <span class="text-sm text-neutral-700 truncate">{{ att }}</span>
                    <button type="button" class="text-neutral-400 hover:text-red-600"
                      (click)="removeAttachment(i)" [attr.aria-label]="'Remove ' + att">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                        class="w-4 h-4" aria-hidden="true">
                        <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                      </svg>
                    </button>
                  </li>
                }
              </ul>
            }
          </div>

          <!-- Desktop submit -->
          <div class="hidden md:flex items-center justify-end gap-3">
            <button type="button" class="btn-secondary" (click)="cancel()">Cancel</button>
            <button type="submit" class="btn-primary" [disabled]="isSubmitting() || projection().insufficient">
              @if (isSubmitting()) {
                <span class="btn-spinner"></span> Submitting...
              } @else {
                Submit request
              }
            </button>
          </div>
        </form>

        <!-- Mobile sticky submit (§8) -->
        <div class="md:hidden fixed bottom-0 inset-x-0 bg-white border-t border-neutral-200 p-3 z-40 flex gap-3">
          <button type="button" class="btn-secondary flex-1" (click)="cancel()">Cancel</button>
          <button type="button" class="btn-primary flex-1"
            [disabled]="isSubmitting() || projection().insufficient" (click)="submit()">
            @if (isSubmitting()) {
              <span class="btn-spinner"></span> Submitting...
            } @else {
              Submit
            }
          </button>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-2xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

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

    .type-badge {
      @apply inline-flex items-center px-2.5 py-1 rounded-md text-xs font-semibold;
    }
    .days-chip {
      @apply inline-flex items-center px-3 py-1 rounded-full text-sm font-semibold
        bg-brand-50 text-brand-700;
    }
    .balance-cell { @apply rounded-lg bg-neutral-50 py-2.5; }
    .balance-label { @apply text-xs text-neutral-400; }
    .balance-value { @apply text-lg font-semibold text-neutral-900; }

    .dropzone {
      @apply rounded-xl border-2 border-dashed border-neutral-200 bg-neutral-50/50
        py-8 px-4 text-center cursor-pointer transition-colors duration-200
        hover:border-brand-300 hover:bg-brand-50/30 focus:outline-none focus:ring-2 focus:ring-brand-500/20;
    }
    .dropzone-active { @apply border-brand-400 bg-brand-50/50; }

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
        transition-all duration-200 hover:bg-neutral-50;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class LeaveApplicationComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly leaveRequestService = inject(LeaveRequestService);
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly toastr = inject(ToastrService);
  private readonly router = inject(Router);
  private readonly destroy$ = new Subject<void>();

  readonly sessionOptions = HALF_DAY_SESSION_OPTIONS;

  // ─── Data signals ─────────────────────────────────────────
  readonly leaveTypes = signal<ILeaveType[]>([]);
  readonly balances = signal<ILeaveBalance[]>([]);
  readonly isLoading = signal(true);
  readonly isSubmitting = signal(false);

  // Drag-and-drop UI state. Attachments hold file names (URL upload DEFERRED).
  readonly dragActive = signal(false);
  readonly attachments = signal<string[]>([]);

  // Reactive form value mirror so computed signals recompute on every change.
  readonly formValue = signal<{
    leaveTypeId: string;
    startDate: string;
    endDate: string;
    isHalfDay: boolean;
    halfDaySession: string;
  }>({ leaveTypeId: '', startDate: '', endDate: '', isHalfDay: false, halfDaySession: '' });

  readonly form: FormGroup = this.fb.group(
    {
      leaveTypeId: ['', Validators.required],
      startDate: ['', Validators.required],
      endDate: ['', Validators.required],
      isHalfDay: [false],
      halfDaySession: [''],
      reason: ['', [Validators.required, Validators.maxLength(1000)]],
    },
    { validators: [dateRangeValidator, halfDayValidator] },
  );

  // ─── Computed ─────────────────────────────────────────────

  readonly selectedType = computed(() =>
    this.leaveTypes().find((lt) => lt.leaveTypeId === this.formValue().leaveTypeId) ?? null,
  );

  /** Requested working days (weekends excluded); halved for a half-day (AC-4, AC-6). */
  readonly requestedDays = computed(() => {
    const v = this.formValue();
    const days = countWorkingDays(v.startDate, v.endDate);
    if (v.isHalfDay && days === 1) {
      return 0.5;
    }
    return days;
  });

  readonly projection = computed(() => {
    const lt = this.selectedType();
    const remaining = lt ? this.remainingFor(lt.leaveTypeId) : 0;
    const negativeAllowed = lt?.negativeBalanceAllowed ?? false;
    return buildProjection(remaining, this.requestedDays(), negativeAllowed);
  });

  /** Document-required hint for sick-type leave over threshold (AC-3). */
  readonly documentHint = computed(() => {
    const lt = this.selectedType();
    if (!lt || !lt.documentsRequired) {
      return '';
    }
    const threshold = lt.documentDayThreshold;
    if (threshold !== null && this.requestedDays() > threshold) {
      return `A medical certificate is required for ${lt.name} exceeding ${threshold} days.`;
    }
    return '';
  });

  // ─── Lifecycle ────────────────────────────────────────────

  ngOnInit(): void {
    this.form.valueChanges.pipe(takeUntil(this.destroy$)).subscribe((v) => {
      this.formValue.set({
        leaveTypeId: v.leaveTypeId ?? '',
        startDate: v.startDate ?? '',
        endDate: v.endDate ?? '',
        isHalfDay: v.isHalfDay ?? false,
        halfDaySession: v.halfDaySession ?? '',
      });
    });
    this.loadData();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ─────────────────────────────────────────

  loadData(): void {
    this.isLoading.set(true);
    forkJoin({
      leaveTypes: this.leaveTypeService.getLeaveTypes(),
      balances: this.leaveRequestService.getMyBalances(),
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ leaveTypes, balances }) => {
          this.leaveTypes.set(leaveTypes.filter((lt) => lt.isActive));
          this.balances.set(balances);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.toastr.error('Failed to load leave types and balances.');
        },
      });
  }

  // ─── Helpers ──────────────────────────────────────────────

  /** Remaining balance for a leave type; 0 if no balance record exists. */
  remainingFor(leaveTypeId: string): number {
    return this.balances().find((b) => b.leaveTypeId === leaveTypeId)?.remainingDays ?? 0;
  }

  contrastText(hex: string): string {
    return getContrastTextColor(hex);
  }

  showError(controlName: string): boolean {
    const control = this.form.get(controlName);
    return !!control && control.invalid && (control.touched || control.dirty);
  }

  // ─── Drag-and-drop (blob upload DEFERRED) ─────────────────

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragActive.set(false);
    const files = event.dataTransfer?.files;
    if (files) {
      this.addFiles(files);
    }
  }

  onFilesPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (input.files) {
      this.addFiles(input.files);
    }
    input.value = '';
  }

  private addFiles(files: FileList): void {
    const names = Array.from(files).map((f) => f.name);
    // Cap at 3 files total per constraints (§10).
    const merged = [...this.attachments(), ...names].slice(0, 3);
    this.attachments.set(merged);
  }

  removeAttachment(index: number): void {
    this.attachments.set(this.attachments().filter((_, i) => i !== index));
  }

  // ─── Submit (AC-1) ────────────────────────────────────────

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toastr.warning('Please fix the highlighted fields.');
      return;
    }
    if (this.projection().insufficient) {
      this.toastr.error('Insufficient leave balance for this request.');
      return;
    }
    // AC-3: client-side document-required hint becomes a hard block before submit.
    const lt = this.selectedType();
    if (
      lt?.documentsRequired &&
      lt.documentDayThreshold !== null &&
      this.requestedDays() > lt.documentDayThreshold &&
      this.attachments().length === 0
    ) {
      this.toastr.error(
        `Medical certificate is required for ${lt.name} exceeding ${lt.documentDayThreshold} days.`,
      );
      return;
    }

    const raw = this.form.getRawValue();
    const request: ICreateLeaveRequest = {
      leaveTypeId: raw.leaveTypeId,
      startDate: raw.startDate,
      endDate: raw.endDate,
      isHalfDay: raw.isHalfDay,
      halfDaySession: raw.isHalfDay ? (raw.halfDaySession || null) : null,
      reason: raw.reason,
      attachments: this.attachments(),
    };

    this.isSubmitting.set(true);
    this.leaveRequestService
      .createLeaveRequest(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (created) => {
          this.isSubmitting.set(false);
          // §8: green toast with request ID, then navigate to My Leaves.
          this.toastr.success(
            `Leave request #${created.leaveRequestId} submitted for approval.`,
            'Request submitted',
          );
          this.router.navigate(['/leave/my-requests']);
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmitting.set(false);
          // Backend is the source of truth: surface overlap (AC-5),
          // insufficient balance (AC-2), document-required (AC-3) via toast.
          this.toastr.error(LeaveRequestService.parseErrorMessage(err));
        },
      });
  }

  cancel(): void {
    this.router.navigate(['/leave/my-requests']);
  }
}
