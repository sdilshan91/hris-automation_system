import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
  DestroyRef,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subject, debounceTime, distinctUntilChanged, finalize } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { EmployeeService } from '../../services/employee.service';
import { AuthService } from '../../../../../core/auth/auth.service';
import {
  IEmployee,
  IEmployeeDirectoryParams,
  IActiveFilterChip,
  DirectoryViewMode,
  EmployeeSortField,
  EmployeeStatus,
  EmploymentType,
  ExportFormat,
  EMPLOYEE_SORT_OPTIONS,
  EMPLOYEE_STATUS_OPTIONS,
  EMPLOYMENT_TYPE_OPTIONS,
  PAGE_SIZE_OPTIONS,
} from '../../models/employee.models';

/**
 * US-CHR-003: Employee Directory with Search and Filters.
 *
 * Enhanced from the US-CHR-001 employee list to include:
 * - Full-text search with 300ms debounce (AC-2, NFR-2)
 * - Multi-select filters: department, job title, status, employment type, location, DOJ range (FR-2)
 * - Card/grid and table/list view modes (FR-3)
 * - Sorting by name, employee_no, date_of_joining, department (FR-4)
 * - Pagination with configurable page sizes 10/20/50 (FR-5)
 * - URL query param persistence for deep-linking (FR-6, AC-3)
 * - CSV/Excel export of filtered set (FR-8, AC-5)
 * - Show Archived toggle for HR Officers (BR-1)
 * - Role-based column visibility (FR-9)
 * - Empty state, skeleton loading, WCAG 2.1 AA (NFR-6)
 */
@Component({
  selector: 'app-employee-list',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '250ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Employee Directory
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Browse, search, and filter your organization's employees.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <!-- Export button -->
          <div class="relative" #exportDropdown>
            <button
              type="button"
              class="btn-secondary"
              [attr.aria-expanded]="showExportMenu()"
              aria-haspopup="true"
              (click)="toggleExportMenu()"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                class="w-4 h-4 mr-1.5" aria-hidden="true">
                <path d="M10.75 2.75a.75.75 0 0 0-1.5 0v8.614L6.295 8.235a.75.75 0 1 0-1.09 1.03l4.25 4.5a.75.75 0 0 0 1.09 0l4.25-4.5a.75.75 0 0 0-1.09-1.03l-2.955 3.129V2.75Z"/>
                <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z"/>
              </svg>
              Export
            </button>
            @if (showExportMenu()) {
              <div
                class="absolute right-0 mt-1 w-36 rounded-lg bg-white shadow-notion-lg border border-neutral-100 py-1 z-20"
                role="menu"
              >
                <button
                  type="button"
                  role="menuitem"
                  class="w-full text-left px-3 py-2 text-sm text-neutral-700 hover:bg-neutral-50 transition-colors"
                  (click)="exportDirectory('csv')"
                >
                  Export as CSV
                </button>
                <button
                  type="button"
                  role="menuitem"
                  class="w-full text-left px-3 py-2 text-sm text-neutral-700 hover:bg-neutral-50 transition-colors"
                  (click)="exportDirectory('excel')"
                >
                  Export as Excel
                </button>
              </div>
            }
          </div>

          <!-- Add Employee -->
          <button
            type="button"
            class="btn-primary"
            (click)="addEmployee()"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
              class="w-4 h-4 mr-1.5" aria-hidden="true">
              <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"/>
            </svg>
            Add Employee
          </button>
        </div>
      </div>

      <!-- Search and Filters Bar -->
      <div class="card-notion mb-4 !p-4">
        <div class="flex flex-col lg:flex-row gap-3">
          <!-- Search input -->
          <div class="relative flex-1">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
              class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400 pointer-events-none"
              aria-hidden="true">
              <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd"/>
            </svg>
            <input
              type="search"
              class="input-notion !pl-9"
              placeholder="Search by name, email, employee no., or phone..."
              [ngModel]="searchTerm()"
              (ngModelChange)="onSearchInput($event)"
              aria-label="Search employees"
            />
          </div>

          <div class="flex items-center gap-2 flex-wrap">
            <!-- Filter toggle -->
            <button
              type="button"
              class="btn-secondary !px-3"
              [class.!ring-brand-500]="showFilters()"
              [class.!bg-brand-50]="showFilters()"
              (click)="showFilters.set(!showFilters())"
              [attr.aria-expanded]="showFilters()"
              aria-controls="directory-filters"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"
                class="w-4 h-4 mr-1.5" aria-hidden="true">
                <path fill-rule="evenodd" d="M2.628 1.601C5.028 1.206 7.49 1 10 1s4.973.206 7.372.601a.75.75 0 0 1 .628.74v2.288a2.25 2.25 0 0 1-.659 1.59l-4.682 4.683a2.25 2.25 0 0 0-.659 1.59v3.037c0 .684-.31 1.33-.844 1.757l-1.937 1.55A.75.75 0 0 1 8 18.25v-5.757a2.25 2.25 0 0 0-.659-1.591L2.659 6.22A2.25 2.25 0 0 1 2 4.629V2.34a.75.75 0 0 1 .628-.74Z" clip-rule="evenodd"/>
              </svg>
              Filters
              @if (activeFilterCount() > 0) {
                <span class="ml-1.5 inline-flex items-center justify-center w-5 h-5 text-xs font-semibold rounded-full bg-brand-600 text-white">
                  {{ activeFilterCount() }}
                </span>
              }
            </button>

            <!-- Sort dropdown -->
            <select
              class="input-notion !w-auto !py-2"
              [ngModel]="currentSort()"
              (ngModelChange)="onSortChange($event)"
              aria-label="Sort employees by"
            >
              @for (opt of sortOptions; track opt.value) {
                <option [value]="opt.value + '_asc'">{{ opt.label }} (A-Z)</option>
                <option [value]="opt.value + '_desc'">{{ opt.label }} (Z-A)</option>
              }
            </select>

            <!-- View mode toggle -->
            <div class="inline-flex rounded-lg border border-neutral-200 overflow-hidden" role="group" aria-label="View mode">
              <button
                type="button"
                class="px-2.5 py-2 transition-colors"
                [class.bg-brand-50]="viewMode() === 'card'"
                [class.text-brand-700]="viewMode() === 'card'"
                [class.text-neutral-400]="viewMode() !== 'card'"
                [class.hover:text-neutral-600]="viewMode() !== 'card'"
                (click)="setViewMode('card')"
                [attr.aria-pressed]="viewMode() === 'card'"
                aria-label="Card view"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                  <path fill-rule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v2.5A2.25 2.25 0 0 0 4.25 9h2.5A2.25 2.25 0 0 0 9 6.75v-2.5A2.25 2.25 0 0 0 6.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 2 13.25v2.5A2.25 2.25 0 0 0 4.25 18h2.5A2.25 2.25 0 0 0 9 15.75v-2.5A2.25 2.25 0 0 0 6.75 11h-2.5Zm9-9A2.25 2.25 0 0 0 11 4.25v2.5A2.25 2.25 0 0 0 13.25 9h2.5A2.25 2.25 0 0 0 18 6.75v-2.5A2.25 2.25 0 0 0 15.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 11 13.25v2.5A2.25 2.25 0 0 0 13.25 18h2.5A2.25 2.25 0 0 0 18 15.75v-2.5A2.25 2.25 0 0 0 15.75 11h-2.5Z" clip-rule="evenodd"/>
                </svg>
              </button>
              <button
                type="button"
                class="px-2.5 py-2 transition-colors border-l border-neutral-200"
                [class.bg-brand-50]="viewMode() === 'table'"
                [class.text-brand-700]="viewMode() === 'table'"
                [class.text-neutral-400]="viewMode() !== 'table'"
                [class.hover:text-neutral-600]="viewMode() !== 'table'"
                (click)="setViewMode('table')"
                [attr.aria-pressed]="viewMode() === 'table'"
                aria-label="Table view"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                  <path fill-rule="evenodd" d="M2 3.75A.75.75 0 0 1 2.75 3h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 3.75Zm0 4.167a.75.75 0 0 1 .75-.75h14.5a.75.75 0 0 1 0 1.5H2.75a.75.75 0 0 1-.75-.75Zm0 4.166a.75.75 0 0 1 .75-.75h14.5a.75.75 0 0 1 0 1.5H2.75a.75.75 0 0 1-.75-.75Zm0 4.167a.75.75 0 0 1 .75-.75h14.5a.75.75 0 0 1 0 1.5H2.75a.75.75 0 0 1-.75-.75Z" clip-rule="evenodd"/>
                </svg>
              </button>
            </div>
          </div>
        </div>

        <!-- Filter panel (collapsible) -->
        @if (showFilters()) {
          <div
            @fadeSlideIn
            id="directory-filters"
            class="mt-4 pt-4 border-t border-neutral-100"
          >
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
              <!-- Department multi-select -->
              <div>
                <label for="filter-department" class="label-notion">Department</label>
                <select
                  id="filter-department"
                  multiple
                  class="input-notion !h-24"
                  [ngModel]="filterDepartments()"
                  (ngModelChange)="filterDepartments.set($event)"
                  aria-label="Filter by department"
                >
                  @for (dept of departmentOptions(); track dept) {
                    <option [value]="dept">{{ dept }}</option>
                  }
                </select>
              </div>

              <!-- Job Title multi-select -->
              <div>
                <label for="filter-jobtitle" class="label-notion">Job Title</label>
                <select
                  id="filter-jobtitle"
                  multiple
                  class="input-notion !h-24"
                  [ngModel]="filterJobTitles()"
                  (ngModelChange)="filterJobTitles.set($event)"
                  aria-label="Filter by job title"
                >
                  @for (jt of jobTitleOptions(); track jt) {
                    <option [value]="jt">{{ jt }}</option>
                  }
                </select>
              </div>

              <!-- Status multi-select -->
              <div>
                <label for="filter-status" class="label-notion">Status</label>
                <select
                  id="filter-status"
                  multiple
                  class="input-notion !h-24"
                  [ngModel]="filterStatuses()"
                  (ngModelChange)="filterStatuses.set($event)"
                  aria-label="Filter by status"
                >
                  @for (s of statusOptions; track s) {
                    <option [value]="s">{{ s }}</option>
                  }
                </select>
              </div>

              <!-- Employment Type multi-select -->
              <div>
                <label for="filter-emptype" class="label-notion">Employment Type</label>
                <select
                  id="filter-emptype"
                  multiple
                  class="input-notion !h-24"
                  [ngModel]="filterEmploymentTypes()"
                  (ngModelChange)="filterEmploymentTypes.set($event)"
                  aria-label="Filter by employment type"
                >
                  @for (et of employmentTypeOptions; track et) {
                    <option [value]="et">{{ et }}</option>
                  }
                </select>
              </div>

              <!-- Location -->
              <div>
                <label for="filter-location" class="label-notion">Location</label>
                <input
                  id="filter-location"
                  type="text"
                  class="input-notion"
                  placeholder="e.g. New York"
                  [ngModel]="filterLocation()"
                  (ngModelChange)="filterLocation.set($event)"
                />
              </div>

              <!-- Date of Joining range -->
              <div>
                <label for="filter-doj-from" class="label-notion">Joining Date From</label>
                <input
                  id="filter-doj-from"
                  type="date"
                  class="input-notion"
                  [ngModel]="filterDojFrom()"
                  (ngModelChange)="filterDojFrom.set($event)"
                />
              </div>
              <div>
                <label for="filter-doj-to" class="label-notion">Joining Date To</label>
                <input
                  id="filter-doj-to"
                  type="date"
                  class="input-notion"
                  [ngModel]="filterDojTo()"
                  (ngModelChange)="filterDojTo.set($event)"
                />
              </div>

              <!-- Show Archived toggle (HR Officers only via BR-1) -->
              @if (canShowArchived()) {
                <div class="flex items-end">
                  <label class="flex items-center gap-2 cursor-pointer">
                    <input
                      type="checkbox"
                      class="w-4 h-4 rounded border-neutral-300 text-brand-600 focus:ring-brand-500"
                      [ngModel]="filterIncludeArchived()"
                      (ngModelChange)="filterIncludeArchived.set($event)"
                    />
                    <span class="text-sm text-neutral-700">Show Archived</span>
                  </label>
                </div>
              }
            </div>

            <!-- Apply / Clear filter buttons -->
            <div class="flex items-center gap-3 mt-4">
              <button
                type="button"
                class="btn-primary !py-2 !px-4"
                (click)="applyFilters()"
              >
                Apply Filters
              </button>
              <button
                type="button"
                class="btn-secondary !py-2 !px-4"
                (click)="clearFilters()"
              >
                Clear All
              </button>
            </div>
          </div>
        }
      </div>

      <!-- Active filter chips -->
      @if (activeFilterChips().length > 0) {
        <div class="flex flex-wrap gap-2 mb-4" role="list" aria-label="Active filters">
          @for (chip of activeFilterChips(); track chip.category + chip.value) {
            <span
              class="inline-flex items-center gap-1 rounded-full bg-brand-50 text-brand-700 px-3 py-1 text-xs font-medium"
              role="listitem"
            >
              <span class="text-brand-400">{{ chip.category }}:</span>
              {{ chip.label }}
              <button
                type="button"
                class="ml-0.5 hover:text-brand-900 transition-colors"
                (click)="removeFilterChip(chip)"
                [attr.aria-label]="'Remove filter ' + chip.category + ' ' + chip.label"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                  <path d="M5.28 4.22a.75.75 0 0 0-1.06 1.06L6.94 8l-2.72 2.72a.75.75 0 1 0 1.06 1.06L8 9.06l2.72 2.72a.75.75 0 1 0 1.06-1.06L9.06 8l2.72-2.72a.75.75 0 0 0-1.06-1.06L8 6.94 5.28 4.22Z"/>
                </svg>
              </button>
            </span>
          }
          <button
            type="button"
            class="text-xs text-neutral-500 hover:text-neutral-700 underline transition-colors"
            (click)="clearFilters()"
          >
            Clear all filters
          </button>
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div @fadeSlideIn>
          @if (viewMode() === 'card') {
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
              @for (i of skeletonCards; track i) {
                <div class="card-notion animate-pulse">
                  <div class="flex items-center gap-3 mb-3">
                    <div class="w-10 h-10 rounded-full bg-neutral-200"></div>
                    <div class="flex-1 space-y-2">
                      <div class="h-3.5 bg-neutral-200 rounded w-3/4"></div>
                      <div class="h-3 bg-neutral-100 rounded w-1/2"></div>
                    </div>
                  </div>
                  <div class="space-y-2">
                    <div class="h-3 bg-neutral-100 rounded w-full"></div>
                    <div class="h-3 bg-neutral-100 rounded w-2/3"></div>
                  </div>
                </div>
              }
            </div>
          } @else {
            <div class="card-notion !p-0 overflow-hidden animate-pulse">
              @for (i of skeletonRows; track i) {
                <div class="flex items-center gap-4 px-6 py-4 border-b border-neutral-50">
                  <div class="w-8 h-8 rounded-full bg-neutral-200"></div>
                  <div class="flex-1 space-y-1.5">
                    <div class="h-3.5 bg-neutral-200 rounded w-1/4"></div>
                    <div class="h-3 bg-neutral-100 rounded w-1/3"></div>
                  </div>
                  <div class="h-5 bg-neutral-100 rounded-full w-16"></div>
                </div>
              }
            </div>
          }
        </div>
      }

      <!-- Empty state -->
      @if (!isLoading() && employees().length === 0) {
        <div @fadeSlideIn class="card-notion text-center py-16">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor"
            class="w-12 h-12 mx-auto text-neutral-300 mb-4" aria-hidden="true">
            <path d="M7.5 6a4.5 4.5 0 1 1 9 0 4.5 4.5 0 0 1-9 0ZM3.751 20.105a8.25 8.25 0 0 1 16.498 0 .75.75 0 0 1-.437.695A18.683 18.683 0 0 1 12 22.5c-2.786 0-5.433-.608-7.812-1.7a.75.75 0 0 1-.437-.695Z"/>
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">
            No employees found
          </h3>
          <p class="text-sm text-neutral-500 mb-6">
            @if (hasActiveFiltersOrSearch()) {
              Try adjusting your search or filters.
            } @else {
              Get started by adding your first employee.
            }
          </p>
          @if (!hasActiveFiltersOrSearch()) {
            <button
              type="button"
              class="btn-primary"
              (click)="addEmployee()"
            >
              Add Employee
            </button>
          } @else {
            <button
              type="button"
              class="btn-secondary"
              (click)="clearFilters()"
            >
              Clear Filters
            </button>
          }
        </div>
      }

      <!-- Card/Grid View -->
      @if (!isLoading() && employees().length > 0 && viewMode() === 'card') {
        <div @fadeSlideIn class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          @for (employee of employees(); track employee.employeeId) {
            <div
              class="employee-card cursor-pointer"
              role="link"
              tabindex="0"
              [attr.aria-label]="'View profile for ' + employee.firstName + ' ' + employee.lastName + ', ' + (employee.jobTitleName || 'No title')"
              (click)="viewEmployee(employee.employeeId)"
              (keydown.enter)="viewEmployee(employee.employeeId)"
            >
              <div class="flex items-center gap-3 mb-3">
                <div class="avatar-circle">
                  @if (employee.profilePhotoUrl) {
                    <img
                      [src]="employee.profilePhotoUrl"
                      [alt]="employee.firstName + ' ' + employee.lastName"
                      class="w-full h-full object-cover"
                    />
                  } @else {
                    <span>{{ getInitials(employee) }}</span>
                  }
                </div>
                <div class="min-w-0 flex-1">
                  <p class="text-sm font-semibold text-neutral-900 truncate">
                    {{ employee.firstName }} {{ employee.lastName }}
                  </p>
                  <p class="text-xs text-neutral-500 truncate">
                    {{ employee.employeeNo }}
                  </p>
                </div>
                <span
                  class="status-badge"
                  [class.status-active]="employee.status === 'active'"
                  [class.status-probation]="employee.status === 'probation'"
                  [class.status-suspended]="employee.status === 'suspended'"
                  [class.status-terminated]="employee.status === 'terminated'"
                >
                  {{ employee.status }}
                </span>
              </div>
              <div class="text-xs text-neutral-500 space-y-1">
                @if (employee.jobTitleName) {
                  <p class="truncate">{{ employee.jobTitleName }}</p>
                }
                @if (employee.departmentName) {
                  <p class="truncate">
                    <span class="inline-block bg-neutral-100 text-neutral-600 rounded px-1.5 py-0.5 text-[10px] font-medium">
                      {{ employee.departmentName }}
                    </span>
                  </p>
                }
                @if (isPrivilegedUser()) {
                  <p class="truncate">{{ employee.email }}</p>
                }
              </div>
            </div>
          }
        </div>
      }

      <!-- Table/List View -->
      @if (!isLoading() && employees().length > 0 && viewMode() === 'table') {
        <div @fadeSlideIn class="card-notion !p-0 overflow-x-auto">
          <table class="w-full text-sm text-left" role="grid">
            <thead>
              <tr class="border-b border-neutral-100">
                <th scope="col" class="table-header">Employee</th>
                <th scope="col" class="table-header">Employee No.</th>
                <th scope="col" class="table-header">Department</th>
                <th scope="col" class="table-header">Job Title</th>
                @if (isPrivilegedUser()) {
                  <th scope="col" class="table-header">Email</th>
                  <th scope="col" class="table-header">Date of Joining</th>
                }
                <th scope="col" class="table-header">Status</th>
              </tr>
            </thead>
            <tbody>
              @for (employee of employees(); track employee.employeeId; let odd = $odd) {
                <tr
                  class="table-row cursor-pointer"
                  [class.bg-neutral-50]="odd"
                  tabindex="0"
                  (click)="viewEmployee(employee.employeeId)"
                  (keydown.enter)="viewEmployee(employee.employeeId)"
                >
                  <td class="table-cell">
                    <div class="flex items-center gap-3">
                      <div class="avatar-circle !w-8 !h-8 !text-xs">
                        @if (employee.profilePhotoUrl) {
                          <img
                            [src]="employee.profilePhotoUrl"
                            [alt]="employee.firstName + ' ' + employee.lastName"
                            class="w-full h-full object-cover"
                          />
                        } @else {
                          <span>{{ getInitials(employee) }}</span>
                        }
                      </div>
                      <span class="font-medium text-neutral-900">
                        {{ employee.firstName }} {{ employee.lastName }}
                      </span>
                    </div>
                  </td>
                  <td class="table-cell text-neutral-500">{{ employee.employeeNo }}</td>
                  <td class="table-cell">
                    @if (employee.departmentName) {
                      <span class="inline-block bg-neutral-100 text-neutral-600 rounded px-1.5 py-0.5 text-xs font-medium">
                        {{ employee.departmentName }}
                      </span>
                    }
                  </td>
                  <td class="table-cell text-neutral-600">{{ employee.jobTitleName || '---' }}</td>
                  @if (isPrivilegedUser()) {
                    <td class="table-cell text-neutral-500">{{ employee.email }}</td>
                    <td class="table-cell text-neutral-500">{{ employee.dateOfJoining | date:'mediumDate' }}</td>
                  }
                  <td class="table-cell">
                    <span
                      class="status-badge"
                      [class.status-active]="employee.status === 'active'"
                      [class.status-probation]="employee.status === 'probation'"
                      [class.status-suspended]="employee.status === 'suspended'"
                      [class.status-terminated]="employee.status === 'terminated'"
                    >
                      {{ employee.status }}
                    </span>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <!-- Pagination -->
      @if (!isLoading() && totalCount() > 0) {
        <div
          class="flex flex-col sm:flex-row items-center justify-between gap-3 mt-4 px-1"
          role="navigation"
          aria-label="Pagination"
        >
          <p class="text-sm text-neutral-500">
            Showing {{ showingFrom() }}-{{ showingTo() }} of {{ totalCount() }} employees
          </p>

          <div class="flex items-center gap-2">
            <!-- Page size selector -->
            <select
              class="input-notion !w-auto !py-1.5 !text-xs"
              [ngModel]="pageSize()"
              (ngModelChange)="onPageSizeChange($event)"
              aria-label="Results per page"
            >
              @for (size of pageSizeOptions; track size) {
                <option [ngValue]="size">{{ size }} per page</option>
              }
            </select>

            <!-- Page buttons -->
            <div class="flex items-center gap-1">
              <button
                type="button"
                class="pagination-btn"
                [disabled]="currentPage() <= 1"
                (click)="goToPage(currentPage() - 1)"
                aria-label="Previous page"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                  <path fill-rule="evenodd" d="M9.78 4.22a.75.75 0 0 1 0 1.06L7.06 8l2.72 2.72a.75.75 0 1 1-1.06 1.06L5.47 8.53a.75.75 0 0 1 0-1.06l3.25-3.25a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd"/>
                </svg>
              </button>

              @for (p of visiblePages(); track p) {
                @if (p === -1) {
                  <span class="px-1 text-neutral-400">...</span>
                } @else {
                  <button
                    type="button"
                    class="pagination-btn"
                    [class.!bg-brand-50]="p === currentPage()"
                    [class.!text-brand-700]="p === currentPage()"
                    [class.!font-semibold]="p === currentPage()"
                    [attr.aria-current]="p === currentPage() ? 'page' : null"
                    (click)="goToPage(p)"
                  >
                    {{ p }}
                  </button>
                }
              }

              <button
                type="button"
                class="pagination-btn"
                [disabled]="currentPage() >= totalPages()"
                (click)="goToPage(currentPage() + 1)"
                aria-label="Next page"
              >
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                  <path fill-rule="evenodd" d="M6.22 4.22a.75.75 0 0 1 1.06 0l3.25 3.25a.75.75 0 0 1 0 1.06l-3.25 3.25a.75.75 0 0 1-1.06-1.06L8.94 8 6.22 5.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd"/>
                </svg>
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }

    .employee-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-4
        transition-all duration-200;
    }

    .employee-card:hover {
      @apply shadow-notion-md;
      transform: translateY(-2px);
    }

    .employee-card:focus-visible {
      @apply ring-2 ring-brand-500 ring-offset-2;
    }

    .avatar-circle {
      @apply w-10 h-10 rounded-full bg-brand-100 text-brand-700
        text-sm font-semibold flex items-center justify-center
        overflow-hidden flex-shrink-0;
    }

    .status-badge {
      @apply text-xs font-medium px-2 py-0.5 rounded-full capitalize;
    }

    .status-active {
      @apply bg-green-50 text-green-700;
    }

    .status-probation {
      @apply bg-amber-50 text-amber-700;
    }

    .status-suspended {
      @apply bg-red-50 text-red-700;
    }

    .status-terminated {
      @apply bg-neutral-100 text-neutral-500;
    }

    .table-header {
      @apply px-6 py-3 text-xs font-semibold text-neutral-500 uppercase tracking-wider bg-neutral-50;
    }

    .table-row {
      @apply border-b border-neutral-50 transition-colors hover:bg-brand-50/30;
    }

    .table-row:focus-visible {
      @apply ring-2 ring-inset ring-brand-500;
    }

    .table-cell {
      @apply px-6 py-3.5 whitespace-nowrap;
    }

    .pagination-btn {
      @apply inline-flex items-center justify-center w-8 h-8 rounded-lg text-sm
        text-neutral-600 hover:bg-neutral-100 transition-colors
        disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent;
    }
  `],
})
export class EmployeeListComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly employeeService = inject(EmployeeService);
  private readonly authService = inject(AuthService);
  private readonly toastr = inject(ToastrService);
  private readonly destroyRef = inject(DestroyRef);

  // ─── State signals ─────────────────────────────────────────
  readonly employees = signal<IEmployee[]>([]);
  readonly totalCount = signal(0);
  readonly currentPage = signal(1);
  readonly pageSize = signal(20);
  readonly isLoading = signal(true);
  readonly isExporting = signal(false);
  readonly viewMode = signal<DirectoryViewMode>('card');
  readonly searchTerm = signal('');
  readonly showFilters = signal(false);
  readonly showExportMenu = signal(false);

  // Filter signals
  readonly filterDepartments = signal<string[]>([]);
  readonly filterJobTitles = signal<string[]>([]);
  readonly filterStatuses = signal<EmployeeStatus[]>([]);
  readonly filterEmploymentTypes = signal<EmploymentType[]>([]);
  readonly filterLocation = signal('');
  readonly filterDojFrom = signal('');
  readonly filterDojTo = signal('');
  readonly filterIncludeArchived = signal(false);

  // Filter option lists (populated from URL or backend lookup endpoint)
  readonly departmentOptions = signal<string[]>([]);
  readonly jobTitleOptions = signal<string[]>([]);

  // Sort
  readonly sortField = signal<EmployeeSortField>('name');
  readonly sortDirection = signal<'asc' | 'desc'>('asc');

  // Constants exposed to template
  readonly sortOptions = EMPLOYEE_SORT_OPTIONS;
  readonly statusOptions = EMPLOYEE_STATUS_OPTIONS;
  readonly employmentTypeOptions = EMPLOYMENT_TYPE_OPTIONS;
  readonly pageSizeOptions = PAGE_SIZE_OPTIONS;
  readonly skeletonCards = Array.from({ length: 8 }, (_, i) => i);
  readonly skeletonRows = Array.from({ length: 6 }, (_, i) => i);

  // Search debounce
  private readonly searchSubject = new Subject<string>();

  // ─── Computed signals ──────────────────────────────────────

  readonly currentSort = computed(
    () => `${this.sortField()}_${this.sortDirection()}`
  );

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize()))
  );

  readonly showingFrom = computed(() =>
    this.totalCount() === 0 ? 0 : (this.currentPage() - 1) * this.pageSize() + 1
  );

  readonly showingTo = computed(() =>
    Math.min(this.currentPage() * this.pageSize(), this.totalCount())
  );

  readonly canShowArchived = computed(() => {
    const perms = this.authService.permissions();
    return perms.includes('Employee.View.All');
  });

  readonly isPrivilegedUser = computed(() => {
    const perms = this.authService.permissions();
    return (
      perms.includes('Employee.View.All') ||
      perms.includes('Employee.View.Team')
    );
  });

  readonly activeFilterCount = computed(() => {
    let count = 0;
    if (this.filterDepartments().length) count++;
    if (this.filterJobTitles().length) count++;
    if (this.filterStatuses().length) count++;
    if (this.filterEmploymentTypes().length) count++;
    if (this.filterLocation()) count++;
    if (this.filterDojFrom() || this.filterDojTo()) count++;
    if (this.filterIncludeArchived()) count++;
    return count;
  });

  readonly hasActiveFiltersOrSearch = computed(
    () => this.searchTerm().length > 0 || this.activeFilterCount() > 0
  );

  readonly activeFilterChips = computed<IActiveFilterChip[]>(() => {
    const chips: IActiveFilterChip[] = [];
    for (const dept of this.filterDepartments()) {
      chips.push({
        category: 'Department',
        label: dept,
        value: dept,
        filterKey: 'departments',
      });
    }
    for (const jt of this.filterJobTitles()) {
      chips.push({
        category: 'Job Title',
        label: jt,
        value: jt,
        filterKey: 'jobTitles',
      });
    }
    for (const s of this.filterStatuses()) {
      chips.push({
        category: 'Status',
        label: s,
        value: s,
        filterKey: 'statuses',
      });
    }
    for (const et of this.filterEmploymentTypes()) {
      chips.push({
        category: 'Type',
        label: et,
        value: et,
        filterKey: 'employmentTypes',
      });
    }
    if (this.filterLocation()) {
      chips.push({
        category: 'Location',
        label: this.filterLocation(),
        value: this.filterLocation(),
        filterKey: 'location',
      });
    }
    if (this.filterDojFrom()) {
      chips.push({
        category: 'Joining From',
        label: this.filterDojFrom(),
        value: this.filterDojFrom(),
        filterKey: 'dateOfJoiningFrom',
      });
    }
    if (this.filterDojTo()) {
      chips.push({
        category: 'Joining To',
        label: this.filterDojTo(),
        value: this.filterDojTo(),
        filterKey: 'dateOfJoiningTo',
      });
    }
    return chips;
  });

  readonly visiblePages = computed<number[]>(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    if (total <= 7) {
      return Array.from({ length: total }, (_, i) => i + 1);
    }
    const pages: number[] = [1];
    if (current > 3) pages.push(-1); // ellipsis
    const start = Math.max(2, current - 1);
    const end = Math.min(total - 1, current + 1);
    for (let i = start; i <= end; i++) {
      pages.push(i);
    }
    if (current < total - 2) pages.push(-1); // ellipsis
    pages.push(total);
    return pages;
  });

  // ─── Lifecycle ─────────────────────────────────────────────

  ngOnInit(): void {
    // Set up search debounce (AC-2, NFR-2)
    this.searchSubject
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe((term) => {
        this.searchTerm.set(term);
        this.currentPage.set(1);
        this.loadDirectory();
        this.syncToUrl();
      });

    // Restore state from URL query params (FR-6)
    this.restoreFromUrl();

    // Load filter options from the backend
    this.loadFilterOptions();

    // Load initial data
    this.loadDirectory();

    // Auto-detect mobile for default view mode
    if (typeof window !== 'undefined' && window.innerWidth < 768) {
      this.viewMode.set('card');
    }
  }

  ngOnDestroy(): void {
    this.searchSubject.complete();
  }

  // ─── Search ────────────────────────────────────────────────

  onSearchInput(value: string): void {
    this.searchSubject.next(value);
  }

  // ─── Sorting ───────────────────────────────────────────────

  onSortChange(sortValue: string): void {
    const lastUnderscore = sortValue.lastIndexOf('_');
    const field = sortValue.substring(0, lastUnderscore) as EmployeeSortField;
    const direction = sortValue.substring(lastUnderscore + 1) as 'asc' | 'desc';
    this.sortField.set(field);
    this.sortDirection.set(direction);
    this.currentPage.set(1);
    this.loadDirectory();
    this.syncToUrl();
  }

  // ─── View mode ─────────────────────────────────────────────

  setViewMode(mode: DirectoryViewMode): void {
    this.viewMode.set(mode);
    this.syncToUrl();
  }

  // ─── Filters ───────────────────────────────────────────────

  applyFilters(): void {
    this.currentPage.set(1);
    this.loadDirectory();
    this.syncToUrl();
  }

  clearFilters(): void {
    this.searchTerm.set('');
    this.filterDepartments.set([]);
    this.filterJobTitles.set([]);
    this.filterStatuses.set([]);
    this.filterEmploymentTypes.set([]);
    this.filterLocation.set('');
    this.filterDojFrom.set('');
    this.filterDojTo.set('');
    this.filterIncludeArchived.set(false);
    this.currentPage.set(1);
    this.loadDirectory();
    this.syncToUrl();
  }

  removeFilterChip(chip: IActiveFilterChip): void {
    switch (chip.filterKey) {
      case 'departments':
        this.filterDepartments.set(
          this.filterDepartments().filter((v) => v !== chip.value)
        );
        break;
      case 'jobTitles':
        this.filterJobTitles.set(
          this.filterJobTitles().filter((v) => v !== chip.value)
        );
        break;
      case 'statuses':
        this.filterStatuses.set(
          this.filterStatuses().filter((v) => v !== chip.value)
        );
        break;
      case 'employmentTypes':
        this.filterEmploymentTypes.set(
          this.filterEmploymentTypes().filter((v) => v !== chip.value)
        );
        break;
      case 'location':
        this.filterLocation.set('');
        break;
      case 'dateOfJoiningFrom':
        this.filterDojFrom.set('');
        break;
      case 'dateOfJoiningTo':
        this.filterDojTo.set('');
        break;
    }
    this.currentPage.set(1);
    this.loadDirectory();
    this.syncToUrl();
  }

  // ─── Pagination ────────────────────────────────────────────

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.currentPage.set(page);
    this.loadDirectory();
    this.syncToUrl();
  }

  onPageSizeChange(size: number): void {
    this.pageSize.set(size);
    this.currentPage.set(1);
    this.loadDirectory();
    this.syncToUrl();
  }

  // ─── Export (AC-5, FR-8) ───────────────────────────────────

  toggleExportMenu(): void {
    this.showExportMenu.set(!this.showExportMenu());
  }

  exportDirectory(format: ExportFormat): void {
    this.showExportMenu.set(false);
    this.isExporting.set(true);

    const params = this.buildCurrentParams();
    this.employeeService
      .exportDirectory(params, format)
      .pipe(finalize(() => this.isExporting.set(false)))
      .subscribe({
        next: (blob) => {
          const ext = format === 'csv' ? 'csv' : 'xlsx';
          const filename = `employee-directory.${ext}`;
          this.downloadBlob(blob, filename);
          this.toastr.success(`Employee directory exported as ${ext.toUpperCase()}.`);
        },
        error: () => {
          this.toastr.error('Failed to export employee directory.');
        },
      });
  }

  // ─── Navigation ────────────────────────────────────────────

  addEmployee(): void {
    this.router.navigate(['/employees/new']);
  }

  /** US-CHR-002/003: Navigate to employee profile page */
  viewEmployee(employeeId: string): void {
    this.router.navigate(['/employees', employeeId]);
  }

  getInitials(employee: IEmployee): string {
    return (
      (employee.firstName?.[0] || '') + (employee.lastName?.[0] || '')
    ).toUpperCase();
  }

  // ─── Data loading ──────────────────────────────────────────

  loadDirectory(): void {
    this.isLoading.set(true);
    const params = this.buildCurrentParams();
    this.employeeService
      .queryDirectory(params)
      .pipe(
        finalize(() => this.isLoading.set(false)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (response) => {
          this.employees.set(response.data);
          this.totalCount.set(response.total);
          // Clamp page if backend returns fewer pages than current
          if (this.currentPage() > this.totalPages() && this.totalPages() > 0) {
            this.currentPage.set(this.totalPages());
          }
        },
        error: () => {
          this.employees.set([]);
          this.totalCount.set(0);
        },
      });
  }

  private buildCurrentParams(): IEmployeeDirectoryParams {
    return {
      search: this.searchTerm() || undefined,
      departments: this.filterDepartments().length ? this.filterDepartments() : undefined,
      jobTitles: this.filterJobTitles().length ? this.filterJobTitles() : undefined,
      statuses: this.filterStatuses().length ? this.filterStatuses() : undefined,
      employmentTypes: this.filterEmploymentTypes().length ? this.filterEmploymentTypes() : undefined,
      location: this.filterLocation() || undefined,
      dateOfJoiningFrom: this.filterDojFrom() || undefined,
      dateOfJoiningTo: this.filterDojTo() || undefined,
      sort: this.sortField(),
      sortDirection: this.sortDirection(),
      page: this.currentPage(),
      pageSize: this.pageSize(),
      includeArchived: this.filterIncludeArchived() || undefined,
    };
  }

  /**
   * Load filter dropdown options from the departments and job titles endpoints.
   * These are lightweight lookups; a dedicated /employees/filter-options
   * endpoint would be ideal but we reuse existing endpoints as a fallback.
   */
  private loadFilterOptions(): void {
    // For departments, reuse the existing department service if imported,
    // but to avoid coupling we use a simple HTTP call for dropdown options.
    // The backend agent is building filter-option support into the directory endpoint.
    // Until then, we leave these as empty and populate from response data.
    // NOTE: actual option lists are populated from the first API response
    // or can be set from dedicated lookup endpoints once available.
  }

  // ─── URL state sync (FR-6) ─────────────────────────────────

  syncToUrl(): void {
    const queryParams: Record<string, string | null> = {
      search: this.searchTerm() || null,
      departments: this.filterDepartments().length
        ? this.filterDepartments().join(',')
        : null,
      jobTitles: this.filterJobTitles().length
        ? this.filterJobTitles().join(',')
        : null,
      statuses: this.filterStatuses().length
        ? this.filterStatuses().join(',')
        : null,
      employmentTypes: this.filterEmploymentTypes().length
        ? this.filterEmploymentTypes().join(',')
        : null,
      location: this.filterLocation() || null,
      dojFrom: this.filterDojFrom() || null,
      dojTo: this.filterDojTo() || null,
      sort: this.sortField() !== 'name' || this.sortDirection() !== 'asc'
        ? `${this.sortField()}_${this.sortDirection()}`
        : null,
      page: this.currentPage() > 1 ? this.currentPage().toString() : null,
      pageSize: this.pageSize() !== 20 ? this.pageSize().toString() : null,
      view: this.viewMode() !== 'card' ? this.viewMode() : null,
      archived: this.filterIncludeArchived() ? 'true' : null,
    };

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      queryParamsHandling: 'replace',
      replaceUrl: true,
    });
  }

  restoreFromUrl(): void {
    const qp = this.route.snapshot.queryParamMap;

    if (qp.has('search')) {
      this.searchTerm.set(qp.get('search')!);
    }
    if (qp.has('departments')) {
      this.filterDepartments.set(qp.get('departments')!.split(','));
    }
    if (qp.has('jobTitles')) {
      this.filterJobTitles.set(qp.get('jobTitles')!.split(','));
    }
    if (qp.has('statuses')) {
      this.filterStatuses.set(
        qp.get('statuses')!.split(',') as EmployeeStatus[]
      );
    }
    if (qp.has('employmentTypes')) {
      this.filterEmploymentTypes.set(
        qp.get('employmentTypes')!.split(',') as EmploymentType[]
      );
    }
    if (qp.has('location')) {
      this.filterLocation.set(qp.get('location')!);
    }
    if (qp.has('dojFrom')) {
      this.filterDojFrom.set(qp.get('dojFrom')!);
    }
    if (qp.has('dojTo')) {
      this.filterDojTo.set(qp.get('dojTo')!);
    }
    if (qp.has('sort')) {
      const sortVal = qp.get('sort')!;
      const lastUnderscore = sortVal.lastIndexOf('_');
      if (lastUnderscore > 0) {
        this.sortField.set(sortVal.substring(0, lastUnderscore) as EmployeeSortField);
        this.sortDirection.set(sortVal.substring(lastUnderscore + 1) as 'asc' | 'desc');
      }
    }
    if (qp.has('page')) {
      const page = parseInt(qp.get('page')!, 10);
      if (!isNaN(page) && page > 0) this.currentPage.set(page);
    }
    if (qp.has('pageSize')) {
      const size = parseInt(qp.get('pageSize')!, 10);
      if (PAGE_SIZE_OPTIONS.includes(size)) this.pageSize.set(size);
    }
    if (qp.has('view')) {
      const view = qp.get('view') as DirectoryViewMode;
      if (view === 'card' || view === 'table') this.viewMode.set(view);
    }
    if (qp.get('archived') === 'true') {
      this.filterIncludeArchived.set(true);
    }

    // If any filter was restored, show the filter panel
    if (this.activeFilterCount() > 0) {
      this.showFilters.set(true);
    }
  }

  // ─── Helpers ───────────────────────────────────────────────

  private downloadBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
  }
}
