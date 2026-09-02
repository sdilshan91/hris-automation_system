import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate, query, stagger } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { CareersService } from '../../../services/careers.service';
import { CareersBrandingComponent } from '../careers-branding/careers-branding.component';
import { IPublicVacancy } from '../../../models/applicant.models';

/**
 * US-REC-002: Public careers landing page (§8 / NFR-2 / NFR-5).
 *
 * Anonymous route (NOT behind the auth/role guard). Lists Open vacancies in a
 * clean card grid, tenant-branded via `app-careers-branding`. Search (title) +
 * filters by department / location / employment type are applied client-side
 * over the fetched list (the dataset is small per tenant; the service also
 * accepts server-side params if a later story needs them). Mobile-first to 360px.
 */
@Component({
  selector: 'app-careers-page',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, CareersBrandingComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('listIn', [
      transition('* => *', [
        query(
          ':enter',
          [
            style({ opacity: 0, transform: 'translateY(8px)' }),
            stagger(40, [
              animate(
                '220ms ease-out',
                style({ opacity: 1, transform: 'translateY(0)' }),
              ),
            ]),
          ],
          { optional: true },
        ),
      ]),
    ]),
  ],
  template: `
    <div class="min-h-screen bg-neutral-50">
      <app-careers-branding />

      <main class="mx-auto max-w-5xl px-4 py-8 sm:px-6 lg:py-12">
        <div class="mb-8">
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900 sm:text-3xl">
            Open positions
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Find your next role and apply in minutes.
          </p>
        </div>

        <!-- Search + filters -->
        <div class="mb-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div class="sm:col-span-2 lg:col-span-1">
            <label for="careers-search" class="sr-only">Search positions</label>
            <input
              id="careers-search"
              type="search"
              class="ctrl"
              placeholder="Search title…"
              [ngModel]="search()"
              (ngModelChange)="search.set($event)"
            />
          </div>
          <div>
            <label for="f-dept" class="sr-only">Filter by department</label>
            <select id="f-dept" class="ctrl" [ngModel]="dept()" (ngModelChange)="dept.set($event)">
              <option value="">All departments</option>
              @for (d of departments(); track d) {
                <option [value]="d">{{ d }}</option>
              }
            </select>
          </div>
          <div>
            <label for="f-loc" class="sr-only">Filter by location</label>
            <select id="f-loc" class="ctrl" [ngModel]="location()" (ngModelChange)="location.set($event)">
              <option value="">All locations</option>
              @for (l of locations(); track l) {
                <option [value]="l">{{ l }}</option>
              }
            </select>
          </div>
          <div>
            <label for="f-type" class="sr-only">Filter by employment type</label>
            <select id="f-type" class="ctrl" [ngModel]="empType()" (ngModelChange)="empType.set($event)">
              <option value="">All types</option>
              @for (t of employmentTypes(); track t) {
                <option [value]="t">{{ t }}</option>
              }
            </select>
          </div>
        </div>

        @if (loading()) {
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            @for (n of [1, 2, 3, 4]; track n) {
              <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
            }
          </div>
        } @else if (error()) {
          <div class="rounded-xl border border-red-200 bg-red-50 py-10 text-center">
            <p class="text-sm font-medium text-red-700">{{ error() }}</p>
            <button
              type="button"
              class="mt-3 text-sm font-medium text-indigo-600 hover:text-indigo-700"
              (click)="load()"
            >
              Try again
            </button>
          </div>
        } @else if (filtered().length === 0) {
          <div
            class="rounded-xl border border-dashed border-neutral-200 bg-white py-16 text-center"
          >
            <p class="text-sm font-medium text-neutral-700">No open positions</p>
            <p class="mt-1 text-sm text-neutral-500">
              @if (vacancies().length > 0) {
                Try clearing your filters.
              } @else {
                Please check back soon.
              }
            </p>
          </div>
        } @else {
          <div [@listIn]="filtered().length" class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            @for (v of filtered(); track v.id) {
              <a
                [routerLink]="['/careers', v.slug]"
                class="group flex flex-col rounded-xl border border-neutral-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md"
              >
                <h2 class="text-base font-semibold text-neutral-900 group-hover:text-indigo-600">
                  {{ v.title }}
                </h2>
                <p class="mt-1 text-sm text-neutral-500">
                  {{ v.departmentName || 'General' }}
                </p>
                <div class="mt-3 flex flex-wrap gap-2">
                  @if (v.locationName) {
                    <span class="meta-chip">{{ v.locationName }}</span>
                  }
                  @if (v.employmentType) {
                    <span class="meta-chip">{{ v.employmentType }}</span>
                  }
                </div>
                <span class="mt-4 text-sm font-medium text-indigo-600">
                  View &amp; apply →
                </span>
              </a>
            }
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
      .ctrl {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #171717;
        transition: all 150ms ease;
      }
      .ctrl:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 3px #e0e7ff;
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
      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
    `,
  ],
})
export class CareersPageComponent implements OnInit {
  private readonly careers = inject(CareersService);

  readonly vacancies = signal<IPublicVacancy[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  readonly search = signal('');
  readonly dept = signal('');
  readonly location = signal('');
  readonly empType = signal('');

  // Distinct filter options derived from the loaded vacancies.
  readonly departments = computed(() =>
    this.distinct(this.vacancies().map((v) => v.departmentName)),
  );
  readonly locations = computed(() =>
    this.distinct(this.vacancies().map((v) => v.locationName)),
  );
  readonly employmentTypes = computed(() =>
    this.distinct(this.vacancies().map((v) => v.employmentType)),
  );

  readonly filtered = computed(() => {
    const q = this.search().trim().toLowerCase();
    const d = this.dept();
    const l = this.location();
    const t = this.empType();
    return this.vacancies().filter((v) => {
      if (q && !v.title.toLowerCase().includes(q)) return false;
      if (d && v.departmentName !== d) return false;
      if (l && v.locationName !== l) return false;
      if (t && v.employmentType !== t) return false;
      return true;
    });
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.careers.listOpenVacancies().subscribe({
      next: (list) => {
        this.vacancies.set(list);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(CareersService.parseErrorMessage(err));
      },
    });
  }

  private distinct(values: Array<string | null>): string[] {
    return Array.from(
      new Set(values.filter((v): v is string => !!v && v.trim().length > 0)),
    ).sort((a, b) => a.localeCompare(b));
  }
}
