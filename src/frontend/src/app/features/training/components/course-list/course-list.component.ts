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
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { TrainingService } from '../../services/training.service';
import {
  ICourse,
  CourseStatus,
  TrainingMode,
  ITrainingErrorResponse,
} from '../../models/training.models';
import { CourseFormComponent } from '../course-form/course-form.component';

/**
 * US-TRN-001: Training catalog list page.
 *
 * Shows courses as a card-based table with status, enrolled/capacity, and
 * waitlist counts. Supports create/edit (Manage), status transitions (AC-2),
 * and self-enrollment on Open courses (AC-4/AC-5). Role-gated via the route
 * guard (Tenant Admin / HR Officer).
 */
@Component({
  selector: 'app-course-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, CourseFormComponent],
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
            Training Catalog
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Manage courses and enrollments across your organization.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <a routerLink="/training/my-enrollments" class="btn-secondary">
            My Enrollments
          </a>
          <button type="button" class="btn-primary" (click)="openCreate()">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
              <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
            </svg>
            Add Course
          </button>
        </div>
      </div>

      <!-- Search -->
      @if (!isLoading() && !loadError() && courses().length > 0) {
        <div class="mb-5">
          <div class="relative max-w-sm">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400 pointer-events-none" aria-hidden="true">
              <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd" />
            </svg>
            <input
              type="search"
              class="input-notion pl-9"
              placeholder="Search courses..."
              [ngModel]="searchQuery()"
              (ngModelChange)="searchQuery.set($event)"
              aria-label="Search courses"
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

      <!-- Error -->
      @if (loadError()) {
        <div class="card-notion text-center py-12">
          <div class="w-12 h-12 rounded-full bg-red-50 flex items-center justify-center mx-auto mb-4">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-red-500" aria-hidden="true">
              <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
            </svg>
          </div>
          <p class="text-sm text-neutral-600">{{ loadError() }}</p>
          <button class="btn-secondary mt-4" (click)="loadCourses()">Try Again</button>
        </div>
      }

      <!-- Content -->
      @if (!isLoading() && !loadError()) {
        @if (courses().length === 0) {
          <div class="card-notion text-center py-12">
            <div class="w-14 h-14 rounded-full bg-neutral-50 flex items-center justify-center mx-auto mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-7 h-7 text-neutral-400" aria-hidden="true">
                <path d="M11 4a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h7Zm5 0a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1h-1V4h1Z" />
              </svg>
            </div>
            <p class="text-sm font-medium text-neutral-700 mb-1">No courses yet</p>
            <p class="text-xs text-neutral-400 mb-4">
              Create your first training course to start enrolling employees.
            </p>
            <button type="button" class="btn-primary" (click)="openCreate()">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
                <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
              </svg>
              Add Course
            </button>
          </div>
        } @else {
          <div class="card-notion overflow-hidden p-0" @fadeSlideIn>
            <div class="hidden sm:block overflow-x-auto">
              <table class="w-full" role="table">
                <thead>
                  <tr class="border-b border-neutral-100">
                    <th class="th-notion text-left">Course</th>
                    <th class="th-notion text-left">Mode</th>
                    <th class="th-notion text-center">Status</th>
                    <th class="th-notion text-center">Enrolled</th>
                    <th class="th-notion text-center">Waitlist</th>
                    <th class="th-notion text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of filteredCourses(); track c.id) {
                    <tr
                      class="table-row-notion group"
                      (click)="openEdit(c)"
                      (keydown.enter)="openEdit(c)"
                      tabindex="0"
                      [attr.aria-label]="'Edit course: ' + c.title"
                    >
                      <td class="td-notion">
                        <span class="font-medium text-neutral-900">{{ c.title }}</span>
                        @if (c.instructor) {
                          <p class="text-xs text-neutral-400 mt-0.5 line-clamp-1">{{ c.instructor }}</p>
                        }
                      </td>
                      <td class="td-notion text-neutral-500">{{ modeLabel(c.mode) }}</td>
                      <td class="td-notion text-center">
                        <span class="badge" [class]="statusClass(c.status)">{{ c.status }}</span>
                      </td>
                      <td class="td-notion text-center text-neutral-600">
                        {{ c.enrolledCount }} / {{ c.capacity === null ? '∞' : c.capacity }}
                      </td>
                      <td class="td-notion text-center text-neutral-600">{{ c.waitlistCount }}</td>
                      <td class="td-notion text-right">
                        <div class="flex items-center justify-end gap-1.5 opacity-0 group-hover:opacity-100 transition-opacity">
                          @if (c.status === 'Open') {
                            <button
                              type="button"
                              class="text-btn"
                              (click)="enroll(c, $event)"
                              [disabled]="enrollingCourseId() === c.id"
                              [attr.aria-label]="'Enroll in course: ' + c.title"
                            >
                              Enroll
                            </button>
                          }
                          @if (allowedTransitions(c.status).length > 0) {
                            <button
                              type="button"
                              class="text-btn"
                              (click)="openStatusChange(c, $event)"
                              [attr.aria-label]="'Change status of course: ' + c.title"
                            >
                              Status
                            </button>
                          }
                          <button
                            type="button"
                            class="action-btn"
                            (click)="openEdit(c); $event.stopPropagation()"
                            [attr.aria-label]="'Edit course: ' + c.title"
                            title="Edit"
                          >
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                              <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z" />
                              <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z" />
                            </svg>
                          </button>
                        </div>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="6" class="td-notion text-center text-neutral-400 py-8">
                        No courses match your search.
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <!-- Mobile cards -->
            <div class="sm:hidden divide-y divide-neutral-100">
              @for (c of filteredCourses(); track c.id) {
                <div
                  class="p-4 hover:bg-neutral-50 transition-colors duration-150 cursor-pointer"
                  (click)="openEdit(c)"
                  (keydown.enter)="openEdit(c)"
                  tabindex="0"
                  role="button"
                  [attr.aria-label]="'Edit course: ' + c.title"
                >
                  <div class="flex items-start justify-between mb-1">
                    <h3 class="text-sm font-semibold text-neutral-900">{{ c.title }}</h3>
                    <span class="badge" [class]="statusClass(c.status)">{{ c.status }}</span>
                  </div>
                  <div class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-400 mb-2">
                    <span>{{ modeLabel(c.mode) }}</span>
                    <span>Enrolled: {{ c.enrolledCount }} / {{ c.capacity === null ? '∞' : c.capacity }}</span>
                    <span>Waitlist: {{ c.waitlistCount }}</span>
                  </div>
                  <div class="flex items-center gap-3 mt-2">
                    @if (c.status === 'Open') {
                      <button
                        type="button"
                        class="text-xs text-brand-600 hover:text-brand-700"
                        (click)="enroll(c, $event)"
                        [disabled]="enrollingCourseId() === c.id"
                      >
                        Enroll
                      </button>
                    }
                    @if (allowedTransitions(c.status).length > 0) {
                      <button
                        type="button"
                        class="text-xs text-neutral-500 hover:text-neutral-700"
                        (click)="openStatusChange(c, $event)"
                      >
                        Change status
                      </button>
                    }
                  </div>
                </div>
              } @empty {
                <div class="p-6 text-center text-sm text-neutral-400">
                  No courses match your search.
                </div>
              }
            </div>
          </div>
        }
      }

      <!-- Slide-over form -->
      @if (formOpen()) {
        <div class="fixed inset-0 z-50" (keydown.escape)="closeForm()">
          <div class="fixed inset-0 bg-black/20 backdrop-blur-sm" @overlayFade (click)="closeForm()" aria-hidden="true"></div>
          <div
            class="fixed inset-y-0 right-0 w-full sm:w-[30rem] bg-white shadow-notion-lg overflow-y-auto"
            @slideOver
            role="dialog"
            aria-modal="true"
            [attr.aria-label]="editingCourse() ? 'Edit course' : 'Create course'"
          >
            <app-course-form
              [course]="editingCourse()"
              (saved)="onFormSaved()"
              (cancelled)="closeForm()"
            />
          </div>
        </div>
      }

      <!-- Status-change dialog (AC-2) -->
      @if (courseToChangeStatus()) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/20 backdrop-blur-sm px-4"
          (click)="cancelStatusChange()"
          (keydown.escape)="cancelStatusChange()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="status-dialog-title"
        >
          <div class="w-full max-w-md rounded-xl bg-white shadow-notion-lg p-6" (click)="$event.stopPropagation()">
            <h3 id="status-dialog-title" class="text-lg font-semibold text-neutral-900 mb-2">
              Change Course Status
            </h3>
            <p class="text-sm text-neutral-600 mb-4">
              <strong>{{ courseToChangeStatus()!.title }}</strong> is currently
              <strong>{{ courseToChangeStatus()!.status }}</strong>.
            </p>
            <label class="label-notion" for="target-status">New status</label>
            <select
              id="target-status"
              class="input-notion mt-1"
              [ngModel]="targetStatus()"
              (ngModelChange)="targetStatus.set($event)"
            >
              @for (s of allowedTransitions(courseToChangeStatus()!.status); track s) {
                <option [value]="s">{{ s }}</option>
              }
            </select>
            <div class="flex justify-end gap-3 mt-6">
              <button type="button" class="btn-secondary" (click)="cancelStatusChange()">Cancel</button>
              <button
                type="button"
                class="btn-primary"
                (click)="confirmStatusChange()"
                [disabled]="isChangingStatus() || !targetStatus()"
              >
                @if (isChangingStatus()) {
                  <span class="btn-spinner"></span>
                  Updating...
                } @else {
                  Update Status
                }
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .line-clamp-1 {
      display: -webkit-box;
      -webkit-line-clamp: 1;
      -webkit-box-orient: vertical;
      overflow: hidden;
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

    .badge {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium whitespace-nowrap;
    }

    .badge-draft { @apply bg-neutral-100 text-neutral-600; }
    .badge-open { @apply bg-green-50 text-green-700; }
    .badge-closed { @apply bg-amber-50 text-amber-700; }
    .badge-cancelled { @apply bg-red-50 text-red-700; }
    .badge-completed { @apply bg-blue-50 text-blue-700; }

    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 transition-colors duration-150
        hover:text-neutral-600 hover:bg-neutral-100;
    }

    .text-btn {
      @apply px-2 py-1 rounded-md text-xs font-medium text-brand-600
        transition-colors duration-150 hover:bg-brand-50
        disabled:opacity-50 disabled:cursor-not-allowed;
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
export class CourseListComponent implements OnInit {
  private readonly trainingService = inject(TrainingService);
  private readonly toastr = inject(ToastrService);

  readonly courses = signal<ICourse[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly searchQuery = signal('');

  // Form slide-over state
  readonly formOpen = signal(false);
  readonly editingCourse = signal<ICourse | null>(null);

  // Status-change dialog state
  readonly courseToChangeStatus = signal<ICourse | null>(null);
  readonly targetStatus = signal<CourseStatus | null>(null);
  readonly isChangingStatus = signal(false);

  // Enroll state
  readonly enrollingCourseId = signal<string | null>(null);

  readonly skeletonItems = [1, 2, 3, 4, 5, 6];

  readonly filteredCourses = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    const list = this.courses();
    if (!query) return list;
    return list.filter(
      (c) =>
        c.title.toLowerCase().includes(query) ||
        (c.instructor && c.instructor.toLowerCase().includes(query)) ||
        (c.location && c.location.toLowerCase().includes(query))
    );
  });

  ngOnInit(): void {
    this.loadCourses();
  }

  loadCourses(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.trainingService.getCourses().subscribe({
      next: (courses) => {
        this.courses.set(courses);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load courses. Please try again.'
        );
      },
    });
  }

  // --- Display helpers ----------------------------------------

  modeLabel(mode: TrainingMode): string {
    switch (mode) {
      case 'InPerson':
        return 'In person';
      case 'Online':
        return 'Online';
      case 'Hybrid':
        return 'Hybrid';
    }
  }

  statusClass(status: CourseStatus): string {
    return `badge-${status.toLowerCase()}`;
  }

  /**
   * Legal course status transitions (AC-2), mirroring the backend
   * ChangeStatusAsync rules. Terminal states (Cancelled/Completed) → none.
   */
  allowedTransitions(status: CourseStatus): CourseStatus[] {
    switch (status) {
      case 'Draft':
        return ['Open', 'Cancelled'];
      case 'Open':
        return ['Closed', 'Cancelled', 'Completed'];
      case 'Closed':
        return ['Completed', 'Cancelled'];
      case 'Cancelled':
      case 'Completed':
        return [];
    }
  }

  // --- Form slide-over ----------------------------------------

  openCreate(): void {
    this.editingCourse.set(null);
    this.formOpen.set(true);
  }

  openEdit(course: ICourse): void {
    this.editingCourse.set(course);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingCourse.set(null);
  }

  onFormSaved(): void {
    this.closeForm();
    this.loadCourses();
  }

  // --- Status change ------------------------------------------

  openStatusChange(course: ICourse, event?: Event): void {
    event?.stopPropagation();
    const transitions = this.allowedTransitions(course.status);
    if (transitions.length === 0) return;
    this.courseToChangeStatus.set(course);
    this.targetStatus.set(transitions[0]);
  }

  cancelStatusChange(): void {
    this.courseToChangeStatus.set(null);
    this.targetStatus.set(null);
  }

  confirmStatusChange(): void {
    const course = this.courseToChangeStatus();
    const target = this.targetStatus();
    if (!course || !target) return;

    this.isChangingStatus.set(true);

    this.trainingService
      .changeCourseStatus(course.id, { status: target })
      .subscribe({
        next: () => {
          this.toastr.success(`"${course.title}" is now ${target}.`);
          this.isChangingStatus.set(false);
          this.cancelStatusChange();
          this.loadCourses();
        },
        error: (err: HttpErrorResponse) => {
          this.isChangingStatus.set(false);
          const body = err.error as ITrainingErrorResponse | undefined;
          this.toastr.error(body?.message || 'Failed to change course status.');
        },
      });
  }

  // --- Enrollment ---------------------------------------------

  enroll(course: ICourse, event?: Event): void {
    event?.stopPropagation();
    if (this.enrollingCourseId()) return;

    this.enrollingCourseId.set(course.id);

    this.trainingService.enroll(course.id, { employeeId: null }).subscribe({
      next: (enrollment) => {
        this.enrollingCourseId.set(null);
        if (enrollment.status === 'Waitlisted') {
          this.toastr.info(
            `"${course.title}" is full — you have been waitlisted (position ${enrollment.waitlistPosition ?? '?'}).`
          );
        } else {
          this.toastr.success(`You are enrolled in "${course.title}".`);
        }
        this.loadCourses();
      },
      error: (err: HttpErrorResponse) => {
        this.enrollingCourseId.set(null);
        const body = err.error as ITrainingErrorResponse | undefined;
        if (body?.code === 'already_enrolled') {
          this.toastr.warning(
            body.message || `You are already enrolled in "${course.title}".`
          );
        } else {
          this.toastr.error(body?.message || 'Failed to enroll.');
        }
      },
    });
  }
}
