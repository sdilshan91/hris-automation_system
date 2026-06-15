import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  effect,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { ScorecardService } from '../../services/scorecard.service';
import {
  IScorecard,
  IScorecardBoard,
  IScorecardCriterion,
  RatingScore,
  recommendationBadge,
  recommendationLabel,
  scorecardInitials,
} from '../../models/scorecard.models';

/** A criterion row in the consolidated comparison table. */
interface ICriterionRow {
  key: string;
  name: string;
  /** Score per scorecard, index-aligned to `board.scorecards`; null when unrated. */
  scores: (RatingScore | null)[];
}

/**
 * US-REC-006: Recruiter's consolidated scorecard panel (AC-2 / AC-3 / FR-8).
 *
 * Rendered on the applicant detail Scorecards tab for one interview. Shows each
 * interviewer's criterion scores side-by-side in a clean comparison table, with
 * each scorecard's average and the aggregate average highlighted (FR-3).
 *
 * No chart library is a project dependency (chart.js / ngx-charts are NOT
 * installed), so the visual comparison (FR-8) is a comparison TABLE rather than a
 * radar/bar chart — an acceptable Phase-1 form per the story note. Revisit if a
 * chart lib is added.
 *
 * Anti-bias (FR-6 / BR-5): when the backend reports the current user is an
 * assigned interviewer who has not submitted (`canViewOthers === false`), the
 * panel blurs the table behind a "Submit your scorecard first" overlay. The
 * backend is the authority (it omits the data); the FE only reflects the flag.
 */
@Component({
  selector: 'app-scorecard-panel',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <div class="space-y-3">
        @for (n of [1, 2, 3]; track n) {
          <div class="h-10 animate-pulse rounded-lg bg-neutral-100"></div>
        }
      </div>
    } @else if (board(); as b) {
      <!-- Interviewer's own CTA (assigned but not submitted, or editable) -->
      @if (b.antiBiasApplies) {
        <div
          class="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-indigo-100 bg-indigo-50/60 px-4 py-3"
        >
          <p class="text-sm text-indigo-800">
            You are assigned to this interview — submit your scorecard.
          </p>
          <button
            type="button"
            class="rounded-lg bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-indigo-700"
            (click)="submitOwn.emit()"
          >
            Submit scorecard
          </button>
        </div>
      }

      @if (b.scorecards.length === 0 && canView()) {
        <div class="py-10 text-center">
          <p class="text-sm font-medium text-neutral-700">No scorecards yet</p>
          <p class="mt-1 text-sm text-neutral-500">
            Scorecards appear here once interviewers submit their evaluations.
          </p>
        </div>
      } @else {
        <!-- Anti-bias overlay wrapper (FR-6 / BR-5) -->
        <div class="relative">
          <div [class.pointer-events-none]="!canView()" [class.blur-sm]="!canView()">
            <!-- Consolidated comparison table (AC-3 / FR-8) -->
            <div class="overflow-x-auto rounded-xl border border-neutral-200">
              <table class="w-full min-w-[28rem] text-sm">
                <thead>
                  <tr class="border-b border-neutral-200 bg-neutral-50">
                    <th
                      class="sticky left-0 z-10 bg-neutral-50 px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-neutral-500"
                    >
                      Criterion
                    </th>
                    @for (sc of b.scorecards; track sc.id) {
                      <th class="px-3 py-2.5 text-center font-medium text-neutral-700">
                        <div class="flex flex-col items-center gap-1">
                          <span
                            class="flex h-7 w-7 items-center justify-center rounded-full bg-indigo-100 text-[0.6rem] font-semibold text-indigo-700"
                            [title]="sc.interviewerName"
                            >{{ initials(sc.interviewerName) }}</span
                          >
                          <span class="max-w-[6rem] truncate text-xs">
                            {{ sc.interviewerName || 'Interviewer' }}
                          </span>
                        </div>
                      </th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of criterionRows(); track row.key) {
                    <tr class="border-b border-neutral-100 last:border-0">
                      <td
                        class="sticky left-0 z-10 bg-white px-3 py-2.5 font-medium text-neutral-700"
                      >
                        {{ row.name }}
                      </td>
                      @for (s of row.scores; track $index) {
                        <td class="px-3 py-2.5 text-center text-neutral-800">
                          {{ s ?? '—' }}
                        </td>
                      }
                    </tr>
                  }

                  <!-- Per-scorecard average (FR-3) -->
                  <tr class="border-t-2 border-neutral-200 bg-neutral-50/60">
                    <td
                      class="sticky left-0 z-10 bg-neutral-50 px-3 py-2.5 text-xs font-semibold uppercase tracking-wide text-neutral-500"
                    >
                      Average
                    </td>
                    @for (sc of b.scorecards; track sc.id) {
                      <td class="px-3 py-2.5 text-center font-semibold text-neutral-900">
                        {{ sc.averageScore | number: '1.1-1' }}
                      </td>
                    }
                  </tr>

                  <!-- Recommendation row -->
                  <tr class="border-t border-neutral-100">
                    <td
                      class="sticky left-0 z-10 bg-white px-3 py-2.5 text-xs font-semibold uppercase tracking-wide text-neutral-500"
                    >
                      Recommendation
                    </td>
                    @for (sc of b.scorecards; track sc.id) {
                      <td class="px-3 py-2.5 text-center">
                        <span
                          class="inline-flex rounded-full px-2 py-0.5 text-[0.6875rem] font-medium ring-1 ring-inset"
                          [class]="recBadge(sc.overallRecommendation)"
                        >
                          {{ recLabel(sc.overallRecommendation) }}
                        </span>
                      </td>
                    }
                  </tr>
                </tbody>
              </table>
            </div>

            <!-- Aggregate average (highlighted, FR-3) -->
            @if (b.aggregateAverageScore !== null) {
              <div
                class="mt-4 flex items-center justify-between rounded-xl border border-indigo-200 bg-indigo-50 px-4 py-3"
              >
                <span class="text-sm font-medium text-indigo-800">
                  Aggregate average ({{ b.scorecards.length }}
                  {{ b.scorecards.length === 1 ? 'interviewer' : 'interviewers' }})
                </span>
                <span class="text-lg font-bold text-indigo-700">
                  {{ b.aggregateAverageScore | number: '1.1-1' }} / 5
                </span>
              </div>
            }

            <!-- Per-scorecard general notes (AC-2 written feedback) -->
            @for (sc of b.scorecards; track sc.id) {
              @if (sc.generalNotes) {
                <div class="mt-3 rounded-xl border border-neutral-200 px-4 py-3">
                  <p class="text-xs font-medium text-neutral-500">
                    {{ sc.interviewerName || 'Interviewer' }}
                  </p>
                  <p class="mt-1 whitespace-pre-wrap text-sm text-neutral-700">
                    {{ sc.generalNotes }}
                  </p>
                </div>
              }
            }
          </div>

          <!-- Anti-bias overlay (FR-6 / BR-5) -->
          @if (!canView()) {
            <div
              class="absolute inset-0 z-20 flex flex-col items-center justify-center rounded-xl bg-white/70 px-6 text-center backdrop-blur-sm"
            >
              <svg
                class="h-8 w-8 text-neutral-400"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.5"
                aria-hidden="true"
              >
                <rect x="3" y="11" width="18" height="11" rx="2" />
                <path d="M7 11V7a5 5 0 0 1 10 0v4" />
              </svg>
              <p class="mt-3 text-sm font-semibold text-neutral-800">
                Submit your scorecard first
              </p>
              <p class="mt-1 max-w-xs text-sm text-neutral-500">
                To keep evaluations unbiased, other interviewers' scorecards are
                hidden until you submit yours.
              </p>
              <button
                type="button"
                class="mt-4 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-indigo-700"
                (click)="submitOwn.emit()"
              >
                Submit scorecard
              </button>
            </div>
          }
        </div>
      }
    } @else {
      <p class="text-sm text-neutral-500">Could not load scorecards.</p>
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
    `,
  ],
})
export class ScorecardPanelComponent {
  private readonly scorecardService = inject(ScorecardService);
  private readonly toastr = inject(ToastrService);

  /** The interview whose scorecards to show. */
  readonly interviewId = input.required<string>();
  /**
   * Criteria to label the table rows. Optional — when provided (the parent already
   * fetched them) the table uses these names; otherwise it derives rows from the
   * union of criterion keys present in the scorecards.
   */
  readonly criteria = input<IScorecardCriterion[]>([]);
  /** Bump to force a reload (e.g. after the current user submits). */
  readonly reloadToken = input<number>(0);

  /** Emitted when the interviewer clicks Submit/Edit (parent opens the form). */
  readonly submitOwn = output<void>();

  readonly board = signal<IScorecardBoard | null>(null);
  readonly loading = signal(true);

  /** Whether other scorecards are visible (anti-bias gate, FR-6). */
  readonly canView = computed(() => !(this.board()?.antiBiasApplies ?? false));

  /** Comparison-table rows: one per criterion, scores index-aligned to scorecards. */
  readonly criterionRows = computed<ICriterionRow[]>(() => {
    const b = this.board();
    if (!b) {
      return [];
    }
    const cards = b.scorecards;
    const provided = this.criteria();
    // Determine the ordered set of criterion keys + names.
    const order: { key: string; name: string }[] =
      provided.length > 0
        ? provided.map((c) => ({ key: c.key, name: c.label }))
        : this.deriveKeys(cards);
    return order.map(({ key, name }) => ({
      key,
      name,
      scores: cards.map((sc) => {
        const r = sc.ratings.find((x) => x.criterionKey === key);
        return r ? (r.score as RatingScore) : null;
      }),
    }));
  });

  /** Union of criterion keys across scorecards, preserving first-seen order. */
  private deriveKeys(cards: IScorecard[]): { key: string; name: string }[] {
    const seen = new Set<string>();
    const out: { key: string; name: string }[] = [];
    for (const sc of cards) {
      for (const r of sc.ratings) {
        if (!seen.has(r.criterionKey)) {
          seen.add(r.criterionKey);
          out.push({ key: r.criterionKey, name: r.criterionLabel ?? r.criterionKey });
        }
      }
    }
    return out;
  }

  constructor() {
    effect(() => {
      const id = this.interviewId();
      // Re-run when the reload token changes.
      this.reloadToken();
      if (id) {
        this.load(id);
      }
    });
  }

  private load(interviewId: string): void {
    this.loading.set(true);
    this.scorecardService.listForInterview(interviewId).subscribe({
      next: (b) => {
        this.board.set(b);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.toastr.error(ScorecardService.parseErrorMessage(err));
      },
    });
  }

  initials(name: string): string {
    return scorecardInitials(name);
  }
  recLabel(value: string): string {
    return recommendationLabel(value);
  }
  recBadge(value: string): string {
    return recommendationBadge(value);
  }
}
