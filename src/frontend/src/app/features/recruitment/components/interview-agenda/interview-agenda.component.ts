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
import { InterviewService } from '../../services/interview.service';
import { VacancyService } from '../../services/vacancy.service';
import { ILookupOption } from '../../models/vacancy.models';
import { InterviewCardComponent } from '../interview-card/interview-card.component';
import {
  IInterview,
  IInterviewFilters,
  InterviewStatus,
  INTERVIEW_STATUSES,
  interviewStatusLabel,
  groupByDate,
} from '../../models/interview.models';

/**
 * US-REC-005 FR-5/FR-6: Tenant-wide interviews agenda view.
 *
 * An agenda/list grouped by date (a calendar grid is deferred — the list is the
 * Phase-1 acceptable form per the story and is what we render on mobile, NFR-5).
 * Filterable by interviewer, vacancy, date range, and status. Read-only cards
 * (scheduling/rescheduling happens from the applicant detail); each card shows the
 * applicant + vacancy context.
 *
 * Role-gated by the route guard; the backend enforces tenant RLS (AC-5 / NFR-2).
 */
@Component({
  selector: 'app-interview-agenda',
  standalone: true,
  imports: [CommonModule, FormsModule, InterviewCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '220ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-5xl px-4 py-6 sm:px-6">
      <!-- Header -->
      <div class="mb-5">
        <h1 class="text-xl font-semibold text-neutral-900">Interviews</h1>
        <p class="mt-0.5 text-sm text-neutral-500">
          All scheduled interviews across the organisation.
        </p>
      </div>

      <!-- Filters -->
      <div
        class="mb-6 grid grid-cols-1 gap-3 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm sm:grid-cols-2 lg:grid-cols-4"
      >
        <!-- Interviewer search -->
        <div class="relative">
          <label class="flt-lbl" for="ag-interviewer">Interviewer</label>
          <input
            id="ag-interviewer"
            type="text"
            class="flt-inp"
            placeholder="Search…"
            autocomplete="off"
            [value]="interviewerQuery()"
            (input)="onInterviewerSearch($event)"
          />
          @if (selectedInterviewer(); as si) {
            <span
              class="mt-1 inline-flex items-center gap-1 rounded-full bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700"
            >
              {{ si.label }}
              <button
                type="button"
                (click)="clearInterviewer()"
                aria-label="Clear interviewer filter"
                >×</button
              >
            </span>
          } @else if (interviewerResults().length && interviewerQuery()) {
            <ul
              class="absolute z-10 mt-1 max-h-44 w-full overflow-y-auto rounded-lg border border-neutral-200 bg-white shadow-md"
            >
              @for (r of interviewerResults(); track r.id) {
                <li>
                  <button
                    type="button"
                    class="block w-full truncate px-3 py-1.5 text-left text-sm hover:bg-neutral-50"
                    (click)="pickInterviewer(r)"
                  >
                    {{ r.label }}
                  </button>
                </li>
              }
            </ul>
          }
        </div>

        <!-- Vacancy -->
        <div>
          <label class="flt-lbl" for="ag-vacancy">Vacancy</label>
          <select
            id="ag-vacancy"
            class="flt-inp"
            [(ngModel)]="vacancyId"
            (ngModelChange)="applyFilters()"
          >
            <option [ngValue]="null">All vacancies</option>
            @for (v of vacancies(); track v.id) {
              <option [ngValue]="v.id">{{ v.label }}</option>
            }
          </select>
        </div>

        <!-- Status -->
        <div>
          <label class="flt-lbl" for="ag-status">Status</label>
          <select
            id="ag-status"
            class="flt-inp"
            [(ngModel)]="status"
            (ngModelChange)="applyFilters()"
          >
            <option [ngValue]="null">All statuses</option>
            @for (s of statuses; track s) {
              <option [ngValue]="s">{{ statusLabel(s) }}</option>
            }
          </select>
        </div>

        <!-- Date range -->
        <div class="grid grid-cols-2 gap-2">
          <div>
            <label class="flt-lbl" for="ag-from">From</label>
            <input
              id="ag-from"
              type="date"
              class="flt-inp"
              [(ngModel)]="from"
              (ngModelChange)="applyFilters()"
            />
          </div>
          <div>
            <label class="flt-lbl" for="ag-to">To</label>
            <input
              id="ag-to"
              type="date"
              class="flt-inp"
              [(ngModel)]="to"
              (ngModelChange)="applyFilters()"
            />
          </div>
        </div>
      </div>

      <!-- Content -->
      @if (loading()) {
        <div class="space-y-4">
          @for (n of [1, 2, 3]; track n) {
            <div class="h-28 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (groups().length === 0) {
        <div
          class="rounded-xl border border-dashed border-neutral-300 bg-white py-16 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">No interviews found</p>
          <p class="mt-1 text-sm text-neutral-500">
            Try adjusting the filters or schedule an interview from an applicant.
          </p>
        </div>
      } @else {
        <div class="space-y-7">
          @for (g of groups(); track g.date) {
            <section @fadeIn>
              <h2
                class="sticky top-0 z-[1] mb-3 bg-neutral-50/90 py-1 text-sm font-semibold text-neutral-700 backdrop-blur"
              >
                {{ g.date | date: 'EEEE, MMMM d, y' }}
                <span class="font-normal text-neutral-400"
                  >· {{ g.interviews.length }}</span
                >
              </h2>
              <div class="grid grid-cols-1 gap-3 md:grid-cols-2">
                @for (iv of g.interviews; track iv.id) {
                  <app-interview-card
                    [interview]="iv"
                    [showActions]="false"
                    [showContext]="true"
                  />
                }
              </div>
            </section>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .flt-lbl {
        display: block;
        margin-bottom: 0.25rem;
        font-size: 0.6875rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: #a3a3a3;
      }
      .flt-inp {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        padding: 0.375rem 0.625rem;
        font-size: 0.875rem;
        color: #262626;
        background: #fff;
      }
      .flt-inp:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 3px rgb(129 140 248 / 0.2);
      }
    `,
  ],
})
export class InterviewAgendaComponent implements OnInit {
  private readonly interviewService = inject(InterviewService);
  private readonly vacancyService = inject(VacancyService);
  private readonly toastr = inject(ToastrService);

  readonly statuses = INTERVIEW_STATUSES;

  readonly loading = signal(false);
  readonly interviews = signal<IInterview[]>([]);
  readonly vacancies = signal<ILookupOption[]>([]);

  // Filter models (ngModel) — interviewer is selected via the typeahead below.
  vacancyId: string | null = null;
  status: InterviewStatus | null = null;
  from: string | null = null;
  to: string | null = null;

  readonly interviewerQuery = signal('');
  readonly interviewerResults = signal<ILookupOption[]>([]);
  readonly selectedInterviewer = signal<ILookupOption | null>(null);
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  /** Date-grouped agenda (FR-5) derived from the loaded interviews. */
  readonly groups = computed(() => groupByDate(this.interviews()));

  ngOnInit(): void {
    this.loadVacancies();
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    const filters: IInterviewFilters = {
      interviewerId: this.selectedInterviewer()?.id ?? null,
      vacancyId: this.vacancyId,
      status: this.status,
      from: this.from,
      to: this.to,
    };
    this.interviewService.listInterviews(filters).subscribe({
      next: (rows) => {
        this.interviews.set(rows);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.toastr.error(InterviewService.parseErrorMessage(err));
      },
    });
  }

  applyFilters(): void {
    this.load();
  }

  private loadVacancies(): void {
    // Reuse the vacancy list for the filter dropdown; tolerate failure (non-blocking).
    this.vacancyService.listVacancies({ pageSize: 200 }).subscribe({
      next: (page) =>
        this.vacancies.set(
          page.data.map((v) => ({ id: v.id, label: v.title })),
        ),
      error: () => this.vacancies.set([]),
    });
  }

  // ─── Interviewer typeahead ───────────────────────────────

  onInterviewerSearch(event: Event): void {
    const q = (event.target as HTMLInputElement).value;
    this.interviewerQuery.set(q);
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
    }
    if (q.trim().length === 0) {
      this.interviewerResults.set([]);
      return;
    }
    this.searchTimer = setTimeout(() => {
      this.interviewService.searchEmployees(q.trim()).subscribe({
        next: (rows) => this.interviewerResults.set(rows),
        error: () => this.interviewerResults.set([]),
      });
    }, 300);
  }

  pickInterviewer(option: ILookupOption): void {
    this.selectedInterviewer.set(option);
    this.interviewerQuery.set('');
    this.interviewerResults.set([]);
    this.load();
  }

  clearInterviewer(): void {
    this.selectedInterviewer.set(null);
    this.interviewerQuery.set('');
    this.load();
  }

  statusLabel(status: InterviewStatus): string {
    return interviewStatusLabel(status);
  }
}
