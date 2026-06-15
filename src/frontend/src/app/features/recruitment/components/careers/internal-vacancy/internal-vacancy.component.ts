import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { CareersService } from '../../../services/careers.service';
import { InternalApplyComponent } from '../internal-apply/internal-apply.component';
import { IPublicVacancy } from '../../../models/applicant.models';

/**
 * US-REC-002 (AC-4 / FR-8): Authenticated employee view of an open vacancy with an
 * internal "Apply" action that opens the pre-filled slide-over.
 *
 * This route sits behind the auth guard (a broad employee role set), distinct from
 * the recruiter-only vacancy management screens. It reuses the public vacancy
 * detail endpoint to render the role and offers the internal apply path.
 */
@Component({
  selector: 'app-internal-vacancy',
  standalone: true,
  imports: [CommonModule, RouterLink, InternalApplyComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl px-4 py-6 sm:px-6 lg:px-8">
      @if (loading()) {
        <div class="space-y-3">
          <div class="h-8 w-2/3 animate-pulse rounded-lg bg-neutral-200"></div>
          <div class="h-40 animate-pulse rounded-xl bg-neutral-100"></div>
        </div>
      } @else if (notFound()) {
        <div class="rounded-xl border border-dashed border-neutral-200 bg-white py-16 text-center">
          <p class="text-sm font-medium text-neutral-700">
            This position is no longer available.
          </p>
          <a
            routerLink="/recruitment/vacancies"
            class="mt-3 inline-block text-sm font-medium text-indigo-600 hover:text-indigo-700"
          >
            Back to vacancies
          </a>
        </div>
      } @else if (vacancy(); as v) {
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          {{ v.title }}
        </h1>
        <div class="mt-3 flex flex-wrap items-center gap-2">
          @if (v.departmentName) {
            <span class="meta-chip">{{ v.departmentName }}</span>
          }
          @if (v.locationName) {
            <span class="meta-chip">{{ v.locationName }}</span>
          }
          @if (v.employmentType) {
            <span class="meta-chip">{{ v.employmentType }}</span>
          }
        </div>

        <div class="mt-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm sm:p-6">
          @if (v.description) {
            <div class="prose prose-sm max-w-none text-neutral-700" [innerHTML]="v.description"></div>
          } @else {
            <p class="text-sm text-neutral-400">No description provided.</p>
          }
        </div>

        <div class="mt-6">
          <button
            type="button"
            class="rounded-lg bg-indigo-600 px-5 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-indigo-700"
            (click)="showApply.set(true)"
          >
            Apply
          </button>
        </div>
      }
    </div>

    @if (showApply() && vacancy(); as v) {
      <app-internal-apply
        [vacancyId]="v.id"
        [vacancyTitle]="v.title"
        (closed)="showApply.set(false)"
      />
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .meta-chip {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        background: #f5f5f5;
        padding: 0.1875rem 0.6875rem;
        font-size: 0.75rem;
        font-weight: 500;
        color: #525252;
      }
    `,
  ],
})
export class InternalVacancyComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly careers = inject(CareersService);

  readonly vacancy = signal<IPublicVacancy | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly showApply = signal(false);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.loading.set(false);
      this.notFound.set(true);
      return;
    }
    this.careers.getPublicVacancy(id).subscribe({
      next: (v) => {
        this.vacancy.set(v);
        this.loading.set(false);
      },
      error: (_err: HttpErrorResponse) => {
        this.loading.set(false);
        this.notFound.set(true);
      },
    });
  }
}
