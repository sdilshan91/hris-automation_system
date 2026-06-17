import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  input,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { OffboardingService } from '../../services/offboarding.service';
import {
  OFFBOARDING_REASONS,
  OffboardingReason,
  IInitiateOffboardingRequest,
  MAX_NOTES_LEN,
  isPastDate,
  todayIso,
} from '../../models/offboarding.models';

/** Cross-field validator: last working day must be today or in the future. */
function notPastDate(control: AbstractControl): ValidationErrors | null {
  const value = control.value as string;
  return value && isPastDate(value) ? { pastDate: true } : null;
}

/**
 * US-ONB-005: HR "Initiate Offboarding" form for a departing employee (AC-1, FR-2).
 *
 * Routed at `offboarding/initiate/:employeeId` (routed input() employeeId with a
 * route-snapshot fallback). Onboarding-side only — never touches Core HR employee
 * screens; keyed purely by employeeId. Captures the last working day (today or
 * future), a reason (Resignation / Termination / Contract End / Retirement), an
 * optional template id, and optional notes. On success the backend generates the
 * exit checklist and returns the instance; we navigate to the clearance dashboard.
 *
 * Standalone, signals-first, OnPush. WCAG 2.1 AA: labelled controls, inline field
 * errors with role="alert", live-region status announcements.
 */
@Component({
  selector: 'app-offboarding-initiate',
  standalone: true,
  imports: [CommonModule, RouterLink, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container max-w-2xl">
      <div class="mb-6">
        <a routerLink="/onboarding" class="text-xs font-medium text-brand-600 hover:text-brand-700">
          &larr; Back to onboarding
        </a>
        <h1 class="mt-2 text-2xl font-semibold text-neutral-900 tracking-tight">
          Initiate Offboarding
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Generate an exit checklist for {{ employeeName() }}. Tasks for asset return,
          access revocation, knowledge transfer, and departmental clearances are created
          automatically from your tenant's offboarding template.
        </p>
      </div>

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="card">
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
          <!-- Reason -->
          <div>
            <label class="label-notion" for="reason">Reason for leaving *</label>
            <select id="reason" class="input-notion" formControlName="reason">
              @for (reason of reasons; track reason) {
                <option [value]="reason">{{ reason }}</option>
              }
            </select>
            @if (ctrl('reason').touched && ctrl('reason').hasError('required')) {
              <p class="field-error" role="alert">Select a reason.</p>
            }
          </div>

          <!-- Last working day -->
          <div>
            <label class="label-notion" for="lwd">Last working day *</label>
            <input
              id="lwd"
              type="date"
              class="input-notion"
              formControlName="lastWorkingDay"
              [min]="minDate"
            />
            @if (ctrl('lastWorkingDay').touched && ctrl('lastWorkingDay').hasError('required')) {
              <p class="field-error" role="alert">Set the last working day.</p>
            }
            @if (ctrl('lastWorkingDay').touched && ctrl('lastWorkingDay').hasError('pastDate')) {
              <p class="field-error" role="alert">
                The last working day must be today or in the future.
              </p>
            }
          </div>

          <!-- Optional template id -->
          <div class="sm:col-span-2">
            <label class="label-notion" for="template">Offboarding template (optional)</label>
            <input
              id="template"
              type="text"
              class="input-notion"
              formControlName="offboardingTemplateId"
              placeholder="Leave blank to use the default template"
              autocomplete="off"
            />
            <p class="text-[11px] text-neutral-400 mt-1">
              If left blank, your tenant's default offboarding template is applied.
            </p>
          </div>

          <!-- Notes -->
          <div class="sm:col-span-2">
            <label class="label-notion" for="notes">Notes (optional)</label>
            <textarea
              id="notes"
              class="input-notion"
              formControlName="notes"
              rows="3"
              [maxlength]="maxNotes"
              placeholder="Context for the offboarding (visible to HR)…"
            ></textarea>
            <p class="text-[11px] text-neutral-400 mt-1 text-right">
              {{ ctrl('notes').value?.length ?? 0 }} / {{ maxNotes }}
            </p>
          </div>
        </div>

        @if (submitError()) {
          <div class="submit-error mt-5" role="alert" @fadeIn>{{ submitError() }}</div>
        }

        <div class="flex items-center justify-end gap-2 mt-6">
          <a routerLink="/onboarding" class="btn-secondary">Cancel</a>
          <button type="submit" class="btn-primary" [disabled]="submitting() || form.invalid">
            @if (submitting()) {
              <span class="btn-spinner"></span> Initiating…
            } @else {
              Initiate Offboarding
            }
          </button>
        </div>
      </form>

      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .card { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-6; }
    .label-notion { @apply block text-xs font-medium text-neutral-600 mb-1; }
    .input-notion {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900
        transition-colors duration-150 focus:outline-none focus:ring-2 focus:ring-brand-200 focus:border-brand-400;
    }
    .field-error { @apply text-xs text-red-600 mt-1; }
    .submit-error { @apply rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700; }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2 text-sm
        font-medium text-white transition-all duration-200 hover:bg-brand-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2 text-sm
        font-medium text-neutral-700 ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50 disabled:opacity-50;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class OffboardingInitiateComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly offboardingService = inject(OffboardingService);
  private readonly destroy$ = new Subject<void>();

  /** Routed input — employee being offboarded (AC-1, keyed by id). */
  readonly employeeId = input<string>('');

  readonly reasons = OFFBOARDING_REASONS;
  readonly minDate = todayIso();
  readonly maxNotes = MAX_NOTES_LEN;

  /** Display name; resolved from the route query param if present. */
  readonly employeeName = signal<string>('this employee');

  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly liveMessage = signal('');

  readonly form: FormGroup<{
    reason: FormControl<OffboardingReason>;
    lastWorkingDay: FormControl<string>;
    offboardingTemplateId: FormControl<string | null>;
    notes: FormControl<string | null>;
  }> = this.fb.group({
    reason: this.fb.control<OffboardingReason>('Resignation', { nonNullable: true, validators: [Validators.required] }),
    lastWorkingDay: this.fb.control(this.minDate, {
      nonNullable: true,
      validators: [Validators.required, notPastDate],
    }),
    offboardingTemplateId: this.fb.control<string | null>(null),
    notes: this.fb.control<string | null>(null, [Validators.maxLength(MAX_NOTES_LEN)]),
  });

  ngOnInit(): void {
    const name = this.route.snapshot.queryParamMap.get('employeeName');
    if (name) this.employeeName.set(name);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  ctrl(name: keyof OffboardingInitiateComponent['form']['controls']): AbstractControl {
    return this.form.get(name)!;
  }

  /** Resolved employee id (input wins, else the route snapshot). */
  private resolvedEmployeeId(): string {
    return this.employeeId() || this.route.snapshot.paramMap.get('employeeId') || '';
  }

  submit(): void {
    this.submitError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const employeeId = this.resolvedEmployeeId();
    if (!employeeId) {
      this.submitError.set('No employee selected for offboarding.');
      return;
    }

    const v = this.form.getRawValue();
    const request: IInitiateOffboardingRequest = {
      employeeId,
      lastWorkingDay: v.lastWorkingDay,
      offboardingTemplateId: v.offboardingTemplateId?.trim() || null,
      reason: v.reason,
      notes: v.notes?.trim() || null,
    };

    this.submitting.set(true);
    this.offboardingService
      .initiate(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (instance) => {
          this.submitting.set(false);
          const msg = `Offboarding initiated for ${this.employeeName()}.`;
          this.liveMessage.set(msg);
          this.toastr.success(msg);
          // Jump straight to the clearance dashboard for the new instance (AC-3).
          this.router.navigate(['/offboarding', instance.offboardingId]);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          // BR-1 invalid-status / already-offboarding surfaced verbatim inline.
          this.submitError.set(OffboardingService.parseErrorMessage(err));
        },
      });
  }
}
