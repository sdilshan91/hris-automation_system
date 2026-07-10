import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { TrainingService } from '../../services/training.service';
import {
  IEnrollment,
  EnrollmentStatus,
  ITrainingErrorResponse,
} from '../../models/training.models';

/**
 * US-TRN-001 AC-9: the current user's training enrollments (self view).
 *
 * Lists current + past enrollments with status, dates, certificate and score.
 * Active enrollments (Enrolled/Waitlisted) can be cancelled (AC-7).
 */
@Component({
  selector: 'app-my-enrollments',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-container">
      <div class="mb-6">
        <a routerLink="/training" class="text-sm text-neutral-500 hover:text-neutral-700 inline-flex items-center gap-1 mb-3">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
            <path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 0 1 0 1.06L9.06 10l3.73 3.71a.75.75 0 1 1-1.06 1.06l-4.25-4.24a.75.75 0 0 1 0-1.06l4.25-4.24a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd" />
          </svg>
          Back to catalog
        </a>
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">My Enrollments</h1>
        <p class="mt-1 text-sm text-neutral-500">Your training enrollments and completions.</p>
      </div>

      @if (isLoading()) {
        <div class="card-notion animate-pulse space-y-3 p-5">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-5 bg-neutral-50 rounded w-2/3"></div>
          }
        </div>
      } @else if (loadError()) {
        <div class="card-notion text-center py-12">
          <p class="text-sm text-neutral-600">{{ loadError() }}</p>
          <button class="btn-secondary mt-4" (click)="load()">Try Again</button>
        </div>
      } @else if (enrollments().length === 0) {
        <div class="card-notion text-center py-12">
          <p class="text-sm font-medium text-neutral-700 mb-1">No enrollments yet</p>
          <p class="text-xs text-neutral-400">
            Browse the <a routerLink="/training" class="text-brand-600 hover:underline">training catalog</a> to enroll.
          </p>
        </div>
      } @else {
        <div class="card-notion overflow-hidden p-0">
          <ul class="divide-y divide-neutral-100">
            @for (e of enrollments(); track e.id) {
              <li class="p-4 sm:px-6 flex items-start justify-between gap-4">
                <div class="min-w-0">
                  <div class="flex items-center gap-2 flex-wrap">
                    <span class="font-medium text-neutral-900">{{ e.courseTitle }}</span>
                    <span class="badge" [class]="statusClass(e.status)">{{ e.status }}</span>
                    @if (e.status === 'Waitlisted' && e.waitlistPosition !== null) {
                      <span class="text-xs text-neutral-400">position {{ e.waitlistPosition }}</span>
                    }
                  </div>
                  <div class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-400 mt-1">
                    <span>Enrolled {{ e.enrolledAt | date: 'mediumDate' }}</span>
                    @if (e.completedAt) {
                      <span>Completed {{ e.completedAt | date: 'mediumDate' }}</span>
                    }
                    @if (e.certificateReference) {
                      <span>Certificate: {{ e.certificateReference }}</span>
                    }
                    @if (e.score !== null) {
                      <span>Score: {{ e.score }}</span>
                    }
                  </div>
                </div>
                @if (isActive(e.status)) {
                  <button
                    type="button"
                    class="text-xs text-red-500 hover:text-red-700 whitespace-nowrap"
                    (click)="cancel(e)"
                    [disabled]="cancellingId() === e.id"
                    [attr.aria-label]="'Cancel enrollment: ' + e.courseTitle"
                  >
                    Cancel
                  </button>
                }
              </li>
            }
          </ul>
        </div>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .badge {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium whitespace-nowrap;
    }

    .badge-enrolled { @apply bg-green-50 text-green-700; }
    .badge-waitlisted { @apply bg-amber-50 text-amber-700; }
    .badge-cancelled { @apply bg-red-50 text-red-700; }
    .badge-completed { @apply bg-blue-50 text-blue-700; }
    .badge-noshow { @apply bg-neutral-100 text-neutral-600; }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }
  `],
})
export class MyEnrollmentsComponent implements OnInit {
  private readonly trainingService = inject(TrainingService);
  private readonly toastr = inject(ToastrService);

  readonly enrollments = signal<IEnrollment[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly cancellingId = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.trainingService.getMyEnrollments().subscribe({
      next: (enrollments) => {
        this.enrollments.set(enrollments);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load your enrollments. Please try again.'
        );
      },
    });
  }

  statusClass(status: EnrollmentStatus): string {
    return `badge-${status.toLowerCase()}`;
  }

  isActive(status: EnrollmentStatus): boolean {
    return status === 'Enrolled' || status === 'Waitlisted';
  }

  cancel(enrollment: IEnrollment): void {
    if (this.cancellingId()) return;
    this.cancellingId.set(enrollment.id);

    this.trainingService.cancelEnrollment(enrollment.id).subscribe({
      next: () => {
        this.cancellingId.set(null);
        this.toastr.success(`Cancelled your enrollment in "${enrollment.courseTitle}".`);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.cancellingId.set(null);
        const body = err.error as ITrainingErrorResponse | undefined;
        this.toastr.error(body?.message || 'Failed to cancel enrollment.');
      },
    });
  }
}
