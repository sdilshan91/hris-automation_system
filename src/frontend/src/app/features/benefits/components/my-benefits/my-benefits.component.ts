import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { BenefitService } from '../../services/benefit.service';
import {
  IEligiblePlan,
  IBenefitEnrollment,
  IEnrollRequest,
  IBenefitErrorResponse,
  BenefitEnrollmentStatus,
  CoverageLevel,
  COVERAGE_LEVELS,
  COVERAGE_LEVEL_LABELS,
} from '../../models/benefit.models';

/**
 * US-TRN-003: Employee self-service — "My Benefits".
 *
 * Shows plans the current employee qualifies for (enrollable when the window is
 * open) plus the employee's current/past enrollments. Enroll picks a coverage
 * level; active enrollments can be terminated. Server-side errors surface as
 * toasts: 422 not_eligible / 409 already_enrolled / 422 enrollment_window_closed.
 * Gated (route + nav) on Benefits.View.Own.
 */
@Component({
  selector: 'app-my-benefits',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-container">
      <div class="mb-6">
        <a routerLink="/benefits" class="text-sm text-neutral-500 hover:text-neutral-700 inline-flex items-center gap-1 mb-3">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
            <path fill-rule="evenodd" d="M12.79 5.23a.75.75 0 0 1 0 1.06L9.06 10l3.73 3.71a.75.75 0 1 1-1.06 1.06l-4.25-4.24a.75.75 0 0 1 0-1.06l4.25-4.24a.75.75 0 0 1 1.06 0Z" clip-rule="evenodd" />
          </svg>
          Back to plans
        </a>
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">My Benefits</h1>
        <p class="mt-1 text-sm text-neutral-500">
          Plans you qualify for and your current enrollments.
        </p>
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
      } @else {
        <!-- Eligible plans -->
        <section class="mb-8">
          <h2 class="text-sm font-semibold text-neutral-800 mb-3">Available to you</h2>
          @if (eligiblePlans().length === 0) {
            <div class="card-notion text-center py-10">
              <p class="text-sm font-medium text-neutral-700 mb-1">No plans available</p>
              <p class="text-xs text-neutral-400">
                You don't currently qualify for any open benefit plans.
              </p>
            </div>
          } @else {
            <div class="grid gap-4 sm:grid-cols-2">
              @for (p of eligiblePlans(); track p.planId) {
                <div class="card-notion p-5">
                  <div class="flex items-start justify-between gap-2 mb-1">
                    <h3 class="text-sm font-semibold text-neutral-900">{{ p.name }}</h3>
                    <span class="text-xs text-neutral-400">{{ p.type }}</span>
                  </div>
                  <p class="text-xs text-neutral-500 mb-3">
                    Your cost: {{ costLabel(p.employeeCost, p.currency) }}
                    · Effective {{ p.effectiveFrom }}{{ p.effectiveTo ? ' → ' + p.effectiveTo : '' }}
                  </p>
                  @if (p.enrollmentOpen) {
                    <div class="flex items-end gap-2">
                      <div class="flex-1">
                        <label class="label-notion text-xs" [attr.for]="'coverage-' + p.planId">Coverage</label>
                        <select
                          [id]="'coverage-' + p.planId"
                          class="input-notion"
                          [ngModel]="coverageFor(p.planId)"
                          (ngModelChange)="setCoverage(p.planId, $event)"
                        >
                          @for (c of coverageLevels; track c) {
                            <option [value]="c">{{ coverageLabel(c) }}</option>
                          }
                        </select>
                      </div>
                      <button
                        type="button"
                        class="btn-primary"
                        (click)="enroll(p)"
                        [disabled]="enrollingId() === p.planId"
                        [attr.aria-label]="'Enroll in ' + p.name"
                      >
                        @if (enrollingId() === p.planId) {
                          <span class="btn-spinner"></span> Enrolling...
                        } @else {
                          Enroll
                        }
                      </button>
                    </div>
                  } @else {
                    <span class="badge badge-closed">Enrollment closed</span>
                  }
                </div>
              }
            </div>
          }
        </section>

        <!-- Current enrollments -->
        <section>
          <h2 class="text-sm font-semibold text-neutral-800 mb-3">Your enrollments</h2>
          @if (enrollments().length === 0) {
            <div class="card-notion text-center py-10">
              <p class="text-sm text-neutral-500">You have no benefit enrollments yet.</p>
            </div>
          } @else {
            <div class="card-notion overflow-hidden p-0">
              <ul class="divide-y divide-neutral-100">
                @for (e of enrollments(); track e.id) {
                  <li class="p-4 sm:px-6 flex items-start justify-between gap-4">
                    <div class="min-w-0">
                      <div class="flex items-center gap-2 flex-wrap">
                        <span class="font-medium text-neutral-900">{{ e.planName }}</span>
                        <span class="badge" [class]="statusClass(e.status)">{{ e.status }}</span>
                      </div>
                      <div class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-400 mt-1">
                        <span>{{ coverageLabel(e.coverageLevel) }}</span>
                        <span>Effective {{ e.effectiveDate }}</span>
                        @if (e.endDate) {
                          <span>Ended {{ e.endDate }}</span>
                        }
                      </div>
                    </div>
                    @if (isActive(e.status)) {
                      <button
                        type="button"
                        class="text-xs text-red-500 hover:text-red-700 whitespace-nowrap"
                        (click)="terminate(e)"
                        [disabled]="terminatingId() === e.id"
                        [attr.aria-label]="'Terminate enrollment: ' + e.planName"
                      >
                        Terminate
                      </button>
                    }
                  </li>
                }
              </ul>
            </div>
          }
        </section>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }

    .badge {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium whitespace-nowrap;
    }

    .badge-active { @apply bg-green-50 text-green-700; }
    .badge-pending { @apply bg-amber-50 text-amber-700; }
    .badge-declined { @apply bg-neutral-100 text-neutral-600; }
    .badge-terminated { @apply bg-red-50 text-red-700; }
    .badge-closed { @apply bg-neutral-100 text-neutral-500; }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2.5
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
export class MyBenefitsComponent implements OnInit {
  private readonly benefitService = inject(BenefitService);
  private readonly toastr = inject(ToastrService);

  readonly eligiblePlans = signal<IEligiblePlan[]>([]);
  readonly enrollments = signal<IBenefitEnrollment[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly enrollingId = signal<string | null>(null);
  readonly terminatingId = signal<string | null>(null);

  /** Per-plan selected coverage level (defaults to EmployeeOnly). */
  readonly coverageSelections = signal<Record<string, CoverageLevel>>({});

  readonly coverageLevels = COVERAGE_LEVELS;

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    let pending = 2;
    let failed = false;
    const done = () => {
      if (--pending === 0) this.isLoading.set(false);
    };

    this.benefitService.getEligiblePlans().subscribe({
      next: (plans) => {
        this.eligiblePlans.set(plans);
        done();
      },
      error: (err: HttpErrorResponse) => {
        if (!failed) {
          failed = true;
          this.loadError.set(
            err.error?.message || 'Failed to load your benefits. Please try again.'
          );
        }
        done();
      },
    });

    this.benefitService.getMyEnrollments().subscribe({
      next: (list) => {
        this.enrollments.set(list);
        done();
      },
      error: (err: HttpErrorResponse) => {
        if (!failed) {
          failed = true;
          this.loadError.set(
            err.error?.message || 'Failed to load your benefits. Please try again.'
          );
        }
        done();
      },
    });
  }

  // --- Display helpers ----------------------------------------

  costLabel(cost: number | null, currency: string): string {
    if (cost === null) return 'free';
    return `${currency} ${cost.toFixed(2)}`;
  }

  coverageLabel(level: CoverageLevel): string {
    return COVERAGE_LEVEL_LABELS[level];
  }

  statusClass(status: BenefitEnrollmentStatus): string {
    return `badge-${status.toLowerCase()}`;
  }

  isActive(status: BenefitEnrollmentStatus): boolean {
    return status === 'Active' || status === 'Pending';
  }

  coverageFor(planId: string): CoverageLevel {
    return this.coverageSelections()[planId] ?? 'EmployeeOnly';
  }

  setCoverage(planId: string, level: CoverageLevel): void {
    this.coverageSelections.update((m) => ({ ...m, [planId]: level }));
  }

  // --- Actions ------------------------------------------------

  enroll(plan: IEligiblePlan): void {
    if (this.enrollingId()) return;
    this.enrollingId.set(plan.planId);

    const request: IEnrollRequest = {
      planId: plan.planId,
      coverageLevel: this.coverageFor(plan.planId),
    };

    this.benefitService.enroll(request).subscribe({
      next: () => {
        this.enrollingId.set(null);
        this.toastr.success(`Enrolled in "${plan.name}".`);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.enrollingId.set(null);
        const body = err.error as IBenefitErrorResponse | undefined;
        this.toastr.error(body?.message || this.enrollFallback(body?.code));
      },
    });
  }

  terminate(enrollment: IBenefitEnrollment): void {
    if (this.terminatingId()) return;
    this.terminatingId.set(enrollment.id);

    this.benefitService.terminate(enrollment.id).subscribe({
      next: () => {
        this.terminatingId.set(null);
        this.toastr.success(`Terminated your "${enrollment.planName}" enrollment.`);
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.terminatingId.set(null);
        const body = err.error as IBenefitErrorResponse | undefined;
        this.toastr.error(body?.message || 'Failed to terminate enrollment.');
      },
    });
  }

  /** Fallback message keyed off the error code when no server message is present. */
  private enrollFallback(code?: string): string {
    switch (code) {
      case 'not_eligible':
        return 'You are not eligible for this plan.';
      case 'already_enrolled':
        return 'You are already enrolled in this plan.';
      case 'enrollment_window_closed':
        return 'The enrollment window for this plan is closed.';
      case 'plan_not_active':
        return 'This plan is not currently active.';
      default:
        return 'Failed to enroll in this plan.';
    }
  }
}
