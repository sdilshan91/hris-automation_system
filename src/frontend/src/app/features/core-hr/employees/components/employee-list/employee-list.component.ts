import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { EmployeeService } from '../../services/employee.service';
import { IEmployee } from '../../models/employee.models';

/**
 * US-CHR-001 AC-1: Employee list page.
 *
 * Displays all employees as a card-based list with an "Add Employee"
 * button that navigates to the multi-step wizard.
 *
 * Role-gated to HR Officer via the route guard.
 */
@Component({
  selector: 'app-employee-list',
  standalone: true,
  imports: [CommonModule],
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
            Employees
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Manage your organization's employee records.
          </p>
        </div>
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

      <!-- Loading state -->
      @if (isLoading()) {
        <div class="card-notion flex items-center justify-center py-16">
          <div class="loading-spinner"></div>
          <span class="ml-3 text-sm text-neutral-500">Loading employees...</span>
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
            No employees yet
          </h3>
          <p class="text-sm text-neutral-500 mb-6">
            Get started by adding your first employee.
          </p>
          <button
            type="button"
            class="btn-primary"
            (click)="addEmployee()"
          >
            Add Employee
          </button>
        </div>
      }

      <!-- Employee cards -->
      @if (!isLoading() && employees().length > 0) {
        <div @fadeSlideIn class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          @for (employee of employees(); track employee.employeeId) {
            <div class="employee-card">
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
                >
                  {{ employee.status }}
                </span>
              </div>
              <div class="text-xs text-neutral-500 space-y-1">
                @if (employee.jobTitleName) {
                  <p class="truncate">{{ employee.jobTitleName }}</p>
                }
                @if (employee.departmentName) {
                  <p class="truncate">{{ employee.departmentName }}</p>
                }
                <p class="truncate">{{ employee.email }}</p>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .loading-spinner {
      @apply w-6 h-6 border-2 border-neutral-200 border-t-brand-600 rounded-full;
      animation: spin 0.7s linear infinite;
    }

    .employee-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-4
        hover:shadow-notion-md transition-shadow duration-200;
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

    @keyframes spin {
      to { transform: rotate(360deg); }
    }
  `],
})
export class EmployeeListComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly employeeService = inject(EmployeeService);

  readonly employees = signal<IEmployee[]>([]);
  readonly isLoading = signal(true);

  ngOnInit(): void {
    this.loadEmployees();
  }

  addEmployee(): void {
    this.router.navigate(['/employees/new']);
  }

  getInitials(employee: IEmployee): string {
    return (
      (employee.firstName?.[0] || '') + (employee.lastName?.[0] || '')
    ).toUpperCase();
  }

  private loadEmployees(): void {
    this.isLoading.set(true);
    this.employeeService.getEmployees().subscribe({
      next: (employees) => {
        this.employees.set(employees);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      },
    });
  }
}
