import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  output,
  signal,
  computed,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { forkJoin } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { ConversionService } from '../../services/conversion.service';
import { VacancyService } from '../../services/vacancy.service';
import { ILookupOption } from '../../models/vacancy.models';
import {
  IConversionPrefill,
  IConvertEmployeeRequest,
  IConvertResult,
  ALREADY_CONVERTED_CODE,
  PLAN_LIMIT_CODE,
} from '../../models/conversion.models';
import {
  EmploymentType,
  EMPLOYMENT_TYPE_OPTIONS,
} from '../../../core-hr/employees/models/employee.models';

type ConversionStep = 'loading' | 'form' | 'confirm' | 'success';

/**
 * US-REC-010: "Convert to Employee" slide-over.
 *
 * A Notion-style right slide-in drawer (full-screen on mobile) opened from the
 * applicant detail when the applicant is Hired with an accepted offer (FR-1). It:
 *  - fetches the pre-filled employee data mapped from the application + offer
 *    (FR-2/FR-3/AC-1) and renders a two-column (single on mobile) form grouped into
 *    Personal Info / Employment Details / Compensation. Pre-filled fields are
 *    visually marked "auto-filled" (§8) and every field is editable (FR-3).
 *  - collects the remaining required fields: employee number (auto-generated with a
 *    manual override, FR-4), employment type, and work location (lookup, FR-2).
 *  - shows a confirmation summary (name / position / department / start date /
 *    salary) before "Create Employee" (§8).
 *  - on success (AC-4) shows a success state with a "View Employee Profile" link and
 *    emits the new employee link to the parent for the "Converted" badge.
 *  - surfaces blocked conversions (already converted BR-2 / plan limit BR-3)
 *    verbatim as an inline banner.
 *
 * The sticky "Create Employee" button sits at the bottom (§8 mobile).
 */
@Component({
  selector: 'app-conversion-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate(
          '260ms cubic-bezier(0.22, 1, 0.36, 1)',
          style({ transform: 'translateX(0)' }),
        ),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <!-- Backdrop -->
    <div
      @backdrop
      class="fixed inset-0 z-[55] bg-neutral-900/30"
      (click)="cancel()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap -->
    <div class="pointer-events-none fixed inset-0 z-[56] flex justify-end">
      <div
        @drawer
        class="pointer-events-auto flex h-full w-full max-w-2xl flex-col bg-white shadow-xl sm:w-[65%]"
        role="dialog"
        aria-modal="true"
        aria-labelledby="conversion-form-title"
      >
        <!-- Header -->
        <div
          class="flex items-start justify-between gap-3 border-b border-neutral-100 px-6 py-4"
        >
          <div>
            <h2
              id="conversion-form-title"
              class="text-base font-semibold text-neutral-900"
            >
              Convert to employee
            </h2>
            <p class="mt-0.5 text-sm text-neutral-500">
              {{ applicantName() || 'Candidate' }}
            </p>
          </div>
          <button
            type="button"
            class="rounded-md p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            (click)="cancel()"
            aria-label="Close"
          >
            <svg
              class="h-5 w-5"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
            >
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-6 py-5">
          <!-- Blocked-conversion banner (BR-2 / BR-3) -->
          @if (blockedMessage(); as msg) {
            <div
              class="mb-5 flex items-start gap-3 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-800"
              role="alert"
            >
              <svg
                class="mt-0.5 h-5 w-5 shrink-0"
                viewBox="0 0 20 20"
                fill="currentColor"
                aria-hidden="true"
              >
                <path
                  fill-rule="evenodd"
                  d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z"
                  clip-rule="evenodd"
                />
              </svg>
              <div>
                <p>{{ msg }}</p>
                @if (alreadyConvertedEmployeeId(); as eid) {
                  <a
                    class="mt-1 inline-block font-medium text-red-700 underline"
                    [routerLink]="['/employees', eid]"
                    (click)="cancel()"
                  >
                    View employee record
                  </a>
                }
              </div>
            </div>
          }

          <!-- Loading -->
          @if (step() === 'loading') {
            <div class="space-y-4">
              @for (n of [1, 2, 3, 4, 5, 6]; track n) {
                <div class="h-10 animate-pulse rounded-lg bg-neutral-100"></div>
              }
            </div>
          }

          <!-- Form (FR-2 / FR-3) -->
          @if (step() === 'form' && prefill()) {
            <form [formGroup]="form" (ngSubmit)="review()">
              <!-- Personal Info -->
              <section class="section">
                <h3 class="section-title">Personal Information</h3>
                <div class="grid">
                  <div class="field">
                    <label class="lbl" for="firstName">
                      First name <span class="req">*</span>
                      @if (isAutoFilled('firstName')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <input
                      id="firstName"
                      type="text"
                      formControlName="firstName"
                      class="inp"
                      [class.inp-auto]="isAutoFilled('firstName')"
                    />
                    @if (err('firstName')) {
                      <p class="ferr" role="alert">First name is required.</p>
                    }
                  </div>
                  <div class="field">
                    <label class="lbl" for="lastName">
                      Last name <span class="req">*</span>
                      @if (isAutoFilled('lastName')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <input
                      id="lastName"
                      type="text"
                      formControlName="lastName"
                      class="inp"
                      [class.inp-auto]="isAutoFilled('lastName')"
                    />
                    @if (err('lastName')) {
                      <p class="ferr" role="alert">Last name is required.</p>
                    }
                  </div>
                  <div class="field">
                    <label class="lbl" for="email">
                      Email <span class="req">*</span>
                      @if (isAutoFilled('email')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <input
                      id="email"
                      type="email"
                      formControlName="email"
                      class="inp"
                      [class.inp-auto]="isAutoFilled('email')"
                    />
                    @if (err('email')) {
                      <p class="ferr" role="alert">
                        A valid email is required.
                      </p>
                    }
                  </div>
                  <div class="field">
                    <label class="lbl" for="phone">
                      Phone
                      @if (isAutoFilled('phone')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <input
                      id="phone"
                      type="tel"
                      formControlName="phone"
                      class="inp"
                      [class.inp-auto]="isAutoFilled('phone')"
                    />
                  </div>
                </div>
              </section>

              <!-- Employment Details -->
              <section class="section">
                <h3 class="section-title">Employment Details</h3>
                <div class="grid">
                  <!-- Employee number (auto-generated, override FR-4) -->
                  <div class="field sm:col-span-2">
                    <label class="lbl" for="employeeNumber">
                      Employee number
                      @if (!overrideNumber()) {
                        <span class="auto">auto-generated</span>
                      }
                    </label>
                    <div class="flex items-center gap-2">
                      <input
                        id="employeeNumber"
                        type="text"
                        formControlName="employeeNumber"
                        class="inp flex-1"
                        [class.inp-auto]="!overrideNumber()"
                        [readonly]="!overrideNumber()"
                      />
                      <button
                        type="button"
                        class="shrink-0 rounded-lg border border-neutral-200 bg-white px-3 py-2 text-xs font-medium text-neutral-600 transition hover:bg-neutral-50"
                        (click)="toggleOverride()"
                      >
                        {{ overrideNumber() ? 'Use auto' : 'Override' }}
                      </button>
                    </div>
                    <p class="fhint">
                      {{
                        overrideNumber()
                          ? 'Enter a custom employee number.'
                          : 'Generated from the tenant numbering pattern (FR-4).'
                      }}
                    </p>
                  </div>

                  <div class="field">
                    <label class="lbl" for="jobTitleId">
                      Job title <span class="req">*</span>
                      @if (isAutoFilled('jobTitleId')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <select
                      id="jobTitleId"
                      formControlName="jobTitleId"
                      class="inp sel"
                      [class.inp-auto]="isAutoFilled('jobTitleId')"
                    >
                      <option [ngValue]="''">Select job title</option>
                      @for (jt of jobTitles(); track jt.id) {
                        <option [ngValue]="jt.id">{{ jt.label }}</option>
                      }
                    </select>
                    @if (err('jobTitleId')) {
                      <p class="ferr" role="alert">Job title is required.</p>
                    }
                  </div>

                  <div class="field">
                    <label class="lbl" for="departmentId">
                      Department <span class="req">*</span>
                      @if (isAutoFilled('departmentId')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <select
                      id="departmentId"
                      formControlName="departmentId"
                      class="inp sel"
                      [class.inp-auto]="isAutoFilled('departmentId')"
                    >
                      <option [ngValue]="''">Select department</option>
                      @for (d of departments(); track d.id) {
                        <option [ngValue]="d.id">{{ d.label }}</option>
                      }
                    </select>
                    @if (err('departmentId')) {
                      <p class="ferr" role="alert">Department is required.</p>
                    }
                  </div>

                  <div class="field">
                    <label class="lbl" for="employmentType">
                      Employment type <span class="req">*</span>
                    </label>
                    <select
                      id="employmentType"
                      formControlName="employmentType"
                      class="inp sel"
                    >
                      <option [ngValue]="''">Select type</option>
                      @for (et of employmentTypeOptions; track et.value) {
                        <option [ngValue]="et.value">{{ et.label }}</option>
                      }
                    </select>
                    @if (err('employmentType')) {
                      <p class="ferr" role="alert">
                        Employment type is required.
                      </p>
                    }
                  </div>

                  <div class="field">
                    <label class="lbl" for="workLocationId">
                      Work location <span class="req">*</span>
                    </label>
                    <select
                      id="workLocationId"
                      formControlName="workLocationId"
                      class="inp sel"
                    >
                      <option [ngValue]="''">Select location</option>
                      @for (l of locations(); track l.id) {
                        <option [ngValue]="l.id">{{ l.label }}</option>
                      }
                    </select>
                    @if (err('workLocationId')) {
                      <p class="ferr" role="alert">Work location is required.</p>
                    }
                  </div>

                  <div class="field">
                    <label class="lbl" for="reportingManagerId">
                      Reporting manager
                      @if (isAutoFilled('reportingManagerId')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <select
                      id="reportingManagerId"
                      formControlName="reportingManagerId"
                      class="inp sel"
                      [class.inp-auto]="isAutoFilled('reportingManagerId')"
                    >
                      <option [ngValue]="''">No manager</option>
                      @for (m of managers(); track m.id) {
                        <option [ngValue]="m.id">{{ m.label }}</option>
                      }
                    </select>
                  </div>

                  <div class="field">
                    <label class="lbl" for="dateOfJoining">
                      Date of joining <span class="req">*</span>
                      @if (isAutoFilled('dateOfJoining')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <input
                      id="dateOfJoining"
                      type="date"
                      formControlName="dateOfJoining"
                      class="inp"
                      [class.inp-auto]="isAutoFilled('dateOfJoining')"
                    />
                    @if (err('dateOfJoining')) {
                      <p class="ferr" role="alert">
                        Date of joining is required.
                      </p>
                    }
                  </div>
                </div>
              </section>

              <!-- Compensation -->
              <section class="section">
                <h3 class="section-title">Compensation</h3>
                <div class="grid">
                  <div class="field">
                    <label class="lbl" for="salaryAmount">
                      Salary amount
                      @if (isAutoFilled('salaryAmount')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <!--
                      BUG-292: READ-ONLY on purpose. This field was editable and the edited value was silently
                      discarded — the backend ConvertApplicantRequest has no salaryAmount property, so ASP.NET
                      model binding dropped it with no error and the conversion still reported success. HR could
                      type a salary, see it echoed in the review summary, and lose it.
                      It stays VISIBLE because the offer's agreed figure is useful context at conversion time,
                      but it is no longer editable and is no longer submitted. Assigning a real salary needs a
                      SalaryStructureId (see AssignSalaryStructureCommand), which an offer's flat amount cannot
                      supply — that is tracked on BUG-292 and needs a product decision.
                    -->
                    <input
                      id="salaryAmount"
                      type="number"
                      min="0"
                      formControlName="salaryAmount"
                      class="inp"
                      readonly
                      [attr.aria-readonly]="true"
                      [class.inp-auto]="isAutoFilled('salaryAmount')"
                    />
                    <p class="hint">
                      From the accepted offer. Set the employee's salary structure after conversion —
                      it is not applied here.
                    </p>
                  </div>
                  <div class="field">
                    <label class="lbl" for="currency">
                      Currency
                      @if (isAutoFilled('currency')) {
                        <span class="auto">auto-filled</span>
                      }
                    </label>
                    <!-- BUG-292: read-only for the same reason as Salary amount above — the backend request
                         record has no Currency property either, so an edit here was also silently discarded. -->
                    <input
                      id="currency"
                      type="text"
                      maxlength="3"
                      formControlName="currency"
                      class="inp uppercase"
                      readonly
                      [attr.aria-readonly]="true"
                      [class.inp-auto]="isAutoFilled('currency')"
                      placeholder="e.g. USD"
                    />
                  </div>
                </div>
                <p class="mt-3 text-xs text-neutral-400">
                  Bank details and other payroll fields are collected during
                  onboarding, not here.
                </p>
              </section>
            </form>
          }

          <!-- Confirmation summary (§8) -->
          @if (step() === 'confirm') {
            <div>
              <p class="mb-4 text-sm text-neutral-600">
                Please review the key details before creating the employee record.
                This cannot be undone.
              </p>
              <dl class="space-y-3 rounded-xl border border-neutral-200 p-4">
                <div class="sumrow">
                  <dt class="sumdt">Name</dt>
                  <dd class="sumdd">{{ summaryName() }}</dd>
                </div>
                <div class="sumrow">
                  <dt class="sumdt">Position</dt>
                  <dd class="sumdd">{{ summaryPosition() || '—' }}</dd>
                </div>
                <div class="sumrow">
                  <dt class="sumdt">Department</dt>
                  <dd class="sumdd">{{ summaryDepartment() || '—' }}</dd>
                </div>
                <div class="sumrow">
                  <dt class="sumdt">Start date</dt>
                  <dd class="sumdd">{{ summaryStartDate() || '—' }}</dd>
                </div>
                <div class="sumrow">
                  <dt class="sumdt">Salary</dt>
                  <dd class="sumdd">{{ summarySalary() || '—' }}</dd>
                </div>
              </dl>
            </div>
          }

          <!-- Success (AC-4) -->
          @if (step() === 'success' && result(); as r) {
            <div class="py-6 text-center">
              <div
                class="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-green-50"
              >
                <svg
                  class="h-7 w-7 text-green-600"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  aria-hidden="true"
                >
                  <path d="M20 6 9 17l-5-5" />
                </svg>
              </div>
              <h3 class="text-lg font-semibold text-neutral-900">
                Employee created
              </h3>
              <p class="mt-1 text-sm text-neutral-500">
                {{ r.firstName }} {{ r.lastName }}
                @if (r.employeeNumber) {
                  · {{ r.employeeNumber }}
                }
                has been added to your organisation.
              </p>
              @if (r.userAccountCreated) {
                <p class="mt-1 text-xs text-neutral-400">
                  A login account was created and a welcome email is on its way.
                </p>
              }
              @if (vacancyRatio(); as ratio) {
                <p class="mt-3 text-sm text-neutral-600">
                  Vacancy filled: {{ ratio }}
                  @if (r.vacancyClosed) {
                    <span class="ml-1 font-medium text-green-700"
                      >· position closed</span
                    >
                  }
                </p>
              }
              <a
                class="mt-6 inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-medium text-white transition hover:bg-indigo-700"
                [routerLink]="['/employees', r.employeeId]"
                (click)="cancel()"
              >
                View employee profile
                <svg
                  class="h-4 w-4"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  stroke-width="2"
                  aria-hidden="true"
                >
                  <path d="M5 12h14M13 6l6 6-6 6" />
                </svg>
              </a>
            </div>
          }
        </div>

        <!-- Sticky footer actions (§8 mobile) -->
        @if (step() === 'form') {
          <div
            class="flex items-center justify-end gap-2 border-t border-neutral-100 px-6 py-4"
          >
            <button type="button" class="btn-ghost" (click)="cancel()">
              Cancel
            </button>
            <button type="button" class="btn-primary" (click)="review()">
              Review &amp; create
            </button>
          </div>
        }
        @if (step() === 'confirm') {
          <div
            class="flex items-center justify-between gap-2 border-t border-neutral-100 px-6 py-4"
          >
            <button
              type="button"
              class="btn-ghost"
              [disabled]="creating()"
              (click)="backToForm()"
            >
              Back
            </button>
            <button
              type="button"
              class="btn-primary"
              [disabled]="creating()"
              (click)="create()"
            >
              {{ creating() ? 'Creating…' : 'Create employee' }}
            </button>
          </div>
        }
        @if (step() === 'success') {
          <div
            class="flex items-center justify-end border-t border-neutral-100 px-6 py-4"
          >
            <button type="button" class="btn-ghost" (click)="cancel()">
              Done
            </button>
          </div>
        }
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .section {
        margin-bottom: 1.75rem;
      }
      .section-title {
        font-size: 0.8125rem;
        font-weight: 600;
        color: #404040;
        margin-bottom: 0.875rem;
        padding-bottom: 0.5rem;
        border-bottom: 1px solid #f5f5f5;
      }
      .grid {
        display: grid;
        grid-template-columns: 1fr;
        gap: 1.25rem;
      }
      @media (min-width: 640px) {
        .grid {
          grid-template-columns: 1fr 1fr;
        }
      }
      .field {
        display: flex;
        flex-direction: column;
        gap: 0.375rem;
      }
      .lbl {
        display: flex;
        align-items: center;
        gap: 0.375rem;
        font-size: 0.75rem;
        font-weight: 500;
        color: #525252;
      }
      .req {
        color: #ef4444;
      }
      .auto {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        background: #eef2ff;
        color: #4338ca;
        padding: 0.0625rem 0.4375rem;
        font-size: 0.625rem;
        font-weight: 600;
      }
      .inp {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #262626;
        transition: border-color 150ms ease, box-shadow 150ms ease;
      }
      .inp:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 3px rgb(99 102 241 / 0.15);
      }
      .inp[readonly] {
        background: #fafafa;
        color: #525252;
      }
      .inp-auto {
        background: #f5f3ff;
        border-color: #ddd6fe;
      }
      .sel {
        cursor: pointer;
        appearance: none;
        background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
        background-position: right 0.5rem center;
        background-repeat: no-repeat;
        background-size: 1.25em 1.25em;
        padding-right: 2.25rem;
      }
      .fhint {
        font-size: 0.6875rem;
        color: #a3a3a3;
      }
      .ferr {
        font-size: 0.6875rem;
        color: #dc2626;
      }
      .sumrow {
        display: flex;
        justify-content: space-between;
        gap: 1rem;
      }
      .sumdt {
        font-size: 0.8125rem;
        color: #737373;
      }
      .sumdd {
        font-size: 0.8125rem;
        font-weight: 500;
        color: #262626;
        text-align: right;
      }
      .btn-primary {
        border-radius: 0.5rem;
        background: #4f46e5;
        color: #fff;
        padding: 0.5rem 1rem;
        font-size: 0.8125rem;
        font-weight: 500;
        transition: background 150ms ease;
      }
      .btn-primary:hover {
        background: #4338ca;
      }
      .btn-primary:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
      .btn-ghost {
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        color: #404040;
        padding: 0.5rem 1rem;
        font-size: 0.8125rem;
        font-weight: 500;
        transition: background 150ms ease;
      }
      .btn-ghost:hover {
        background: #fafafa;
      }
      .btn-ghost:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
    `,
  ],
})
export class ConversionFormComponent {
  private readonly fb = inject(FormBuilder);
  private readonly conversionService = inject(ConversionService);
  private readonly vacancyService = inject(VacancyService);
  private readonly toastr = inject(ToastrService);

  /** Applicant to convert; the parent passes the detail's id. */
  readonly applicantId = input.required<string>();
  /** Display name for the header. */
  readonly applicantName = input<string>('');

  /** Emitted when the drawer is closed/cancelled. */
  readonly cancelled = output<void>();
  /**
   * Emitted on a successful conversion (AC-4) so the parent can show the
   * "Converted" badge + link to the new employee record. Carries the result.
   */
  readonly converted = output<IConvertResult>();

  readonly employmentTypeOptions = EMPLOYMENT_TYPE_OPTIONS;

  readonly step = signal<ConversionStep>('loading');
  readonly prefill = signal<IConversionPrefill | null>(null);
  readonly creating = signal(false);
  readonly result = signal<IConvertResult | null>(null);
  /** Verbatim blocked-conversion message (BR-2 / BR-3), or null. */
  readonly blockedMessage = signal<string | null>(null);
  readonly alreadyConvertedEmployeeId = signal<string | null>(null);
  /** Whether the employee number is being manually overridden (FR-4). */
  readonly overrideNumber = signal(false);

  // Lookups
  readonly departments = signal<ILookupOption[]>([]);
  readonly jobTitles = signal<ILookupOption[]>([]);
  readonly locations = signal<ILookupOption[]>([]);
  readonly managers = signal<ILookupOption[]>([]);

  /** Field keys that were pre-filled from the application/offer (for the badge). */
  private readonly autoFilled = signal<Set<string>>(new Set());

  readonly form = this.fb.group({
    firstName: ['', [Validators.required]],
    lastName: ['', [Validators.required]],
    email: ['', [Validators.required, Validators.email]],
    phone: [''],
    employeeNumber: [''],
    jobTitleId: ['', [Validators.required]],
    departmentId: ['', [Validators.required]],
    reportingManagerId: [''],
    employmentType: ['' as EmploymentType | '', [Validators.required]],
    workLocationId: ['', [Validators.required]],
    dateOfJoining: ['', [Validators.required]],
    salaryAmount: [null as number | null],
    currency: [''],
  });

  // ─── Confirmation summary (§8) ────────────────────────────

  readonly summaryName = computed(() => {
    const v = this.form.value;
    return `${v.firstName ?? ''} ${v.lastName ?? ''}`.trim();
  });
  readonly summaryPosition = computed(() =>
    this.lookupLabel(this.jobTitles(), this.form.value.jobTitleId),
  );
  readonly summaryDepartment = computed(() =>
    this.lookupLabel(this.departments(), this.form.value.departmentId),
  );
  readonly summaryStartDate = computed(() => this.form.value.dateOfJoining ?? '');
  readonly summarySalary = computed(() => {
    const v = this.form.value;
    if (v.salaryAmount == null) {
      return '';
    }
    const amount = Number(v.salaryAmount).toLocaleString('en-US');
    return `${(v.currency ?? '').toUpperCase()} ${amount}`.trim();
  });

  readonly vacancyRatio = computed(() => {
    const r = this.result();
    if (!r || r.vacancyFilledCount == null || r.vacancyHeadcount == null) {
      return '';
    }
    return `${r.vacancyFilledCount} / ${r.vacancyHeadcount}`;
  });

  constructor() {
    effect(() => {
      const id = this.applicantId();
      this.bootstrap(id);
    });
  }

  /** Load lookups + the prefill in parallel, then build the form. */
  private bootstrap(applicantId: string): void {
    this.step.set('loading');
    this.blockedMessage.set(null);
    this.alreadyConvertedEmployeeId.set(null);
    forkJoin({
      prefill: this.conversionService.getPrefill(applicantId),
      departments: this.vacancyService.getDepartments(),
      jobTitles: this.vacancyService.getJobTitles(),
      locations: this.vacancyService.getLocations(),
      managers: this.vacancyService.searchEmployees(),
    }).subscribe({
      next: ({ prefill, departments, jobTitles, locations, managers }) => {
        this.departments.set(departments);
        this.jobTitles.set(jobTitles);
        this.locations.set(locations);
        this.managers.set(managers);
        this.applyPrefill(prefill);
      },
      error: (err: HttpErrorResponse) => {
        this.toastr.error(ConversionService.parseErrorMessage(err));
        this.cancelled.emit();
      },
    });
  }

  /** Map prefill values into the form, mark auto-filled fields (FR-2/§8). */
  private applyPrefill(prefill: IConversionPrefill): void {
    this.prefill.set(prefill);

    // BR-2: already converted — block before showing the form.
    if (prefill.alreadyConverted) {
      this.blockedMessage.set(
        'This applicant has already been converted to an employee.',
      );
      this.alreadyConvertedEmployeeId.set(prefill.convertedToEmployeeId ?? null);
    }

    const filled = new Set<string>();
    const mark = (key: string, value: unknown): void => {
      if (value !== null && value !== undefined && value !== '') {
        filled.add(key);
      }
    };
    mark('firstName', prefill.firstName);
    mark('lastName', prefill.lastName);
    mark('email', prefill.email);
    mark('phone', prefill.phone);
    mark('jobTitleId', prefill.jobTitleId);
    mark('departmentId', prefill.departmentId);
    mark('reportingManagerId', prefill.reportingManagerId);
    mark('dateOfJoining', prefill.dateOfJoining);
    mark('salaryAmount', prefill.salaryAmount);
    mark('currency', prefill.currency);
    this.autoFilled.set(filled);

    this.form.patchValue({
      firstName: prefill.firstName,
      lastName: prefill.lastName,
      email: prefill.email,
      phone: prefill.phone ?? '',
      employeeNumber: prefill.suggestedEmployeeNumber ?? '',
      jobTitleId: prefill.jobTitleId ?? '',
      departmentId: prefill.departmentId ?? '',
      reportingManagerId: prefill.reportingManagerId ?? '',
      dateOfJoining: prefill.dateOfJoining ?? '',
      salaryAmount: prefill.salaryAmount,
      currency: prefill.currency ?? '',
    });

    // The auto-generated number is read-only until the user opts to override (FR-4).
    this.form.get('employeeNumber')?.disable({ emitEvent: false });

    this.step.set('form');
  }

  /** Whether a field was pre-filled from the application/offer (§8 visual mark). */
  isAutoFilled(field: string): boolean {
    return this.autoFilled().has(field);
  }

  /** Show a validation error only when the field is invalid and touched. */
  err(field: string): boolean {
    const ctrl = this.form.get(field);
    return !!ctrl && ctrl.invalid && ctrl.touched;
  }

  /** Toggle manual override of the auto-generated employee number (FR-4). */
  toggleOverride(): void {
    const next = !this.overrideNumber();
    this.overrideNumber.set(next);
    const ctrl = this.form.get('employeeNumber');
    if (!ctrl) {
      return;
    }
    if (next) {
      ctrl.enable({ emitEvent: false });
    } else {
      // Revert to the suggested number and lock it again.
      ctrl.setValue(this.prefill()?.suggestedEmployeeNumber ?? '', {
        emitEvent: false,
      });
      ctrl.disable({ emitEvent: false });
    }
  }

  /** Validate then advance to the confirmation summary (§8). */
  review(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.step.set('confirm');
  }

  backToForm(): void {
    this.step.set('form');
  }

  /** POST the conversion (AC-2). On success show the success state (AC-4). */
  create(): void {
    if (this.creating()) {
      return;
    }
    this.creating.set(true);
    this.blockedMessage.set(null);
    this.alreadyConvertedEmployeeId.set(null);

    const v = this.form.getRawValue();
    const request: IConvertEmployeeRequest = {
      firstName: (v.firstName ?? '').trim(),
      lastName: (v.lastName ?? '').trim(),
      email: (v.email ?? '').trim().toLowerCase(),
      phone: v.phone?.trim() || null,
      employeeNumber: this.overrideNumber()
        ? v.employeeNumber?.trim() || null
        : null,
      jobTitleId: v.jobTitleId ?? '',
      departmentId: v.departmentId ?? '',
      reportingManagerId: v.reportingManagerId || null,
      employmentType: v.employmentType as EmploymentType,
      workLocationId: v.workLocationId ?? '',
      dateOfJoining: v.dateOfJoining ?? '',
      // BUG-292: salaryAmount and currency are deliberately NOT sent. The backend request record has neither
      // property, so both were silently dropped by model binding while the UI reported success. Sending fields
      // the server does not accept is how the illusion was created; stop sending them. Both remain VISIBLE
      // read-only because the offer's agreed figures are useful context at conversion time.
    };

    this.conversionService.convert(this.applicantId(), request).subscribe({
      next: (res) => {
        this.creating.set(false);
        this.result.set(res);
        this.step.set('success');
        this.toastr.success('Employee created from applicant.');
        this.converted.emit(res);
      },
      error: (err: HttpErrorResponse) => {
        this.creating.set(false);
        this.handleConvertError(err);
      },
    });
  }

  /** Blocked conversions (BR-2/BR-3) show inline; others fall back to a toast. */
  private handleConvertError(err: HttpErrorResponse): void {
    const body = ConversionService.parseError(err);
    if (body?.code === ALREADY_CONVERTED_CODE) {
      this.blockedMessage.set(
        body.message ||
          'This applicant has already been converted to an employee.',
      );
      this.step.set('form');
    } else if (body?.code === PLAN_LIMIT_CODE) {
      this.blockedMessage.set(
        body.message ||
          'Converting would exceed your plan’s employee limit. Please upgrade your subscription.',
      );
      this.step.set('form');
    } else {
      this.toastr.error(body?.message ?? 'Failed to convert applicant.');
    }
  }

  cancel(): void {
    this.cancelled.emit();
  }

  // ─── Helpers ─────────────────────────────────────────────

  private lookupLabel(
    options: ILookupOption[],
    id: string | null | undefined,
  ): string {
    if (!id) {
      return '';
    }
    return options.find((o) => o.id === id)?.label ?? '';
  }
}
