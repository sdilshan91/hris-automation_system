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
  Validators,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { CustomFieldService } from '../../services/custom-field.service';
import {
  ICustomFieldDefinition,
  ICustomFieldPlanLimits,
  ICreateCustomFieldRequest,
  CUSTOM_FIELD_TYPES,
  CustomFieldType,
  slugifyFieldKey,
  fieldTypeHasOptions,
  fieldTypeToInputType,
} from '../../models/custom-field.models';

/**
 * US-CHR-012 AC-1: Custom Fields management page.
 *
 * Features:
 *   - Card-based list of defined fields with name, type, required, usage count, active toggle
 *   - Reorder controls (up/down arrow buttons)
 *   - Plan limit progress bar ("3 of N custom fields used")
 *   - "Add Custom Field" slide-over modal
 *   - Deactivate/reactivate toggle (AC-5)
 *   - Live preview of field rendering (UI/UX notes)
 */
@Component({
  selector: 'app-custom-field-list',
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
    trigger('modalOverlay', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
    trigger('slideOver', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ opacity: 1, transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, transform: 'translateX(100%)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Page header -->
      <div class="flex items-center justify-between mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Custom Fields
          </h1>
          <p class="mt-0.5 text-sm text-neutral-500">
            Define custom data fields for employee records.
          </p>
        </div>
        <button
          type="button"
          class="btn-primary"
          (click)="openAddModal()"
          [disabled]="isAtPlanLimit()"
          [attr.aria-label]="isAtPlanLimit() ? 'Plan limit reached, cannot add more custom fields' : 'Add custom field'"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"/>
          </svg>
          Add Custom Field
        </button>
      </div>

      <!-- Plan limit progress bar (AC-4) -->
      @if (planLimits()) {
        <div class="plan-limit-card mb-6" @fadeIn>
          <div class="flex items-center justify-between mb-2">
            <span class="text-sm font-medium text-neutral-700">
              {{ planLimits()!.currentCount }} of {{ planLimits()!.maxAllowed ?? 'unlimited' }} custom fields used
            </span>
            @if (planLimits()!.maxAllowed !== null) {
              <span class="text-xs text-neutral-400">
                {{ planLimits()!.maxAllowed! - planLimits()!.currentCount }} remaining
              </span>
            }
          </div>
          @if (planLimits()!.maxAllowed !== null) {
            <div class="progress-bar-bg">
              <div
                class="progress-bar-fill"
                [class.progress-bar-warn]="planLimitPercent() >= 80"
                [class.progress-bar-full]="planLimitPercent() >= 100"
                [style.width.%]="planLimitPercent()"
                role="progressbar"
                [attr.aria-valuenow]="planLimits()!.currentCount"
                [attr.aria-valuemin]="0"
                [attr.aria-valuemax]="planLimits()!.maxAllowed"
                aria-label="Custom field plan usage"
              ></div>
            </div>
          }
        </div>
      }

      <!-- Plan limit reached banner -->
      @if (planLimitError()) {
        <div class="plan-limit-banner mb-4" role="alert" @fadeIn>
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5 flex-shrink-0" aria-hidden="true">
            <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd"/>
          </svg>
          <span>{{ planLimitError() }}</span>
        </div>
      }

      <!-- Loading state -->
      @if (isLoading()) {
        <div class="flex flex-col items-center justify-center py-16 gap-3" aria-live="polite">
          <div class="loading-spinner"></div>
          <span class="text-sm text-neutral-500">Loading custom fields...</span>
        </div>
      }

      <!-- Empty state -->
      @if (!isLoading() && definitions().length === 0) {
        <div class="card-notion text-center py-16" @fadeIn>
          <div class="w-16 h-16 rounded-2xl bg-brand-50 flex items-center justify-center mx-auto mb-4">
            <span class="text-2xl text-brand-600">+</span>
          </div>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No custom fields yet</h3>
          <p class="text-sm text-neutral-500 mb-4">
            Create custom fields to capture tenant-specific data points.
          </p>
          <button type="button" class="btn-primary" (click)="openAddModal()">
            Add Custom Field
          </button>
        </div>
      }

      <!-- Field definitions list -->
      @if (!isLoading() && definitions().length > 0) {
        <div class="space-y-3" @fadeIn>
          @for (field of definitions(); track field.customFieldId; let i = $index; let first = $first; let last = $last) {
            <div
              class="field-card"
              [class.field-card-inactive]="!field.isActive"
            >
              <!-- Reorder controls (mobile: up/down arrows; NFR-4, FR-8) -->
              <div class="reorder-controls">
                <button
                  type="button"
                  class="reorder-btn"
                  [disabled]="first"
                  (click)="moveField(i, -1)"
                  [attr.aria-label]="'Move ' + field.fieldName + ' up'"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                    <path fill-rule="evenodd" d="M11.78 9.78a.75.75 0 0 1-1.06 0L8 7.06 5.28 9.78a.75.75 0 0 1-1.06-1.06l3.25-3.25a.75.75 0 0 1 1.06 0l3.25 3.25a.75.75 0 0 1 0 1.06Z" clip-rule="evenodd"/>
                  </svg>
                </button>
                <button
                  type="button"
                  class="reorder-btn"
                  [disabled]="last"
                  (click)="moveField(i, 1)"
                  [attr.aria-label]="'Move ' + field.fieldName + ' down'"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                    <path fill-rule="evenodd" d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd"/>
                  </svg>
                </button>
              </div>

              <!-- Field info -->
              <div class="field-info">
                <div class="flex items-center gap-2 mb-1">
                  <span class="field-type-badge">{{ getTypeLabel(field.fieldType) }}</span>
                  <h3 class="text-sm font-semibold text-neutral-900">{{ field.fieldName }}</h3>
                  @if (field.isRequired) {
                    <span class="required-badge">Required</span>
                  }
                  @if (!field.isActive) {
                    <span class="inactive-badge">Inactive</span>
                  }
                </div>
                <div class="flex items-center gap-3 text-xs text-neutral-400">
                  <span>Key: <code class="font-mono text-neutral-500">{{ field.fieldKey }}</code></span>
                  <span>Used by {{ field.usageCount }} {{ field.usageCount === 1 ? 'employee' : 'employees' }}</span>
                </div>
                @if (field.options && field.options.length > 0) {
                  <div class="flex flex-wrap gap-1 mt-1.5">
                    @for (opt of field.options; track opt) {
                      <span class="option-chip">{{ opt }}</span>
                    }
                  </div>
                }
              </div>

              <!-- Active toggle (AC-5) -->
              <div class="field-actions">
                <label class="toggle-label" [for]="'toggle-' + field.customFieldId">
                  <span class="sr-only">{{ field.isActive ? 'Deactivate' : 'Activate' }} {{ field.fieldName }}</span>
                  <button
                    type="button"
                    role="switch"
                    [id]="'toggle-' + field.customFieldId"
                    [attr.aria-checked]="field.isActive"
                    class="toggle-switch"
                    [class.toggle-switch-on]="field.isActive"
                    (click)="toggleActive(field)"
                    [disabled]="isTogglingField() === field.customFieldId"
                  >
                    <span
                      class="toggle-knob"
                      [class.toggle-knob-on]="field.isActive"
                    ></span>
                  </button>
                </label>
              </div>
            </div>
          }
        </div>
      }

      <!-- Add Custom Field Modal / Slide-over -->
      @if (showAddModal()) {
        <div
          class="modal-overlay"
          @modalOverlay
          (click)="closeAddModal()"
          (keydown.escape)="closeAddModal()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="add-field-title"
        >
          <div
            class="slide-over-panel"
            @slideOver
            (click)="$event.stopPropagation()"
          >
            <div class="slide-over-header">
              <h2 id="add-field-title" class="text-lg font-semibold text-neutral-900">
                Add Custom Field
              </h2>
              <button
                type="button"
                class="modal-close-btn"
                (click)="closeAddModal()"
                aria-label="Close add custom field panel"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                </svg>
              </button>
            </div>

            <div class="slide-over-body">
              <form [formGroup]="addFieldForm" (ngSubmit)="submitAddField()">
                <!-- Field Name -->
                <div class="form-section">
                  <label class="label-notion" for="cf-fieldName">
                    Field Name <span class="text-red-500" aria-hidden="true">*</span>
                  </label>
                  <input
                    id="cf-fieldName"
                    type="text"
                    formControlName="fieldName"
                    class="input-notion"
                    placeholder="e.g. T-Shirt Size"
                    maxlength="100"
                    (input)="onFieldNameChange()"
                  />
                  @if (addFieldForm.get('fieldName')?.touched && addFieldForm.get('fieldName')?.hasError('required')) {
                    <p class="field-error" role="alert">Field name is required.</p>
                  }
                </div>

                <!-- Field Key (auto-generated, editable) -->
                <div class="form-section">
                  <label class="label-notion" for="cf-fieldKey">
                    Field Key <span class="text-red-500" aria-hidden="true">*</span>
                  </label>
                  <input
                    id="cf-fieldKey"
                    type="text"
                    formControlName="fieldKey"
                    class="input-notion font-mono text-sm"
                    placeholder="e.g. tshirt_size"
                    maxlength="100"
                  />
                  <p class="field-hint">Auto-generated from name. Used as the JSONB key.</p>
                  @if (addFieldForm.get('fieldKey')?.touched && addFieldForm.get('fieldKey')?.hasError('required')) {
                    <p class="field-error" role="alert">Field key is required.</p>
                  }
                  @if (addFieldForm.get('fieldKey')?.touched && addFieldForm.get('fieldKey')?.hasError('pattern')) {
                    <p class="field-error" role="alert">Key must be lowercase alphanumeric with underscores.</p>
                  }
                </div>

                <!-- Field Type visual selector (FR-2) -->
                <div class="form-section">
                  <label class="label-notion">
                    Field Type <span class="text-red-500" aria-hidden="true">*</span>
                  </label>
                  <div class="type-selector-grid" role="radiogroup" aria-label="Select field type">
                    @for (ft of fieldTypes; track ft.value) {
                      <button
                        type="button"
                        class="type-selector-btn"
                        [class.type-selector-btn-active]="addFieldForm.get('fieldType')?.value === ft.value"
                        (click)="selectFieldType(ft.value)"
                        [attr.aria-pressed]="addFieldForm.get('fieldType')?.value === ft.value"
                        [attr.aria-label]="ft.label"
                      >
                        <span class="type-icon">{{ ft.icon }}</span>
                        <span class="type-label">{{ ft.label }}</span>
                      </button>
                    }
                  </div>
                  @if (addFieldForm.get('fieldType')?.touched && addFieldForm.get('fieldType')?.hasError('required')) {
                    <p class="field-error" role="alert">Field type is required.</p>
                  }
                </div>

                <!-- Required toggle -->
                <div class="form-section">
                  <div class="flex items-center gap-3">
                    <button
                      type="button"
                      role="switch"
                      [attr.aria-checked]="addFieldForm.get('isRequired')?.value"
                      class="toggle-switch"
                      [class.toggle-switch-on]="addFieldForm.get('isRequired')?.value"
                      (click)="toggleRequired()"
                    >
                      <span
                        class="toggle-knob"
                        [class.toggle-knob-on]="addFieldForm.get('isRequired')?.value"
                      ></span>
                    </button>
                    <label class="label-notion !mb-0 cursor-pointer" (click)="toggleRequired()">Required field</label>
                  </div>
                </div>

                <!-- Options tag input (for dropdown/multi_select) -->
                @if (showOptionsInput()) {
                  <div class="form-section">
                    <label class="label-notion" for="cf-optionInput">
                      Options <span class="text-red-500" aria-hidden="true">*</span>
                    </label>
                    <div class="options-container">
                      @for (opt of currentOptions(); track opt; let oi = $index) {
                        <span class="option-chip option-chip-removable">
                          {{ opt }}
                          <button
                            type="button"
                            class="option-remove"
                            (click)="removeOption(oi)"
                            [attr.aria-label]="'Remove option ' + opt"
                          >&times;</button>
                        </span>
                      }
                    </div>
                    <div class="flex gap-2 mt-2">
                      <input
                        id="cf-optionInput"
                        type="text"
                        class="input-notion flex-1"
                        placeholder="Type an option and press Enter"
                        [value]="optionInputValue()"
                        (input)="optionInputValue.set($any($event.target).value)"
                        (keydown.enter)="addOption($event)"
                      />
                    </div>
                    @if (addFieldForm.get('fieldType')?.value && currentOptions().length === 0) {
                      <p class="field-error" role="alert">At least one option is required.</p>
                    }
                  </div>
                }

                <!-- Display Order -->
                <div class="form-section">
                  <label class="label-notion" for="cf-displayOrder">Display Order</label>
                  <input
                    id="cf-displayOrder"
                    type="number"
                    formControlName="displayOrder"
                    class="input-notion"
                    min="0"
                  />
                </div>

                <!-- Live Preview (UI/UX notes) -->
                <div class="form-section">
                  <label class="label-notion">Preview</label>
                  <div class="preview-card">
                    @if (addFieldForm.get('fieldName')?.value) {
                      <label class="label-notion preview-label">
                        {{ addFieldForm.get('fieldName')?.value }}
                        @if (addFieldForm.get('isRequired')?.value) {
                          <span class="text-red-500" aria-hidden="true">*</span>
                        }
                      </label>
                      @switch (previewInputType()) {
                        @case ('textarea') {
                          <textarea class="input-notion" rows="2" disabled placeholder="Enter text..."></textarea>
                        }
                        @case ('checkbox') {
                          <div class="flex items-center gap-2">
                            <input type="checkbox" disabled class="w-4 h-4 rounded border-neutral-300" />
                            <span class="text-sm text-neutral-600">{{ addFieldForm.get('fieldName')?.value }}</span>
                          </div>
                        }
                        @case ('select') {
                          <select class="input-notion select-input" disabled>
                            <option>Select...</option>
                            @for (opt of currentOptions(); track opt) {
                              <option>{{ opt }}</option>
                            }
                          </select>
                        }
                        @case ('multi-select') {
                          <div class="flex flex-wrap gap-1">
                            @for (opt of currentOptions(); track opt) {
                              <span class="option-chip">{{ opt }}</span>
                            }
                          </div>
                        }
                        @default {
                          <input
                            [type]="previewInputType()"
                            class="input-notion"
                            disabled
                            [placeholder]="'Enter ' + (addFieldForm.get('fieldName')?.value || 'value') + '...'"
                          />
                        }
                      }
                    } @else {
                      <p class="text-sm text-neutral-400">Enter a field name to see a preview.</p>
                    }
                  </div>
                </div>

                <!-- Submit -->
                <div class="form-actions-sticky">
                  <button type="button" class="btn-secondary" (click)="closeAddModal()">Cancel</button>
                  <button
                    type="submit"
                    class="btn-primary"
                    [disabled]="isSubmitting() || !isAddFormValid()"
                  >
                    @if (isSubmitting()) {
                      <span class="btn-spinner"></span> Creating...
                    } @else {
                      Create Field
                    }
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    /* ─── Plan limit card ────────────────── */
    .plan-limit-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-4;
    }
    .progress-bar-bg {
      @apply w-full h-2 bg-neutral-100 rounded-full overflow-hidden;
    }
    .progress-bar-fill {
      @apply h-full bg-brand-600 rounded-full transition-all duration-300;
    }
    .progress-bar-warn {
      @apply bg-amber-500;
    }
    .progress-bar-full {
      @apply bg-red-500;
    }
    .plan-limit-banner {
      @apply flex items-center gap-3 rounded-xl bg-red-50 border border-red-200
        px-4 py-3 text-sm text-red-800;
    }

    /* ─── Field card ─────────────────────── */
    .field-card {
      @apply flex items-start gap-3 rounded-xl bg-white border border-neutral-100
        shadow-sm p-4 transition-all duration-200 hover:shadow-md;
    }
    .field-card-inactive {
      @apply opacity-60 bg-neutral-50;
    }

    /* ─── Reorder controls ───────────────── */
    .reorder-controls {
      @apply flex flex-col gap-0.5;
    }
    .reorder-btn {
      @apply w-6 h-6 rounded flex items-center justify-center
        text-neutral-300 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150
        disabled:opacity-30 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-neutral-300;
    }

    /* ─── Field info ─────────────────────── */
    .field-info {
      @apply flex-1 min-w-0;
    }
    .field-type-badge {
      @apply text-[10px] font-semibold uppercase tracking-wider
        px-1.5 py-0.5 rounded bg-brand-50 text-brand-700;
    }
    .required-badge {
      @apply text-[10px] font-medium px-1.5 py-0.5 rounded
        bg-red-50 text-red-600;
    }
    .inactive-badge {
      @apply text-[10px] font-medium px-1.5 py-0.5 rounded
        bg-neutral-100 text-neutral-500;
    }
    .option-chip {
      @apply text-xs px-2 py-0.5 rounded-full bg-neutral-100 text-neutral-600;
    }
    .option-chip-removable {
      @apply inline-flex items-center gap-1;
    }
    .option-remove {
      @apply text-neutral-400 hover:text-red-500 font-bold cursor-pointer ml-0.5 text-sm leading-none;
    }

    /* ─── Toggle switch ──────────────────── */
    .toggle-label {
      @apply flex items-center;
    }
    .toggle-switch {
      @apply relative inline-flex h-5 w-9 items-center rounded-full
        transition-colors duration-200 bg-neutral-200 cursor-pointer
        focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-600;
    }
    .toggle-switch-on {
      @apply bg-brand-600;
    }
    .toggle-knob {
      @apply inline-block h-3.5 w-3.5 rounded-full bg-white shadow-sm
        transition-transform duration-200 translate-x-1;
    }
    .toggle-knob-on {
      @apply translate-x-[18px];
    }

    /* ─── Field actions ──────────────────── */
    .field-actions {
      @apply flex items-center gap-2 flex-shrink-0;
    }

    /* ─── Modal / Slide-over ─────────────── */
    .modal-overlay {
      @apply fixed inset-0 z-50 flex items-stretch justify-end
        bg-black/40 backdrop-blur-sm;
    }
    .slide-over-panel {
      @apply bg-white w-full max-w-md h-full overflow-y-auto shadow-xl
        flex flex-col;
    }
    @media (max-width: 639px) {
      .slide-over-panel {
        @apply max-w-full;
      }
    }
    .slide-over-header {
      @apply flex items-center justify-between px-6 pt-5 pb-3 border-b border-neutral-100;
    }
    .slide-over-body {
      @apply px-6 py-4 flex-1;
    }
    .modal-close-btn {
      @apply w-8 h-8 rounded-lg flex items-center justify-center
        text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100
        transition-colors duration-150;
    }

    /* ─── Type selector grid ─────────────── */
    .type-selector-grid {
      @apply grid grid-cols-2 sm:grid-cols-5 gap-2;
    }
    .type-selector-btn {
      @apply flex flex-col items-center gap-1 px-2 py-2.5 rounded-lg
        border border-neutral-200 bg-white text-neutral-600
        transition-all duration-150 cursor-pointer
        hover:border-brand-300 hover:bg-brand-50;
    }
    .type-selector-btn-active {
      @apply border-brand-600 bg-brand-50 text-brand-700 ring-1 ring-brand-600;
    }
    .type-icon {
      @apply text-sm font-bold;
    }
    .type-label {
      @apply text-[10px] font-medium;
    }

    /* ─── Preview card ───────────────────── */
    .preview-card {
      @apply rounded-lg border border-dashed border-neutral-200 bg-neutral-50/50 p-4;
    }
    .preview-label {
      @apply block mb-1.5;
    }

    /* ─── Form sections ──────────────────── */
    .form-section {
      @apply space-y-1.5 mb-5;
    }
    .field-error {
      @apply text-xs text-red-600 mt-1;
    }
    .field-hint {
      @apply text-xs text-neutral-400;
    }
    .options-container {
      @apply flex flex-wrap gap-1;
    }
    .select-input {
      @apply cursor-pointer appearance-none;
      background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' fill='none' viewBox='0 0 20 20'%3e%3cpath stroke='%236b7280' stroke-linecap='round' stroke-linejoin='round' stroke-width='1.5' d='M6 8l4 4 4-4'/%3e%3c/svg%3e");
      background-position: right 0.5rem center;
      background-repeat: no-repeat;
      background-size: 1.5em 1.5em;
      padding-right: 2.5rem;
    }
    .form-actions-sticky {
      @apply flex items-center justify-end gap-3 pt-4 mt-4 border-t border-neutral-100
        sticky bottom-0 bg-white pb-4;
    }

    /* ─── Buttons ─────────────────────────── */
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
    .loading-spinner {
      @apply w-8 h-8 border-[3px] border-neutral-200 border-t-brand-600 rounded-full;
      animation: spin 0.7s linear infinite;
    }
    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `],
})
export class CustomFieldListComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);
  private readonly customFieldService = inject(CustomFieldService);

  private readonly destroy$ = new Subject<void>();

  // ─── Readonly data ────────────────────────────────────────
  readonly fieldTypes = CUSTOM_FIELD_TYPES;

  // ─── Signals ──────────────────────────────────────────────
  readonly definitions = signal<ICustomFieldDefinition[]>([]);
  readonly planLimits = signal<ICustomFieldPlanLimits | null>(null);
  readonly isLoading = signal(true);
  readonly showAddModal = signal(false);
  readonly isSubmitting = signal(false);
  readonly isTogglingField = signal<string | null>(null);
  readonly planLimitError = signal<string | null>(null);
  readonly currentOptions = signal<string[]>([]);
  readonly optionInputValue = signal('');

  // ─── Computed ─────────────────────────────────────────────
  readonly planLimitPercent = computed(() => {
    const limits = this.planLimits();
    if (!limits || limits.maxAllowed === null || limits.maxAllowed === 0) return 0;
    return Math.min(100, (limits.currentCount / limits.maxAllowed) * 100);
  });

  readonly isAtPlanLimit = computed(() => {
    const limits = this.planLimits();
    if (!limits || limits.maxAllowed === null) return false;
    return limits.currentCount >= limits.maxAllowed;
  });

  readonly showOptionsInput = computed(() => {
    const ft = this.addFieldForm?.get('fieldType')?.value;
    return fieldTypeHasOptions(ft);
  });

  readonly previewInputType = computed(() => {
    const ft = this.addFieldForm?.get('fieldType')?.value;
    if (!ft) return 'text';
    return fieldTypeToInputType(ft);
  });

  // ─── Form ────────────────────────────────────────────────
  addFieldForm!: FormGroup;

  ngOnInit(): void {
    this.buildForm();
    this.loadCustomFields();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ─────────────────────────────────────────

  loadCustomFields(): void {
    this.isLoading.set(true);
    this.customFieldService
      .getCustomFields('employee')
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.definitions.set(response.definitions);
          this.planLimits.set(response.planLimits);
          this.isLoading.set(false);
        },
        error: () => {
          this.toastr.error('Failed to load custom fields.');
          this.isLoading.set(false);
        },
      });
  }

  // ─── Form ─────────────────────────────────────────────────

  private buildForm(): void {
    this.addFieldForm = this.fb.group({
      fieldName: ['', [Validators.required, Validators.maxLength(100)]],
      fieldKey: ['', [Validators.required, Validators.pattern(/^[a-z0-9_]+$/)]],
      fieldType: ['' as CustomFieldType | '', [Validators.required]],
      isRequired: [false],
      displayOrder: [0],
    });
  }

  onFieldNameChange(): void {
    const name = this.addFieldForm.get('fieldName')?.value ?? '';
    const key = slugifyFieldKey(name);
    this.addFieldForm.get('fieldKey')?.setValue(key, { emitEvent: false });
  }

  selectFieldType(type: CustomFieldType): void {
    this.addFieldForm.get('fieldType')?.setValue(type);
    this.addFieldForm.get('fieldType')?.markAsTouched();
    if (!fieldTypeHasOptions(type)) {
      this.currentOptions.set([]);
    }
  }

  toggleRequired(): void {
    const ctrl = this.addFieldForm.get('isRequired');
    ctrl?.setValue(!ctrl.value);
  }

  // ─── Options tag input ────────────────────────────────────

  addOption(event: Event): void {
    event.preventDefault();
    const value = this.optionInputValue().trim();
    if (!value) return;
    const opts = [...this.currentOptions()];
    if (!opts.includes(value)) {
      opts.push(value);
      this.currentOptions.set(opts);
    }
    this.optionInputValue.set('');
  }

  removeOption(index: number): void {
    const opts = [...this.currentOptions()];
    opts.splice(index, 1);
    this.currentOptions.set(opts);
  }

  // ─── Modal ────────────────────────────────────────────────

  openAddModal(): void {
    this.addFieldForm.reset({ fieldName: '', fieldKey: '', fieldType: '', isRequired: false, displayOrder: this.definitions().length });
    this.currentOptions.set([]);
    this.optionInputValue.set('');
    this.planLimitError.set(null);
    this.showAddModal.set(true);
  }

  closeAddModal(): void {
    this.showAddModal.set(false);
  }

  isAddFormValid(): boolean {
    if (this.addFieldForm.invalid) return false;
    const ft = this.addFieldForm.get('fieldType')?.value as CustomFieldType;
    if (fieldTypeHasOptions(ft) && this.currentOptions().length === 0) return false;
    return true;
  }

  submitAddField(): void {
    this.addFieldForm.markAllAsTouched();
    if (!this.isAddFormValid()) return;

    this.isSubmitting.set(true);
    this.planLimitError.set(null);

    const formValue = this.addFieldForm.value;
    const ft = formValue.fieldType as CustomFieldType;

    const request: ICreateCustomFieldRequest = {
      fieldName: formValue.fieldName.trim(),
      fieldKey: formValue.fieldKey.trim(),
      fieldType: ft,
      isRequired: formValue.isRequired,
      options: fieldTypeHasOptions(ft) ? this.currentOptions() : null,
      displayOrder: formValue.displayOrder ?? 0,
      entityType: 'employee',
    };

    this.customFieldService
      .createCustomField(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.isSubmitting.set(false);
          this.closeAddModal();
          this.toastr.success(`Custom field "${request.fieldName}" created.`);
          this.loadCustomFields();
        },
        error: (err: HttpErrorResponse) => {
          this.isSubmitting.set(false);
          this.handleCreateError(err);
        },
      });
  }

  // ─── Reorder (FR-8) ──────────────────────────────────────

  moveField(index: number, direction: -1 | 1): void {
    const defs = [...this.definitions()];
    const targetIndex = index + direction;
    if (targetIndex < 0 || targetIndex >= defs.length) return;

    // Swap
    const temp = defs[index];
    defs[index] = defs[targetIndex];
    defs[targetIndex] = temp;

    // Update display orders
    defs.forEach((d, i) => d.displayOrder = i);
    this.definitions.set(defs);

    // Persist
    const orderedIds = defs.map(d => d.customFieldId);
    this.customFieldService
      .reorderCustomFields({ orderedIds })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        error: () => {
          this.toastr.error('Failed to reorder fields. Please try again.');
          this.loadCustomFields(); // rollback
        },
      });
  }

  // ─── Deactivate / Activate toggle (AC-5) ─────────────────

  toggleActive(field: ICustomFieldDefinition): void {
    this.isTogglingField.set(field.customFieldId);

    const action$ = field.isActive
      ? this.customFieldService.deactivateCustomField(field.customFieldId)
      : this.customFieldService.activateCustomField(field.customFieldId);

    action$
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.isTogglingField.set(null);
          const defs = this.definitions().map(d =>
            d.customFieldId === updated.customFieldId ? updated : d
          );
          this.definitions.set(defs);
          this.toastr.success(
            updated.isActive
              ? `"${updated.fieldName}" reactivated.`
              : `"${updated.fieldName}" deactivated.`
          );
        },
        error: () => {
          this.isTogglingField.set(null);
          this.toastr.error('Failed to toggle field status.');
        },
      });
  }

  // ─── Helpers ──────────────────────────────────────────────

  getTypeLabel(type: CustomFieldType): string {
    return CUSTOM_FIELD_TYPES.find(t => t.value === type)?.label ?? type;
  }

  private handleCreateError(err: HttpErrorResponse): void {
    const body = CustomFieldService.parseError(err);
    if (body?.code === 'plan_limit_exceeded') {
      const max = body.maxAllowed ?? this.planLimits()?.maxAllowed ?? 'N';
      this.planLimitError.set(
        `You have reached the maximum number of custom fields (${max}) for your current plan. Upgrade to add more.`
      );
      this.closeAddModal();
    } else if (body?.code === 'duplicate_name' || body?.code === 'duplicate_key') {
      this.toastr.error(body.message || 'A field with this name or key already exists.');
    } else {
      this.toastr.error(body?.message || 'Failed to create custom field.');
    }
  }
}
