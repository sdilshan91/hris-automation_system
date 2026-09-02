import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { CareersService } from '../../../services/careers.service';
import { ApplicationFormComponent } from '../application-form/application-form.component';
import { CareersBrandingComponent } from '../careers-branding/careers-branding.component';
import { IApplicant, IPublicVacancy } from '../../../models/applicant.models';

/**
 * US-REC-002: Public vacancy detail + application form + confirmation (AC-1).
 *
 * Anonymous route (NOT behind the auth/role guard). Loads the public vacancy by
 * id, renders the (sanitized) description/qualifications and the application form.
 * On successful submission it swaps to a confirmation screen showing the
 * application reference number and next-steps copy (§8). Mobile-responsive: the
 * job description collapses behind a toggle on small screens (§8 / NFR-5).
 */
@Component({
  selector: 'app-careers-vacancy-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    ApplicationFormComponent,
    CareersBrandingComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '260ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="min-h-screen bg-neutral-50">
      <app-careers-branding />

      <main class="mx-auto max-w-3xl px-4 py-8 sm:px-6 lg:py-12">
        <a
          routerLink="/careers"
          class="inline-flex items-center gap-1 text-sm text-neutral-500 transition hover:text-neutral-800"
        >
          <span aria-hidden="true">←</span> All openings
        </a>

        @if (loading()) {
          <div class="mt-6 space-y-3">
            <div class="h-8 w-2/3 animate-pulse rounded-lg bg-neutral-200"></div>
            <div class="h-4 w-1/2 animate-pulse rounded bg-neutral-100"></div>
            <div class="mt-6 h-40 animate-pulse rounded-xl bg-neutral-100"></div>
          </div>
        } @else if (notFound()) {
          <div
            @fadeIn
            class="mt-10 rounded-xl border border-dashed border-neutral-200 bg-white py-16 text-center"
          >
            <p class="text-sm font-medium text-neutral-700">
              This position is no longer available
            </p>
            <a
              routerLink="/careers"
              class="mt-3 inline-block text-sm font-medium text-indigo-600 hover:text-indigo-700"
            >
              Browse other openings
            </a>
          </div>
        } @else if (confirmation(); as appn) {
          <!-- Confirmation screen (AC-1) -->
          <div
            @fadeIn
            class="mt-8 rounded-2xl border border-neutral-200 bg-white p-8 text-center shadow-sm sm:p-10"
          >
            <div
              class="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-green-100"
            >
              <svg class="h-7 w-7 text-green-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
                <path d="m5 13 4 4L19 7" />
              </svg>
            </div>
            <h1 class="mt-5 text-xl font-semibold text-neutral-900">
              Application submitted
            </h1>
            <p class="mt-2 text-sm text-neutral-500">
              Thanks, {{ appn.firstName }}. We've received your application for
              <span class="font-medium text-neutral-700">{{ vacancy()?.title }}</span>.
            </p>
            <div class="mt-5 inline-flex flex-col items-center rounded-lg bg-neutral-50 px-5 py-3">
              <span class="text-xs uppercase tracking-wide text-neutral-400">
                Reference number
              </span>
              <span class="mt-0.5 font-mono text-base font-semibold text-neutral-800">
                {{ appn.applicationReferenceNumber }}
              </span>
            </div>
            <p class="mx-auto mt-5 max-w-sm text-sm text-neutral-500">
              A confirmation email is on its way. Our team will review your
              application and reach out about next steps.
            </p>
            <a
              routerLink="/careers"
              class="mt-6 inline-block rounded-lg border border-neutral-200 px-5 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
            >
              Back to all openings
            </a>
          </div>
        } @else if (vacancy(); as v) {
          <!-- Vacancy header -->
          <div @fadeIn class="mt-6">
            <h1 class="text-2xl font-semibold tracking-tight text-neutral-900 sm:text-3xl">
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

            <!-- Description (collapsible on mobile, §8) -->
            <div class="mt-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm sm:p-6">
              <button
                type="button"
                class="flex w-full items-center justify-between sm:cursor-default sm:pointer-events-none"
                [attr.aria-expanded]="descOpen()"
                (click)="descOpen.set(!descOpen())"
              >
                <h2 class="text-base font-semibold text-neutral-800">
                  About this role
                </h2>
                <span class="text-neutral-400 sm:hidden" aria-hidden="true">
                  {{ descOpen() ? '−' : '+' }}
                </span>
              </button>
              <div
                class="prose prose-sm mt-3 max-w-none text-neutral-700"
                [class.hidden]="!descOpen()"
                [class.sm:block]="true"
              >
                @if (v.description) {
                  <div [innerHTML]="v.description"></div>
                } @else {
                  <p class="text-neutral-400">No description provided.</p>
                }
                @if (v.qualifications) {
                  <h3 class="mt-5 text-sm font-semibold text-neutral-800">
                    Qualifications
                  </h3>
                  <div [innerHTML]="v.qualifications"></div>
                }
              </div>
            </div>

            <!-- Application form -->
            <div class="mt-6 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm sm:p-6">
              <h2 class="text-base font-semibold text-neutral-800">
                Apply for this position
              </h2>
              <p class="mb-5 mt-1 text-sm text-neutral-500">
                Fill in your details and attach your resume.
              </p>
              <app-application-form
                [vacancyId]="v.id"
                submitLabel="Submit application"
                (submitted)="onSubmitted($event)"
              />
            </div>
          </div>
        }
      </main>
    </div>
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
export class CareersVacancyDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly careers = inject(CareersService);

  readonly vacancy = signal<IPublicVacancy | null>(null);
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly confirmation = signal<IApplicant | null>(null);
  readonly descOpen = signal(true);

  ngOnInit(): void {
    // The public detail endpoint is keyed by SLUG (`GET /careers/vacancies/{slug}`),
    // so the route param is the slug the careers list linked with — not the vacancy id.
    const slug = this.route.snapshot.paramMap.get('slug');
    if (!slug) {
      this.loading.set(false);
      this.notFound.set(true);
      return;
    }
    this.careers.getPublicVacancy(slug).subscribe({
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

  onSubmitted(applicant: IApplicant): void {
    this.confirmation.set(applicant);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }
}
