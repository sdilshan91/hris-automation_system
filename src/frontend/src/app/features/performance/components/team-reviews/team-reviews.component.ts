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
import { ManagerReviewService } from '../../services/manager-review.service';
import {
  IManagerTeamRow,
  MANAGER_REVIEW_STATUS_BADGE,
  MANAGER_REVIEW_STATUS_LABEL,
  ManagerReviewStatus,
} from '../../models/manager-review.models';

type SortKey = 'employeeName' | 'status';
type SortDir = 'asc' | 'desc';

/**
 * US-PRF-003 (AC-4): Team Reviews dashboard. A sortable/filterable table of the
 * manager's direct reports with a color-coded review-status chip per row
 * (pending self-assessment / self-assessment submitted / manager review pending /
 * completed). Each actionable row links into the per-employee manager-review view.
 *
 * Default `/performance` screens are role-gated to managers/HR; this is the
 * '/performance/team-reviews' child route. Notion-like table, responsive from 360px
 * (the table scrolls horizontally; key fields stay visible).
 */
@Component({
  selector: 'app-team-reviews',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
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
    <div class="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          Team reviews
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Rate your direct reports against their goals for this appraisal cycle.
        </p>
      </div>

      @if (loading()) {
        <div class="space-y-3">
          @for (i of [1, 2, 3, 4, 5]; track i) {
            <div class="h-14 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (rows().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-12 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">
            No team members yet
          </p>
          <p class="mt-1 text-sm text-neutral-500">
            You have no direct reports assigned in the org tree for this cycle.
          </p>
        </div>
      } @else {
        <!-- Filters (AC-4) -->
        <div
          class="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
        >
          <label class="relative block w-full sm:max-w-xs">
            <span class="sr-only">Search team members</span>
            <input
              type="search"
              [ngModel]="search()"
              (ngModelChange)="search.set($event)"
              placeholder="Search by name…"
              class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
              data-testid="search-input"
            />
          </label>
          <label class="block w-full sm:w-auto">
            <span class="sr-only">Filter by status</span>
            <select
              [ngModel]="statusFilter()"
              (ngModelChange)="statusFilter.set($event)"
              class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 sm:w-auto"
              data-testid="status-filter"
            >
              <option value="all">All statuses</option>
              @for (s of statusKeys; track s) {
                <option [value]="s">{{ statusLabel(s) }}</option>
              }
            </select>
          </label>
        </div>

        @if (visibleRows().length === 0) {
          <div
            class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-10 text-center text-sm text-neutral-500"
          >
            No team members match your filters.
          </div>
        } @else {
          <div
            class="overflow-x-auto rounded-xl border border-neutral-200 bg-white shadow-sm"
          >
            <table class="min-w-full divide-y divide-neutral-100 text-sm">
              <thead class="bg-neutral-50/70">
                <tr>
                  <th scope="col" class="px-4 py-3 text-left">
                    <button
                      type="button"
                      class="inline-flex items-center gap-1 font-medium text-neutral-600 transition hover:text-neutral-900"
                      (click)="toggleSort('employeeName')"
                      [attr.aria-label]="'Sort by name ' + sortHint('employeeName')"
                    >
                      Employee {{ sortArrow('employeeName') }}
                    </button>
                  </th>
                  <th
                    scope="col"
                    class="hidden px-4 py-3 text-left font-medium text-neutral-600 sm:table-cell"
                  >
                    Goals
                  </th>
                  <th scope="col" class="px-4 py-3 text-left">
                    <button
                      type="button"
                      class="inline-flex items-center gap-1 font-medium text-neutral-600 transition hover:text-neutral-900"
                      (click)="toggleSort('status')"
                      [attr.aria-label]="'Sort by status ' + sortHint('status')"
                    >
                      Status {{ sortArrow('status') }}
                    </button>
                  </th>
                  <th
                    scope="col"
                    class="px-4 py-3 text-right font-medium text-neutral-600"
                  >
                    <span class="sr-only">Action</span>
                  </th>
                </tr>
              </thead>
              <tbody class="divide-y divide-neutral-100" @fadeIn>
                @for (row of visibleRows(); track row.employeeId) {
                  <tr class="transition hover:bg-neutral-50/60" data-testid="team-row">
                    <td class="px-4 py-3">
                      <p
                        class="font-medium text-neutral-900"
                        [title]="row.employeeName"
                      >
                        {{ row.employeeName }}
                      </p>
                      @if (row.jobTitle) {
                        <p class="text-xs text-neutral-500">{{ row.jobTitle }}</p>
                      }
                    </td>
                    <td
                      class="hidden px-4 py-3 text-neutral-600 sm:table-cell"
                    >
                      {{ row.goalCount }}
                      goal{{ row.goalCount === 1 ? '' : 's' }}
                    </td>
                    <td class="px-4 py-3">
                      <span
                        class="inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                        [class]="statusBadge(row.status)"
                        data-testid="status-chip"
                      >
                        {{ statusLabel(row.status) }}
                      </span>
                    </td>
                    <td class="px-4 py-3 text-right">
                      @if (isReviewable(row)) {
                        <a
                          [routerLink]="reviewLink(row.employeeId)"
                          class="inline-flex items-center rounded-lg px-3 py-1.5 text-xs font-medium text-white shadow-sm transition hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-offset-2"
                          [style.background-color]="'var(--brand-primary)'"
                          [style.--tw-ring-color]="'var(--brand-primary)'"
                          data-testid="review-link"
                        >
                          {{ row.status === 'ManagerReviewSubmitted' || row.status === 'Completed' ? 'View' : 'Review' }}
                        </a>
                      } @else {
                        <span class="text-xs text-neutral-400">Awaiting self-assessment</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </div>
  `,
})
export class TeamReviewsComponent implements OnInit {
  private readonly service = inject(ManagerReviewService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly rows = signal<IManagerTeamRow[]>([]);

  readonly search = signal('');
  readonly statusFilter = signal<ManagerReviewStatus | 'all'>('all');
  readonly sortKey = signal<SortKey>('employeeName');
  readonly sortDir = signal<SortDir>('asc');

  readonly statusKeys: ManagerReviewStatus[] = [
    'PendingSelfAssessment',
    'SelfAssessmentSubmitted',
    'ManagerReviewSubmitted',
    'Completed',
  ];

  /** Filtered + sorted rows for the table (AC-4). */
  readonly visibleRows = computed(() => {
    const term = this.search().trim().toLowerCase();
    const status = this.statusFilter();
    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;

    const filtered = this.rows().filter((r) => {
      const matchesTerm =
        term === '' || r.employeeName.toLowerCase().includes(term);
      const matchesStatus = status === 'all' || r.status === status;
      return matchesTerm && matchesStatus;
    });

    return [...filtered].sort((a, b) => {
      const av = (a[key] ?? '').toString().toLowerCase();
      const bv = (b[key] ?? '').toString().toLowerCase();
      if (av < bv) {
        return -1 * dir;
      }
      if (av > bv) {
        return 1 * dir;
      }
      return 0;
    });
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getTeam().subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load your team reviews.');
        this.loading.set(false);
      },
    });
  }

  toggleSort(key: SortKey): void {
    if (this.sortKey() === key) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortKey.set(key);
      this.sortDir.set('asc');
    }
  }

  sortArrow(key: SortKey): string {
    if (this.sortKey() !== key) {
      return '';
    }
    return this.sortDir() === 'asc' ? '▲' : '▼';
  }

  sortHint(key: SortKey): string {
    if (this.sortKey() !== key) {
      return 'ascending';
    }
    return this.sortDir() === 'asc' ? 'descending' : 'ascending';
  }

  /** A row is actionable once self-assessment is in (BR-1 review-phase ready). */
  isReviewable(row: IManagerTeamRow): boolean {
    return row.status !== 'PendingSelfAssessment';
  }

  reviewLink(employeeId: string): unknown[] {
    return ['/performance', 'reviews', employeeId];
  }

  statusBadge(status: ManagerReviewStatus): string {
    return MANAGER_REVIEW_STATUS_BADGE[status];
  }

  statusLabel(status: ManagerReviewStatus): string {
    return MANAGER_REVIEW_STATUS_LABEL[status];
  }
}
