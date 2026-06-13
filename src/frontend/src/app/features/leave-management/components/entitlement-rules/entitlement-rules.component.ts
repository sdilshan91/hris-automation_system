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
import { Subject, forkJoin } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { LeaveEntitlementService } from '../../services/leave-entitlement.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import {
  IEntitlementRule,
  ICreateEntitlementRuleRequest,
  ILookupItem,
  IBulkEntitlementRequest,
  EMPLOYMENT_TYPE_OPTIONS,
  PRIORITY_HELP_TEXT,
} from '../../models/leave-entitlement.models';
import { EntitlementRuleFormComponent } from '../entitlement-rule-form/entitlement-rule-form.component';

/**
 * US-LV-002 AC-1: Entitlement Rules page.
 *
 * Desktop: filterable matrix/table view. Rows = leave types, grouped.
 * Cells show entitlement days with inline editing.
 * Mobile: card-based list grouped by leave type.
 *
 * Includes:
 *   - Filters (leave type, department, employment type, active only)
 *   - Create/edit rule via slide-over form
 *   - Inline cell editing (click to edit days)
 *   - Bulk assignment modal (FR-4)
 *   - Priority/specificity help tooltip (AC-2)
 *   - Recalculation toast on save (AC-5)
 */
@Component({
  selector: 'app-entitlement-rules',
  standalone: true,
  imports: [CommonModule, FormsModule, EntitlementRuleFormComponent],
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
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Leave Entitlement Rules
          </h1>
          <p class="text-sm text-neutral-500 mt-1">
            Configure how many leave days each employee group receives.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <button
            type="button"
            class="btn-secondary text-sm"
            (click)="showBulkModal.set(true)"
            aria-label="Bulk assign entitlements"
          >
            Bulk Assign
          </button>
          <button
            type="button"
            class="btn-primary text-sm"
            (click)="openCreateForm()"
            aria-label="Create a new entitlement rule"
          >
            + New Rule
          </button>
        </div>
      </div>

      <!-- Priority help banner -->
      <div class="bg-blue-50 border border-blue-100 rounded-lg p-3 mb-4 flex items-start gap-2.5">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
          class="w-5 h-5 text-blue-500 flex-shrink-0 mt-0.5" aria-hidden="true">
          <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-7-4a1 1 0 1 1-2 0 1 1 0 0 1 2 0ZM9 9a.75.75 0 0 0 0 1.5h.253a.25.25 0 0 1 .244.304l-.459 2.066A1.75 1.75 0 0 0 10.747 15H11a.75.75 0 0 0 0-1.5h-.253a.25.25 0 0 1-.244-.304l.459-2.066A1.75 1.75 0 0 0 9.253 9H9Z" clip-rule="evenodd"/>
        </svg>
        <p class="text-sm text-blue-700">{{ priorityHelpText }}</p>
      </div>

      <!-- Filters -->
      <div class="card-notion mb-4">
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
          <div>
            <label class="label-sm" for="f-leaveType">Leave Type</label>
            <select id="f-leaveType" class="input-sm select-input"
              [ngModel]="filterLeaveTypeId()"
              (ngModelChange)="filterLeaveTypeId.set($event); applyFilter()">
              <option value="">All leave types</option>
              @for (lt of leaveTypeLookups(); track lt.id) {
                <option [value]="lt.id">{{ lt.name }}</option>
              }
            </select>
          </div>
          <div>
            <label class="label-sm" for="f-dept">Department</label>
            <select id="f-dept" class="input-sm select-input"
              [ngModel]="filterDepartmentId()"
              (ngModelChange)="filterDepartmentId.set($event); applyFilter()">
              <option value="">All departments</option>
              @for (d of departmentLookups(); track d.id) {
                <option [value]="d.id">{{ d.name }}</option>
              }
            </select>
          </div>
          <div>
            <label class="label-sm" for="f-empType">Employment Type</label>
            <select id="f-empType" class="input-sm select-input"
              [ngModel]="filterEmploymentType()"
              (ngModelChange)="filterEmploymentType.set($event); applyFilter()">
              <option value="">All types</option>
              @for (et of employmentTypeOptions; track et.value) {
                <option [value]="et.value">{{ et.label }}</option>
              }
            </select>
          </div>
          <div class="flex items-end">
            <label class="flex items-center gap-2 text-sm text-neutral-700 cursor-pointer h-[42px]">
              <input type="checkbox"
                class="w-4 h-4 rounded border-neutral-300 text-brand-600"
                [ngModel]="filterActiveOnly()"
                (ngModelChange)="filterActiveOnly.set($event); applyFilter()" />
              Active only
            </label>
          </div>
        </div>
      </div>

      <!-- Loading state -->
      @if (isLoading()) {
        <div class="card-notion" aria-live="polite" aria-busy="true">
          <div class="space-y-3">
            @for (_ of [1,2,3,4]; track $index) {
              <div class="skeleton-line w-full h-10"></div>
            }
          </div>
        </div>
      }

      <!-- Empty state -->
      @if (!isLoading() && filteredRules().length === 0) {
        <div @fadeIn class="card-notion text-center py-16">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
            class="w-12 h-12 mx-auto text-neutral-300 mb-4" aria-hidden="true">
            <path d="M5.625 1.5c-1.036 0-1.875.84-1.875 1.875v17.25c0 1.035.84 1.875 1.875 1.875h12.75c1.035 0 1.875-.84 1.875-1.875V12.75A3.75 3.75 0 0 0 16.5 9h-1.875a1.875 1.875 0 0 1-1.875-1.875V5.25A3.75 3.75 0 0 0 9 1.5H5.625Z"/>
            <path d="M12.971 1.816A5.23 5.23 0 0 1 14.25 5.25v1.875c0 .207.168.375.375.375H16.5a5.23 5.23 0 0 1 3.434 1.279 9.768 9.768 0 0 0-6.963-6.963Z"/>
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No entitlement rules yet</h3>
          <p class="text-sm text-neutral-500 mb-4">
            Create your first rule to define leave entitlements by department, job title, or employment type.
          </p>
          <button type="button" class="btn-primary" (click)="openCreateForm()">
            + Create Rule
          </button>
        </div>
      }

      <!-- Desktop matrix/table view -->
      @if (!isLoading() && filteredRules().length > 0) {
        <!-- Desktop table -->
        <div class="hidden md:block card-notion overflow-x-auto" @fadeIn>
          <table class="w-full text-sm" role="grid" aria-label="Entitlement rules matrix">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Leave Type</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Department</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Job Title</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Emp. Type</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Tenure</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Days</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Priority</th>
                <th class="text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Effective</th>
                <th class="text-center py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Status</th>
                <th class="text-right py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (rule of filteredRules(); track rule.ruleId) {
                <tr class="border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors group">
                  <td class="py-3 px-3 font-medium text-neutral-900">{{ rule.leaveTypeName }}</td>
                  <td class="py-3 px-3 text-neutral-600">{{ rule.departmentName || 'All' }}</td>
                  <td class="py-3 px-3 text-neutral-600">{{ rule.jobTitleName || 'All' }}</td>
                  <td class="py-3 px-3 text-neutral-600">{{ rule.employmentType || 'All' }}</td>
                  <td class="py-3 px-3 text-neutral-600">{{ formatTenure(rule) }}</td>
                  <td class="py-3 px-3 text-center">
                    @if (editingCellRuleId() === rule.ruleId) {
                      <input
                        type="number"
                        class="inline-cell-input"
                        [value]="rule.entitlementDays"
                        (blur)="saveInlineEdit(rule, $event)"
                        (keydown.enter)="saveInlineEdit(rule, $event)"
                        (keydown.escape)="editingCellRuleId.set(null)"
                        min="0"
                        step="0.5"
                        autofocus
                        [attr.aria-label]="'Edit entitlement days for ' + rule.leaveTypeName"
                      />
                    } @else {
                      <button
                        type="button"
                        class="inline-cell-value"
                        (click)="editingCellRuleId.set(rule.ruleId)"
                        [attr.aria-label]="'Click to edit ' + rule.entitlementDays + ' days for ' + rule.leaveTypeName"
                        title="Click to edit"
                      >
                        {{ rule.entitlementDays }}
                      </button>
                    }
                  </td>
                  <td class="py-3 px-3 text-center">
                    <span class="priority-badge">{{ rule.priority }}</span>
                  </td>
                  <td class="py-3 px-3 text-neutral-500 text-xs">
                    {{ rule.effectiveFrom | date:'mediumDate' }}
                    @if (rule.effectiveTo) {
                      <br/>to {{ rule.effectiveTo | date:'mediumDate' }}
                    }
                  </td>
                  <td class="py-3 px-3 text-center">
                    <span class="status-dot" [class.status-dot-active]="rule.isActive" [class.status-dot-inactive]="!rule.isActive"
                      [attr.aria-label]="rule.isActive ? 'Active' : 'Inactive'"></span>
                  </td>
                  <td class="py-3 px-3 text-right">
                    <div class="flex items-center justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                      <button type="button" class="action-btn"
                        (click)="openEditForm(rule)"
                        [attr.aria-label]="'Edit rule for ' + rule.leaveTypeName">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                          <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z"/>
                          <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z"/>
                        </svg>
                      </button>
                      <button type="button" class="action-btn action-btn-danger"
                        (click)="deleteRule(rule)"
                        [attr.aria-label]="'Delete rule for ' + rule.leaveTypeName">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                          <path fill-rule="evenodd" d="M8.75 1A2.75 2.75 0 0 0 6 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 1 0 .23 1.482l.149-.022.841 10.518A2.75 2.75 0 0 0 7.596 19h4.807a2.75 2.75 0 0 0 2.742-2.53l.841-10.52.149.023a.75.75 0 0 0 .23-1.482A41.03 41.03 0 0 0 14 4.193V3.75A2.75 2.75 0 0 0 11.25 1h-2.5ZM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4ZM8.58 7.72a.75.75 0 0 0-1.5.06l.3 7.5a.75.75 0 1 0 1.5-.06l-.3-7.5Zm4.34.06a.75.75 0 1 0-1.5-.06l-.3 7.5a.75.75 0 1 0 1.5.06l.3-7.5Z" clip-rule="evenodd"/>
                        </svg>
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <!-- Mobile card view -->
        <div class="md:hidden space-y-3" @fadeIn>
          @for (group of groupedByLeaveType(); track group.leaveTypeId) {
            <div class="card-notion">
              <h3 class="text-sm font-semibold text-neutral-900 mb-3 pb-2 border-b border-neutral-100">
                {{ group.leaveTypeName }}
              </h3>
              @for (rule of group.rules; track rule.ruleId) {
                <div class="mb-3 last:mb-0 p-3 rounded-lg bg-neutral-50/60 border border-neutral-100">
                  <div class="flex items-center justify-between mb-2">
                    <span class="text-lg font-semibold text-brand-700">{{ rule.entitlementDays }} days</span>
                    <div class="flex items-center gap-1">
                      <span class="priority-badge">P{{ rule.priority }}</span>
                      <span class="status-dot ml-1" [class.status-dot-active]="rule.isActive" [class.status-dot-inactive]="!rule.isActive"></span>
                    </div>
                  </div>
                  <div class="text-xs text-neutral-500 space-y-0.5">
                    <p>Dept: {{ rule.departmentName || 'All' }}</p>
                    <p>Title: {{ rule.jobTitleName || 'All' }}</p>
                    <p>Type: {{ rule.employmentType || 'All' }}</p>
                    @if (rule.tenureMinMonths !== null || rule.tenureMaxMonths !== null) {
                      <p>Tenure: {{ formatTenure(rule) }}</p>
                    }
                    <p>From: {{ rule.effectiveFrom | date:'mediumDate' }}
                      @if (rule.effectiveTo) { to {{ rule.effectiveTo | date:'mediumDate' }} }
                    </p>
                  </div>
                  <div class="flex items-center gap-2 mt-2 pt-2 border-t border-neutral-100">
                    <button type="button" class="text-xs text-brand-600 hover:text-brand-700 font-medium"
                      (click)="openEditForm(rule)">
                      Edit
                    </button>
                    <button type="button" class="text-xs text-red-600 hover:text-red-700 font-medium"
                      (click)="deleteRule(rule)">
                      Delete
                    </button>
                  </div>
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- Slide-over form -->
      @if (showForm()) {
        <app-entitlement-rule-form
          [rule]="editingRule()"
          [leaveTypes]="leaveTypeLookups()"
          [departments]="departmentLookups()"
          [jobTitles]="jobTitleLookups()"
          (save)="onFormSave($event)"
          (close)="closeForm()"
        />
      }

      <!-- Bulk Assignment Modal (FR-4) -->
      @if (showBulkModal()) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 backdrop-blur-sm p-4"
          @modalOverlay
          (click)="showBulkModal.set(false)"
          (keydown.escape)="showBulkModal.set(false)"
          role="dialog"
          aria-modal="true"
          aria-labelledby="bulk-modal-title"
        >
          <div class="bg-white rounded-xl shadow-xl max-w-md w-full max-h-[90vh] overflow-y-auto"
            @modalSlide (click)="$event.stopPropagation()">
            <div class="px-6 pt-5 pb-3 flex items-center justify-between">
              <h3 id="bulk-modal-title" class="text-lg font-semibold text-neutral-900">Bulk Assign Entitlements</h3>
              <button type="button"
                class="w-8 h-8 rounded-lg flex items-center justify-center text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100"
                (click)="showBulkModal.set(false)" aria-label="Close bulk assign dialog">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                </svg>
              </button>
            </div>
            <div class="px-6 pb-6">
              <p class="text-sm text-neutral-500 mb-4">
                This will create per-employee overrides for the selected leave type and year. Enter comma-separated employee IDs.
              </p>
              <div class="space-y-4">
                <div>
                  <label class="label-sm" for="bulk-lt">Leave Type</label>
                  <select id="bulk-lt" class="input-sm select-input" [(ngModel)]="bulkLeaveTypeId">
                    <option value="">Select...</option>
                    @for (lt of leaveTypeLookups(); track lt.id) {
                      <option [value]="lt.id">{{ lt.name }}</option>
                    }
                  </select>
                </div>
                <div>
                  <label class="label-sm" for="bulk-year">Leave Year</label>
                  <input id="bulk-year" type="number" class="input-sm" [(ngModel)]="bulkLeaveYear" [min]="2020" />
                </div>
                <div>
                  <label class="label-sm" for="bulk-days">Entitlement Days</label>
                  <input id="bulk-days" type="number" class="input-sm" [(ngModel)]="bulkDays" min="0" step="0.5" />
                </div>
                <div>
                  <label class="label-sm" for="bulk-ids">Employee IDs (comma-separated)</label>
                  <textarea id="bulk-ids" class="input-sm" rows="3" [(ngModel)]="bulkEmployeeIds"
                    placeholder="e.g. emp-001, emp-002, emp-003"></textarea>
                </div>
                <div>
                  <label class="label-sm" for="bulk-reason">Reason</label>
                  <input id="bulk-reason" type="text" class="input-sm" [(ngModel)]="bulkReason" placeholder="Optional reason" />
                </div>
              </div>
              <div class="flex items-center justify-end gap-3 pt-4 mt-4 border-t border-neutral-100">
                <button type="button" class="btn-secondary" (click)="showBulkModal.set(false)">Cancel</button>
                <button type="button" class="btn-primary" [disabled]="isBulkSubmitting()"
                  (click)="submitBulk()">
                  @if (isBulkSubmitting()) {
                    <span class="btn-spinner"></span> Assigning...
                  } @else {
                    Assign
                  }
                </button>
              </div>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .page-container { @apply max-w-7xl mx-auto; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .label-sm { @apply block text-xs font-medium text-neutral-500 mb-1; }
    .input-sm {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2
        text-sm text-neutral-900 placeholder-neutral-400
        transition-all duration-150
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

    /* Inline cell editing */
    .inline-cell-value {
      @apply px-2 py-1 rounded-md text-sm font-semibold text-brand-700 bg-brand-50
        hover:bg-brand-100 cursor-pointer transition-colors min-w-[3rem] inline-block;
    }
    .inline-cell-input {
      @apply w-16 rounded-md border border-brand-300 bg-white px-2 py-1
        text-sm text-center font-semibold text-brand-700
        focus:outline-none focus:ring-2 focus:ring-brand-500/30;
    }

    .priority-badge {
      @apply inline-flex items-center justify-center w-6 h-6 rounded-full
        text-xs font-medium bg-neutral-100 text-neutral-600;
    }

    .status-dot {
      @apply inline-block w-2.5 h-2.5 rounded-full;
    }
    .status-dot-active { @apply bg-green-500; }
    .status-dot-inactive { @apply bg-neutral-300; }

    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-700 hover:bg-neutral-100
        transition-colors duration-150;
    }
    .action-btn-danger {
      @apply hover:text-red-600 hover:bg-red-50;
    }

    .skeleton-line {
      @apply rounded bg-neutral-200;
      animation: shimmer 1.5s ease-in-out infinite;
    }
    @keyframes shimmer {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.4; }
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
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class EntitlementRulesComponent implements OnInit, OnDestroy {
  private readonly entitlementService = inject(LeaveEntitlementService);
  private readonly leaveTypeService = inject(LeaveTypeService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // ─── Data signals ─────────────────────────────────────────
  readonly rules = signal<IEntitlementRule[]>([]);
  readonly leaveTypeLookups = signal<ILookupItem[]>([]);
  readonly departmentLookups = signal<ILookupItem[]>([]);
  readonly jobTitleLookups = signal<ILookupItem[]>([]);
  readonly isLoading = signal(true);

  // ─── Filter signals ───────────────────────────────────────
  readonly filterLeaveTypeId = signal('');
  readonly filterDepartmentId = signal('');
  readonly filterEmploymentType = signal('');
  readonly filterActiveOnly = signal(false);

  // ─── UI state ─────────────────────────────────────────────
  readonly showForm = signal(false);
  readonly editingRule = signal<IEntitlementRule | null>(null);
  readonly editingCellRuleId = signal<string | null>(null);
  readonly showBulkModal = signal(false);
  readonly isBulkSubmitting = signal(false);

  readonly priorityHelpText = PRIORITY_HELP_TEXT;
  readonly employmentTypeOptions = EMPLOYMENT_TYPE_OPTIONS;

  // Bulk form fields
  bulkLeaveTypeId = '';
  bulkLeaveYear = new Date().getFullYear();
  bulkDays = 0;
  bulkEmployeeIds = '';
  bulkReason = '';

  // ─── Computed ─────────────────────────────────────────────

  readonly filteredRules = computed(() => {
    let result = this.rules();
    const ltId = this.filterLeaveTypeId();
    const deptId = this.filterDepartmentId();
    const empType = this.filterEmploymentType();
    const activeOnly = this.filterActiveOnly();

    if (ltId) {
      result = result.filter(r => r.leaveTypeId === ltId);
    }
    if (deptId) {
      result = result.filter(r => r.departmentId === deptId);
    }
    if (empType) {
      result = result.filter(r => r.employmentType === empType);
    }
    if (activeOnly) {
      result = result.filter(r => r.isActive);
    }
    return result;
  });

  readonly groupedByLeaveType = computed(() => {
    const rules = this.filteredRules();
    const map = new Map<string, { leaveTypeId: string; leaveTypeName: string; rules: IEntitlementRule[] }>();
    for (const rule of rules) {
      if (!map.has(rule.leaveTypeId)) {
        map.set(rule.leaveTypeId, {
          leaveTypeId: rule.leaveTypeId,
          leaveTypeName: rule.leaveTypeName,
          rules: [],
        });
      }
      map.get(rule.leaveTypeId)!.rules.push(rule);
    }
    return Array.from(map.values());
  });

  // ─── Lifecycle ────────────────────────────────────────────

  ngOnInit(): void {
    this.loadData();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ─────────────────────────────────────────

  loadData(): void {
    this.isLoading.set(true);

    // Load rules + leave types in parallel.
    // Departments and job titles are fetched from their own endpoints.
    forkJoin({
      rules: this.entitlementService.getRules(),
      leaveTypes: this.leaveTypeService.getLeaveTypes(),
    })
    .pipe(takeUntil(this.destroy$))
    .subscribe({
      next: ({ rules, leaveTypes }) => {
        this.rules.set(rules);
        this.leaveTypeLookups.set(
          leaveTypes.map(lt => ({ id: lt.leaveTypeId, name: lt.name }))
        );
        // Extract unique departments and job titles from rules for filters
        this.extractLookups(rules);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.toastr.error('Failed to load entitlement rules.');
      },
    });
  }

  private extractLookups(rules: IEntitlementRule[]): void {
    const deptMap = new Map<string, string>();
    const jtMap = new Map<string, string>();
    for (const r of rules) {
      if (r.departmentId && r.departmentName) {
        deptMap.set(r.departmentId, r.departmentName);
      }
      if (r.jobTitleId && r.jobTitleName) {
        jtMap.set(r.jobTitleId, r.jobTitleName);
      }
    }
    this.departmentLookups.set(
      Array.from(deptMap.entries()).map(([id, name]) => ({ id, name }))
    );
    this.jobTitleLookups.set(
      Array.from(jtMap.entries()).map(([id, name]) => ({ id, name }))
    );
  }

  applyFilter(): void {
    // Filters are reactive via signals + computed, no explicit action needed
  }

  // ─── CRUD actions ─────────────────────────────────────────

  openCreateForm(): void {
    this.editingRule.set(null);
    this.showForm.set(true);
  }

  openEditForm(rule: IEntitlementRule): void {
    this.editingRule.set(rule);
    this.showForm.set(true);
  }

  closeForm(): void {
    this.showForm.set(false);
    this.editingRule.set(null);
  }

  onFormSave(request: ICreateEntitlementRuleRequest): void {
    const existing = this.editingRule();

    if (existing) {
      this.entitlementService
        .updateRule(existing.ruleId, request)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: () => {
            this.closeForm();
            this.loadData();
            this.toastr.success('Rule updated. Background recalculation of affected employees has been triggered.');
          },
          error: (err: HttpErrorResponse) => {
            this.toastr.error(LeaveEntitlementService.parseError(err));
          },
        });
    } else {
      this.entitlementService
        .createRule(request)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: () => {
            this.closeForm();
            this.loadData();
            this.toastr.success('Rule created. Background recalculation of affected employees has been triggered.');
          },
          error: (err: HttpErrorResponse) => {
            this.toastr.error(LeaveEntitlementService.parseError(err));
          },
        });
    }
  }

  saveInlineEdit(rule: IEntitlementRule, event: Event): void {
    const target = event.target as HTMLInputElement;
    const newDays = parseFloat(target.value);
    if (isNaN(newDays) || newDays < 0) {
      this.editingCellRuleId.set(null);
      return;
    }
    if (newDays === rule.entitlementDays) {
      this.editingCellRuleId.set(null);
      return;
    }

    this.entitlementService
      .updateRuleDays(rule.ruleId, { entitlementDays: newDays })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (updated) => {
          this.editingCellRuleId.set(null);
          // Update the rule in place
          const current = this.rules();
          this.rules.set(
            current.map(r => r.ruleId === updated.ruleId ? updated : r)
          );
          this.toastr.success('Days updated. Background recalculation triggered.');
        },
        error: (err: HttpErrorResponse) => {
          this.editingCellRuleId.set(null);
          this.toastr.error(LeaveEntitlementService.parseError(err));
        },
      });
  }

  deleteRule(rule: IEntitlementRule): void {
    if (!confirm(`Delete the entitlement rule for ${rule.leaveTypeName}?`)) {
      return;
    }
    this.entitlementService
      .deleteRule(rule.ruleId)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.rules.set(this.rules().filter(r => r.ruleId !== rule.ruleId));
          this.toastr.success('Rule deleted.');
        },
        error: (err: HttpErrorResponse) => {
          this.toastr.error(LeaveEntitlementService.parseError(err));
        },
      });
  }

  // ─── Bulk assign (FR-4) ───────────────────────────────────

  submitBulk(): void {
    if (!this.bulkLeaveTypeId || !this.bulkEmployeeIds.trim() || this.bulkDays < 0) {
      this.toastr.warning('Please fill in all required fields.');
      return;
    }

    const employeeIds = this.bulkEmployeeIds
      .split(',')
      .map(id => id.trim())
      .filter(id => id.length > 0);

    if (employeeIds.length === 0) {
      this.toastr.warning('Please enter at least one employee ID.');
      return;
    }

    const request: IBulkEntitlementRequest = {
      leaveTypeId: this.bulkLeaveTypeId,
      entitlementDays: this.bulkDays,
      employeeIds,
      leaveYear: this.bulkLeaveYear,
      reason: this.bulkReason || null,
    };

    this.isBulkSubmitting.set(true);
    this.entitlementService
      .bulkAssign(request)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (response) => {
          this.isBulkSubmitting.set(false);
          this.showBulkModal.set(false);
          this.toastr.success(
            `Bulk assignment complete: ${response.totalSuccess} succeeded, ${response.totalFailed} failed.`
          );
        },
        error: (err: HttpErrorResponse) => {
          this.isBulkSubmitting.set(false);
          this.toastr.error(LeaveEntitlementService.parseError(err));
        },
      });
  }

  // ─── Helpers ──────────────────────────────────────────────

  formatTenure(rule: IEntitlementRule): string {
    if (rule.tenureMinMonths === null && rule.tenureMaxMonths === null) {
      return 'Any';
    }
    const min = rule.tenureMinMonths ?? 0;
    const max = rule.tenureMaxMonths;
    if (max === null) {
      return `${min}+ months`;
    }
    return `${min}-${max} months`;
  }
}
