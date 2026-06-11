import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { DepartmentService } from '../../services/department.service';
import { IDepartment, IDepartmentErrorResponse } from '../../models/department.models';
import { DepartmentFormComponent } from '../department-form/department-form.component';
import { DepartmentTreeComponent } from '../department-tree/department-tree.component';

/**
 * US-CHR-004: Department list page.
 *
 * Displays departments as a card-based table (FR-8 flat list) with a toggle
 * to show the hierarchical tree view. Supports creating, editing, and
 * deactivating departments via a slide-over form panel (AC-1).
 *
 * Role-gated to Tenant Admin / HR Officer via the route guard.
 */
@Component({
  selector: 'app-department-list',
  standalone: true,
  imports: [
    CommonModule,
    DepartmentFormComponent,
    DepartmentTreeComponent,
  ],
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
    trigger('slideOver', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(100%)' }),
        animate(
          '300ms ease-out',
          style({ opacity: 1, transform: 'translateX(0)' })
        ),
      ]),
      transition(':leave', [
        animate(
          '200ms ease-in',
          style({ opacity: 0, transform: 'translateX(100%)' })
        ),
      ]),
    ]),
    trigger('overlayFade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Departments
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Manage departments and organizational hierarchy.
          </p>
        </div>
        <div class="flex items-center gap-3">
          <!-- View toggle -->
          <div class="view-toggle" role="radiogroup" aria-label="View mode">
            <button
              type="button"
              class="toggle-btn"
              [class.toggle-active]="viewMode() === 'list'"
              (click)="viewMode.set('list')"
              role="radio"
              [attr.aria-checked]="viewMode() === 'list'"
              aria-label="List view"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                <path fill-rule="evenodd" d="M2 4.75A.75.75 0 0 1 2.75 4h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 4.75Zm0 10.5a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5a.75.75 0 0 1-.75-.75ZM2 10a.75.75 0 0 1 .75-.75h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 10Z" clip-rule="evenodd" />
              </svg>
            </button>
            <button
              type="button"
              class="toggle-btn"
              [class.toggle-active]="viewMode() === 'tree'"
              (click)="viewMode.set('tree')"
              role="radio"
              [attr.aria-checked]="viewMode() === 'tree'"
              aria-label="Tree view"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                <path d="M10 2a.75.75 0 0 1 .75.75v7.5a.75.75 0 0 1-1.5 0v-7.5A.75.75 0 0 1 10 2ZM5.404 4.343a.75.75 0 0 1 0 1.06L4.06 6.75h2.19A.75.75 0 0 1 6.25 8.25H2.75a.75.75 0 0 1-.75-.75v-3.5a.75.75 0 0 1 1.5 0v2.19l1.343-1.347a.75.75 0 0 1 1.061 0Zm9.192 0a.75.75 0 0 1 1.06 0l1.344 1.346V3.5a.75.75 0 1 1 1.5 0V7.5a.75.75 0 0 1-.75.75h-3.5a.75.75 0 0 1 0-1.5h2.19l-1.344-1.347a.75.75 0 0 1 0-1.06ZM10 11.25a.75.75 0 0 1 .75.75v5.25a.75.75 0 0 1-1.5 0V12a.75.75 0 0 1 .75-.75Zm-4.25 1a.75.75 0 0 1 .75.75v4.25a.75.75 0 0 1-1.5 0V13a.75.75 0 0 1 .75-.75Zm8.5 0a.75.75 0 0 1 .75.75v4.25a.75.75 0 0 1-1.5 0V13a.75.75 0 0 1 .75-.75Z" />
              </svg>
            </button>
          </div>
          <!-- Add Department button -->
          <button
            type="button"
            class="btn-primary"
            (click)="openCreate()"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
              <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
            </svg>
            Add Department
          </button>
        </div>
      </div>

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          @for (i of skeletonItems; track i) {
            <div class="card-notion animate-pulse">
              <div class="h-5 bg-neutral-100 rounded w-2/3 mb-3"></div>
              <div class="h-4 bg-neutral-50 rounded w-full mb-2"></div>
              <div class="h-4 bg-neutral-50 rounded w-1/2 mb-4"></div>
              <div class="flex gap-3">
                <div class="h-4 bg-neutral-50 rounded w-1/3"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/4"></div>
              </div>
            </div>
          }
        </div>
      }

      <!-- Error state -->
      @if (loadError()) {
        <div class="card-notion text-center py-12">
          <div class="w-12 h-12 rounded-full bg-red-50 flex items-center justify-center mx-auto mb-4">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-red-500" aria-hidden="true">
              <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
            </svg>
          </div>
          <p class="text-sm text-neutral-600">{{ loadError() }}</p>
          <button class="btn-secondary mt-4" (click)="loadDepartments()">
            Try Again
          </button>
        </div>
      }

      <!-- Content -->
      @if (!isLoading() && !loadError()) {
        @if (departments().length === 0) {
          <!-- Empty state -->
          <div class="card-notion text-center py-12">
            <div class="w-14 h-14 rounded-full bg-neutral-50 flex items-center justify-center mx-auto mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-7 h-7 text-neutral-400" aria-hidden="true">
                <path fill-rule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v2.5A2.25 2.25 0 0 0 4.25 9h2.5A2.25 2.25 0 0 0 9 6.75v-2.5A2.25 2.25 0 0 0 6.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 2 13.25v2.5A2.25 2.25 0 0 0 4.25 18h2.5A2.25 2.25 0 0 0 9 15.75v-2.5A2.25 2.25 0 0 0 6.75 11h-2.5Zm9-9A2.25 2.25 0 0 0 11 4.25v2.5A2.25 2.25 0 0 0 13.25 9h2.5A2.25 2.25 0 0 0 18 6.75v-2.5A2.25 2.25 0 0 0 15.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 11 13.25v2.5A2.25 2.25 0 0 0 13.25 18h2.5A2.25 2.25 0 0 0 18 15.75v-2.5A2.25 2.25 0 0 0 15.75 11h-2.5Z" clip-rule="evenodd" />
              </svg>
            </div>
            <p class="text-sm font-medium text-neutral-700 mb-1">No departments yet</p>
            <p class="text-xs text-neutral-400 mb-4">
              Create your first department to start building your org structure.
            </p>
            <button type="button" class="btn-primary" (click)="openCreate()">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
                <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
              </svg>
              Add Department
            </button>
          </div>
        } @else {
          <!-- List view -->
          @if (viewMode() === 'list') {
            <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              @for (dept of departments(); track dept.departmentId) {
                <div
                  class="card-notion group cursor-pointer hover:shadow-notion-md transition-shadow duration-200"
                  [class.opacity-60]="!dept.isActive"
                  @fadeSlideIn
                  (click)="openEdit(dept)"
                  (keydown.enter)="openEdit(dept)"
                  tabindex="0"
                  role="button"
                  [attr.aria-label]="'Edit department: ' + dept.name"
                >
                  <div class="flex items-start justify-between mb-2">
                    <div class="flex items-center gap-2 min-w-0">
                      <h3 class="text-base font-semibold text-neutral-900 truncate">
                        {{ dept.name }}
                      </h3>
                      @if (!dept.isActive) {
                        <span class="badge-inactive">Inactive</span>
                      }
                    </div>
                    <!-- Actions (visible on hover) -->
                    <div class="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity flex-shrink-0">
                      @if (dept.isActive) {
                        <button
                          type="button"
                          class="action-btn action-btn-danger"
                          (click)="confirmDeactivate(dept, $event)"
                          [attr.aria-label]="'Deactivate department: ' + dept.name"
                          title="Deactivate"
                        >
                          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                            <path d="M2 3a1 1 0 0 0-1 1v1a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1H2Z" />
                            <path fill-rule="evenodd" d="M2 7.5h16l-.811 7.71a2 2 0 0 1-1.99 1.79H4.802a2 2 0 0 1-1.99-1.79L2 7.5Zm5.22 1.72a.75.75 0 0 1 1.06 0L10 10.94l1.72-1.72a.75.75 0 1 1 1.06 1.06L11.06 12l1.72 1.72a.75.75 0 1 1-1.06 1.06L10 13.06l-1.72 1.72a.75.75 0 0 1-1.06-1.06L8.94 12l-1.72-1.72a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd" />
                          </svg>
                        </button>
                      }
                    </div>
                  </div>

                  @if (dept.description) {
                    <p class="text-sm text-neutral-500 mb-3 line-clamp-2">
                      {{ dept.description }}
                    </p>
                  }

                  <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-neutral-400 mt-auto">
                    @if (dept.parentDepartmentName) {
                      <span class="flex items-center gap-1" [title]="'Parent: ' + dept.parentDepartmentName">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                          <path fill-rule="evenodd" d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd" />
                        </svg>
                        {{ dept.parentDepartmentName }}
                      </span>
                    } @else {
                      <span class="flex items-center gap-1">
                        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                          <path d="M8.75 3.75a.75.75 0 0 0-1.5 0v3.5h-3.5a.75.75 0 0 0 0 1.5h3.5v3.5a.75.75 0 0 0 1.5 0v-3.5h3.5a.75.75 0 0 0 0-1.5h-3.5v-3.5Z" />
                        </svg>
                        Root
                      </span>
                    }
                    <span class="flex items-center gap-1">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                        <path d="M8 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM12.735 14c.618 0 1.093-.561.872-1.139a6.002 6.002 0 0 0-11.215 0c-.22.578.255 1.139.872 1.139h9.47Z" />
                      </svg>
                      {{ dept.employeeCount }} {{ dept.employeeCount === 1 ? 'employee' : 'employees' }}
                    </span>
                    <!-- Manager: TODO(US-CHR-001) — shows dash until Employee entity exists -->
                    <span class="flex items-center gap-1" title="Department manager">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                        <path fill-rule="evenodd" d="M15 8A7 7 0 1 1 1 8a7 7 0 0 1 14 0Zm-5-2a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm-2 9c-2.841 0-4.263-.722-5.004-1.483-.173-.177-.18-.454-.023-.644A4.504 4.504 0 0 1 6.5 10.5h3a4.504 4.504 0 0 1 3.527 2.373c.157.19.15.467-.023.644C12.263 14.278 10.841 15 8 15Z" clip-rule="evenodd" />
                      </svg>
                      {{ dept.managerName || '—' }}
                    </span>
                  </div>
                </div>
              }
            </div>
          }

          <!-- Tree view -->
          @if (viewMode() === 'tree') {
            <app-department-tree
              [departments]="departments()"
              (editDepartment)="openEdit($event)"
              (deactivateDepartment)="confirmDeactivate($event)"
            />
          }
        }
      }

      <!-- Slide-over form panel (AC-1) -->
      @if (formOpen()) {
        <div
          class="fixed inset-0 z-50"
          (keydown.escape)="closeForm()"
        >
          <!-- Overlay -->
          <div
            class="fixed inset-0 bg-black/20 backdrop-blur-sm"
            @overlayFade
            (click)="closeForm()"
            aria-hidden="true"
          ></div>

          <!-- Slide-over panel -->
          <div
            class="fixed inset-y-0 right-0 w-full sm:w-[28rem] bg-white shadow-notion-lg overflow-y-auto"
            @slideOver
            role="dialog"
            aria-modal="true"
            [attr.aria-label]="editingDepartment() ? 'Edit department' : 'Create department'"
          >
            <app-department-form
              [department]="editingDepartment()"
              [allDepartments]="activeDepartments()"
              (saved)="onFormSaved()"
              (cancelled)="closeForm()"
            />
          </div>
        </div>
      }

      <!-- Deactivate confirmation dialog (AC-5) -->
      @if (departmentToDeactivate()) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/20 backdrop-blur-sm px-4"
          (click)="cancelDeactivate()"
          (keydown.escape)="cancelDeactivate()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="deactivate-dialog-title"
        >
          <div
            class="w-full max-w-md rounded-xl bg-white shadow-notion-lg p-6"
            (click)="$event.stopPropagation()"
          >
            <h3
              id="deactivate-dialog-title"
              class="text-lg font-semibold text-neutral-900 mb-2"
            >
              Deactivate Department
            </h3>
            <p class="text-sm text-neutral-600 mb-1">
              Are you sure you want to deactivate
              <strong>{{ departmentToDeactivate()!.name }}</strong>?
            </p>
            @if (departmentToDeactivate()!.employeeCount > 0) {
              <p class="text-sm text-amber-600 bg-amber-50 rounded-lg px-3 py-2 mt-3">
                This department has {{ departmentToDeactivate()!.employeeCount }}
                active {{ departmentToDeactivate()!.employeeCount === 1 ? 'employee' : 'employees' }}.
                Please reassign them before deactivating.
              </p>
            }
            <div class="flex justify-end gap-3 mt-6">
              <button
                type="button"
                class="btn-secondary"
                (click)="cancelDeactivate()"
              >
                Cancel
              </button>
              <button
                type="button"
                class="btn-danger"
                (click)="deactivateDepartment()"
                [disabled]="isDeactivating() || departmentToDeactivate()!.employeeCount > 0"
              >
                @if (isDeactivating()) {
                  <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  Deactivating...
                } @else {
                  Deactivate
                }
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

    .line-clamp-2 {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .badge-inactive {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium
        bg-neutral-100 text-neutral-500 whitespace-nowrap;
    }

    .view-toggle {
      @apply inline-flex items-center rounded-lg bg-neutral-100 p-0.5;
    }

    .toggle-btn {
      @apply flex items-center justify-center w-8 h-8 rounded-md text-neutral-400
        transition-all duration-200 hover:text-neutral-600
        focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-brand-500;
    }

    .toggle-active {
      @apply bg-white text-neutral-900 shadow-sm;
    }

    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 transition-colors duration-150;
    }

    .action-btn-danger {
      @apply hover:text-red-500 hover:bg-red-50;
    }

    .btn-danger {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-red-700 focus-visible:outline-2 focus-visible:outline-offset-2
        focus-visible:outline-red-600 disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class DepartmentListComponent implements OnInit {
  private readonly departmentService = inject(DepartmentService);
  private readonly toastr = inject(ToastrService);

  readonly departments = signal<IDepartment[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly viewMode = signal<'list' | 'tree'>('list');

  // Form slide-over state
  readonly formOpen = signal(false);
  readonly editingDepartment = signal<IDepartment | null>(null);

  // Deactivation dialog state
  readonly departmentToDeactivate = signal<IDepartment | null>(null);
  readonly isDeactivating = signal(false);

  /** Only active departments are available as parent options in the form */
  readonly activeDepartments = computed(() =>
    this.departments().filter((d) => d.isActive)
  );

  /** Skeleton loading placeholder items */
  readonly skeletonItems = [1, 2, 3, 4, 5, 6];

  ngOnInit(): void {
    this.loadDepartments();
  }

  loadDepartments(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.departmentService.getDepartments().subscribe({
      next: (departments) => {
        this.departments.set(departments);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load departments. Please try again.'
        );
      },
    });
  }

  // ─── Form Slide-over ──────────────────────────────────────

  openCreate(): void {
    this.editingDepartment.set(null);
    this.formOpen.set(true);
  }

  openEdit(department: IDepartment): void {
    this.editingDepartment.set(department);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingDepartment.set(null);
  }

  onFormSaved(): void {
    this.closeForm();
    this.loadDepartments();
  }

  // ─── Deactivation ─────────────────────────────────────────

  confirmDeactivate(department: IDepartment, event?: Event): void {
    event?.stopPropagation();
    this.departmentToDeactivate.set(department);
  }

  cancelDeactivate(): void {
    this.departmentToDeactivate.set(null);
  }

  deactivateDepartment(): void {
    const dept = this.departmentToDeactivate();
    if (!dept) return;

    // AC-5: Block if department has active employees
    if (dept.employeeCount > 0) {
      return;
    }

    this.isDeactivating.set(true);

    this.departmentService.deactivateDepartment(dept.departmentId).subscribe({
      next: () => {
        this.toastr.success(`"${dept.name}" has been deactivated.`);
        this.departmentToDeactivate.set(null);
        this.isDeactivating.set(false);
        this.loadDepartments();
      },
      error: (err: HttpErrorResponse) => {
        this.isDeactivating.set(false);
        const body = err.error as IDepartmentErrorResponse | undefined;
        if (body?.code === 'has_active_employees') {
          this.toastr.warning(
            body.message ||
              `This department has ${body.employeeCount ?? 'some'} active employees. Please reassign them before deactivating.`
          );
        } else {
          this.toastr.error(
            body?.message || 'Failed to deactivate department.'
          );
        }
      },
    });
  }
}
