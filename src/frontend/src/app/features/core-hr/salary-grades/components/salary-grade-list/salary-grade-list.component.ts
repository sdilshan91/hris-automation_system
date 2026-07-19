import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { SalaryGradeService } from '../../services/salary-grade.service';
import {
  ISalaryGrade,
  ISalaryGradeErrorResponse,
} from '../../models/salary-grade.models';
import { SalaryGradeFormComponent } from '../salary-grade-form/salary-grade-form.component';

/**
 * ISSUE-021: Salary Grades admin list page.
 *
 * Card-based table with search. Columns: Code, Name, Min/Mid/Max, Currency,
 * Status, Actions. Create / edit via a slide-over form panel; deactivate via
 * a soft-delete confirmation dialog.
 *
 * Role-gated to Tenant Admin / HR Officer via the route guard.
 */
@Component({
  selector: 'app-salary-grade-list',
  standalone: true,
  imports: [CommonModule, FormsModule, SalaryGradeFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
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
    trigger('overlayFade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Salary Grades
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Define pay bands and link them to job titles.
          </p>
        </div>
        <button type="button" class="btn-primary" (click)="openCreate()">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
          </svg>
          Add Salary Grade
        </button>
      </div>

      <!-- Search + include-inactive -->
      @if (!isLoading() && !loadError() && grades().length > 0) {
        <div class="mb-5 flex flex-col sm:flex-row sm:items-center gap-3">
          <div class="relative max-w-sm w-full">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400 pointer-events-none" aria-hidden="true">
              <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd" />
            </svg>
            <input
              type="search"
              class="input-notion pl-9"
              placeholder="Search grades..."
              [ngModel]="searchQuery()"
              (ngModelChange)="searchQuery.set($event)"
              aria-label="Search salary grades"
            />
          </div>
          <label class="inline-flex items-center gap-2 text-sm text-neutral-600">
            <input
              type="checkbox"
              [ngModel]="includeInactive()"
              (ngModelChange)="onIncludeInactiveChange($event)"
              aria-label="Include inactive grades"
            />
            Show inactive
          </label>
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion overflow-hidden">
          <div class="animate-pulse space-y-4 p-2">
            <div class="h-5 bg-neutral-100 rounded w-1/3 mb-4"></div>
            @for (i of skeletonItems; track i) {
              <div class="flex items-center gap-4">
                <div class="h-4 bg-neutral-50 rounded w-1/6"></div>
                <div class="h-4 bg-neutral-50 rounded w-2/5"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/6"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/6"></div>
              </div>
            }
          </div>
        </div>
      }

      <!-- Error state -->
      @if (loadError()) {
        <div class="card-notion text-center py-12">
          <p class="text-sm text-neutral-600">{{ loadError() }}</p>
          <button class="btn-secondary mt-4" (click)="loadGrades()">Try Again</button>
        </div>
      }

      <!-- Content -->
      @if (!isLoading() && !loadError()) {
        @if (grades().length === 0) {
          <div class="card-notion text-center py-12">
            <p class="text-sm font-medium text-neutral-700 mb-1">No salary grades yet</p>
            <p class="text-xs text-neutral-400 mb-4">
              Create your first grade to define pay bands.
            </p>
            <button type="button" class="btn-primary" (click)="openCreate()">
              Add Salary Grade
            </button>
          </div>
        } @else {
          <div class="card-notion overflow-hidden p-0" @fadeSlideIn>
            <div class="overflow-x-auto">
              <table class="w-full" role="table">
                <thead>
                  <tr class="border-b border-neutral-100">
                    <th class="th-notion text-left">Code</th>
                    <th class="th-notion text-left">Name</th>
                    <th class="th-notion text-right">Min</th>
                    <th class="th-notion text-right">Mid</th>
                    <th class="th-notion text-right">Max</th>
                    <th class="th-notion text-center">Currency</th>
                    <th class="th-notion text-center">Status</th>
                    <th class="th-notion text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  @for (g of filteredGrades(); track g.id) {
                    <tr
                      class="table-row-notion group"
                      [class.opacity-60]="!g.isActive"
                      (click)="openEdit(g)"
                      (keydown.enter)="openEdit(g)"
                      tabindex="0"
                      [attr.aria-label]="'Edit salary grade: ' + g.name"
                    >
                      <td class="td-notion font-medium text-neutral-900">{{ g.code }}</td>
                      <td class="td-notion text-neutral-700">{{ g.name }}</td>
                      <td class="td-notion text-right text-neutral-600">{{ g.minAmount | number }}</td>
                      <td class="td-notion text-right text-neutral-600">
                        {{ g.midAmount != null ? (g.midAmount | number) : '—' }}
                      </td>
                      <td class="td-notion text-right text-neutral-600">{{ g.maxAmount | number }}</td>
                      <td class="td-notion text-center text-neutral-500">{{ g.currency }}</td>
                      <td class="td-notion text-center">
                        @if (g.isActive) {
                          <span class="badge-active">Active</span>
                        } @else {
                          <span class="badge-inactive">Inactive</span>
                        }
                      </td>
                      <td class="td-notion text-right">
                        <div class="flex items-center justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                          <button
                            type="button"
                            class="action-btn"
                            (click)="openEdit(g); $event.stopPropagation()"
                            [attr.aria-label]="'Edit salary grade: ' + g.name"
                            title="Edit"
                          >
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                              <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z" />
                              <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z" />
                            </svg>
                          </button>
                          @if (g.isActive) {
                            <button
                              type="button"
                              class="action-btn action-btn-danger"
                              (click)="confirmDeactivate(g, $event)"
                              [attr.aria-label]="'Deactivate salary grade: ' + g.name"
                              title="Deactivate"
                            >
                              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                                <path d="M2 3a1 1 0 0 0-1 1v1a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1H2Z" />
                                <path fill-rule="evenodd" d="M2 7.5h16l-.811 7.71a2 2 0 0 1-1.99 1.79H4.802a2 2 0 0 1-1.99-1.79L2 7.5Zm5.22 1.72a.75.75 0 0 1 1.06 0L10 10.94l1.72-1.72a.75.75 0 1 1 1.06 1.06L11.06 12l1.72 1.72a.75.75 0 1 1-1.06 1.06L10 13.06l-1.72 1.72a.75.75 0 0 1-1.06-1.06L8.94 12l-1.72-1.72a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd" />
                              </svg>
                            </button>
                          }
                        </div>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="8" class="td-notion text-center text-neutral-400 py-8">
                        No salary grades match your search.
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        }
      }

      <!-- Slide-over form panel -->
      @if (formOpen()) {
        <div class="fixed inset-0 z-50" (keydown.escape)="closeForm()">
          <div class="fixed inset-0 bg-black/20 backdrop-blur-sm" @overlayFade (click)="closeForm()" aria-hidden="true"></div>
          <div
            class="fixed inset-y-0 right-0 w-full sm:w-[32rem] bg-white shadow-notion-lg overflow-y-auto"
            @slideOver
            role="dialog"
            aria-modal="true"
            [attr.aria-label]="editingGrade() ? 'Edit salary grade' : 'Create salary grade'"
          >
            <app-salary-grade-form
              [grade]="editingGrade()"
              (saved)="onFormSaved()"
              (cancelled)="closeForm()"
            />
          </div>
        </div>
      }

      <!-- Deactivate confirmation dialog -->
      @if (gradeToDeactivate()) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/20 backdrop-blur-sm px-4"
          (click)="cancelDeactivate()"
          (keydown.escape)="cancelDeactivate()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="deactivate-dialog-title"
        >
          <div class="w-full max-w-md rounded-xl bg-white shadow-notion-lg p-6" (click)="$event.stopPropagation()">
            <h3 id="deactivate-dialog-title" class="text-lg font-semibold text-neutral-900 mb-2">
              Deactivate Salary Grade
            </h3>
            <p class="text-sm text-neutral-600 mb-1">
              Are you sure you want to deactivate
              <strong>{{ gradeToDeactivate()!.name }}</strong>?
            </p>
            <p class="text-xs text-neutral-400 mt-2">
              Deactivated grades are hidden from job-title grade pickers but remain in admin views.
            </p>
            <div class="flex justify-end gap-3 mt-6">
              <button type="button" class="btn-secondary" (click)="cancelDeactivate()">
                Cancel
              </button>
              <button
                type="button"
                class="btn-danger"
                (click)="deactivateGrade()"
                [disabled]="isDeactivating()"
              >
                @if (isDeactivating()) {
                  <span class="btn-spinner"></span>
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

    .th-notion {
      @apply px-4 py-3 text-xs font-semibold uppercase tracking-wider text-neutral-400 whitespace-nowrap;
    }

    .td-notion {
      @apply px-4 py-3.5 text-sm;
    }

    .table-row-notion {
      @apply border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors duration-150 cursor-pointer;
    }

    .table-row-notion:last-child {
      @apply border-b-0;
    }

    .badge-active {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-green-50 text-green-700 whitespace-nowrap;
    }

    .badge-inactive {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-neutral-100 text-neutral-500 whitespace-nowrap;
    }

    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center text-neutral-400 transition-colors duration-150 hover:text-neutral-600 hover:bg-neutral-100;
    }

    .action-btn-danger {
      @apply hover:text-red-500 hover:bg-red-50;
    }

    .btn-danger {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2.5 text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-red-700 disabled:opacity-50 disabled:cursor-not-allowed;
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
export class SalaryGradeListComponent implements OnInit {
  private readonly gradeService = inject(SalaryGradeService);
  private readonly toastr = inject(ToastrService);

  readonly grades = signal<ISalaryGrade[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly searchQuery = signal('');
  readonly includeInactive = signal(false);

  // Form slide-over state
  readonly formOpen = signal(false);
  readonly editingGrade = signal<ISalaryGrade | null>(null);

  // Deactivation dialog state
  readonly gradeToDeactivate = signal<ISalaryGrade | null>(null);
  readonly isDeactivating = signal(false);

  readonly filteredGrades = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    const rows = this.grades();
    if (!query) return rows;
    return rows.filter(
      (g) =>
        g.code.toLowerCase().includes(query) ||
        g.name.toLowerCase().includes(query)
    );
  });

  readonly skeletonItems = [1, 2, 3, 4, 5, 6];

  ngOnInit(): void {
    this.loadGrades();
  }

  loadGrades(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.gradeService.list(this.includeInactive()).subscribe({
      next: (grades) => {
        this.grades.set(grades);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load salary grades. Please try again.'
        );
      },
    });
  }

  onIncludeInactiveChange(value: boolean): void {
    this.includeInactive.set(value);
    this.loadGrades();
  }

  // --- Form slide-over ---------------------------------------

  openCreate(): void {
    this.editingGrade.set(null);
    this.formOpen.set(true);
  }

  openEdit(grade: ISalaryGrade): void {
    this.editingGrade.set(grade);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingGrade.set(null);
  }

  onFormSaved(): void {
    this.closeForm();
    this.loadGrades();
  }

  // --- Deactivation ------------------------------------------

  confirmDeactivate(grade: ISalaryGrade, event?: Event): void {
    event?.stopPropagation();
    this.gradeToDeactivate.set(grade);
  }

  cancelDeactivate(): void {
    this.gradeToDeactivate.set(null);
  }

  deactivateGrade(): void {
    const g = this.gradeToDeactivate();
    if (!g) return;

    this.isDeactivating.set(true);

    this.gradeService.deactivate(g.id).subscribe({
      next: () => {
        this.toastr.success(`"${g.name}" has been deactivated.`);
        this.gradeToDeactivate.set(null);
        this.isDeactivating.set(false);
        this.loadGrades();
      },
      error: (err: HttpErrorResponse) => {
        this.isDeactivating.set(false);
        const body = err.error as ISalaryGradeErrorResponse | undefined;
        this.toastr.error(body?.message || 'Failed to deactivate salary grade.');
      },
    });
  }
}
