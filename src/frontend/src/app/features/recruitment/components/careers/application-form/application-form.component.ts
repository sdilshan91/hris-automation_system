import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { CareersService, IUploadEvent } from '../../../services/careers.service';
import { ResumeUploadComponent } from '../resume-upload/resume-upload.component';
import {
  IApplicant,
  IApplicationRequest,
  COVER_LETTER_MAX,
} from '../../../models/applicant.models';

/** Pre-fill values for the internal-employee flow (AC-4 / FR-8). */
export interface IApplicationPrefill {
  firstName?: string;
  lastName?: string;
  email?: string;
}

/**
 * US-REC-002: The application form (FR-1) — reused by the PUBLIC careers detail
 * page and the INTERNAL employee slide-over.
 *
 * Reactive form + signals + OnPush. Fields: firstName (req), lastName (req),
 * email (req, validated), phone (optional), coverLetter (optional, max 2000 with
 * live counter), resume (required, validated by `app-resume-upload`).
 *
 * On submit it streams upload progress (§8 / NFR-1) and, on success, emits the
 * created applicant so the host can show the confirmation screen (AC-1). Duplicate
 * (AC-3) and file-rejection (AC-2) errors render inline.
 */
@Component({
  selector: 'app-application-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ResumeUploadComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="space-y-5">
      <!-- Duplicate / server error banner -->
      @if (serverError(); as msg) {
        <div
          class="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700"
          role="alert"
        >
          {{ msg }}
        </div>
      }

      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <!-- First name -->
        <div>
          <label for="firstName" class="field-label">
            First name <span class="text-red-500">*</span>
          </label>
          <input
            id="firstName"
            type="text"
            class="field-input"
            formControlName="firstName"
            autocomplete="given-name"
            [attr.aria-invalid]="invalid('firstName')"
          />
          @if (invalid('firstName')) {
            <p class="field-error" role="alert">First name is required.</p>
          }
        </div>

        <!-- Last name -->
        <div>
          <label for="lastName" class="field-label">
            Last name <span class="text-red-500">*</span>
          </label>
          <input
            id="lastName"
            type="text"
            class="field-input"
            formControlName="lastName"
            autocomplete="family-name"
            [attr.aria-invalid]="invalid('lastName')"
          />
          @if (invalid('lastName')) {
            <p class="field-error" role="alert">Last name is required.</p>
          }
        </div>
      </div>

      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <!-- Email -->
        <div>
          <label for="email" class="field-label">
            Email <span class="text-red-500">*</span>
          </label>
          <input
            id="email"
            type="email"
            class="field-input"
            formControlName="email"
            autocomplete="email"
            [attr.aria-invalid]="invalid('email')"
          />
          @if (invalid('email')) {
            <p class="field-error" role="alert">
              @if (form.controls.email.hasError('required')) {
                Email is required.
              } @else {
                Enter a valid email address.
              }
            </p>
          }
        </div>

        <!-- Phone (optional) -->
        <div>
          <label for="phone" class="field-label">Phone</label>
          <input
            id="phone"
            type="tel"
            class="field-input"
            formControlName="phone"
            autocomplete="tel"
          />
        </div>
      </div>

      <!-- Cover letter (optional, counter) -->
      <div>
        <div class="flex items-center justify-between">
          <label for="coverLetter" class="field-label">Cover letter</label>
          <span
            class="text-xs"
            [class.text-red-500]="coverLen() > maxCover"
            [class.text-neutral-400]="coverLen() <= maxCover"
          >
            {{ coverLen() }}/{{ maxCover }}
          </span>
        </div>
        <textarea
          id="coverLetter"
          rows="5"
          class="field-input resize-y"
          formControlName="coverLetter"
          [attr.aria-invalid]="invalid('coverLetter')"
          placeholder="Tell us why you're a great fit (optional)"
        ></textarea>
        @if (form.controls.coverLetter.hasError('maxlength')) {
          <p class="field-error" role="alert">
            Cover letter must be {{ maxCover }} characters or fewer.
          </p>
        }
      </div>

      <!-- Resume upload (required) -->
      <div>
        <label class="field-label">
          Resume <span class="text-red-500">*</span>
        </label>
        <app-resume-upload formControlName="resume" [disabled]="submitting()" />
        @if (resumeMissing()) {
          <p class="field-error" role="alert">Please attach your resume.</p>
        }
      </div>

      <!-- Upload progress (§8 / NFR-1) -->
      @if (submitting()) {
        <div aria-live="polite">
          <div class="flex items-center justify-between text-xs text-neutral-500">
            <span>Uploading…</span>
            <span>{{ progress() }}%</span>
          </div>
          <div class="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-neutral-100">
            <div
              class="h-full rounded-full bg-indigo-600 transition-all duration-200"
              [style.width.%]="progress()"
            ></div>
          </div>
        </div>
      }

      <!-- Actions -->
      <div class="flex items-center justify-end gap-3 pt-2">
        @if (showCancel()) {
          <button
            type="button"
            class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
            [disabled]="submitting()"
            (click)="cancelled.emit()"
          >
            Cancel
          </button>
        }
        <button
          type="submit"
          class="rounded-lg bg-indigo-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
          [disabled]="submitting()"
        >
          {{ submitting() ? 'Submitting…' : submitLabel() }}
        </button>
      </div>
    </form>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .field-label {
        display: block;
        margin-bottom: 0.375rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #404040;
      }
      .field-input {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #171717;
        transition: all 150ms ease;
      }
      .field-input:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 3px #e0e7ff;
      }
      .field-error {
        margin-top: 0.375rem;
        font-size: 0.75rem;
        font-weight: 500;
        color: #dc2626;
      }
    `,
  ],
})
export class ApplicationFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly careers = inject(CareersService);

  /** Vacancy being applied to. */
  readonly vacancyId = input.required<string>();
  /** Internal employee flow (AC-4) — switches the submit endpoint + label. */
  readonly internal = input(false);
  /** Pre-fill values (name/email) for the internal flow (AC-4 / FR-8). */
  readonly prefill = input<IApplicationPrefill | null>(null);
  /** Whether to render a Cancel button (true in the slide-over). */
  readonly showCancel = input(false);
  /** Label for the submit button. */
  readonly submitLabel = input('Submit application');

  /** Emits the created applicant on success (AC-1). */
  readonly submitted = output<IApplicant>();
  /** Emitted when the user cancels (slide-over only). */
  readonly cancelled = output<void>();

  readonly maxCover = COVER_LETTER_MAX;

  readonly form = this.fb.nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(255)]],
    phone: [''],
    coverLetter: ['', [Validators.maxLength(COVER_LETTER_MAX)]],
    resume: this.fb.control<File | null>(null, [Validators.required]),
  });

  readonly submitting = signal(false);
  readonly progress = signal(0);
  readonly serverError = signal<string | null>(null);
  /** Tracks an explicit submit attempt so the resume error only shows then. */
  private readonly submitAttempted = signal(false);

  readonly coverLen = signal(0);

  readonly resumeMissing = computed(
    () => this.submitAttempted() && this.form.controls.resume.invalid,
  );

  ngOnInit(): void {
    const pre = this.prefill();
    if (pre) {
      this.form.patchValue({
        firstName: pre.firstName ?? '',
        lastName: pre.lastName ?? '',
        email: pre.email ?? '',
      });
    }
    this.coverLen.set(this.form.controls.coverLetter.value.length);
    this.form.controls.coverLetter.valueChanges.subscribe((v) =>
      this.coverLen.set((v ?? '').length),
    );
  }

  invalid(name: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[name];
    return c.invalid && (c.touched || this.submitAttempted());
  }

  submit(): void {
    this.submitAttempted.set(true);
    this.serverError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const resume = raw.resume;
    if (!resume) {
      return;
    }

    const request: IApplicationRequest = {
      firstName: raw.firstName.trim(),
      lastName: raw.lastName.trim(),
      email: raw.email.trim(),
      phone: raw.phone.trim() || null,
      coverLetter: raw.coverLetter.trim() || null,
    };

    this.submitting.set(true);
    this.progress.set(0);

    const stream = this.internal()
      ? this.careers.applyInternal(this.vacancyId(), request, resume)
      : this.careers.applyPublic(this.vacancyId(), request, resume);

    stream.subscribe({
      next: (event: IUploadEvent) => {
        if (event.type === 'progress') {
          this.progress.set(event.progress);
        } else {
          this.submitting.set(false);
          this.submitted.emit(event.applicant);
        }
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        if (CareersService.isDuplicate(err)) {
          this.serverError.set(
            'You have already applied to this position with this email address.',
          );
        } else {
          this.serverError.set(CareersService.parseErrorMessage(err));
        }
      },
    });
  }
}
