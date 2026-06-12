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
import { JobTitleService } from '../../services/job-title.service';
import { IJobTitle, IJobTitleErrorResponse } from '../../models/job-title.models';
import { JobTitleFormComponent } from '../job-title-form/job-title-form.component';

/**
 * US-CHR-005: Job Titles list page.
 *
 * Displays job titles as a card-based table with search (AC-1).
 * Columns: Title Name, Grade (if linked), Employee Count, Status, Actions.
 * Supports creating, editing, and deactivating job titles via a slide-over
 * form panel.
 *
 * Role-gated to Tenant Admin / HR Officer via the route guard.
 */
@Component({
  selector: 'app-job-title-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    JobTitleFormComponent,
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
            Job Titles
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Manage job titles and positions across your organization.
          </p>
        </div>
        <button
          type="button"
          class="btn-primary"
          (click)="openCreate()"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
          </svg>
          Add Job Title
        </button>
      </div>

      <!-- Search bar -->
      @if (!isLoading() && !loadError() && jobTitles().length > 0) {
        <div class="mb-5">
          <div class="relative max-w-sm">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400 pointer-events-none"
              aria-hidden="true"
            >
              <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd" />
            </svg>
            <input
              type="search"
              class="input-notion pl-9"
              placeholder="Search job titles..."
              [ngModel]="searchQuery()"
              (ngModelChange)="searchQuery.set($event)"
              aria-label="Search job titles"
            />
          </div>
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion overflow-hidden">
          <div class="animate-pulse space-y-4 p-2">
            <div class="h-5 bg-neutral-100 rounded w-1/3 mb-4"></div>
            @for (i of skeletonItems; track i) {
              <div class="flex items-center gap-4">
                <div class="h-4 bg-neutral-50 rounded w-2/5"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/6"></div>
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
          <div class="w-12 h-12 rounded-full bg-red-50 flex items-center justify-center mx-auto mb-4">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-red-500" aria-hidden="true">
              <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
            </svg>
          </div>
          <p class="text-sm text-neutral-600">{{ loadError() }}</p>
          <button class="btn-secondary mt-4" (click)="loadJobTitles()">
            Try Again
          </button>
        </div>
      }

      <!-- Content -->
      @if (!isLoading() && !loadError()) {
        @if (jobTitles().length === 0) {
          <!-- Empty state -->
          <div class="card-notion text-center py-12">
            <div class="w-14 h-14 rounded-full bg-neutral-50 flex items-center justify-center mx-auto mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-7 h-7 text-neutral-400" aria-hidden="true">
                <path fill-rule="evenodd" d="M6 3.75A2.75 2.75 0 0 1 8.75 1h2.5A2.75 2.75 0 0 1 14 3.75v.443c.572.055 1.14.122 1.706.2C17.053 4.582 18 5.75 18 7.07v3.469c0 1.126-.694 2.191-1.83 2.54-1.952.599-4.024.921-6.17.921s-4.219-.322-6.17-.921C2.694 12.73 2 11.665 2 10.539V7.07c0-1.321.947-2.489 2.294-2.676A41.047 41.047 0 0 1 6 4.193V3.75Zm6.5 0v.325a41.622 41.622 0 0 0-5 0V3.75c0-.69.56-1.25 1.25-1.25h2.5c.69 0 1.25.56 1.25 1.25ZM10 10a1 1 0 0 0-1 1v.01a1 1 0 0 0 1 1h.01a1 1 0 0 0 1-1V11a1 1 0 0 0-1-1H10Z" clip-rule="evenodd" />
                <path d="M3 15.055v-.684c.126.053.255.1.39.142 2.092.642 4.313.987 6.61.987 2.297 0 4.518-.345 6.61-.987.135-.041.264-.089.39-.142v.684c0 1.347-.985 2.53-2.363 2.686a41.454 41.454 0 0 1-9.274 0C3.985 17.585 3 16.402 3 15.055Z" />
              </svg>
            </div>
            <p class="text-sm font-medium text-neutral-700 mb-1">No job titles yet</p>
            <p class="text-xs text-neutral-400 mb-4">
              Create your first job title to standardize positions across your organization.
            </p>
            <button type="button" class="btn-primary" (click)="openCreate()">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
                <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
              </svg>
              Add Job Title
            </button>
          </div>
        } @else {
          <!-- Table card (AC-1) -->
          <div class="card-notion overflow-hidden p-0" @fadeSlideIn>
            <!-- Desktop table -->
            <div class="hidden sm:block overflow-x-auto">
              <table class="w-full" role="table">
                <thead>
                  <tr class="border-b border-neutral-100">
                    <th class="th-notion text-left">Title Name</th>
                    <th class="th-notion text-left">Grade</th>
                    <th class="th-notion text-center">Employees</th>
                    <th class="th-notion text-center">Status</th>
                    <th class="th-notion text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  @for (jt of filteredJobTitles(); track jt.jobTitleId) {
                    <tr
                      class="table-row-notion group"
                      [class.opacity-60]="!jt.isActive"
                      (click)="openEdit(jt)"
                      (keydown.enter)="openEdit(jt)"
                      tabindex="0"
                      role="button"
                      [attr.aria-label]="'Edit job title: ' + jt.titleName"
                    >
                      <td class="td-notion">
                        <div class="flex items-center gap-2">
                          <span class="font-medium text-neutral-900">{{ jt.titleName }}</span>
                        </div>
                        @if (jt.description) {
                          <p class="text-xs text-neutral-400 mt-0.5 line-clamp-1">{{ jt.description }}</p>
                        }
                      </td>
                      <td class="td-notion text-neutral-500">
                        <!-- TODO(US-CHR-005): Show grade name once Grade entity exists -->
                        {{ jt.gradeName || '—' }}
                      </td>
                      <td class="td-notion text-center">
                        <!-- TODO(US-CHR-001): Show actual employee count once Employee entity exists -->
                        <span class="inline-flex items-center justify-center px-2 py-0.5 rounded-full text-xs font-medium bg-neutral-100 text-neutral-500">
                          —
                        </span>
                      </td>
                      <td class="td-notion text-center">
                        @if (jt.isActive) {
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
                            (click)="openEdit(jt); $event.stopPropagation()"
                            [attr.aria-label]="'Edit job title: ' + jt.titleName"
                            title="Edit"
                          >
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                              <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z" />
                              <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z" />
                            </svg>
                          </button>
                          @if (jt.isActive) {
                            <button
                              type="button"
                              class="action-btn action-btn-danger"
                              (click)="confirmDeactivate(jt, $event)"
                              [attr.aria-label]="'Deactivate job title: ' + jt.titleName"
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
                      <td colspan="5" class="td-notion text-center text-neutral-400 py-8">
                        No job titles match your search.
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <!-- Mobile card list -->
            <div class="sm:hidden divide-y divide-neutral-100">
              @for (jt of filteredJobTitles(); track jt.jobTitleId) {
                <div
                  class="p-4 hover:bg-neutral-50 transition-colors duration-150 cursor-pointer"
                  [class.opacity-60]="!jt.isActive"
                  (click)="openEdit(jt)"
                  (keydown.enter)="openEdit(jt)"
                  tabindex="0"
                  role="button"
                  [attr.aria-label]="'Edit job title: ' + jt.titleName"
                >
                  <div class="flex items-start justify-between mb-1">
                    <h3 class="text-sm font-semibold text-neutral-900">
                      {{ jt.titleName }}
                    </h3>
                    @if (jt.isActive) {
                      <span class="badge-active">Active</span>
                    } @else {
                      <span class="badge-inactive">Inactive</span>
                    }
                  </div>
                  @if (jt.description) {
                    <p class="text-xs text-neutral-400 mb-2 line-clamp-2">{{ jt.description }}</p>
                  }
                  <div class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-400">
                    <span class="flex items-center gap-1">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                        <path fill-rule="evenodd" d="M15 8A7 7 0 1 1 1 8a7 7 0 0 1 14 0Zm-5-2a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm-2 9c-2.841 0-4.263-.722-5.004-1.483-.173-.177-.18-.454-.023-.644A4.504 4.504 0 0 1 6.5 10.5h3a4.504 4.504 0 0 1 3.527 2.373c.157.19.15.467-.023.644C12.263 14.278 10.841 15 8 15Z" clip-rule="evenodd" />
                      </svg>
                      <!-- TODO(US-CHR-001): Show actual employee count -->
                      — employees
                    </span>
                    <span class="flex items-center gap-1" title="Salary grade">
                      Grade: {{ jt.gradeName || '—' }}
                    </span>
                  </div>
                  <div class="flex items-center gap-2 mt-3">
                    @if (jt.isActive) {
                      <button
                        type="button"
                        class="text-xs text-red-500 hover:text-red-700 transition-colors"
                        (click)="confirmDeactivate(jt, $event)"
                        [attr.aria-label]="'Deactivate job title: ' + jt.titleName"
                      >
                        Deactivate
                      </button>
                    }
                  </div>
                </div>
              } @empty {
                <div class="p-6 text-center text-sm text-neutral-400">
                  No job titles match your search.
                </div>
              }
            </div>
          </div>
        }
      }

      <!-- Slide-over form panel -->
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
            [attr.aria-label]="editingJobTitle() ? 'Edit job title' : 'Create job title'"
          >
            <app-job-title-form
              [jobTitle]="editingJobTitle()"
              (saved)="onFormSaved()"
              (cancelled)="closeForm()"
            />
          </div>
        </div>
      }

      <!-- Deactivate confirmation dialog (AC-5) -->
      @if (jobTitleToDeactivate()) {
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
              Deactivate Job Title
            </h3>
            <p class="text-sm text-neutral-600 mb-1">
              Are you sure you want to deactivate
              <strong>{{ jobTitleToDeactivate()!.titleName }}</strong>?
            </p>
            <p class="text-xs text-neutral-400 mt-2">
              Deactivated job titles will be hidden from assignment dropdowns but remain visible in admin views.
            </p>
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
                (click)="deactivateJobTitle()"
                [disabled]="isDeactivating()"
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

    .line-clamp-1 {
      display: -webkit-box;
      -webkit-line-clamp: 1;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .line-clamp-2 {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    /* --- Table styles (Notion-inspired) --- */

    .th-notion {
      @apply px-4 py-3 text-xs font-semibold uppercase tracking-wider text-neutral-400
        whitespace-nowrap;
    }

    .td-notion {
      @apply px-4 py-3.5 text-sm;
    }

    .table-row-notion {
      @apply border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors
        duration-150 cursor-pointer;
    }

    .table-row-notion:last-child {
      @apply border-b-0;
    }

    /* --- Badges --- */

    .badge-active {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium
        bg-green-50 text-green-700 whitespace-nowrap;
    }

    .badge-inactive {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium
        bg-neutral-100 text-neutral-500 whitespace-nowrap;
    }

    /* --- Action buttons --- */

    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 transition-colors duration-150
        hover:text-neutral-600 hover:bg-neutral-100;
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
export class JobTitleListComponent implements OnInit {
  private readonly jobTitleService = inject(JobTitleService);
  private readonly toastr = inject(ToastrService);

  readonly jobTitles = signal<IJobTitle[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly searchQuery = signal('');

  // Form slide-over state
  readonly formOpen = signal(false);
  readonly editingJobTitle = signal<IJobTitle | null>(null);

  // Deactivation dialog state
  readonly jobTitleToDeactivate = signal<IJobTitle | null>(null);
  readonly isDeactivating = signal(false);

  /** Filter job titles by search query (AC-1) */
  readonly filteredJobTitles = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    const titles = this.jobTitles();
    if (!query) return titles;
    return titles.filter(
      (jt) =>
        jt.titleName.toLowerCase().includes(query) ||
        (jt.description && jt.description.toLowerCase().includes(query))
    );
  });

  /** Skeleton loading placeholder items */
  readonly skeletonItems = [1, 2, 3, 4, 5, 6];

  ngOnInit(): void {
    this.loadJobTitles();
  }

  loadJobTitles(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.jobTitleService.getJobTitles().subscribe({
      next: (jobTitles) => {
        this.jobTitles.set(jobTitles);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load job titles. Please try again.'
        );
      },
    });
  }

  // --- Form Slide-over ----------------------------------------

  openCreate(): void {
    this.editingJobTitle.set(null);
    this.formOpen.set(true);
  }

  openEdit(jobTitle: IJobTitle): void {
    this.editingJobTitle.set(jobTitle);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingJobTitle.set(null);
  }

  onFormSaved(): void {
    this.closeForm();
    this.loadJobTitles();
  }

  // --- Deactivation -------------------------------------------

  confirmDeactivate(jobTitle: IJobTitle, event?: Event): void {
    event?.stopPropagation();
    this.jobTitleToDeactivate.set(jobTitle);
  }

  cancelDeactivate(): void {
    this.jobTitleToDeactivate.set(null);
  }

  deactivateJobTitle(): void {
    const jt = this.jobTitleToDeactivate();
    if (!jt) return;

    this.isDeactivating.set(true);

    this.jobTitleService.deactivateJobTitle(jt.jobTitleId).subscribe({
      next: () => {
        this.toastr.success(`"${jt.titleName}" has been deactivated.`);
        this.jobTitleToDeactivate.set(null);
        this.isDeactivating.set(false);
        this.loadJobTitles();
      },
      error: (err: HttpErrorResponse) => {
        this.isDeactivating.set(false);
        const body = err.error as IJobTitleErrorResponse | undefined;
        if (body?.code === 'has_active_employees') {
          // AC-5: warn when job title is assigned to active employees
          this.toastr.warning(
            body.message ||
              `This job title is assigned to ${body.employeeCount ?? 'some'} active employees. Reassign them before deactivating.`
          );
        } else {
          this.toastr.error(
            body?.message || 'Failed to deactivate job title.'
          );
        }
      },
    });
  }
}
