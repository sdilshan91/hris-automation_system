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
} from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AuthService } from '@core/auth/auth.service';
import { EmployeeService } from '../../services/employee.service';
import {
  IEmployeeProfile,
  IEmployee,
  IUpdateSectionRequest,
  ProfileSection,
  ProfileViewerRole,
  isSectionEditable,
  GENDER_OPTIONS,
  EmployeeGender,
  IStatusTransition,
  IChangeStatusRequest,
  IReportingChainNode,
  getInitialsFromName,
} from '../../models/employee.models';
import { FormsModule } from '@angular/forms';
import { EmployeeDocumentsComponent } from '../employee-documents/employee-documents.component';

/**
 * US-CHR-002: Comprehensive Employee Profile view + edit component.
 *
 * Displays the full employee profile in card-based collapsible sections (FR-1).
 * Supports inline per-section editing with Save/Cancel (FR-2).
 * Enforces field-level permissions by role (FR-3, AC-4, AC-5).
 * Uses xmin optimistic concurrency on save (AC-3, FR-4).
 * Employment history displayed as vertical timeline (FR-6, AC-6).
 */
@Component({
  selector: 'app-employee-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule, EmployeeDocumentsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('sectionExpand', [
      transition(':enter', [
        style({ opacity: 0, height: '0', overflow: 'hidden' }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, height: '0', overflow: 'hidden' })),
      ]),
    ]),
    trigger('modalOverlay', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
    trigger('modalSlide', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(16px) scale(0.97)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0) scale(1)' })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0, transform: 'translateY(8px) scale(0.98)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Back navigation -->
      <div class="flex items-center gap-3 mb-6">
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
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
          Employee Profile
        </h1>
      </div>

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="space-y-4" aria-live="polite" aria-busy="true">
          <!-- Skeleton header -->
          <div class="card-notion">
            <div class="flex items-center gap-4">
              <div class="skeleton-circle w-24 h-24"></div>
              <div class="space-y-3 flex-1">
                <div class="skeleton-line w-48 h-6"></div>
                <div class="skeleton-line w-32 h-4"></div>
                <div class="flex gap-2">
                  <div class="skeleton-line w-20 h-5"></div>
                  <div class="skeleton-line w-24 h-5"></div>
                </div>
              </div>
            </div>
          </div>
          <!-- Skeleton sections -->
          @for (_ of [1, 2, 3]; track $index) {
            <div class="card-notion">
              <div class="skeleton-line w-36 h-5 mb-4"></div>
              <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div class="skeleton-line w-full h-4"></div>
                <div class="skeleton-line w-full h-4"></div>
                <div class="skeleton-line w-3/4 h-4"></div>
                <div class="skeleton-line w-2/3 h-4"></div>
              </div>
            </div>
          }
        </div>
      }

      <!-- Error state -->
      @if (loadError()) {
        <div @fadeIn class="card-notion text-center py-16">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
            class="w-12 h-12 mx-auto text-red-300 mb-4" aria-hidden="true">
            <path fill-rule="evenodd" d="M9.401 3.003c1.155-2 4.043-2 5.197 0l7.355 12.748c1.154 2-.29 4.5-2.599 4.5H4.645c-2.309 0-3.752-2.5-2.598-4.5L9.4 3.003ZM12 8.25a.75.75 0 0 1 .75.75v3.75a.75.75 0 0 1-1.5 0V9a.75.75 0 0 1 .75-.75Zm0 8.25a.75.75 0 1 0 0-1.5.75.75 0 0 0 0 1.5Z" clip-rule="evenodd"/>
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">Failed to load profile</h3>
          <p class="text-sm text-neutral-500 mb-4">{{ loadError() }}</p>
          <button type="button" class="btn-primary" (click)="loadProfile()">
            Retry
          </button>
        </div>
      }

      <!-- Profile content -->
      @if (!isLoading() && !loadError() && profile()) {
        <!-- Section navigation — desktop tabs / mobile dropdown -->
        <div class="section-nav mb-4">
          <!-- Desktop tab bar -->
          <nav class="hidden md:flex gap-1 overflow-x-auto pb-1" aria-label="Profile sections">
            @for (section of sectionList; track section.key; let i = $index) {
              <button
                type="button"
                class="tab-btn"
                [class.tab-btn-active]="activeTab() === i"
                (click)="activeTab.set(i)"
                [attr.aria-selected]="activeTab() === i"
                role="tab"
              >
                {{ section.label }}
              </button>
            }
          </nav>
          <!-- Mobile dropdown -->
          <div class="md:hidden">
            <label for="section-select" class="sr-only">Select profile section</label>
            <select
              id="section-select"
              class="input-notion select-input"
              [value]="activeTab()"
              (change)="activeTab.set(+$any($event.target).value)"
            >
              @for (section of sectionList; track section.key; let i = $index) {
                <option [value]="i">{{ section.label }}</option>
              }
            </select>
          </div>
        </div>

        <!-- Summary header card (always visible) -->
        <div @fadeIn class="profile-header-card mb-4" role="banner">
          <div class="flex flex-col sm:flex-row items-center sm:items-start gap-4">
            <!-- Avatar -->
            <div class="avatar-lg" [class.avatar-lg-mobile]="false">
              @if (profile()!.profilePhotoUrl) {
                <img
                  [src]="profile()!.profilePhotoUrl"
                  [alt]="profile()!.firstName + ' ' + profile()!.lastName + ' profile photo'"
                  class="w-full h-full object-cover"
                />
              } @else {
                <span class="text-2xl font-semibold">{{ getInitials() }}</span>
              }
            </div>
            <!-- Info -->
            <div class="flex-1 text-center sm:text-left min-w-0">
              <h2 class="text-xl font-semibold text-neutral-900 truncate">
                {{ profile()!.firstName }} {{ profile()!.lastName }}
              </h2>
              @if (profile()!.jobTitleName) {
                <p class="text-sm text-neutral-500 truncate">{{ profile()!.jobTitleName }}</p>
              }
              <div class="flex flex-wrap items-center justify-center sm:justify-start gap-2 mt-2">
                <!-- Employee No badge -->
                <span class="badge badge-neutral" aria-label="Employee number">
                  {{ profile()!.employeeNo }}
                </span>
                <!-- Department tag -->
                @if (profile()!.departmentName) {
                  <span class="badge badge-brand" aria-label="Department">
                    {{ profile()!.departmentName }}
                  </span>
                }
                <!-- Status badge (US-CHR-009 FR-7) -->
                <span
                  class="badge status-badge-animated"
                  [ngClass]="getStatusBadgeClass(profile()!.status)"
                  aria-label="Employment status"
                >
                  {{ profile()!.status | titlecase }}
                </span>
                <!-- Change Status button (BR-2: HR Officer / Tenant Admin only) -->
                @if (canChangeStatus()) {
                  <button
                    type="button"
                    class="change-status-btn"
                    (click)="openStatusChangeModal()"
                    aria-label="Change employee status"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                      <path fill-rule="evenodd" d="M15.312 11.424a5.5 5.5 0 0 1-9.379 2.624l-1.436 1.436a.75.75 0 0 1-1.06-1.06l1.436-1.437A5.502 5.502 0 0 1 7.5 4.5a.75.75 0 0 1 0 1.5 4 4 0 1 0 3.394 6.107.75.75 0 1 1 1.272.793A5.48 5.48 0 0 1 10 14.5a5.48 5.48 0 0 1-2.688-.697l-1.436 1.436a.75.75 0 0 1-1.06-1.06l1.436-1.437Z" clip-rule="evenodd"/>
                      <path fill-rule="evenodd" d="M13.78 1.72a.75.75 0 0 1 0 1.06L6.56 10H9.25a.75.75 0 0 1 0 1.5H5a.75.75 0 0 1-.75-.75V6.5a.75.75 0 0 1 1.5 0v2.69l7.22-7.22a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd"/>
                    </svg>
                    Change Status
                  </button>
                }
              </div>
            </div>
          </div>
        </div>

        <!-- Section cards -->
        <div class="space-y-4">
          <!-- Personal Info Section -->
          @if (activeTab() === 0) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Personal Information'"
            >
              <div class="section-header">
                <h3 class="section-title">Personal Information</h3>
                @if (canEditSection('personal-info')) {
                  <button
                    type="button"
                    class="edit-btn"
                    [attr.aria-label]="editingSection() === 'personal-info' ? 'Cancel editing personal information' : 'Edit personal information'"
                    (click)="toggleEdit('personal-info')"
                  >
                    @if (editingSection() === 'personal-info') {
                      Cancel
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                      </svg>
                      Edit
                    }
                  </button>
                }
              </div>
              @if (editingSection() === 'personal-info') {
                <form [formGroup]="personalInfoForm" (ngSubmit)="saveSection('personal-info')" @sectionExpand>
                  <div class="form-grid">
                    <div class="form-field">
                      <label class="label-notion" for="pi-firstName">First Name</label>
                      <input id="pi-firstName" type="text" formControlName="firstName" class="input-notion" maxlength="100" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="pi-lastName">Last Name</label>
                      <input id="pi-lastName" type="text" formControlName="lastName" class="input-notion" maxlength="100" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="pi-dateOfBirth">Date of Birth</label>
                      <input id="pi-dateOfBirth" type="date" formControlName="dateOfBirth" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="pi-gender">Gender</label>
                      <select id="pi-gender" formControlName="gender" class="input-notion select-input">
                        <option [ngValue]="null">Not specified</option>
                        @for (g of genderOptions; track g) {
                          <option [ngValue]="g">{{ g }}</option>
                        }
                      </select>
                    </div>
                  </div>
                  <div class="form-actions">
                    <button type="button" class="btn-secondary" (click)="cancelEdit()">Cancel</button>
                    <button type="submit" class="btn-primary" [disabled]="isSaving() || personalInfoForm.invalid">
                      @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save }
                    </button>
                  </div>
                </form>
              } @else {
                <div class="data-grid" @sectionExpand>
                  <div class="data-field">
                    <dt class="data-label">First Name</dt>
                    <dd class="data-value">{{ profile()!.firstName }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Last Name</dt>
                    <dd class="data-value">{{ profile()!.lastName }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Date of Birth</dt>
                    <dd class="data-value">{{ profile()!.dateOfBirth ? (profile()!.dateOfBirth | date:'mediumDate') : 'Not specified' }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Gender</dt>
                    <dd class="data-value">{{ profile()!.gender || 'Not specified' }}</dd>
                  </div>
                </div>
              }
            </section>
          }

          <!-- Contact Section -->
          @if (activeTab() === 1) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Contact Information'"
            >
              <div class="section-header">
                <h3 class="section-title">Contact Information</h3>
                @if (canEditSection('contact')) {
                  <button
                    type="button"
                    class="edit-btn"
                    [attr.aria-label]="editingSection() === 'contact' ? 'Cancel editing contact information' : 'Edit contact information'"
                    (click)="toggleEdit('contact')"
                  >
                    @if (editingSection() === 'contact') {
                      Cancel
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                      </svg>
                      Edit
                    }
                  </button>
                }
              </div>
              @if (editingSection() === 'contact') {
                <form [formGroup]="contactForm" (ngSubmit)="saveSection('contact')" @sectionExpand>
                  <div class="form-grid">
                    <div class="form-field">
                      <label class="label-notion" for="ct-email">Work Email</label>
                      <input id="ct-email" type="email" [value]="profile()!.email" class="input-notion" disabled readonly
                        aria-describedby="ct-email-hint" />
                      <p id="ct-email-hint" class="field-hint">Work email cannot be changed here.</p>
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="ct-personalEmail">Personal Email</label>
                      <input id="ct-personalEmail" type="email" formControlName="personalEmail" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="ct-phone">Phone</label>
                      <input id="ct-phone" type="tel" formControlName="phone" class="input-notion" />
                    </div>
                    <div class="form-field col-span-full">
                      <label class="label-notion" for="ct-address">Address</label>
                      <textarea id="ct-address" formControlName="address" class="input-notion" rows="2"></textarea>
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="ct-city">City</label>
                      <input id="ct-city" type="text" formControlName="city" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="ct-state">State / Province</label>
                      <input id="ct-state" type="text" formControlName="state" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="ct-postalCode">Postal Code</label>
                      <input id="ct-postalCode" type="text" formControlName="postalCode" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="ct-country">Country</label>
                      <input id="ct-country" type="text" formControlName="country" class="input-notion" />
                    </div>
                  </div>
                  <div class="form-actions">
                    <button type="button" class="btn-secondary" (click)="cancelEdit()">Cancel</button>
                    <button type="submit" class="btn-primary" [disabled]="isSaving()">
                      @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save }
                    </button>
                  </div>
                </form>
              } @else {
                <div class="data-grid" @sectionExpand>
                  <div class="data-field">
                    <dt class="data-label">Work Email</dt>
                    <dd class="data-value">{{ profile()!.email }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Personal Email</dt>
                    <dd class="data-value">{{ profile()!.personalEmail || 'Not specified' }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Phone</dt>
                    <dd class="data-value">{{ profile()!.phone || 'Not specified' }}</dd>
                  </div>
                  <div class="data-field col-span-full">
                    <dt class="data-label">Address</dt>
                    <dd class="data-value">{{ formatAddress() || 'Not specified' }}</dd>
                  </div>
                </div>
              }
            </section>
          }

          <!-- Emergency Contacts Section -->
          @if (activeTab() === 2) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Emergency Contacts'"
            >
              <div class="section-header">
                <h3 class="section-title">Emergency Contacts</h3>
                @if (canEditSection('emergency-contacts')) {
                  <button
                    type="button"
                    class="edit-btn"
                    [attr.aria-label]="editingSection() === 'emergency-contacts' ? 'Cancel editing emergency contacts' : 'Edit emergency contacts'"
                    (click)="toggleEdit('emergency-contacts')"
                  >
                    @if (editingSection() === 'emergency-contacts') {
                      Cancel
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                      </svg>
                      Edit
                    }
                  </button>
                }
              </div>
              @if (editingSection() === 'emergency-contacts') {
                <form [formGroup]="emergencyContactsForm" (ngSubmit)="saveSection('emergency-contacts')" @sectionExpand>
                  @for (ec of emergencyContactControls.controls; track $index; let i = $index) {
                    <div class="repeater-card" formArrayName="contacts">
                      <div class="repeater-card-header">
                        <span class="text-sm font-medium text-neutral-700">Contact #{{ i + 1 }}</span>
                        <button type="button" class="repeater-remove-btn" (click)="removeEmergencyContact(i)"
                          [attr.aria-label]="'Remove emergency contact ' + (i + 1)">
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                          </svg>
                        </button>
                      </div>
                      <div [formGroupName]="i" class="form-grid">
                        <div class="form-field">
                          <label class="label-notion" [for]="'ec-name-' + i">Name</label>
                          <input [id]="'ec-name-' + i" type="text" formControlName="name" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'ec-rel-' + i">Relationship</label>
                          <input [id]="'ec-rel-' + i" type="text" formControlName="relationship" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'ec-phone-' + i">Phone</label>
                          <input [id]="'ec-phone-' + i" type="tel" formControlName="phone" class="input-notion" />
                        </div>
                      </div>
                    </div>
                  }
                  <button type="button" class="add-repeater-btn" (click)="addEmergencyContact()">
                    + Add Contact
                  </button>
                  <div class="form-actions">
                    <button type="button" class="btn-secondary" (click)="cancelEdit()">Cancel</button>
                    <button type="submit" class="btn-primary" [disabled]="isSaving()">
                      @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save }
                    </button>
                  </div>
                </form>
              } @else {
                <div @sectionExpand>
                  @if (profile()!.emergencyContacts.length === 0) {
                    <p class="text-sm text-neutral-400 py-4">No emergency contacts on file.</p>
                  }
                  @for (ec of profile()!.emergencyContacts; track $index) {
                    <div class="repeater-card-read">
                      <div class="data-grid">
                        <div class="data-field">
                          <dt class="data-label">Name</dt>
                          <dd class="data-value">{{ ec.name }}</dd>
                        </div>
                        <div class="data-field">
                          <dt class="data-label">Relationship</dt>
                          <dd class="data-value">{{ ec.relationship }}</dd>
                        </div>
                        <div class="data-field">
                          <dt class="data-label">Phone</dt>
                          <dd class="data-value">{{ ec.phone }}</dd>
                        </div>
                      </div>
                    </div>
                  }
                </div>
              }
            </section>
          }

          <!-- Employment Details Section -->
          @if (activeTab() === 3) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Employment Details'"
            >
              <div class="section-header">
                <h3 class="section-title">Employment Details</h3>
                @if (canEditSection('employment')) {
                  <button
                    type="button"
                    class="edit-btn"
                    [attr.aria-label]="editingSection() === 'employment' ? 'Cancel editing employment details' : 'Edit employment details'"
                    (click)="toggleEdit('employment')"
                  >
                    @if (editingSection() === 'employment') {
                      Cancel
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                      </svg>
                      Edit
                    }
                  </button>
                }
              </div>
              @if (editingSection() === 'employment') {
                <form [formGroup]="employmentForm" (ngSubmit)="saveSection('employment')" @sectionExpand>
                  <div class="form-grid">
                    <div class="form-field">
                      <label class="label-notion" for="emp-department">Department</label>
                      <input id="emp-department" type="text" formControlName="departmentName" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="emp-jobTitle">Job Title</label>
                      <input id="emp-jobTitle" type="text" formControlName="jobTitleName" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="emp-type">Employment Type</label>
                      <input id="emp-type" type="text" formControlName="employmentType" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="emp-status">Status</label>
                      <input id="emp-status" type="text" formControlName="status" class="input-notion" />
                    </div>
                    <div class="form-field">
                      <label class="label-notion" for="emp-dateOfJoining">Date of Joining</label>
                      <input id="emp-dateOfJoining" type="date" formControlName="dateOfJoining" class="input-notion" />
                    </div>
                  </div>
                  <div class="form-actions">
                    <button type="button" class="btn-secondary" (click)="cancelEdit()">Cancel</button>
                    <button type="submit" class="btn-primary" [disabled]="isSaving()">
                      @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save }
                    </button>
                  </div>
                </form>
              } @else {
                <div class="data-grid" @sectionExpand>
                  <div class="data-field">
                    <dt class="data-label">Department</dt>
                    <dd class="data-value">{{ profile()!.departmentName || 'Not assigned' }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Job Title</dt>
                    <dd class="data-value">{{ profile()!.jobTitleName || 'Not assigned' }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Employment Type</dt>
                    <dd class="data-value">{{ profile()!.employmentType }}</dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Status</dt>
                    <dd class="data-value">
                      <span class="badge" [ngClass]="getStatusBadgeClass(profile()!.status)">
                        {{ profile()!.status | titlecase }}
                      </span>
                    </dd>
                  </div>
                  <div class="data-field">
                    <dt class="data-label">Date of Joining</dt>
                    <dd class="data-value">{{ profile()!.dateOfJoining | date:'mediumDate' }}</dd>
                  </div>

                  <!-- US-CHR-011 AC-1: Reporting Manager mini-card -->
                  <div class="data-field col-span-full">
                    <dt class="data-label">Reporting Manager</dt>
                    <dd class="data-value">
                      <div class="flex items-center gap-3 mt-1">
                        @if (profile()!.reportingManagerId) {
                          <div
                            class="manager-mini-card cursor-pointer"
                            role="link"
                            tabindex="0"
                            [attr.aria-label]="'View profile of ' + profile()!.reportingManagerName"
                            (click)="navigateToManager()"
                            (keydown.enter)="navigateToManager()"
                          >
                            <div class="manager-avatar">
                              @if (profile()!.reportingManagerPhotoUrl) {
                                <img
                                  [src]="profile()!.reportingManagerPhotoUrl"
                                  [alt]="profile()!.reportingManagerName || ''"
                                  class="w-full h-full object-cover"
                                />
                              } @else {
                                <span class="text-xs">{{ getManagerInitials() }}</span>
                              }
                            </div>
                            <div class="min-w-0">
                              <p class="text-sm font-medium text-neutral-900 truncate">
                                {{ profile()!.reportingManagerName }}
                              </p>
                              @if (profile()!.reportingManagerJobTitle) {
                                <p class="text-xs text-neutral-500 truncate">
                                  {{ profile()!.reportingManagerJobTitle }}
                                </p>
                              }
                            </div>
                          </div>
                        } @else {
                          <span class="text-sm text-neutral-400">Not Assigned</span>
                        }
                        @if (canEditSection('employment')) {
                          <button
                            type="button"
                            class="edit-btn !px-2 !py-1"
                            (click)="openManagerSelector()"
                            aria-label="Change reporting manager"
                          >
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                              <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                              <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                            </svg>
                            Change
                          </button>
                        }
                      </div>
                    </dd>
                  </div>
                </div>

                <!-- US-CHR-011: Reporting chain breadcrumb -->
                @if (reportingChain().length > 0) {
                  <div class="mt-4 pt-4 border-t border-neutral-100">
                    <p class="text-xs font-medium text-neutral-400 uppercase tracking-wider mb-2">Reporting Chain</p>
                    <nav class="flex flex-wrap items-center gap-1" aria-label="Reporting chain">
                      <span class="chain-node chain-node-current" aria-current="true">
                        {{ profile()!.firstName }} {{ profile()!.lastName }}
                      </span>
                      @for (node of reportingChain(); track node.employeeId) {
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor"
                          class="w-3.5 h-3.5 text-neutral-300 flex-shrink-0" aria-hidden="true">
                          <path fill-rule="evenodd" d="M6.22 4.22a.75.75 0 0 1 1.06 0l3.25 3.25a.75.75 0 0 1 0 1.06l-3.25 3.25a.75.75 0 0 1-1.06-1.06L8.94 8 6.22 5.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd"/>
                        </svg>
                        <button
                          type="button"
                          class="chain-node"
                          (click)="navigateToEmployee(node.employeeId)"
                          [attr.aria-label]="'Navigate to ' + node.firstName + ' ' + node.lastName"
                        >
                          {{ node.firstName }} {{ node.lastName }}
                        </button>
                      }
                    </nav>
                  </div>
                }
              }
            </section>
          }

          <!-- Education Section -->
          @if (activeTab() === 4) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Education History'"
            >
              <div class="section-header">
                <h3 class="section-title">Education</h3>
                @if (canEditSection('education')) {
                  <button
                    type="button"
                    class="edit-btn"
                    [attr.aria-label]="editingSection() === 'education' ? 'Cancel editing education' : 'Edit education'"
                    (click)="toggleEdit('education')"
                  >
                    @if (editingSection() === 'education') {
                      Cancel
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                      </svg>
                      Edit
                    }
                  </button>
                }
              </div>
              @if (editingSection() === 'education') {
                <form [formGroup]="educationForm" (ngSubmit)="saveSection('education')" @sectionExpand>
                  @for (edu of educationFormControls.controls; track $index; let i = $index) {
                    <div class="repeater-card" formArrayName="records">
                      <div class="repeater-card-header">
                        <span class="text-sm font-medium text-neutral-700">Education #{{ i + 1 }}</span>
                        <button type="button" class="repeater-remove-btn" (click)="removeEducationRecord(i)"
                          [attr.aria-label]="'Remove education record ' + (i + 1)">
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                          </svg>
                        </button>
                      </div>
                      <div [formGroupName]="i" class="form-grid">
                        <div class="form-field">
                          <label class="label-notion" [for]="'edu-inst-' + i">Institution</label>
                          <input [id]="'edu-inst-' + i" type="text" formControlName="institution" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'edu-deg-' + i">Degree</label>
                          <input [id]="'edu-deg-' + i" type="text" formControlName="degree" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'edu-year-' + i">Year</label>
                          <input [id]="'edu-year-' + i" type="text" formControlName="endYear" class="input-notion" />
                        </div>
                      </div>
                    </div>
                  }
                  <button type="button" class="add-repeater-btn" (click)="addEducationRecord()">
                    + Add Education
                  </button>
                  <div class="form-actions">
                    <button type="button" class="btn-secondary" (click)="cancelEdit()">Cancel</button>
                    <button type="submit" class="btn-primary" [disabled]="isSaving()">
                      @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save }
                    </button>
                  </div>
                </form>
              } @else {
                <div @sectionExpand>
                  @if (profile()!.education.length === 0) {
                    <p class="text-sm text-neutral-400 py-4">No education records on file.</p>
                  }
                  @for (edu of profile()!.education; track $index) {
                    <div class="repeater-card-read">
                      <p class="text-sm font-medium text-neutral-900">{{ edu.degree }}</p>
                      <p class="text-sm text-neutral-500">{{ edu.institution }}</p>
                      @if (edu.endYear) {
                        <p class="text-xs text-neutral-400">{{ edu.endYear }}</p>
                      }
                    </div>
                  }
                </div>
              }
            </section>
          }

          <!-- Work History Section -->
          @if (activeTab() === 5) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Work History'"
            >
              <div class="section-header">
                <h3 class="section-title">Work History</h3>
                @if (canEditSection('work-history')) {
                  <button
                    type="button"
                    class="edit-btn"
                    [attr.aria-label]="editingSection() === 'work-history' ? 'Cancel editing work history' : 'Edit work history'"
                    (click)="toggleEdit('work-history')"
                  >
                    @if (editingSection() === 'work-history') {
                      Cancel
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                      </svg>
                      Edit
                    }
                  </button>
                }
              </div>
              @if (editingSection() === 'work-history') {
                <form [formGroup]="workHistoryForm" (ngSubmit)="saveSection('work-history')" @sectionExpand>
                  @for (wh of workHistoryFormControls.controls; track $index; let i = $index) {
                    <div class="repeater-card" formArrayName="records">
                      <div class="repeater-card-header">
                        <span class="text-sm font-medium text-neutral-700">Experience #{{ i + 1 }}</span>
                        <button type="button" class="repeater-remove-btn" (click)="removeWorkHistoryRecord(i)"
                          [attr.aria-label]="'Remove work history record ' + (i + 1)">
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                          </svg>
                        </button>
                      </div>
                      <div [formGroupName]="i" class="form-grid">
                        <div class="form-field">
                          <label class="label-notion" [for]="'wh-company-' + i">Company</label>
                          <input [id]="'wh-company-' + i" type="text" formControlName="company" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'wh-pos-' + i">Position</label>
                          <input [id]="'wh-pos-' + i" type="text" formControlName="position" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'wh-from-' + i">From</label>
                          <input [id]="'wh-from-' + i" type="date" formControlName="fromDate" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'wh-to-' + i">To</label>
                          <input [id]="'wh-to-' + i" type="date" formControlName="toDate" class="input-notion" />
                        </div>
                      </div>
                    </div>
                  }
                  <button type="button" class="add-repeater-btn" (click)="addWorkHistoryRecord()">
                    + Add Work Experience
                  </button>
                  <div class="form-actions">
                    <button type="button" class="btn-secondary" (click)="cancelEdit()">Cancel</button>
                    <button type="submit" class="btn-primary" [disabled]="isSaving()">
                      @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save }
                    </button>
                  </div>
                </form>
              } @else {
                <div @sectionExpand>
                  @if (profile()!.workHistory.length === 0) {
                    <p class="text-sm text-neutral-400 py-4">No work history on file.</p>
                  }
                  @for (wh of profile()!.workHistory; track $index) {
                    <div class="repeater-card-read">
                      <p class="text-sm font-medium text-neutral-900">{{ wh.position }}</p>
                      <p class="text-sm text-neutral-500">{{ wh.company }}</p>
                      <p class="text-xs text-neutral-400">
                        {{ wh.fromDate ? (wh.fromDate | date:'mediumDate') : '?' }}
                        &mdash;
                        {{ wh.toDate ? (wh.toDate | date:'mediumDate') : 'Present' }}
                      </p>
                    </div>
                  }
                </div>
              }
            </section>
          }

          <!-- Dependents Section -->
          @if (activeTab() === 6) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Dependents'"
            >
              <div class="section-header">
                <h3 class="section-title">Dependents</h3>
                @if (canEditSection('dependents')) {
                  <button
                    type="button"
                    class="edit-btn"
                    [attr.aria-label]="editingSection() === 'dependents' ? 'Cancel editing dependents' : 'Edit dependents'"
                    (click)="toggleEdit('dependents')"
                  >
                    @if (editingSection() === 'dependents') {
                      Cancel
                    } @else {
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                        <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                        <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                      </svg>
                      Edit
                    }
                  </button>
                }
              </div>
              @if (editingSection() === 'dependents') {
                <form [formGroup]="dependentsForm" (ngSubmit)="saveSection('dependents')" @sectionExpand>
                  @for (dep of dependentFormControls.controls; track $index; let i = $index) {
                    <div class="repeater-card" formArrayName="records">
                      <div class="repeater-card-header">
                        <span class="text-sm font-medium text-neutral-700">Dependent #{{ i + 1 }}</span>
                        <button type="button" class="repeater-remove-btn" (click)="removeDependentRecord(i)"
                          [attr.aria-label]="'Remove dependent ' + (i + 1)">
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                          </svg>
                        </button>
                      </div>
                      <div [formGroupName]="i" class="form-grid">
                        <div class="form-field">
                          <label class="label-notion" [for]="'dep-name-' + i">Name</label>
                          <input [id]="'dep-name-' + i" type="text" formControlName="name" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'dep-rel-' + i">Relationship</label>
                          <input [id]="'dep-rel-' + i" type="text" formControlName="relationship" class="input-notion" />
                        </div>
                        <div class="form-field">
                          <label class="label-notion" [for]="'dep-dob-' + i">Date of Birth</label>
                          <input [id]="'dep-dob-' + i" type="date" formControlName="dateOfBirth" class="input-notion" />
                        </div>
                      </div>
                    </div>
                  }
                  <button type="button" class="add-repeater-btn" (click)="addDependentRecord()">
                    + Add Dependent
                  </button>
                  <div class="form-actions">
                    <button type="button" class="btn-secondary" (click)="cancelEdit()">Cancel</button>
                    <button type="submit" class="btn-primary" [disabled]="isSaving()">
                      @if (isSaving()) { <span class="btn-spinner"></span> Saving... } @else { Save }
                    </button>
                  </div>
                </form>
              } @else {
                <div @sectionExpand>
                  @if (profile()!.dependents.length === 0) {
                    <p class="text-sm text-neutral-400 py-4">No dependents on file.</p>
                  }
                  @for (dep of profile()!.dependents; track $index) {
                    <div class="repeater-card-read">
                      <div class="data-grid">
                        <div class="data-field">
                          <dt class="data-label">Name</dt>
                          <dd class="data-value">{{ dep.name }}</dd>
                        </div>
                        <div class="data-field">
                          <dt class="data-label">Relationship</dt>
                          <dd class="data-value">{{ dep.relationship }}</dd>
                        </div>
                        <div class="data-field">
                          <dt class="data-label">Date of Birth</dt>
                          <dd class="data-value">{{ dep.dateOfBirth ? (dep.dateOfBirth | date:'mediumDate') : 'Not specified' }}</dd>
                        </div>
                      </div>
                    </div>
                  }
                </div>
              }
            </section>
          }

          <!-- Employment History Timeline (FR-6, AC-6) -->
          @if (activeTab() === 7) {
            <section
              @fadeIn
              class="card-notion"
              [attr.aria-label]="'Employment History Timeline'"
            >
              <div class="section-header">
                <h3 class="section-title">Employment History</h3>
              </div>
              @if (profile()!.employmentHistory.length === 0) {
                <p class="text-sm text-neutral-400 py-4">No employment history entries yet.</p>
              } @else {
                <div class="timeline">
                  @for (entry of profile()!.employmentHistory; track entry.id) {
                    <div class="timeline-item">
                      <div class="timeline-dot" [class.timeline-dot-status]="entry.changeType === 'status_change' || entry.changeType === 'status'"></div>
                      <div class="timeline-content">
                        <p class="text-sm font-medium text-neutral-900">
                          {{ formatChangeType(entry.changeType) }}
                        </p>
                        <div class="text-sm text-neutral-600">
                          @if (entry.changeType === 'status_change' || entry.changeType === 'status') {
                            @if (entry.previousValue) {
                              <span class="badge badge-sm" [ngClass]="getStatusBadgeClass(entry.previousValue)">{{ entry.previousValue | titlecase }}</span>
                              <span class="mx-1">&rarr;</span>
                              <span class="badge badge-sm" [ngClass]="getStatusBadgeClass(entry.newValue)">{{ entry.newValue | titlecase }}</span>
                            } @else {
                              Set to <span class="badge badge-sm" [ngClass]="getStatusBadgeClass(entry.newValue)">{{ entry.newValue | titlecase }}</span>
                            }
                          } @else {
                            @if (entry.previousValue) {
                              {{ entry.previousValue }} &rarr; {{ entry.newValue }}
                            } @else {
                              Set to {{ entry.newValue }}
                            }
                          }
                        </div>
                        @if (entry.reason) {
                          <p class="text-xs text-neutral-500 mt-0.5 italic">"{{ entry.reason }}"</p>
                        }
                        <p class="text-xs text-neutral-400 mt-0.5">
                          {{ entry.effectiveDate | date:'mediumDate' }}
                          @if (entry.changedBy) {
                            &middot; by {{ entry.changedBy }}
                          }
                        </p>
                      </div>
                    </div>
                  }
                </div>
              }
            </section>
          }

          <!-- Documents Section (US-CHR-008) -->
          @if (activeTab() === 8) {
            <section @fadeIn class="card-notion" [attr.aria-label]="'Documents'">
              <div class="section-header">
                <h3 class="section-title">Documents</h3>
              </div>
              <app-employee-documents [employeeId]="employeeId" />
            </section>
          }

          @if (activeTab() === 9) {
            <section @fadeIn class="card-notion" [attr.aria-label]="'Custom Fields'">
              <div class="section-header">
                <h3 class="section-title">Custom Fields</h3>
              </div>
              @if (profile()!.customFields && objectKeys(profile()!.customFields!).length > 0) {
                <div class="data-grid">
                  @for (key of objectKeys(profile()!.customFields!); track key) {
                    <div class="data-field">
                      <dt class="data-label">{{ key }}</dt>
                      <dd class="data-value">{{ profile()!.customFields![key] }}</dd>
                    </div>
                  }
                </div>
              } @else {
                <p class="text-sm text-neutral-400 py-4">No custom fields configured.</p>
              }
            </section>
          }
        </div>
      }

      <!-- US-CHR-011: Manager Selector Modal -->
      @if (showManagerSelector()) {
        <div
          class="modal-overlay"
          @modalOverlay
          (click)="closeManagerSelector()"
          (keydown.escape)="closeManagerSelector()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="manager-modal-title"
        >
          <div
            class="modal-card manager-selector-card"
            @modalSlide
            (click)="$event.stopPropagation()"
          >
            <div class="modal-header">
              <h3 id="manager-modal-title" class="text-lg font-semibold text-neutral-900">
                Select Reporting Manager
              </h3>
              <button
                type="button"
                class="modal-close-btn"
                (click)="closeManagerSelector()"
                aria-label="Close manager selector"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                </svg>
              </button>
            </div>
            <div class="modal-body">
              <!-- Search input -->
              <div class="relative mb-4">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                  class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400 pointer-events-none" aria-hidden="true">
                  <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd"/>
                </svg>
                <input
                  type="search"
                  class="input-notion !pl-9"
                  placeholder="Search active employees by name or email..."
                  [ngModel]="managerSearchTerm()"
                  (ngModelChange)="onManagerSearch($event)"
                  aria-label="Search for a manager"
                  autofocus
                />
              </div>

              <!-- Remove manager option -->
              @if (profile()!.reportingManagerId) {
                <button
                  type="button"
                  class="w-full text-left px-3 py-2.5 rounded-lg text-sm text-red-600 hover:bg-red-50 transition-colors mb-2"
                  (click)="assignManagerToEmployee(null)"
                >
                  Remove current manager
                </button>
              }

              <!-- Search results -->
              @if (isSearchingManagers()) {
                <div class="flex items-center justify-center py-6">
                  <div class="btn-spinner border-brand-300 border-t-brand-600 w-5 h-5"></div>
                  <span class="ml-2 text-sm text-neutral-500">Searching...</span>
                </div>
              } @else if (managerSearchResults().length > 0) {
                <div class="max-h-64 overflow-y-auto -mx-1">
                  @for (emp of managerSearchResults(); track emp.employeeId) {
                    <button
                      type="button"
                      class="w-full text-left flex items-center gap-3 px-3 py-2.5 rounded-lg hover:bg-neutral-50 transition-colors"
                      [class.opacity-50]="emp.employeeId === employeeId"
                      [class.pointer-events-none]="emp.employeeId === employeeId"
                      [disabled]="emp.employeeId === employeeId"
                      (click)="assignManagerToEmployee(emp.employeeId)"
                      [attr.aria-label]="'Assign ' + emp.firstName + ' ' + emp.lastName + ' as manager'"
                    >
                      <div class="manager-avatar">
                        @if (emp.profilePhotoUrl) {
                          <img [src]="emp.profilePhotoUrl" [alt]="emp.firstName + ' ' + emp.lastName" class="w-full h-full object-cover" />
                        } @else {
                          <span class="text-xs">{{ getInitialsFor(emp.firstName, emp.lastName) }}</span>
                        }
                      </div>
                      <div class="min-w-0 flex-1">
                        <p class="text-sm font-medium text-neutral-900 truncate">
                          {{ emp.firstName }} {{ emp.lastName }}
                        </p>
                        <p class="text-xs text-neutral-500 truncate">
                          {{ emp.jobTitleName || 'No title' }}
                          @if (emp.departmentName) {
                            &middot; {{ emp.departmentName }}
                          }
                        </p>
                      </div>
                      @if (emp.employeeId === profile()!.reportingManagerId) {
                        <span class="text-xs text-brand-600 font-medium">Current</span>
                      }
                      @if (emp.employeeId === employeeId) {
                        <span class="text-xs text-neutral-400">Self</span>
                      }
                    </button>
                  }
                </div>
              } @else if (managerSearchTerm().length >= 2) {
                <p class="text-sm text-neutral-400 text-center py-6">No active employees found.</p>
              } @else {
                <p class="text-sm text-neutral-400 text-center py-6">Type at least 2 characters to search.</p>
              }

              <!-- Assigning spinner -->
              @if (isAssigningManager()) {
                <div class="flex items-center justify-center py-4 mt-2 border-t border-neutral-100">
                  <div class="btn-spinner border-brand-300 border-t-brand-600 w-5 h-5"></div>
                  <span class="ml-2 text-sm text-neutral-500">Assigning manager...</span>
                </div>
              }
            </div>
          </div>
        </div>
      }

      <!-- US-CHR-009: Status Change Modal -->
      @if (showStatusModal()) {
        <div
          class="modal-overlay"
          @modalOverlay
          (click)="closeStatusModal()"
          (keydown.escape)="closeStatusModal()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="status-modal-title"
        >
          <div
            class="modal-card"
            @modalSlide
            (click)="$event.stopPropagation()"
          >
            <!-- Step 1: Status change form -->
            @if (!showConfirmation()) {
              <div class="modal-header">
                <h3 id="status-modal-title" class="text-lg font-semibold text-neutral-900">
                  Change Employee Status
                </h3>
                <button
                  type="button"
                  class="modal-close-btn"
                  (click)="closeStatusModal()"
                  aria-label="Close status change dialog"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
                    <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                  </svg>
                </button>
              </div>
              <div class="modal-body">
                @if (isLoadingTransitions()) {
                  <div class="flex items-center justify-center py-8">
                    <div class="btn-spinner border-brand-300 border-t-brand-600 w-6 h-6"></div>
                    <span class="ml-3 text-sm text-neutral-500">Loading available transitions...</span>
                  </div>
                } @else if (validTransitions().length === 0) {
                  <div class="text-center py-8">
                    <p class="text-sm text-neutral-500">No status transitions are available for the current status.</p>
                    <button type="button" class="btn-secondary mt-4" (click)="closeStatusModal()">Close</button>
                  </div>
                } @else {
                  <form [formGroup]="statusChangeForm" (ngSubmit)="proceedToConfirmation()">
                    <div class="space-y-4">
                      <!-- Current status display -->
                      <div>
                        <p class="text-sm text-neutral-500 mb-1">Current Status</p>
                        <span
                          class="badge"
                          [ngClass]="getStatusBadgeClass(profile()!.status)"
                        >
                          {{ profile()!.status | titlecase }}
                        </span>
                      </div>

                      <!-- New Status dropdown (FR-3, AC-1) -->
                      <div class="form-field">
                        <label class="label-notion" for="sc-newStatus">New Status <span class="text-red-500">*</span></label>
                        <select
                          id="sc-newStatus"
                          formControlName="newStatus"
                          class="input-notion select-input"
                          aria-required="true"
                        >
                          <option value="">Select a status...</option>
                          @for (t of validTransitions(); track t.targetStatus) {
                            <option [value]="t.targetStatus">{{ t.label }}</option>
                          }
                        </select>
                        @if (statusChangeForm.get('newStatus')?.touched && statusChangeForm.get('newStatus')?.hasError('required')) {
                          <p class="text-xs text-red-500 mt-1" role="alert">New status is required.</p>
                        }
                      </div>

                      <!-- Effective Date (FR-3) -->
                      <div class="form-field">
                        <label class="label-notion" for="sc-effectiveDate">Effective Date <span class="text-red-500">*</span></label>
                        <input
                          id="sc-effectiveDate"
                          type="date"
                          formControlName="effectiveDate"
                          class="input-notion"
                          aria-required="true"
                        />
                        @if (statusChangeForm.get('effectiveDate')?.touched && statusChangeForm.get('effectiveDate')?.hasError('required')) {
                          <p class="text-xs text-red-500 mt-1" role="alert">Effective date is required.</p>
                        }
                      </div>

                      <!-- Reason textarea (FR-3) -->
                      <div class="form-field">
                        <label class="label-notion" for="sc-reason">Reason <span class="text-red-500">*</span></label>
                        <textarea
                          id="sc-reason"
                          formControlName="reason"
                          class="input-notion"
                          rows="3"
                          placeholder="Provide a reason for this status change..."
                          aria-required="true"
                        ></textarea>
                        @if (statusChangeForm.get('reason')?.touched && statusChangeForm.get('reason')?.hasError('required')) {
                          <p class="text-xs text-red-500 mt-1" role="alert">Reason is required.</p>
                        }
                      </div>
                    </div>
                    <div class="form-actions">
                      <button type="button" class="btn-secondary" (click)="closeStatusModal()">Cancel</button>
                      <button
                        type="submit"
                        class="btn-primary"
                        [disabled]="statusChangeForm.invalid"
                      >
                        Continue
                      </button>
                    </div>
                  </form>
                }
              </div>
            }

            <!-- Step 2: Confirmation (UI/UX notes) -->
            @if (showConfirmation()) {
              <div class="modal-header">
                <h3 id="status-modal-title" class="text-lg font-semibold text-neutral-900">
                  Confirm Status Change
                </h3>
              </div>
              <div class="modal-body">
                <div class="rounded-lg bg-amber-50 border border-amber-200 p-4 mb-4">
                  <p class="text-sm text-amber-800 font-medium mb-2">
                    Are you sure you want to change {{ profile()!.firstName }} {{ profile()!.lastName }}'s status
                    from <strong>{{ profile()!.status | titlecase }}</strong>
                    to <strong>{{ statusChangeForm.value.newStatus | titlecase }}</strong>?
                  </p>
                  @if (selectedTransitionSideEffects().length > 0) {
                    <p class="text-sm text-amber-700 mb-1">This will:</p>
                    <ul class="list-disc list-inside text-sm text-amber-700 space-y-0.5">
                      @for (effect of selectedTransitionSideEffects(); track effect) {
                        <li>{{ effect }}</li>
                      }
                    </ul>
                  }
                </div>
                <div class="text-sm text-neutral-600 space-y-1 mb-4">
                  <p><span class="font-medium">Effective Date:</span> {{ statusChangeForm.value.effectiveDate }}</p>
                  <p><span class="font-medium">Reason:</span> {{ statusChangeForm.value.reason }}</p>
                </div>
                <div class="form-actions">
                  <button
                    type="button"
                    class="btn-secondary"
                    (click)="backToForm()"
                    [disabled]="isSubmittingStatus()"
                  >
                    Back
                  </button>
                  <button
                    type="button"
                    class="btn-primary btn-danger"
                    (click)="submitStatusChange()"
                    [disabled]="isSubmittingStatus()"
                  >
                    @if (isSubmittingStatus()) {
                      <span class="btn-spinner"></span> Submitting...
                    } @else {
                      Confirm Change
                    }
                  </button>
                </div>
              </div>
            }
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    /* ─── Back link ─────────────────────────── */
    .back-link {
      @apply w-9 h-9 rounded-lg flex items-center justify-center
        text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100
        transition-colors duration-150;
    }

    /* ─── Skeleton ──────────────────────────── */
    .skeleton-circle {
      @apply rounded-full bg-neutral-200;
      animation: shimmer 1.5s ease-in-out infinite;
    }
    .skeleton-line {
      @apply rounded bg-neutral-200;
      animation: shimmer 1.5s ease-in-out infinite;
    }
    @keyframes shimmer {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.4; }
    }

    /* ─── Profile header ───────────────────── */
    .profile-header-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-6;
    }
    .avatar-lg {
      @apply w-24 h-24 rounded-full bg-brand-100 text-brand-700
        flex items-center justify-center overflow-hidden flex-shrink-0;
    }
    @media (max-width: 639px) {
      .avatar-lg { @apply w-16 h-16; }
      .avatar-lg span { @apply text-lg; }
    }

    /* ─── Badges ────────────────────────────── */
    .badge {
      @apply text-xs font-medium px-2.5 py-0.5 rounded-full;
    }
    .badge-neutral {
      @apply bg-neutral-100 text-neutral-600;
    }
    .badge-brand {
      @apply bg-brand-50 text-brand-700;
    }
    .badge-active {
      @apply bg-green-50 text-green-700;
    }
    .badge-probation {
      @apply bg-amber-50 text-amber-700;
    }
    .badge-terminated {
      @apply bg-red-50 text-red-700;
    }
    .badge-suspended {
      @apply bg-gray-100 text-gray-800;
    }
    .badge-inactive {
      @apply bg-slate-100 text-slate-800;
    }
    .status-badge-animated {
      transition: background-color 200ms ease, color 200ms ease;
    }

    /* ─── Change Status button ─────────────── */
    .change-status-btn {
      @apply inline-flex items-center gap-1 text-xs font-medium
        text-brand-600 hover:text-brand-700 transition-colors duration-150
        px-2 py-1 rounded-md hover:bg-brand-50 cursor-pointer;
    }

    /* ─── Modal ────────────────────────────── */
    .modal-overlay {
      @apply fixed inset-0 z-50 flex items-center justify-center
        bg-black/40 backdrop-blur-sm p-4;
    }
    @media (max-width: 639px) {
      .modal-overlay {
        @apply items-end p-0;
      }
    }
    .modal-card {
      @apply bg-white rounded-xl shadow-xl max-w-md w-full max-h-[90vh] overflow-y-auto;
    }
    @media (max-width: 639px) {
      .modal-card {
        @apply rounded-b-none rounded-t-2xl max-w-full;
      }
    }
    .modal-header {
      @apply flex items-center justify-between px-6 pt-5 pb-3;
    }
    .modal-close-btn {
      @apply w-8 h-8 rounded-lg flex items-center justify-center
        text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100
        transition-colors duration-150;
    }
    .modal-body {
      @apply px-6 pb-6;
    }
    .btn-danger {
      @apply bg-red-600 hover:bg-red-700;
    }

    /* ─── Tab navigation ───────────────────── */
    .tab-btn {
      @apply px-3 py-2 text-sm font-medium text-neutral-500 rounded-lg
        transition-colors duration-150 whitespace-nowrap
        hover:text-neutral-700 hover:bg-neutral-50;
    }
    .tab-btn-active {
      @apply text-brand-700 bg-brand-50;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }

    /* ─── Section card ─────────────────────── */
    .section-header {
      @apply flex items-center justify-between mb-4 pb-3 border-b border-neutral-100;
    }
    .section-title {
      @apply text-base font-semibold text-neutral-900;
    }
    .edit-btn {
      @apply inline-flex items-center gap-1.5 text-sm font-medium
        text-brand-600 hover:text-brand-700 transition-colors duration-150
        px-2.5 py-1.5 rounded-lg hover:bg-brand-50;
    }

    /* ─── Data display grid ────────────────── */
    .data-grid {
      @apply grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-4;
    }
    .data-field {
      @apply space-y-0.5;
    }
    .data-label {
      @apply text-xs font-medium text-neutral-400 uppercase tracking-wider;
    }
    .data-value {
      @apply text-sm text-neutral-900;
    }

    /* ─── Form edit grid ───────────────────── */
    .form-grid {
      @apply grid grid-cols-1 md:grid-cols-2 gap-4;
    }
    .form-field {
      @apply space-y-1.5;
    }
    .col-span-full {
      @apply md:col-span-2;
    }
    .form-actions {
      @apply flex items-center justify-end gap-3 pt-4 mt-4 border-t border-neutral-100;
    }
    .field-hint {
      @apply text-xs text-neutral-400;
    }

    /* ─── Repeater cards ───────────────────── */
    .repeater-card {
      @apply rounded-lg border border-neutral-100 bg-neutral-50/50 p-4 mb-3;
    }
    .repeater-card-read {
      @apply rounded-lg border border-neutral-50 bg-neutral-50/30 p-4 mb-2;
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
        mt-1 mb-2 px-2 py-1 rounded-lg hover:bg-brand-50;
    }

    /* ─── Buttons ──────────────────────────── */
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

    /* ─── Timeline (FR-6, AC-6) ────────────── */
    .timeline {
      @apply relative pl-6;
    }
    .timeline::before {
      content: '';
      @apply absolute left-2 top-2 bottom-2 w-px bg-neutral-200;
    }
    .timeline-item {
      @apply relative pb-6 last:pb-0;
    }
    .timeline-dot {
      @apply absolute -left-6 top-1 w-4 h-4 rounded-full bg-brand-100 border-2 border-brand-500;
    }
    .timeline-dot-status {
      @apply bg-amber-100 border-amber-500;
    }
    .timeline-content {
      @apply pl-2;
    }
    .badge-sm {
      @apply text-[10px] font-medium px-1.5 py-0.5 rounded-full;
    }

    .manager-mini-card { display:flex; align-items:center; gap:0.625rem; border-radius:0.5rem; border:1px solid #e5e5e5; background:#fafafa80; padding:0.5rem 0.75rem; transition:all 200ms; }
    .manager-mini-card:hover { box-shadow:0 1px 2px rgb(0 0 0/0.05); border-color:#d4d4d4; }
    .manager-avatar { width:2rem; height:2rem; border-radius:9999px; display:flex; align-items:center; justify-content:center; overflow:hidden; flex-shrink:0; font-size:0.875rem; font-weight:600; }
    .chain-node { display:inline-flex; align-items:center; font-size:0.75rem; font-weight:500; padding:0.25rem 0.5rem; border-radius:0.375rem; transition:all 150ms; cursor:pointer; }
    .chain-node-current { cursor:default; }
    .manager-selector-card { max-width:32rem; }
    @media(max-width:639px){ .manager-selector-card{ max-width:100%; max-height:90vh; } }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `],
})
export class EmployeeProfileComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);
  private readonly employeeService = inject(EmployeeService);
  private readonly authService = inject(AuthService);

  private readonly destroy$ = new Subject<void>();

  // ─── Constants ─────────────────────────────────────────────
  readonly genderOptions: EmployeeGender[] = GENDER_OPTIONS;
  readonly objectKeys = Object.keys;

  readonly sectionList: { key: ProfileSection | 'employment-history' | 'documents' | 'custom-fields'; label: string }[] = [
    { key: 'personal-info', label: 'Personal Info' },
    { key: 'contact', label: 'Contact' },
    { key: 'emergency-contacts', label: 'Emergency Contacts' },
    { key: 'employment', label: 'Employment' },
    { key: 'education', label: 'Education' },
    { key: 'work-history', label: 'Work History' },
    { key: 'dependents', label: 'Dependents' },
    { key: 'employment-history', label: 'Timeline' },
    { key: 'documents', label: 'Documents' },
    { key: 'custom-fields', label: 'Custom Fields' },
  ];

  // ─── Signals ───────────────────────────────────────────────
  readonly profile = signal<IEmployeeProfile | null>(null);
  readonly isLoading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly isSaving = signal(false);
  readonly editingSection = signal<ProfileSection | null>(null);
  readonly activeTab = signal(0);

  // ─── US-CHR-009: Status management signals ────────────────
  readonly showStatusModal = signal(false);
  readonly showConfirmation = signal(false);
  readonly isLoadingTransitions = signal(false);
  readonly isSubmittingStatus = signal(false);
  readonly validTransitions = signal<IStatusTransition[]>([]);
  /** Get side effects for the currently selected new-status in the form */
  selectedTransitionSideEffects(): string[] {
    const selected = this.statusChangeForm?.value?.newStatus;
    if (!selected) return [];
    const transition = this.validTransitions().find(t => t.targetStatus === selected);
    return transition?.sideEffects ?? [];
  }

  // ─── US-CHR-011: Manager assignment signals ────────────
  readonly showManagerSelector = signal(false);
  readonly managerSearchTerm = signal('');
  readonly managerSearchResults = signal<IEmployee[]>([]);
  readonly isSearchingManagers = signal(false);
  readonly isAssigningManager = signal(false);
  private managerSearchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Computed reporting chain from profile data */
  readonly reportingChain = computed<IReportingChainNode[]>(() => {
    const p = this.profile();
    if (!p) return [];
    return p.reportingChain ?? [];
  });

  /** Resolved viewer role for field-level permissions */
  readonly viewerRole = computed<ProfileViewerRole>(() => {
    if (this.authService.hasRole('HR Officer') || this.authService.hasRole('Tenant Admin')) {
      return 'hr_officer';
    }
    if (this.authService.hasRole('Manager')) {
      return 'manager';
    }
    return 'employee';
  });

  // ─── Forms (created lazily per section) ────────────────────
  personalInfoForm!: FormGroup;
  contactForm!: FormGroup;
  emergencyContactsForm!: FormGroup;
  employmentForm!: FormGroup;
  educationForm!: FormGroup;
  workHistoryForm!: FormGroup;
  dependentsForm!: FormGroup;
  statusChangeForm!: FormGroup;

  employeeId = '';

  get emergencyContactControls(): FormArray {
    return this.emergencyContactsForm.get('contacts') as FormArray;
  }

  get educationFormControls(): FormArray {
    return this.educationForm.get('records') as FormArray;
  }

  get workHistoryFormControls(): FormArray {
    return this.workHistoryForm.get('records') as FormArray;
  }

  get dependentFormControls(): FormArray {
    return this.dependentsForm.get('records') as FormArray;
  }

  // ─── Lifecycle ─────────────────────────────────────────────

  ngOnInit(): void {
    this.employeeId = this.route.snapshot.paramMap.get('id') ?? '';
    this.initForms();
    this.loadProfile();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Public methods ────────────────────────────────────────

  loadProfile(): void {
    if (!this.employeeId) {
      this.loadError.set('No employee ID provided.');
      this.isLoading.set(false);
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);

    this.employeeService
      .getEmployeeProfile(this.employeeId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (profile) => {
          this.profile.set(profile);
          this.isLoading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          if (err.status === 404) {
            this.loadError.set('Employee not found.');
          } else if (err.status === 403) {
            this.loadError.set('You do not have permission to view this profile.');
          } else {
            this.loadError.set('Failed to load employee profile. Please try again.');
          }
        },
      });
  }

  goBack(): void {
    this.router.navigate(['/employees']);
  }

  getInitials(): string {
    const p = this.profile();
    if (!p) return '';
    return ((p.firstName?.[0] ?? '') + (p.lastName?.[0] ?? '')).toUpperCase();
  }

  getStatusBadgeClass(status: string): string {
    switch (status) {
      case 'active': return 'badge-active';
      case 'probation': return 'badge-probation';
      case 'terminated': return 'badge-terminated';
      case 'suspended': return 'badge-suspended';
      case 'inactive': return 'badge-inactive';
      default: return 'badge-neutral';
    }
  }

  // ─── US-CHR-009: Status change (BR-2, AC-1, AC-2) ─────────

  /** Only HR Officer and Tenant Admin can change status (BR-2) */
  canChangeStatus(): boolean {
    return this.viewerRole() === 'hr_officer';
  }

  openStatusChangeModal(): void {
    this.showStatusModal.set(true);
    this.showConfirmation.set(false);
    this.isLoadingTransitions.set(true);
    this.validTransitions.set([]);
    this.statusChangeForm.reset({ newStatus: '', effectiveDate: '', reason: '' });

    this.employeeService
      .getValidTransitions(this.employeeId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (transitions) => {
          this.validTransitions.set(transitions);
          this.isLoadingTransitions.set(false);
        },
        error: () => {
          this.isLoadingTransitions.set(false);
          this.toastr.error('Failed to load available status transitions.');
        },
      });
  }

  closeStatusModal(): void {
    this.showStatusModal.set(false);
    this.showConfirmation.set(false);
  }

  proceedToConfirmation(): void {
    if (this.statusChangeForm.invalid) {
      this.statusChangeForm.markAllAsTouched();
      return;
    }
    this.showConfirmation.set(true);
  }

  backToForm(): void {
    this.showConfirmation.set(false);
  }

  submitStatusChange(): void {
    const p = this.profile();
    if (!p) return;

    this.isSubmittingStatus.set(true);

    const request: IChangeStatusRequest = {
      newStatus: this.statusChangeForm.value.newStatus,
      effectiveDate: this.statusChangeForm.value.effectiveDate,
      reason: this.statusChangeForm.value.reason,
    };

    // Generate UUID for idempotency (NFR-3)
    const idempotencyKey = crypto.randomUUID();

    this.employeeService
      .changeStatus(this.employeeId, request, idempotencyKey)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.isSubmittingStatus.set(false);
          this.profile.set(response.profile);
          this.closeStatusModal();
          this.toastr.success(
            `Status changed to ${request.newStatus} successfully.`
          );
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmittingStatus.set(false);
          if (err.status === 400) {
            // AC-5: invalid transition — show backend error message
            const body = EmployeeService.parseError(err);
            this.toastr.error(
              body?.message ?? 'Invalid status transition.'
            );
          } else {
            this.toastr.error('Failed to change employee status. Please try again.');
          }
        },
      });
  }

  // ─── US-CHR-011: Manager assignment methods ────────────

  getManagerInitials(): string {
    const p = this.profile();
    if (!p?.reportingManagerName) return '';
    const parts = p.reportingManagerName.split(' ');
    return getInitialsFromName(parts[0] || '', parts[1] || '');
  }

  getInitialsFor(firstName: string, lastName: string): string {
    return getInitialsFromName(firstName, lastName);
  }

  navigateToManager(): void {
    const managerId = this.profile()?.reportingManagerId;
    if (managerId) {
      this.router.navigate(['/employees', managerId]);
    }
  }

  navigateToEmployee(employeeId: string): void {
    this.router.navigate(['/employees', employeeId]);
  }

  openManagerSelector(): void {
    this.showManagerSelector.set(true);
    this.managerSearchTerm.set('');
    this.managerSearchResults.set([]);
    this.isSearchingManagers.set(false);
    this.isAssigningManager.set(false);
  }

  closeManagerSelector(): void {
    this.showManagerSelector.set(false);
    this.managerSearchTerm.set('');
    this.managerSearchResults.set([]);
    if (this.managerSearchTimer) {
      clearTimeout(this.managerSearchTimer);
      this.managerSearchTimer = null;
    }
  }

  onManagerSearch(term: string): void {
    this.managerSearchTerm.set(term);
    if (this.managerSearchTimer) {
      clearTimeout(this.managerSearchTimer);
    }
    if (term.length < 2) {
      this.managerSearchResults.set([]);
      this.isSearchingManagers.set(false);
      return;
    }
    this.isSearchingManagers.set(true);
    this.managerSearchTimer = setTimeout(() => {
      this.employeeService
        .searchActiveEmployees(term)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (response) => {
            this.managerSearchResults.set(response.data);
            this.isSearchingManagers.set(false);
          },
          error: () => {
            this.managerSearchResults.set([]);
            this.isSearchingManagers.set(false);
          },
        });
    }, 300);
  }

  assignManagerToEmployee(managerId: string | null): void {
    this.isAssigningManager.set(true);
    this.employeeService
      .assignManager(this.employeeId, managerId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.isAssigningManager.set(false);
          this.profile.set(response.profile);
          this.closeManagerSelector();
          if (managerId) {
            this.toastr.success('Reporting manager assigned successfully.');
          } else {
            this.toastr.success('Reporting manager removed successfully.');
          }
        },
        error: (err: HttpErrorResponse) => {
          this.isAssigningManager.set(false);
          if (err.status === 400) {
            // AC-3: Circular chain detected — show backend error message
            const body = EmployeeService.parseError(err);
            this.toastr.error(
              body?.message ?? 'Failed to assign manager. A circular reporting chain was detected.'
            );
          } else {
            this.toastr.error('Failed to assign manager. Please try again.');
          }
        },
      });
  }

  formatAddress(): string {
    const p = this.profile();
    if (!p) return '';
    return [p.address, p.city, p.state, p.postalCode, p.country]
      .filter(Boolean)
      .join(', ');
  }

  formatChangeType(type: string): string {
    switch (type) {
      case 'department': return 'Department Change';
      case 'job_title': return 'Job Title Change';
      case 'status': return 'Status Change';
      case 'status_change': return 'Status Change';
      case 'reporting_manager': return 'Reporting Manager Change';
      default: return type;
    }
  }

  /** Check if current viewer can edit a section (AC-4, AC-5, FR-3) */
  canEditSection(section: ProfileSection): boolean {
    return isSectionEditable(section, this.viewerRole());
  }

  toggleEdit(section: ProfileSection): void {
    if (this.editingSection() === section) {
      this.cancelEdit();
    } else {
      this.editingSection.set(section);
      this.populateForm(section);
    }
  }

  cancelEdit(): void {
    this.editingSection.set(null);
  }

  /** Save a section via PATCH with xmin concurrency (AC-2, AC-3) */
  saveSection(section: ProfileSection): void {
    const p = this.profile();
    if (!p) return;

    const form = this.getFormForSection(section);
    if (!form || form.invalid) {
      form?.markAllAsTouched();
      return;
    }

    this.isSaving.set(true);

    const request: IUpdateSectionRequest = {
      xmin: p.xmin,
      data: form.value,
    };

    this.employeeService
      .updateProfileSection(this.employeeId, section, request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.isSaving.set(false);
          this.profile.set(response.profile);
          this.editingSection.set(null);
          this.toastr.success('Changes saved successfully.');
        },
        error: (err: HttpErrorResponse) => {
          this.isSaving.set(false);
          this.handleSaveError(err);
        },
      });
  }

  // ─── Emergency contact repeater ────────────────────────────

  addEmergencyContact(): void {
    this.emergencyContactControls.push(
      this.fb.group({ name: [''], relationship: [''], phone: [''] })
    );
  }

  removeEmergencyContact(index: number): void {
    this.emergencyContactControls.removeAt(index);
  }

  // ─── Education repeater ────────────────────────────────────

  addEducationRecord(): void {
    this.educationFormControls.push(
      this.fb.group({ institution: [''], degree: [''], endYear: [''] })
    );
  }

  removeEducationRecord(index: number): void {
    this.educationFormControls.removeAt(index);
  }

  // ─── Work history repeater ─────────────────────────────────

  addWorkHistoryRecord(): void {
    this.workHistoryFormControls.push(
      this.fb.group({ company: [''], position: [''], fromDate: [''], toDate: [''] })
    );
  }

  removeWorkHistoryRecord(index: number): void {
    this.workHistoryFormControls.removeAt(index);
  }

  // ─── Dependent repeater ────────────────────────────────────

  addDependentRecord(): void {
    this.dependentFormControls.push(
      this.fb.group({ name: [''], relationship: [''], dateOfBirth: [''] })
    );
  }

  removeDependentRecord(index: number): void {
    this.dependentFormControls.removeAt(index);
  }

  // ─── Private ───────────────────────────────────────────────

  private initForms(): void {
    this.personalInfoForm = this.fb.group({
      firstName: ['', [Validators.required, Validators.maxLength(100)]],
      lastName: ['', [Validators.required, Validators.maxLength(100)]],
      dateOfBirth: [null],
      gender: [null as EmployeeGender | null],
    });

    this.contactForm = this.fb.group({
      personalEmail: [''],
      phone: [''],
      address: [''],
      city: [''],
      state: [''],
      postalCode: [''],
      country: [''],
    });

    this.emergencyContactsForm = this.fb.group({
      contacts: this.fb.array([]),
    });

    this.employmentForm = this.fb.group({
      departmentName: [''],
      jobTitleName: [''],
      employmentType: [''],
      status: [''],
      dateOfJoining: [''],
    });

    this.educationForm = this.fb.group({
      records: this.fb.array([]),
    });

    this.workHistoryForm = this.fb.group({
      records: this.fb.array([]),
    });

    this.dependentsForm = this.fb.group({
      records: this.fb.array([]),
    });

    // US-CHR-009: Status change form
    this.statusChangeForm = this.fb.group({
      newStatus: ['', [Validators.required]],
      effectiveDate: ['', [Validators.required]],
      reason: ['', [Validators.required]],
    });
  }

  private populateForm(section: ProfileSection): void {
    const p = this.profile();
    if (!p) return;

    switch (section) {
      case 'personal-info':
        this.personalInfoForm.patchValue({
          firstName: p.firstName,
          lastName: p.lastName,
          dateOfBirth: p.dateOfBirth,
          gender: p.gender,
        });
        break;

      case 'contact':
        this.contactForm.patchValue({
          personalEmail: p.personalEmail ?? '',
          phone: p.phone ?? '',
          address: p.address ?? '',
          city: p.city ?? '',
          state: p.state ?? '',
          postalCode: p.postalCode ?? '',
          country: p.country ?? '',
        });
        break;

      case 'emergency-contacts':
        this.emergencyContactControls.clear();
        for (const ec of p.emergencyContacts) {
          this.emergencyContactControls.push(
            this.fb.group({
              name: [ec.name],
              relationship: [ec.relationship],
              phone: [ec.phone],
            })
          );
        }
        break;

      case 'employment':
        this.employmentForm.patchValue({
          departmentName: p.departmentName ?? '',
          jobTitleName: p.jobTitleName ?? '',
          employmentType: p.employmentType,
          status: p.status,
          dateOfJoining: p.dateOfJoining,
        });
        break;

      case 'education':
        this.educationFormControls.clear();
        for (const edu of p.education) {
          this.educationFormControls.push(
            this.fb.group({
              institution: [edu.institution],
              degree: [edu.degree],
              endYear: [edu.endYear ?? ''],
            })
          );
        }
        break;

      case 'work-history':
        this.workHistoryFormControls.clear();
        for (const wh of p.workHistory) {
          this.workHistoryFormControls.push(
            this.fb.group({
              company: [wh.company],
              position: [wh.position],
              fromDate: [wh.fromDate ?? ''],
              toDate: [wh.toDate ?? ''],
            })
          );
        }
        break;

      case 'dependents':
        this.dependentFormControls.clear();
        for (const dep of p.dependents) {
          this.dependentFormControls.push(
            this.fb.group({
              name: [dep.name],
              relationship: [dep.relationship],
              dateOfBirth: [dep.dateOfBirth ?? ''],
            })
          );
        }
        break;
    }
  }

  private getFormForSection(section: ProfileSection): FormGroup | null {
    switch (section) {
      case 'personal-info': return this.personalInfoForm;
      case 'contact': return this.contactForm;
      case 'emergency-contacts': return this.emergencyContactsForm;
      case 'employment': return this.employmentForm;
      case 'education': return this.educationForm;
      case 'work-history': return this.workHistoryForm;
      case 'dependents': return this.dependentsForm;
      default: return null;
    }
  }

  /** Handle save errors: 409 conflict, 403 forbidden, generic (AC-3, AC-5) */
  private handleSaveError(err: HttpErrorResponse): void {
    if (err.status === 409) {
      // AC-3: Optimistic concurrency conflict
      this.toastr.error(
        'This record was modified by another user. Please refresh and try again.'
      );
    } else if (err.status === 403) {
      // AC-5: Employee tried to edit restricted fields
      this.toastr.error('You do not have permission to edit these fields.');
    } else {
      const body = EmployeeService.parseError(err);
      this.toastr.error(body?.message ?? 'Failed to save changes. Please try again.');
    }
  }
}
