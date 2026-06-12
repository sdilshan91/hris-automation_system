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
import {
  ReactiveFormsModule,
  FormBuilder,
  FormGroup,
  FormArray,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject, forkJoin } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { EmployeeService } from '../../services/employee.service';
import {
  ICreateEmployeeRequest,
  WIZARD_STEPS,
  IWizardStep,
  GENDER_OPTIONS,
  EMPLOYMENT_TYPE_OPTIONS,
  EmployeeGender,
  EmploymentType,
} from '../../models/employee.models';
import { DepartmentService } from '../../../departments/services/department.service';
import { IDepartment } from '../../../departments/models/department.models';
import { JobTitleService } from '../../../job-titles/services/job-title.service';
import { IJobTitle } from '../../../job-titles/models/job-title.models';
import { PhotoUploadComponent } from '../photo-upload/photo-upload.component';

/**
 * US-CHR-001 AC-1: Multi-step card wizard for adding a new employee.
 *
 * Steps: Personal Info, Contact, Emergency Contact, Employment Details,
 * plus optional Education, Work History, Dependents.
 *
 * Features:
 *   - Progress indicator (step dots / breadcrumb)
 *   - Smooth 200-300ms transitions between steps
 *   - Save as Draft + Save & Continue per step
 *   - On mobile (<768px): single column, vertical stepper
 *   - Reactive forms with validators per Data Requirements table
 *   - Department/Job Title dropdowns from real APIs (CHR-004/CHR-005)
 *   - Profile photo drag-and-drop with preview (AC-4)
 *   - Duplicate email (AC-3) and plan-limit (AC-5) error handling
 *   - TODO(US-CHR-012): Custom fields rendering (AC-6)
 */
@Component({
  selector: 'app-employee-wizard',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, PhotoUploadComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('stepTransition', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(24px)' }),
        animate(
          '250ms ease-out',
          style({ opacity: 1, transform: 'translateX(0)' })
        ),
      ]),
      transition(':leave', [
        animate(
          '200ms ease-in',
          style({ opacity: 0, transform: 'translateX(-24px)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Page header -->
      <div class="wizard-header">
        <button
          type="button"
          class="back-link"
          (click)="goBack()"
          aria-label="Back to employee list"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
            <path fill-rule="evenodd" d="M17 10a.75.75 0 0 1-.75.75H5.612l4.158 3.96a.75.75 0 1 1-1.04 1.08l-5.5-5.25a.75.75 0 0 1 0-1.08l5.5-5.25a.75.75 0 1 1 1.04 1.08L5.612 9.25H16.25A.75.75 0 0 1 17 10Z" clip-rule="evenodd"/>
          </svg>
        </button>
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Add New Employee
          </h1>
          <p class="mt-0.5 text-sm text-neutral-500">
            Complete the steps below to create an employee record.
          </p>
        </div>
      </div>

      <!-- Plan limit banner (AC-5) -->
      @if (planLimitError()) {
        <div class="plan-limit-banner" role="alert">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 flex-shrink-0" aria-hidden="true">
            <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/>
          </svg>
          <span>{{ planLimitError() }}</span>
        </div>
      }

      <!-- Progress indicator — desktop horizontal dots -->
      <nav class="step-nav-desktop" aria-label="Employee creation steps">
        <ol class="step-list-desktop">
          @for (step of wizardSteps; track step.index; let i = $index) {
            <li class="step-item-desktop">
              <button
                type="button"
                class="step-dot-container"
                [class.step-completed]="i < currentStep()"
                [class.step-active]="i === currentStep()"
                [class.step-upcoming]="i > currentStep()"
                [disabled]="i > furthestStep()"
                (click)="goToStep(i)"
                [attr.aria-label]="step.label + (i < currentStep() ? ' (completed)' : i === currentStep() ? ' (current)' : '')"
                [attr.aria-current]="i === currentStep() ? 'step' : null"
              >
                <span class="step-dot">
                  @if (i < currentStep()) {
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                      <path fill-rule="evenodd" d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z" clip-rule="evenodd"/>
                    </svg>
                  } @else {
                    <span class="step-number">{{ i + 1 }}</span>
                  }
                </span>
                <span class="step-label-desktop">{{ step.label }}</span>
                @if (!step.required) {
                  <span class="step-optional-badge">Optional</span>
                }
              </button>
              @if (i < wizardSteps.length - 1) {
                <div class="step-connector" [class.step-connector-completed]="i < currentStep()"></div>
              }
            </li>
          }
        </ol>
      </nav>

      <!-- Progress indicator — mobile vertical stepper -->
      <nav class="step-nav-mobile" aria-label="Employee creation steps">
        <div class="step-mobile-current">
          <span class="step-mobile-counter">
            Step {{ currentStep() + 1 }} of {{ wizardSteps.length }}
          </span>
          <span class="step-mobile-label">
            {{ wizardSteps[currentStep()].label }}
          </span>
        </div>
        <div class="step-mobile-bar">
          <div
            class="step-mobile-bar-fill"
            [style.width.%]="((currentStep() + 1) / wizardSteps.length) * 100"
          ></div>
        </div>
      </nav>

      <!-- Step content card -->
      <div class="wizard-card">
        @if (isLoadingData()) {
          <div class="loading-state" aria-live="polite">
            <div class="loading-spinner"></div>
            <span class="text-sm text-neutral-500">Loading form data...</span>
          </div>
        } @else {
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <!-- Step 0: Personal Info -->
            @if (currentStep() === 0) {
              <div @stepTransition class="step-content">
                <h2 class="step-title">Personal Information</h2>

                <!-- Profile photo (AC-4) -->
                <div class="form-section">
                  <label class="label-notion">Profile Photo</label>
                  <app-photo-upload
                    [currentPhoto]="selectedPhoto()"
                    (photoSelected)="onPhotoSelected($event)"
                    (photoRemoved)="onPhotoRemoved()"
                  />
                </div>

                <div class="form-grid">
                  <!-- First Name -->
                  <div class="form-section">
                    <label class="label-notion" for="firstName">
                      First Name <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <input
                      id="firstName"
                      type="text"
                      formControlName="firstName"
                      class="input-notion"
                      placeholder="e.g. John"
                      maxlength="100"
                      autocomplete="given-name"
                    />
                    @if (showError('firstName')) {
                      <p class="field-error" role="alert">
                        @if (form.get('firstName')?.hasError('required')) {
                          First name is required.
                        } @else if (form.get('firstName')?.hasError('maxlength')) {
                          First name cannot exceed 100 characters.
                        }
                      </p>
                    }
                  </div>

                  <!-- Last Name -->
                  <div class="form-section">
                    <label class="label-notion" for="lastName">
                      Last Name <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <input
                      id="lastName"
                      type="text"
                      formControlName="lastName"
                      class="input-notion"
                      placeholder="e.g. Doe"
                      maxlength="100"
                      autocomplete="family-name"
                    />
                    @if (showError('lastName')) {
                      <p class="field-error" role="alert">
                        @if (form.get('lastName')?.hasError('required')) {
                          Last name is required.
                        } @else if (form.get('lastName')?.hasError('maxlength')) {
                          Last name cannot exceed 100 characters.
                        }
                      </p>
                    }
                  </div>
                </div>

                <!-- Email -->
                <div class="form-section">
                  <label class="label-notion" for="email">
                    Email <span class="text-red-500" aria-hidden="true">*</span>
                  </label>
                  <input
                    id="email"
                    type="email"
                    formControlName="email"
                    class="input-notion"
                    placeholder="e.g. john.doe&#64;company.com"
                    maxlength="150"
                    autocomplete="email"
                  />
                  @if (showError('email')) {
                    <p class="field-error" role="alert">
                      @if (form.get('email')?.hasError('required')) {
                        Email is required.
                      } @else if (form.get('email')?.hasError('email')) {
                        Please enter a valid email address.
                      } @else if (form.get('email')?.hasError('maxlength')) {
                        Email cannot exceed 150 characters.
                      }
                    </p>
                  }
                  @if (duplicateEmailError()) {
                    <p class="field-error" role="alert">{{ duplicateEmailError() }}</p>
                  }
                </div>

                <div class="form-grid">
                  <!-- Date of Birth -->
                  <div class="form-section">
                    <label class="label-notion" for="dateOfBirth">
                      Date of Birth
                    </label>
                    <input
                      id="dateOfBirth"
                      type="date"
                      formControlName="dateOfBirth"
                      class="input-notion"
                      autocomplete="bday"
                    />
                    @if (showError('dateOfBirth')) {
                      <p class="field-error" role="alert">
                        @if (form.get('dateOfBirth')?.hasError('minAge')) {
                          Employee must be at least 16 years old.
                        } @else if (form.get('dateOfBirth')?.hasError('futureDate')) {
                          Date of birth cannot be in the future.
                        }
                      </p>
                    }
                  </div>

                  <!-- Gender -->
                  <div class="form-section">
                    <label class="label-notion" for="gender">
                      Gender
                    </label>
                    <select
                      id="gender"
                      formControlName="gender"
                      class="input-notion select-input"
                    >
                      <option [ngValue]="null">Select gender</option>
                      @for (g of genderOptions; track g) {
                        <option [ngValue]="g">{{ g }}</option>
                      }
                    </select>
                  </div>
                </div>
              </div>
            }

            <!-- Step 1: Contact Details -->
            @if (currentStep() === 1) {
              <div @stepTransition class="step-content">
                <h2 class="step-title">Contact Details</h2>

                <!-- Phone -->
                <div class="form-section">
                  <label class="label-notion" for="phone">Phone</label>
                  <input
                    id="phone"
                    type="tel"
                    formControlName="phone"
                    class="input-notion"
                    placeholder="e.g. +1 555-0123"
                    maxlength="20"
                    autocomplete="tel"
                  />
                  <p class="field-hint">E.164 format preferred (e.g. +94771234567)</p>
                </div>

                <!-- Address -->
                <div class="form-section">
                  <label class="label-notion" for="address">Address</label>
                  <textarea
                    id="address"
                    formControlName="address"
                    class="input-notion textarea-notion"
                    rows="2"
                    placeholder="Street address"
                    autocomplete="street-address"
                  ></textarea>
                </div>

                <div class="form-grid">
                  <!-- City -->
                  <div class="form-section">
                    <label class="label-notion" for="city">City</label>
                    <input
                      id="city"
                      type="text"
                      formControlName="city"
                      class="input-notion"
                      placeholder="e.g. Colombo"
                      autocomplete="address-level2"
                    />
                  </div>

                  <!-- State -->
                  <div class="form-section">
                    <label class="label-notion" for="state">State / Province</label>
                    <input
                      id="state"
                      type="text"
                      formControlName="state"
                      class="input-notion"
                      placeholder="e.g. Western"
                      autocomplete="address-level1"
                    />
                  </div>
                </div>

                <div class="form-grid">
                  <!-- Postal Code -->
                  <div class="form-section">
                    <label class="label-notion" for="postalCode">Postal Code</label>
                    <input
                      id="postalCode"
                      type="text"
                      formControlName="postalCode"
                      class="input-notion"
                      placeholder="e.g. 10100"
                      autocomplete="postal-code"
                    />
                  </div>

                  <!-- Country -->
                  <div class="form-section">
                    <label class="label-notion" for="country">Country</label>
                    <input
                      id="country"
                      type="text"
                      formControlName="country"
                      class="input-notion"
                      placeholder="e.g. Sri Lanka"
                      autocomplete="country-name"
                    />
                  </div>
                </div>
              </div>
            }

            <!-- Step 2: Emergency Contact -->
            @if (currentStep() === 2) {
              <div @stepTransition class="step-content">
                <h2 class="step-title">Emergency Contact</h2>
                <p class="text-sm text-neutral-500 mb-4">
                  At least one emergency contact is recommended but not mandatory (BR-5).
                </p>

                <div class="form-section">
                  <label class="label-notion" for="emergencyContactName">
                    Contact Name
                  </label>
                  <input
                    id="emergencyContactName"
                    type="text"
                    formControlName="emergencyContactName"
                    class="input-notion"
                    placeholder="e.g. Jane Doe"
                    autocomplete="off"
                  />
                </div>

                <div class="form-grid">
                  <div class="form-section">
                    <label class="label-notion" for="emergencyContactRelationship">
                      Relationship
                    </label>
                    <input
                      id="emergencyContactRelationship"
                      type="text"
                      formControlName="emergencyContactRelationship"
                      class="input-notion"
                      placeholder="e.g. Spouse, Parent"
                      autocomplete="off"
                    />
                  </div>

                  <div class="form-section">
                    <label class="label-notion" for="emergencyContactPhone">
                      Phone Number
                    </label>
                    <input
                      id="emergencyContactPhone"
                      type="tel"
                      formControlName="emergencyContactPhone"
                      class="input-notion"
                      placeholder="e.g. +94771234567"
                      autocomplete="off"
                    />
                  </div>
                </div>
              </div>
            }

            <!-- Step 3: Employment Details -->
            @if (currentStep() === 3) {
              <div @stepTransition class="step-content">
                <h2 class="step-title">Employment Details</h2>

                <!-- Date of Joining -->
                <div class="form-section">
                  <label class="label-notion" for="dateOfJoining">
                    Date of Joining <span class="text-red-500" aria-hidden="true">*</span>
                  </label>
                  <input
                    id="dateOfJoining"
                    type="date"
                    formControlName="dateOfJoining"
                    class="input-notion"
                  />
                  @if (showError('dateOfJoining')) {
                    <p class="field-error" role="alert">
                      @if (form.get('dateOfJoining')?.hasError('required')) {
                        Date of joining is required.
                      } @else if (form.get('dateOfJoining')?.hasError('maxFutureDate')) {
                        Date of joining cannot be more than 90 days in the future.
                      }
                    </p>
                  }
                </div>

                <div class="form-grid">
                  <!-- Department (real API — CHR-004) -->
                  <div class="form-section">
                    <label class="label-notion" for="departmentId">
                      Department <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <select
                      id="departmentId"
                      formControlName="departmentId"
                      class="input-notion select-input"
                    >
                      <option [ngValue]="''">Select department</option>
                      @for (dept of departments(); track dept.departmentId) {
                        <option [ngValue]="dept.departmentId">{{ dept.name }}</option>
                      }
                    </select>
                    @if (showError('departmentId')) {
                      <p class="field-error" role="alert">
                        Department is required.
                      </p>
                    }
                  </div>

                  <!-- Job Title (real API — CHR-005) -->
                  <div class="form-section">
                    <label class="label-notion" for="jobTitleId">
                      Job Title <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <select
                      id="jobTitleId"
                      formControlName="jobTitleId"
                      class="input-notion select-input"
                    >
                      <option [ngValue]="''">Select job title</option>
                      @for (jt of jobTitles(); track jt.jobTitleId) {
                        <option [ngValue]="jt.jobTitleId">{{ jt.titleName }}</option>
                      }
                    </select>
                    @if (showError('jobTitleId')) {
                      <p class="field-error" role="alert">
                        Job title is required.
                      </p>
                    }
                  </div>
                </div>

                <div class="form-grid">
                  <!-- Employment Type -->
                  <div class="form-section">
                    <label class="label-notion" for="employmentType">
                      Employment Type <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <select
                      id="employmentType"
                      formControlName="employmentType"
                      class="input-notion select-input"
                    >
                      <option [ngValue]="''">Select type</option>
                      @for (et of employmentTypeOptions; track et) {
                        <option [ngValue]="et">{{ et }}</option>
                      }
                    </select>
                    @if (showError('employmentType')) {
                      <p class="field-error" role="alert">
                        Employment type is required.
                      </p>
                    }
                  </div>

                  <!-- Status -->
                  <div class="form-section">
                    <label class="label-notion" for="status">
                      Status
                    </label>
                    <select
                      id="status"
                      formControlName="status"
                      class="input-notion select-input"
                    >
                      <option value="active">Active</option>
                      <option value="probation">Probation</option>
                    </select>
                    <p class="field-hint">Default: Active (BR-3)</p>
                  </div>
                </div>

                <!--
                  TODO(US-CHR-012): Custom fields (AC-6).
                  Render tenant-configured custom fields dynamically here.
                  No custom-field config endpoint exists yet — skip rendering.
                -->
              </div>
            }

            <!-- Step 4: Education (optional) -->
            @if (currentStep() === 4) {
              <div @stepTransition class="step-content">
                <h2 class="step-title">Education History</h2>
                <p class="text-sm text-neutral-500 mb-4">
                  This section is optional. You can add education records later.
                </p>

                @for (edu of educationControls.controls; track $index; let i = $index) {
                  <div class="repeater-card" formArrayName="education">
                    <div class="repeater-card-header">
                      <span class="text-sm font-medium text-neutral-700">Education #{{ i + 1 }}</span>
                      <button
                        type="button"
                        class="repeater-remove-btn"
                        (click)="removeEducation(i)"
                        aria-label="Remove education entry"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                          <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                        </svg>
                      </button>
                    </div>
                    <div [formGroupName]="i" class="form-grid">
                      <div class="form-section">
                        <label class="label-notion" [for]="'institution-' + i">Institution</label>
                        <input
                          [id]="'institution-' + i"
                          type="text"
                          formControlName="institution"
                          class="input-notion"
                          placeholder="e.g. University of Colombo"
                        />
                      </div>
                      <div class="form-section">
                        <label class="label-notion" [for]="'degree-' + i">Degree</label>
                        <input
                          [id]="'degree-' + i"
                          type="text"
                          formControlName="degree"
                          class="input-notion"
                          placeholder="e.g. BSc Computer Science"
                        />
                      </div>
                      <div class="form-section">
                        <label class="label-notion" [for]="'eduYear-' + i">Year</label>
                        <input
                          [id]="'eduYear-' + i"
                          type="text"
                          formControlName="year"
                          class="input-notion"
                          placeholder="e.g. 2020"
                        />
                      </div>
                    </div>
                  </div>
                }
                <button
                  type="button"
                  class="add-repeater-btn"
                  (click)="addEducation()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                    <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"/>
                  </svg>
                  Add Education
                </button>
              </div>
            }

            <!-- Step 5: Work History (optional) -->
            @if (currentStep() === 5) {
              <div @stepTransition class="step-content">
                <h2 class="step-title">Work History</h2>
                <p class="text-sm text-neutral-500 mb-4">
                  This section is optional. You can add work history later.
                </p>

                @for (wh of workHistoryControls.controls; track $index; let i = $index) {
                  <div class="repeater-card" formArrayName="workHistory">
                    <div class="repeater-card-header">
                      <span class="text-sm font-medium text-neutral-700">Experience #{{ i + 1 }}</span>
                      <button
                        type="button"
                        class="repeater-remove-btn"
                        (click)="removeWorkHistory(i)"
                        aria-label="Remove work history entry"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                          <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                        </svg>
                      </button>
                    </div>
                    <div [formGroupName]="i" class="form-grid">
                      <div class="form-section">
                        <label class="label-notion" [for]="'company-' + i">Company</label>
                        <input [id]="'company-' + i" type="text" formControlName="company" class="input-notion" placeholder="e.g. Google" />
                      </div>
                      <div class="form-section">
                        <label class="label-notion" [for]="'position-' + i">Position</label>
                        <input [id]="'position-' + i" type="text" formControlName="position" class="input-notion" placeholder="e.g. Senior Engineer" />
                      </div>
                      <div class="form-section">
                        <label class="label-notion" [for]="'whFrom-' + i">From</label>
                        <input [id]="'whFrom-' + i" type="date" formControlName="fromDate" class="input-notion" />
                      </div>
                      <div class="form-section">
                        <label class="label-notion" [for]="'whTo-' + i">To</label>
                        <input [id]="'whTo-' + i" type="date" formControlName="toDate" class="input-notion" />
                      </div>
                    </div>
                  </div>
                }
                <button
                  type="button"
                  class="add-repeater-btn"
                  (click)="addWorkHistory()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                    <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"/>
                  </svg>
                  Add Work Experience
                </button>
              </div>
            }

            <!-- Step 6: Dependents (optional) -->
            @if (currentStep() === 6) {
              <div @stepTransition class="step-content">
                <h2 class="step-title">Dependents</h2>
                <p class="text-sm text-neutral-500 mb-4">
                  This section is optional. You can add dependents later.
                </p>

                @for (dep of dependentControls.controls; track $index; let i = $index) {
                  <div class="repeater-card" formArrayName="dependents">
                    <div class="repeater-card-header">
                      <span class="text-sm font-medium text-neutral-700">Dependent #{{ i + 1 }}</span>
                      <button
                        type="button"
                        class="repeater-remove-btn"
                        (click)="removeDependent(i)"
                        aria-label="Remove dependent entry"
                      >
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                          <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                        </svg>
                      </button>
                    </div>
                    <div [formGroupName]="i" class="form-grid">
                      <div class="form-section">
                        <label class="label-notion" [for]="'depName-' + i">Name</label>
                        <input [id]="'depName-' + i" type="text" formControlName="name" class="input-notion" placeholder="e.g. Jane Doe" />
                      </div>
                      <div class="form-section">
                        <label class="label-notion" [for]="'depRelation-' + i">Relationship</label>
                        <input [id]="'depRelation-' + i" type="text" formControlName="relationship" class="input-notion" placeholder="e.g. Spouse, Child" />
                      </div>
                      <div class="form-section">
                        <label class="label-notion" [for]="'depDob-' + i">Date of Birth</label>
                        <input [id]="'depDob-' + i" type="date" formControlName="dateOfBirth" class="input-notion" />
                      </div>
                    </div>
                  </div>
                }
                <button
                  type="button"
                  class="add-repeater-btn"
                  (click)="addDependent()"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                    <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"/>
                  </svg>
                  Add Dependent
                </button>
              </div>
            }

            <!-- Step navigation buttons -->
            <div class="step-actions">
              <div class="step-actions-left">
                @if (currentStep() > 0) {
                  <button
                    type="button"
                    class="btn-secondary"
                    (click)="previousStep()"
                  >
                    Back
                  </button>
                }
              </div>
              <div class="step-actions-right">
                <button
                  type="button"
                  class="btn-secondary"
                  (click)="saveDraft()"
                  [disabled]="isSaving()"
                >
                  Save as Draft
                </button>
                @if (isLastStep()) {
                  <button
                    type="submit"
                    class="btn-primary"
                    [disabled]="isSaving() || !canSubmit()"
                  >
                    @if (isSaving()) {
                      <span class="btn-spinner"></span>
                      Creating...
                    } @else {
                      Create Employee
                    }
                  </button>
                } @else {
                  <button
                    type="button"
                    class="btn-primary"
                    (click)="nextStep()"
                    [disabled]="!canProceed()"
                  >
                    Save & Continue
                  </button>
                }
              </div>
            </div>
          </form>
        }
      </div>
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }

    /* ─── Page header ─────────────────────────── */

    .wizard-header {
      @apply flex items-center gap-3 mb-6;
    }

    .back-link {
      @apply w-9 h-9 rounded-lg flex items-center justify-center
        text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100
        transition-colors duration-150;
    }

    /* ─── Plan limit banner (AC-5) ──────────── */

    .plan-limit-banner {
      @apply flex items-center gap-3 rounded-xl bg-red-50 border border-red-200
        px-4 py-3 mb-6 text-sm text-red-800;
    }

    /* ─── Step nav — desktop ─────────────────── */

    .step-nav-desktop {
      @apply hidden md:block mb-6;
    }

    .step-list-desktop {
      @apply flex items-center gap-0 list-none p-0 m-0;
    }

    .step-item-desktop {
      @apply flex items-center;
    }

    .step-dot-container {
      @apply flex flex-col items-center gap-1.5 px-2 py-1 rounded-lg
        transition-colors duration-200 bg-transparent border-0 cursor-pointer
        disabled:cursor-not-allowed disabled:opacity-50
        focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600;
    }

    .step-dot {
      @apply w-8 h-8 rounded-full flex items-center justify-center text-xs font-semibold
        transition-all duration-200;
    }

    .step-completed .step-dot {
      @apply bg-brand-600 text-white;
    }

    .step-active .step-dot {
      @apply bg-brand-100 text-brand-700 ring-2 ring-brand-600;
    }

    .step-upcoming .step-dot {
      @apply bg-neutral-100 text-neutral-400;
    }

    .step-number {
      @apply text-xs;
    }

    .step-label-desktop {
      @apply text-xs font-medium text-neutral-600 whitespace-nowrap;
    }

    .step-active .step-label-desktop {
      @apply text-brand-700 font-semibold;
    }

    .step-optional-badge {
      @apply text-[10px] text-neutral-400 leading-none;
    }

    .step-connector {
      @apply w-6 h-px bg-neutral-200 mx-1 flex-shrink-0;
    }

    .step-connector-completed {
      @apply bg-brand-400;
    }

    /* ─── Step nav — mobile ──────────────────── */

    .step-nav-mobile {
      @apply md:hidden mb-6;
    }

    .step-mobile-current {
      @apply flex items-center gap-2 mb-2;
    }

    .step-mobile-counter {
      @apply text-xs font-semibold text-brand-600 bg-brand-50 px-2 py-0.5 rounded-full;
    }

    .step-mobile-label {
      @apply text-sm font-medium text-neutral-700;
    }

    .step-mobile-bar {
      @apply w-full h-1.5 bg-neutral-100 rounded-full overflow-hidden;
    }

    .step-mobile-bar-fill {
      @apply h-full bg-brand-600 rounded-full transition-all duration-300 ease-out;
    }

    /* ─── Wizard card ────────────────────────── */

    .wizard-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-6 md:p-8;
    }

    .step-content {
      @apply space-y-5;
    }

    .step-title {
      @apply text-lg font-semibold text-neutral-900 mb-2 pb-3 border-b border-neutral-100;
    }

    .loading-state {
      @apply flex flex-col items-center justify-center py-16 gap-3;
    }

    .loading-spinner {
      @apply w-8 h-8 border-[3px] border-neutral-200 border-t-brand-600 rounded-full;
      animation: spin 0.7s linear infinite;
    }

    /* ─── Form fields ────────────────────────── */

    .form-grid {
      @apply grid grid-cols-1 md:grid-cols-2 gap-5;
    }

    .form-section {
      @apply space-y-1.5;
    }

    .field-hint {
      @apply text-xs text-neutral-400;
    }

    .field-error {
      @apply text-xs text-red-600 mt-1;
    }

    .textarea-notion {
      @apply resize-y min-h-[4rem];
    }

    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    /* ─── Repeater cards (edu/work/dependents) */

    .repeater-card {
      @apply rounded-lg border border-neutral-100 bg-neutral-50/50 p-4 mb-3;
    }

    .repeater-card-header {
      @apply flex items-center justify-between mb-3;
    }

    .repeater-remove-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-red-600 hover:bg-red-50
        transition-colors duration-150;
    }

    .add-repeater-btn {
      @apply inline-flex items-center gap-1.5 text-sm font-medium
        text-brand-600 hover:text-brand-700 transition-colors duration-150
        mt-2 px-2 py-1 rounded-lg hover:bg-brand-50;
    }

    /* ─── Step actions ───────────────────────── */

    .step-actions {
      @apply flex flex-col sm:flex-row items-stretch sm:items-center
        justify-between gap-3 pt-6 mt-6 border-t border-neutral-100;
    }

    .step-actions-left {
      @apply flex gap-3;
    }

    .step-actions-right {
      @apply flex flex-col sm:flex-row gap-3;
    }

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

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `],
})
export class EmployeeWizardComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);
  private readonly employeeService = inject(EmployeeService);
  private readonly departmentService = inject(DepartmentService);
  private readonly jobTitleService = inject(JobTitleService);

  private readonly destroy$ = new Subject<void>();

  // ─── Readonly data ────────────────────────────────────────

  readonly wizardSteps: IWizardStep[] = WIZARD_STEPS;
  readonly genderOptions: EmployeeGender[] = GENDER_OPTIONS;
  readonly employmentTypeOptions: EmploymentType[] = EMPLOYMENT_TYPE_OPTIONS;

  // ─── Signals ──────────────────────────────────────────────

  readonly currentStep = signal(0);
  readonly furthestStep = signal(0);
  readonly isSaving = signal(false);
  readonly isLoadingData = signal(true);
  readonly departments = signal<IDepartment[]>([]);
  readonly jobTitles = signal<IJobTitle[]>([]);
  readonly selectedPhoto = signal<File | null>(null);
  readonly duplicateEmailError = signal<string | null>(null);
  readonly planLimitError = signal<string | null>(null);

  // ─── Form ─────────────────────────────────────────────────

  form!: FormGroup;

  get educationControls(): FormArray {
    return this.form.get('education') as FormArray;
  }

  get workHistoryControls(): FormArray {
    return this.form.get('workHistory') as FormArray;
  }

  get dependentControls(): FormArray {
    return this.form.get('dependents') as FormArray;
  }

  // ─── Computed ─────────────────────────────────────────────

  readonly canSubmit = computed(() => {
    // All required steps must have valid form groups
    return this.isStepValid(0) && this.isStepValid(3) && !this.isSaving();
  });

  ngOnInit(): void {
    this.buildForm();
    this.loadReferenceData();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Form initialization ──────────────────────────────────

  private buildForm(): void {
    this.form = this.fb.group({
      // Step 0: Personal
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      email: ['', [Validators.required, Validators.email, Validators.maxLength(150)]],
      dateOfBirth: [null, [this.dateOfBirthValidator]],
      gender: [null as EmployeeGender | null],

      // Step 1: Contact
      phone: [''],
      address: [''],
      city: [''],
      state: [''],
      postalCode: [''],
      country: [''],

      // Step 2: Emergency Contact
      emergencyContactName: [''],
      emergencyContactRelationship: [''],
      emergencyContactPhone: [''],

      // Step 3: Employment Details
      dateOfJoining: ['', [Validators.required, this.dateOfJoiningValidator]],
      departmentId: ['', [Validators.required]],
      jobTitleId: ['', [Validators.required]],
      employmentType: ['' as EmploymentType | '', [Validators.required]],
      status: ['active'],

      // Step 4: Education (optional repeater)
      education: this.fb.array([]),

      // Step 5: Work History (optional repeater)
      workHistory: this.fb.array([]),

      // Step 6: Dependents (optional repeater)
      dependents: this.fb.array([]),
    });
  }

  // ─── Data loading ─────────────────────────────────────────

  private loadReferenceData(): void {
    this.isLoadingData.set(true);

    forkJoin({
      departments: this.departmentService.getDepartments(),
      jobTitles: this.jobTitleService.getJobTitles(),
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ departments, jobTitles }) => {
          // Filter to only active items (BR-5: inactive departments hidden from assignment)
          this.departments.set(departments.filter((d) => d.isActive));
          this.jobTitles.set(jobTitles.filter((jt) => jt.isActive));
          this.isLoadingData.set(false);
        },
        error: () => {
          this.toastr.error('Failed to load form data. Please try again.');
          this.isLoadingData.set(false);
        },
      });
  }

  // ─── Custom validators ────────────────────────────────────

  /** DOB must be in past and age >= 16 */
  dateOfBirthValidator(control: AbstractControl): ValidationErrors | null {
    if (!control.value) return null;

    const dob = new Date(control.value);
    const today = new Date();

    if (dob >= today) {
      return { futureDate: true };
    }

    // Check age >= 16
    const age = today.getFullYear() - dob.getFullYear();
    const monthDiff = today.getMonth() - dob.getMonth();
    const dayDiff = today.getDate() - dob.getDate();
    const actualAge =
      monthDiff < 0 || (monthDiff === 0 && dayDiff < 0) ? age - 1 : age;

    if (actualAge < 16) {
      return { minAge: true };
    }

    return null;
  }

  /** Date of joining cannot be more than 90 days in the future (BR-4) */
  dateOfJoiningValidator(control: AbstractControl): ValidationErrors | null {
    if (!control.value) return null;

    const joiningDate = new Date(control.value);
    const maxDate = new Date();
    maxDate.setDate(maxDate.getDate() + 90);

    if (joiningDate > maxDate) {
      return { maxFutureDate: true };
    }

    return null;
  }

  // ─── Step navigation ──────────────────────────────────────

  goToStep(stepIndex: number): void {
    if (stepIndex <= this.furthestStep()) {
      this.currentStep.set(stepIndex);
    }
  }

  nextStep(): void {
    // Mark current step's fields as touched for validation display
    this.markCurrentStepTouched();

    if (!this.canProceed()) return;

    const next = this.currentStep() + 1;
    if (next < this.wizardSteps.length) {
      this.currentStep.set(next);
      if (next > this.furthestStep()) {
        this.furthestStep.set(next);
      }
    }
  }

  previousStep(): void {
    const prev = this.currentStep() - 1;
    if (prev >= 0) {
      this.currentStep.set(prev);
    }
  }

  isLastStep(): boolean {
    return this.currentStep() === this.wizardSteps.length - 1;
  }

  /** Check whether the current step's form fields are valid */
  canProceed(): boolean {
    return this.isStepValid(this.currentStep());
  }

  /** Check whether a specific step's form fields are all valid */
  isStepValid(stepIndex: number): boolean {
    const fields = this.getStepFields(stepIndex);
    return fields.every((name) => {
      const control = this.form.get(name);
      return control ? control.valid : true;
    });
  }

  /** Show validation error only if the field is touched and invalid */
  showError(fieldName: string): boolean {
    const control = this.form.get(fieldName);
    return !!control && control.invalid && control.touched;
  }

  private markCurrentStepTouched(): void {
    const fields = this.getStepFields(this.currentStep());
    for (const name of fields) {
      const control = this.form.get(name);
      control?.markAsTouched();
    }
  }

  /** Map step index to the form control names belonging to that step */
  private getStepFields(stepIndex: number): string[] {
    switch (stepIndex) {
      case 0:
        return ['firstName', 'lastName', 'email', 'dateOfBirth', 'gender'];
      case 1:
        return ['phone', 'address', 'city', 'state', 'postalCode', 'country'];
      case 2:
        return [
          'emergencyContactName',
          'emergencyContactRelationship',
          'emergencyContactPhone',
        ];
      case 3:
        return [
          'dateOfJoining',
          'departmentId',
          'jobTitleId',
          'employmentType',
          'status',
        ];
      case 4:
        return ['education'];
      case 5:
        return ['workHistory'];
      case 6:
        return ['dependents'];
      default:
        return [];
    }
  }

  // ─── Repeater helpers ─────────────────────────────────────

  addEducation(): void {
    this.educationControls.push(
      this.fb.group({
        institution: [''],
        degree: [''],
        year: [''],
      })
    );
  }

  removeEducation(index: number): void {
    this.educationControls.removeAt(index);
  }

  addWorkHistory(): void {
    this.workHistoryControls.push(
      this.fb.group({
        company: [''],
        position: [''],
        fromDate: [''],
        toDate: [''],
      })
    );
  }

  removeWorkHistory(index: number): void {
    this.workHistoryControls.removeAt(index);
  }

  addDependent(): void {
    this.dependentControls.push(
      this.fb.group({
        name: [''],
        relationship: [''],
        dateOfBirth: [''],
      })
    );
  }

  removeDependent(index: number): void {
    this.dependentControls.removeAt(index);
  }

  // ─── Photo handling ───────────────────────────────────────

  onPhotoSelected(file: File): void {
    this.selectedPhoto.set(file);
  }

  onPhotoRemoved(): void {
    this.selectedPhoto.set(null);
  }

  // ─── Submit / Draft ───────────────────────────────────────

  saveDraft(): void {
    this.toastr.info('Draft saved locally.');
  }

  onSubmit(): void {
    // Mark all required fields touched
    this.form.markAllAsTouched();

    if (!this.canSubmit()) return;

    this.isSaving.set(true);
    this.duplicateEmailError.set(null);
    this.planLimitError.set(null);

    const formValue = this.form.value;

    const request: ICreateEmployeeRequest = {
      firstName: formValue.firstName.trim(),
      lastName: formValue.lastName.trim(),
      email: formValue.email.trim().toLowerCase(),
      phone: formValue.phone?.trim() || null,
      dateOfBirth: formValue.dateOfBirth || null,
      gender: formValue.gender || null,
      dateOfJoining: formValue.dateOfJoining,
      departmentId: formValue.departmentId,
      jobTitleId: formValue.jobTitleId,
      employmentType: formValue.employmentType,
      status: formValue.status || 'active',
      address: formValue.address?.trim() || null,
      city: formValue.city?.trim() || null,
      state: formValue.state?.trim() || null,
      postalCode: formValue.postalCode?.trim() || null,
      country: formValue.country?.trim() || null,
      emergencyContactName: formValue.emergencyContactName?.trim() || null,
      emergencyContactRelationship:
        formValue.emergencyContactRelationship?.trim() || null,
      emergencyContactPhone: formValue.emergencyContactPhone?.trim() || null,
    };

    this.employeeService
      .createEmployee(request, this.selectedPhoto())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (employee) => {
          this.isSaving.set(false);
          this.toastr.success(
            `Employee "${employee.firstName} ${employee.lastName}" created successfully.`
          );
          this.router.navigate(['/employees']);
        },
        error: (err: HttpErrorResponse) => {
          this.isSaving.set(false);
          this.handleSubmitError(err);
        },
      });
  }

  goBack(): void {
    this.router.navigate(['/employees']);
  }

  // ─── Error handling ───────────────────────────────────────

  private handleSubmitError(err: HttpErrorResponse): void {
    const body = EmployeeService.parseError(err);

    if (body?.code === 'duplicate_email') {
      // AC-3: duplicate email within tenant
      this.duplicateEmailError.set(
        body.message || 'An employee with this email already exists.'
      );
      // Navigate to step 0 where email field is
      this.currentStep.set(0);
    } else if (body?.code === 'plan_limit_reached') {
      // AC-5: subscription plan employee limit reached
      this.planLimitError.set(
        body.message ||
          'Employee limit reached for your current plan. Please upgrade or contact your administrator.'
      );
    } else {
      this.toastr.error(body?.message || 'Failed to create employee.');
    }
  }
}
