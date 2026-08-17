import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Feedback360Service } from '../../services/feedback-360.service';
import {
  IFeedback360Results,
  ICompetencyResult,
  ICategoryAverage,
  ReviewerCategory,
  REVIEWER_CATEGORY_ORDER,
  REVIEWER_CATEGORY_LABEL,
  REVIEWER_CATEGORY_BADGE,
  REVIEWER_CATEGORY_BAR,
  ratingBarPercent,
  ratingBand360Classes,
} from '../../models/feedback-360.models';

/**
 * US-PRF-005 (AC-4): the aggregated 360 results dashboard. Shows the composite score
 * (FR-6, display-only), per-competency average bars, a per-category perspective
 * comparison (self/manager/peer/report), and individual comments — anonymized when the
 * cycle's anonymity is on (FR-5/NFR-3: the FE renders ONLY what the API returns and
 * NEVER reconstructs reviewer identity).
 *
 * The §8 "radar chart" is rendered as a NON-CHART CSS visual (grouped horizontal bars
 * per category) — chart.js is deliberately not a FE dependency (see prior Performance
 * stories + the no-chart-lib-comparison-table memory). A download button wires the
 * optional backend PDF export (FR-7) only when `exportAvailable` is true.
 *
 * Reached at `/performance/feedback-360/:employeeId/results` (manager/HR-gated).
 */
@Component({
  selector: 'app-feedback-360-results',
  standalone: true,
  imports: [CommonModule, RouterLink],
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
      <div class="mb-6 flex items-start justify-between gap-3">
        <div>
          <a
            [routerLink]="['/performance', 'feedback-360', employeeId]"
            class="text-sm text-neutral-500 transition hover:text-neutral-800"
            >&larr; Back to reviewers</a
          >
          <h1
            class="mt-2 text-2xl font-semibold tracking-tight text-neutral-900"
          >
            360 results
          </h1>
          @if (results(); as r) {
            <p class="mt-1 text-sm text-neutral-500">
              {{ r.employeeName }}
              @if (r.jobTitle) {
                · {{ r.jobTitle }}
              }
              · {{ r.cycleName }}
              @if (r.anonymous) {
                <span
                  class="ml-1 inline-flex rounded-full bg-neutral-100 px-2 py-0.5 text-xs font-medium text-neutral-600 ring-1 ring-inset ring-neutral-500/20"
                  >Anonymous</span
                >
              }
            </p>
          }
        </div>
        <div class="flex shrink-0 items-center gap-2">
          @if (results(); as r) {
            @if (!r.released && r.minPeerThresholdMet) {
              <button
                type="button"
                class="inline-flex shrink-0 items-center gap-2 rounded-lg bg-neutral-900 px-3 py-2 text-sm font-medium text-white transition hover:bg-neutral-800 disabled:opacity-50"
                [disabled]="releasing()"
                (click)="release()"
                data-testid="release-results"
              >
                @if (releasing()) {
                  <span
                    class="h-4 w-4 animate-spin rounded-full border-2 border-white/60 border-t-transparent"
                  ></span>
                }
                Release results
              </button>
            }
          }
          @if (results()?.exportAvailable) {
            <button
              type="button"
              class="inline-flex shrink-0 items-center gap-2 rounded-lg border border-neutral-200 px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50 disabled:opacity-50"
              [disabled]="exporting()"
              (click)="exportPdf()"
              data-testid="export-pdf"
            >
              @if (exporting()) {
                <span
                  class="h-4 w-4 animate-spin rounded-full border-2 border-neutral-400 border-t-transparent"
                ></span>
              }
              Export PDF
            </button>
          }
        </div>
      </div>

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-40 animate-pulse rounded-xl bg-neutral-100"></div>
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
      } @else if (results(); as r) {
        <div @fadeIn class="space-y-6">
          <!-- Not-released banner (BR-4): distinguish "threshold unmet" from
               "ready to release" using the real server counts. -->
          @if (!r.released) {
            <div
              class="rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
              role="status"
              data-testid="not-released"
            >
              @if (r.minPeerThresholdMet) {
                Results are ready to release — the minimum number of peer
                reviewers ({{ r.minPeerReviewers }}) has submitted feedback.
              } @else {
                Results are not yet released — {{ peerReviewsNeeded(r) }} more
                peer
                {{ peerReviewsNeeded(r) === 1 ? 'review is' : 'reviews are' }}
                needed ({{ r.peerResponseCount }} of
                {{ r.minPeerReviewers }} submitted).
              }
            </div>
          }

          <!-- Composite score (FR-6, display-only) -->
          @if (r.compositeScore != null) {
            <section
              class="flex items-center justify-between rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
              data-testid="composite-score"
            >
              <div>
                <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                  Composite 360 score
                </p>
                <p class="mt-1 text-sm text-neutral-500">
                  Weighted across all perspectives.
                </p>
              </div>
              <p class="text-3xl font-semibold text-neutral-900">
                {{ r.compositeScore }}<span class="text-lg text-neutral-400"
                  >/100</span
                >
              </p>
            </section>
          }

          <!-- Per-category perspective comparison (AC-4 "radar" as CSS bars) -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-1 text-sm font-semibold text-neutral-900">
              Perspective comparison
            </h2>
            <p class="mb-4 text-xs text-neutral-500">
              Average rating by reviewer category (self / manager / peer /
              report).
            </p>
            <div class="space-y-3">
              @for (ca of orderedCategoryAverages(); track ca.category) {
                <div
                  class="flex items-center gap-3"
                  data-testid="category-row"
                >
                  <span
                    class="w-28 shrink-0 text-xs font-medium text-neutral-600"
                    >{{ categoryLabel(ca.category) }}</span
                  >
                  <div
                    class="h-3 flex-1 overflow-hidden rounded-full bg-neutral-100"
                    role="progressbar"
                    [attr.aria-valuenow]="barPercent(ca.average)"
                    aria-valuemin="0"
                    aria-valuemax="100"
                  >
                    <div
                      class="h-full rounded-full transition-all"
                      [style.width.%]="barPercent(ca.average)"
                      [style.background-color]="barColor(ca.category)"
                      data-testid="category-bar"
                    ></div>
                  </div>
                  <span class="w-16 shrink-0 text-right text-xs text-neutral-500">
                    @if (ca.average != null) {
                      {{ ca.average | number: '1.1-1' }} /
                      {{ r.ratingScaleMax }}
                    } @else {
                      —
                    }
                  </span>
                </div>
              }
            </div>
          </section>

          <!-- Per-competency averages (AC-4 bar chart, CSS) -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-4 text-sm font-semibold text-neutral-900">
              Per-competency averages
            </h2>
            @if (r.competencies.length === 0) {
              <p class="text-sm text-neutral-400">No feedback yet.</p>
            } @else {
              <div class="space-y-5">
                @for (c of r.competencies; track c.questionId) {
                  <div data-testid="competency-row">
                    <div class="mb-1 flex items-center justify-between">
                      <span class="text-sm font-medium text-neutral-800">{{
                        c.title
                      }}</span>
                      <span
                        class="inline-flex rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                        [class]="bandClasses(c.overallAverage, r.ratingScaleMax)"
                      >
                        @if (c.overallAverage != null) {
                          {{ c.overallAverage | number: '1.1-1' }} /
                          {{ r.ratingScaleMax }}
                        } @else {
                          No data
                        }
                      </span>
                    </div>
                    <div
                      class="h-3 w-full overflow-hidden rounded-full bg-neutral-100"
                    >
                      <div
                        class="h-full rounded-full bg-neutral-700 transition-all"
                        [style.width.%]="barPercent(c.overallAverage)"
                        data-testid="competency-bar"
                      ></div>
                    </div>
                    <!-- per-category split chips for this competency -->
                    <div class="mt-2 flex flex-wrap gap-2">
                      @for (split of categorySplit(c); track split.category) {
                        <span
                          class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs ring-1 ring-inset"
                          [class]="categoryBadge(split.category)"
                          data-testid="competency-split"
                        >
                          {{ categoryLabel(split.category) }}:
                          @if (split.average != null) {
                            {{ split.average | number: '1.1-1' }}
                          } @else {
                            —
                          }
                        </span>
                      }
                    </div>
                  </div>
                }
              </div>
            }
          </section>

          <!-- Individual comments (AC-4) — anonymized per FR-5 -->
          <section
            class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          >
            <h2 class="mb-1 text-sm font-semibold text-neutral-900">
              Comments
            </h2>
            @if (r.anonymous) {
              <p class="mb-4 text-xs text-neutral-500">
                Reviewer identities are hidden for this cycle.
              </p>
            }
            @if (r.comments.length === 0) {
              <p class="text-sm text-neutral-400">No comments submitted.</p>
            } @else {
              <ul class="space-y-3">
                @for (cm of r.comments; track $index) {
                  <li
                    class="rounded-lg border border-neutral-100 bg-neutral-50/60 p-3"
                    data-testid="comment-row"
                  >
                    <div class="mb-1 flex flex-wrap items-center gap-2">
                      <span
                        class="inline-flex rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                        [class]="categoryBadge(cm.category)"
                        >{{ categoryLabel(cm.category) }}</span
                      >
                      <!-- Identity ONLY when the backend returns it (anonymity off). -->
                      @if (!r.anonymous && cm.reviewerName) {
                        <span
                          class="text-xs font-medium text-neutral-700"
                          data-testid="comment-author"
                          >{{ cm.reviewerName }}</span
                        >
                      } @else {
                        <span
                          class="text-xs text-neutral-400"
                          data-testid="comment-anonymous"
                          >Anonymous</span
                        >
                      }
                      @if (cm.questionTitle) {
                        <span class="text-xs text-neutral-400"
                          >· {{ cm.questionTitle }}</span
                        >
                      }
                    </div>
                    <p class="text-sm text-neutral-700">{{ cm.comment }}</p>
                  </li>
                }
              </ul>
            }
          </section>
        </div>
      }
    </div>
  `,
})
export class Feedback360ResultsComponent implements OnInit {
  private readonly service = inject(Feedback360Service);
  private readonly route = inject(ActivatedRoute);
  private readonly toastr = inject(ToastrService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly results = signal<IFeedback360Results | null>(null);
  readonly exporting = signal(false);
  readonly releasing = signal(false);

  employeeId = '';

  /** Category averages in canonical Self/Manager/Peer/DirectReport order (AC-4). */
  readonly orderedCategoryAverages = computed(() => {
    const r = this.results();
    if (!r) {
      return [] as ICategoryAverage[];
    }
    return REVIEWER_CATEGORY_ORDER.map(
      (cat) =>
        r.categoryAverages.find((c) => c.category === cat) ?? {
          category: cat,
          average: null,
          responseCount: 0,
        },
    );
  });

  ngOnInit(): void {
    this.employeeId = this.route.snapshot.paramMap.get('employeeId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.getResults(this.employeeId).subscribe({
      next: (r) => {
        this.results.set(r);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load the 360 results.');
        this.loading.set(false);
      },
    });
  }

  /** Per-competency category split in canonical order (AC-4 comparison). */
  categorySplit(competency: ICompetencyResult): ICategoryAverage[] {
    return REVIEWER_CATEGORY_ORDER.map(
      (cat) =>
        competency.byCategory.find((c) => c.category === cat) ?? {
          category: cat,
          average: null,
          responseCount: 0,
        },
    ).filter((c) => c.average != null);
  }

  /** BR-4 banner hint: how many more peer reviews are needed before the threshold. */
  peerReviewsNeeded(r: IFeedback360Results): number {
    return Math.max(0, r.minPeerReviewers - r.peerResponseCount);
  }

  /**
   * BR-4/FR-3: release the results to the reviewee. Only reachable when the peer
   * threshold is met (the button is gated on `minPeerThresholdMet`). The backend is the
   * authority — on 422 `min_peer_threshold_not_met` we surface its message; on 403 we
   * show a permission message. On success we reload so the released state renders.
   */
  release(): void {
    if (this.releasing()) {
      return;
    }
    this.releasing.set(true);
    this.service.releaseResults(this.employeeId).subscribe({
      next: () => {
        this.releasing.set(false);
        this.toastr.success('Results released to the employee.');
        this.load();
      },
      error: (err: unknown) => {
        this.releasing.set(false);
        if (this.isThresholdError(err)) {
          this.toastr.error(this.thresholdMessage(err));
        } else if (this.isForbiddenError(err)) {
          this.toastr.error(
            'You are not authorized to release these results.',
          );
        } else {
          this.toastr.error('Could not release the results. Please try again.');
        }
      },
    });
  }

  /** 422 carrying the backend `min_peer_threshold_not_met` code (BR-4). */
  private isThresholdError(err: unknown): boolean {
    return (
      err instanceof HttpErrorResponse &&
      err.status === 422 &&
      (err.error as { code?: string } | null)?.code ===
        'min_peer_threshold_not_met'
    );
  }

  /** Prefer the server's threshold message; fall back to a generic one. */
  private thresholdMessage(err: unknown): string {
    const msg =
      err instanceof HttpErrorResponse
        ? (err.error as { message?: string } | null)?.message
        : null;
    return msg && msg.trim().length > 0
      ? msg
      : 'Not enough peer reviewers have submitted to release results yet.';
  }

  /** 403 forbidden — the caller is not the reviewee's manager / HR (server-enforced). */
  private isForbiddenError(err: unknown): boolean {
    return err instanceof HttpErrorResponse && err.status === 403;
  }

  /** FR-7: download the backend-generated PDF (only when exportAvailable). */
  exportPdf(): void {
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.service.exportResultsPdf(this.employeeId).subscribe({
      next: (response) => {
        const blob = response.body;
        if (blob) {
          const filename =
            this.filenameFromDisposition(
              response.headers.get('Content-Disposition'),
            ) ?? `360-results-${this.employeeId}.pdf`;
          this.triggerDownload(blob, filename);
        }
        this.exporting.set(false);
      },
      error: () => {
        this.exporting.set(false);
        this.toastr.error('Unable to export the PDF. Please try again.');
      },
    });
  }

  private filenameFromDisposition(disposition: string | null): string | null {
    if (!disposition) {
      return null;
    }
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    return match ? decodeURIComponent(match[1]) : null;
  }

  private triggerDownload(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  }

  // ── visual helpers (pure math delegated to model fns) ──
  barPercent(average: number | null): number {
    return ratingBarPercent(average, this.results()?.ratingScaleMax ?? 5);
  }

  barColor(category: ReviewerCategory): string {
    return REVIEWER_CATEGORY_BAR[category];
  }

  bandClasses(average: number | null, scaleMax: number): string {
    return ratingBand360Classes(average, scaleMax);
  }

  categoryLabel(category: ReviewerCategory): string {
    return REVIEWER_CATEGORY_LABEL[category];
  }

  categoryBadge(category: ReviewerCategory): string {
    return REVIEWER_CATEGORY_BADGE[category];
  }
}
