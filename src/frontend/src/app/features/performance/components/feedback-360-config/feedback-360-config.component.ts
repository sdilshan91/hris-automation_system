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
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Feedback360Service } from '../../services/feedback-360.service';
import {
  IReviewerConfig,
  IReviewer,
  IEmployeeOption,
  ICompletionTracker,
  ICategoryProgress,
  ReviewerCategory,
  REVIEWER_CATEGORY_ORDER,
  REVIEWER_CATEGORY_LABEL,
  REVIEWER_CATEGORY_BADGE,
  ISaveReviewersRequest,
  isRemovableReviewer,
  isValidPeerNomination,
  categoryCompletionPercent,
  categoryReviewerTotal,
  isBelowMinimum,
  initials,
} from '../../models/feedback-360.models';

/**
 * US-PRF-005 (AC-1 + AC-3): 360 reviewer-nomination screen for one employee in the
 * active 360-enabled cycle. Shows the auto-assigned Self + Manager, system-suggested
 * Peers (same dept) and Direct Reports (org tree), and a searchable candidate pool with
 * a department filter to manually add/remove reviewers (avatar chips, §8). Below the
 * nomination grid is the completion tracker (submitted/pending/overdue per category).
 *
 * Lives under the manager/HR-gated `/performance` area at
 * `/performance/feedback-360/:employeeId`. Self + the auto-assigned Manager are locked
 * (server-owned, FR-2); BR-2 (self can't be a peer) is enforced client-side and
 * re-validated server-side. Notion-like, responsive from 360px.
 */
@Component({
  selector: 'app-feedback-360-config',
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
    <div class="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="mb-6">
        <a
          routerLink="/performance/cycles"
          class="text-sm text-neutral-500 transition hover:text-neutral-800"
          >&larr; Back to cycles</a
        >
        <h1 class="mt-2 text-2xl font-semibold tracking-tight text-neutral-900">
          360-degree reviewers
        </h1>
        @if (config(); as c) {
          <p class="mt-1 text-sm text-neutral-500">
            Nominate reviewers for
            <span class="font-medium text-neutral-800">{{
              c.employeeName
            }}</span>
            — {{ c.cycleName }}.
            @if (c.anonymous) {
              <span
                class="ml-1 inline-flex rounded-full bg-neutral-100 px-2 py-0.5 text-xs font-medium text-neutral-600 ring-1 ring-inset ring-neutral-500/20"
                >Anonymous feedback</span
              >
            }
          </p>
        }
      </div>

      @if (loading()) {
        <div class="space-y-3">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-24 animate-pulse rounded-xl bg-neutral-100"></div>
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
      } @else if (config(); as c) {
        <div @fadeIn class="space-y-6">
          <!-- Below-minimum warnings (BR-4) -->
          @if (belowMinimumCategories().length > 0) {
            <div
              class="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
              role="status"
              data-testid="minimum-warning"
            >
              Below the required minimum for:
              {{ belowMinimumCategories().join(', ') }}. Add more reviewers
              before releasing results.
            </div>
          }

          <!-- Reviewer categories (AC-1) -->
          @for (cat of categoryOrder; track cat) {
            <section
              class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            >
              <div class="mb-3 flex items-center justify-between">
                <h2 class="flex items-center gap-2 text-sm font-semibold text-neutral-900">
                  <span
                    class="inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                    [class]="categoryBadge(cat)"
                    >{{ categoryLabel(cat) }}</span
                  >
                  <span class="text-xs font-normal text-neutral-500"
                    >{{ reviewersFor(cat).length }} assigned</span
                  >
                </h2>
                <span class="text-xs text-neutral-400"
                  >min {{ minimumFor(cat) }}</span
                >
              </div>

              <!-- Assigned reviewer chips -->
              @if (reviewersFor(cat).length === 0) {
                <p class="text-sm text-neutral-400">
                  No {{ categoryLabel(cat).toLowerCase() }} assigned yet.
                </p>
              } @else {
                <ul class="flex flex-wrap gap-2">
                  @for (r of reviewersFor(cat); track r.reviewerId) {
                    <li
                      class="inline-flex items-center gap-2 rounded-full border border-neutral-200 bg-neutral-50 py-1 pl-1 pr-2 text-sm"
                      data-testid="reviewer-chip"
                    >
                      <span
                        class="inline-flex h-7 w-7 items-center justify-center rounded-full bg-neutral-200 text-xs font-medium text-neutral-700"
                        aria-hidden="true"
                        >{{ avatarInitials(r.name) }}</span
                      >
                      <span class="font-medium text-neutral-800">{{
                        r.name
                      }}</span>
                      @if (canRemove(r) && c.editable) {
                        <button
                          type="button"
                          class="ml-0.5 rounded-full p-0.5 text-neutral-400 transition hover:bg-neutral-200 hover:text-neutral-700"
                          [attr.aria-label]="'Remove ' + r.name"
                          (click)="removeReviewer(r)"
                          data-testid="remove-reviewer"
                        >
                          &times;
                        </button>
                      } @else {
                        <span
                          class="ml-0.5 text-xs text-neutral-400"
                          title="Auto-assigned"
                          >&#128274;</span
                        >
                      }
                    </li>
                  }
                </ul>
              }

              <!-- Add for Peer / Direct Report (FR-2) -->
              @if ((cat === 'Peer' || cat === 'DirectReport') && c.editable) {
                <div class="mt-4 border-t border-neutral-100 pt-4">
                  <p class="mb-2 text-xs font-medium uppercase tracking-wide text-neutral-400">
                    Add {{ categoryLabel(cat).toLowerCase() }}
                  </p>
                  <!-- Suggestions -->
                  @if (suggestionsFor(cat).length > 0) {
                    <div class="mb-3 flex flex-wrap gap-2">
                      @for (s of suggestionsFor(cat); track s.employeeId) {
                        <button
                          type="button"
                          class="inline-flex items-center gap-1 rounded-full border border-dashed border-neutral-300 px-3 py-1 text-xs text-neutral-600 transition hover:border-neutral-400 hover:bg-neutral-50"
                          (click)="addReviewer(s, cat)"
                          data-testid="suggestion-chip"
                        >
                          + {{ s.name }}
                          <span class="text-neutral-400">{{
                            s.departmentName
                          }}</span>
                        </button>
                      }
                    </div>
                  }
                </div>
              }
            </section>
          }

          <!-- Searchable candidate pool with department filter (§8) -->
          @if (config()!.editable) {
            <section
              class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
            >
              <h2 class="mb-3 text-sm font-semibold text-neutral-900">
                Add a reviewer manually
              </h2>
              <div class="mb-3 flex flex-col gap-3 sm:flex-row">
                <label class="relative block w-full sm:max-w-xs">
                  <span class="sr-only">Search employees</span>
                  <input
                    type="search"
                    [ngModel]="search()"
                    (ngModelChange)="search.set($event)"
                    placeholder="Search by name…"
                    class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                    data-testid="pool-search"
                  />
                </label>
                <label class="block w-full sm:w-auto">
                  <span class="sr-only">Filter by department</span>
                  <select
                    [ngModel]="departmentFilter()"
                    (ngModelChange)="departmentFilter.set($event)"
                    class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 sm:w-auto"
                    data-testid="department-filter"
                  >
                    <option value="all">All departments</option>
                    @for (d of departments(); track d.id) {
                      <option [value]="d.id">{{ d.name }}</option>
                    }
                  </select>
                </label>
                <label class="block w-full sm:w-auto">
                  <span class="sr-only">Add as category</span>
                  <select
                    [ngModel]="addAs()"
                    (ngModelChange)="addAs.set($event)"
                    class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 sm:w-auto"
                    data-testid="add-as"
                  >
                    <option value="Peer">As peer</option>
                    <option value="DirectReport">As direct report</option>
                  </select>
                </label>
              </div>

              @if (filteredPool().length === 0) {
                <p class="text-sm text-neutral-400">
                  No matching employees to add.
                </p>
              } @else {
                <ul class="max-h-64 divide-y divide-neutral-100 overflow-y-auto">
                  @for (p of filteredPool(); track p.employeeId) {
                    <li
                      class="flex items-center justify-between py-2"
                      data-testid="pool-row"
                    >
                      <div class="flex items-center gap-2">
                        <span
                          class="inline-flex h-8 w-8 items-center justify-center rounded-full bg-neutral-200 text-xs font-medium text-neutral-700"
                          aria-hidden="true"
                          >{{ avatarInitials(p.name) }}</span
                        >
                        <div>
                          <p class="text-sm font-medium text-neutral-800">
                            {{ p.name }}
                          </p>
                          <p class="text-xs text-neutral-500">
                            {{ p.jobTitle }} · {{ p.departmentName }}
                          </p>
                        </div>
                      </div>
                      <button
                        type="button"
                        class="rounded-lg border border-neutral-200 px-3 py-1.5 text-xs font-medium text-neutral-700 transition hover:bg-neutral-50"
                        (click)="addReviewer(p, addAs())"
                        data-testid="pool-add"
                      >
                        Add
                      </button>
                    </li>
                  }
                </ul>
              }
            </section>
          }

          <!-- Completion tracker (AC-3 / §8) -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-900">
              Completion tracker
            </h2>
            @if (trackerLoading()) {
              <div class="h-20 animate-pulse rounded-lg bg-neutral-100"></div>
            } @else if (tracker(); as t) {
              <div class="space-y-4">
                @for (p of t.categories; track p.category) {
                  <div data-testid="tracker-row">
                    <div class="mb-1 flex items-center justify-between text-sm">
                      <span class="font-medium text-neutral-700">{{
                        categoryLabel(p.category)
                      }}</span>
                      <span class="text-xs text-neutral-500">
                        {{ p.submitted }} of {{ totalFor(p) }} submitted
                        @if (p.overdue > 0) {
                          <span class="ml-1 text-rose-600"
                            >· {{ p.overdue }} overdue</span
                          >
                        }
                      </span>
                    </div>
                    <div
                      class="h-2 w-full overflow-hidden rounded-full bg-neutral-100"
                      role="progressbar"
                      [attr.aria-valuenow]="percentFor(p)"
                      aria-valuemin="0"
                      aria-valuemax="100"
                    >
                      <div
                        class="h-full rounded-full bg-emerald-500 transition-all"
                        [style.width.%]="percentFor(p)"
                        data-testid="tracker-bar"
                      ></div>
                    </div>
                  </div>
                }
              </div>
            }
          </section>

          <!-- Save + results actions -->
          <div class="flex flex-col gap-3 sm:flex-row sm:justify-end">
            <a
              [routerLink]="['/performance', 'feedback-360', config()!.employeeId, 'results']"
              class="inline-flex items-center justify-center rounded-lg border border-neutral-200 px-4 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
              data-testid="view-results"
            >
              View results
            </a>
            @if (config()!.editable) {
              <button
                type="button"
                class="inline-flex items-center justify-center rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="saving()"
                (click)="save()"
                data-testid="save-reviewers"
              >
                @if (saving()) {
                  <span
                    class="mr-2 inline-block h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent"
                  ></span>
                }
                Save reviewers
              </button>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class Feedback360ConfigComponent implements OnInit {
  private readonly service = inject(Feedback360Service);
  private readonly route = inject(ActivatedRoute);
  private readonly toastr = inject(ToastrService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly config = signal<IReviewerConfig | null>(null);
  readonly saving = signal(false);

  readonly tracker = signal<ICompletionTracker | null>(null);
  readonly trackerLoading = signal(true);

  readonly search = signal('');
  readonly departmentFilter = signal<string>('all');
  readonly addAs = signal<'Peer' | 'DirectReport'>('Peer');

  readonly categoryOrder = REVIEWER_CATEGORY_ORDER;

  private employeeId = '';

  /** Distinct departments from the candidate pool for the filter dropdown (§8). */
  readonly departments = computed(() => {
    const pool = this.config()?.candidatePool ?? [];
    const map = new Map<string, string>();
    for (const e of pool) {
      map.set(e.departmentId, e.departmentName);
    }
    return [...map.entries()].map(([id, name]) => ({ id, name }));
  });

  /** Candidate pool filtered by search + department, minus already-assigned (§8). */
  readonly filteredPool = computed(() => {
    const c = this.config();
    if (!c) {
      return [];
    }
    const term = this.search().trim().toLowerCase();
    const dept = this.departmentFilter();
    const assigned = new Set(c.reviewers.map((r) => r.reviewerId));
    return c.candidatePool.filter((e) => {
      if (assigned.has(e.employeeId)) {
        return false;
      }
      // BR-2: an employee cannot be their own peer / reviewer here.
      if (!isValidPeerNomination(c.employeeId, e.employeeId)) {
        return false;
      }
      const matchesTerm =
        term === '' || e.name.toLowerCase().includes(term);
      const matchesDept = dept === 'all' || e.departmentId === dept;
      return matchesTerm && matchesDept;
    });
  });

  /** Category names that are below their required minimum (BR-4 warning). */
  readonly belowMinimumCategories = computed(() => {
    const t = this.tracker();
    if (!t) {
      return [];
    }
    return t.categories
      .filter((p) => isBelowMinimum(p))
      .map((p) => REVIEWER_CATEGORY_LABEL[p.category]);
  });

  ngOnInit(): void {
    this.employeeId = this.route.snapshot.paramMap.get('employeeId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getReviewerConfig(this.employeeId).subscribe({
      next: (cfg) => {
        this.config.set(cfg);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load the 360 reviewer configuration.');
        this.loading.set(false);
      },
    });
    this.loadTracker();
  }

  loadTracker(): void {
    this.trackerLoading.set(true);
    this.service.getTracker(this.employeeId).subscribe({
      next: (t) => {
        this.tracker.set(t);
        this.trackerLoading.set(false);
      },
      error: () => {
        this.trackerLoading.set(false);
      },
    });
  }

  reviewersFor(category: ReviewerCategory): IReviewer[] {
    return (this.config()?.reviewers ?? []).filter(
      (r) => r.category === category,
    );
  }

  suggestionsFor(category: ReviewerCategory): IEmployeeOption[] {
    const c = this.config();
    if (!c) {
      return [];
    }
    const assigned = new Set(c.reviewers.map((r) => r.reviewerId));
    const pool =
      category === 'Peer' ? c.suggestedPeers : c.suggestedDirectReports;
    return pool.filter(
      (s) =>
        !assigned.has(s.employeeId) &&
        isValidPeerNomination(c.employeeId, s.employeeId),
    );
  }

  minimumFor(category: ReviewerCategory): number {
    return (
      this.config()?.minimums.find((m) => m.category === category)?.minimum ?? 0
    );
  }

  /** AC-1: add a candidate as a reviewer in the given category. BR-2 guarded. */
  addReviewer(option: IEmployeeOption, category: ReviewerCategory): void {
    const c = this.config();
    if (!c) {
      return;
    }
    if (
      category === 'Peer' &&
      !isValidPeerNomination(c.employeeId, option.employeeId)
    ) {
      this.toastr.error('An employee cannot review themselves as a peer.');
      return;
    }
    if (c.reviewers.some((r) => r.reviewerId === option.employeeId)) {
      return;
    }
    const reviewer: IReviewer = {
      reviewerId: option.employeeId,
      name: option.name,
      jobTitle: option.jobTitle,
      departmentName: option.departmentName,
      category,
      avatarUrl: option.avatarUrl,
      locked: false,
    };
    this.config.set({ ...c, reviewers: [...c.reviewers, reviewer] });
  }

  /** AC-1: remove a manual reviewer (Self + auto Manager are locked). */
  removeReviewer(reviewer: IReviewer): void {
    const c = this.config();
    if (!c || !this.canRemove(reviewer)) {
      return;
    }
    this.config.set({
      ...c,
      reviewers: c.reviewers.filter(
        (r) => r.reviewerId !== reviewer.reviewerId,
      ),
    });
  }

  canRemove(reviewer: IReviewer): boolean {
    return isRemovableReviewer(reviewer);
  }

  /** AC-1: persist the manual reviewer set (full-replace; Self + Manager excluded). */
  save(): void {
    const c = this.config();
    if (!c || this.saving()) {
      return;
    }
    const request: ISaveReviewersRequest = {
      reviewers: c.reviewers
        .filter((r) => r.category === 'Peer' || r.category === 'DirectReport')
        .map((r) => ({ reviewerId: r.reviewerId, category: r.category })),
    };
    this.saving.set(true);
    this.service.saveReviewers(this.employeeId, request).subscribe({
      next: (cfg) => {
        this.config.set(cfg);
        this.saving.set(false);
        this.toastr.success('Reviewers saved.');
        this.loadTracker();
      },
      error: () => {
        this.saving.set(false);
        this.toastr.error('Unable to save reviewers. Please try again.');
      },
    });
  }

  // ── tracker helpers (pure math delegated to model fns) ──
  percentFor(progress: ICategoryProgress): number {
    return categoryCompletionPercent(progress);
  }

  totalFor(progress: ICategoryProgress): number {
    return categoryReviewerTotal(progress);
  }

  avatarInitials(name: string): string {
    return initials(name);
  }

  categoryLabel(category: ReviewerCategory): string {
    return REVIEWER_CATEGORY_LABEL[category];
  }

  categoryBadge(category: ReviewerCategory): string {
    return REVIEWER_CATEGORY_BADGE[category];
  }
}
