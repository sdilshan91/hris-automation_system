import {
  Component,
  ChangeDetectionStrategy,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { ReviewSignoffService } from '../../services/review-signoff.service';
import {
  IReviewSignoff,
  SIGNOFF_STATUS_BADGE,
  SIGNOFF_STATUS_LABEL,
  buildSignoffTimeline,
} from '../../models/review-signoff.models';

/**
 * US-PRF-006 AC-4: the completed-review record — a presentational view of the full,
 * auditable sign-off record: the goal + rating snapshot, the meeting notes, the
 * sign-off timestamps + digital signatures, and the §8 status timeline (Notes Added →
 * Sign-Off Requested → Signed / Disputed → Completed).
 *
 * Reused by the manager-side (review-signoff) and employee-side
 * (review-signoff-employee) containers AND mountable directly. It owns the "Export
 * PDF" button (FR-6), wired only when `record().exportAvailable` is true. The meeting
 * notes are rendered via Angular's default `[innerHTML]` binding, which sanitizes the
 * stored HTML (NFR-4 XSS) — we never bypassSecurityTrust.
 */
@Component({
  selector: 'app-review-record',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (record(); as r) {
      <div class="space-y-6" data-testid="review-record">
        <!-- Status timeline (AC-4 / §8) -->
        <div
          class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
          data-testid="signoff-timeline"
        >
          <h2 class="mb-4 text-sm font-semibold text-neutral-900">
            Sign-off timeline
          </h2>
          <ol class="relative ml-2 border-l border-neutral-200">
            @for (step of timeline(); track step.key) {
              <li class="mb-5 ml-5 last:mb-0" data-testid="timeline-step">
                <span
                  class="absolute -left-[7px] mt-1 flex h-3.5 w-3.5 items-center justify-center rounded-full ring-4 ring-white"
                  [class.bg-emerald-500]="step.done"
                  [class.bg-neutral-300]="!step.done"
                  aria-hidden="true"
                ></span>
                <p
                  class="text-sm font-medium"
                  [class.text-neutral-900]="step.done"
                  [class.text-neutral-400]="!step.done"
                >
                  {{ step.label }}
                </p>
                @if (step.occurredOn) {
                  <p class="mt-0.5 text-xs text-neutral-500">
                    {{ step.occurredOn | date: 'medium' }}
                  </p>
                }
              </li>
            }
          </ol>
        </div>

        <!-- Goals + ratings snapshot -->
        <div class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm">
          <div class="mb-4 flex items-center justify-between">
            <h2 class="text-sm font-semibold text-neutral-900">
              Goals & ratings
            </h2>
            @if (r.finalScore != null) {
              <span
                class="rounded-full bg-neutral-100 px-2.5 py-0.5 text-xs font-medium text-neutral-700"
                data-testid="record-final-score"
              >
                Final score {{ r.finalScore }}%
              </span>
            }
          </div>
          @if (r.goals.length === 0) {
            <p class="text-sm text-neutral-500">No goals on record.</p>
          } @else {
            <ul class="divide-y divide-neutral-100">
              @for (g of r.goals; track g.goalId) {
                <li class="flex items-center justify-between gap-3 py-2.5">
                  <div class="min-w-0">
                    <p class="truncate text-sm text-neutral-800">{{ g.title }}</p>
                    <p class="text-xs text-neutral-400">Weight {{ g.weight }}%</p>
                  </div>
                  <span class="flex-none text-sm font-medium text-neutral-700">
                    {{
                      g.managerRating != null
                        ? g.managerRating + '/' + r.ratingScaleMax
                        : '—'
                    }}
                  </span>
                </li>
              }
            </ul>
          }
        </div>

        <!-- Meeting notes (sanitized via [innerHTML], NFR-4) -->
        <div class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm">
          <h2 class="mb-3 text-sm font-semibold text-neutral-900">
            Meeting notes
          </h2>
          <div
            class="prose prose-sm max-w-none text-neutral-700"
            data-testid="record-notes"
            [innerHTML]="r.meetingNotesHtml"
          ></div>
        </div>

        <!-- Signatures (AC-4 / §7) -->
        <div class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm">
          <h2 class="mb-4 text-sm font-semibold text-neutral-900">
            Digital signatures
          </h2>
          <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div
              class="rounded-lg border border-neutral-100 bg-neutral-50 p-3"
              data-testid="manager-signature"
            >
              <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                Manager
              </p>
              @if (r.managerSignature; as s) {
                <p class="mt-1 text-sm font-medium text-neutral-900">
                  {{ s.name }}
                </p>
                <p class="text-xs text-neutral-500">
                  Signed {{ s.signedOn | date: 'medium' }}
                </p>
              } @else {
                <p class="mt-1 text-sm text-neutral-400">Not yet signed</p>
              }
            </div>
            <div
              class="rounded-lg border border-neutral-100 bg-neutral-50 p-3"
              data-testid="employee-signature"
            >
              <p class="text-xs font-medium uppercase tracking-wide text-neutral-400">
                Employee
              </p>
              @if (r.employeeSignature; as s) {
                <p class="mt-1 text-sm font-medium text-neutral-900">
                  {{ s.name }}
                </p>
                <p class="text-xs text-neutral-500">
                  Signed {{ s.signedOn | date: 'medium' }}
                </p>
              } @else if (r.disputeComments) {
                <p class="mt-1 text-sm font-medium text-rose-700">Disputed</p>
                <p
                  class="mt-1 whitespace-pre-line text-xs text-neutral-600"
                  data-testid="record-dispute"
                >
                  {{ r.disputeComments }}
                </p>
              } @else {
                <p class="mt-1 text-sm text-neutral-400">Not yet signed</p>
              }
            </div>
          </div>
        </div>

        <!-- Export PDF (FR-6), wired only when the backend exposes it -->
        @if (r.exportAvailable) {
          <div class="flex justify-end">
            <button
              type="button"
              class="inline-flex items-center gap-2 rounded-lg px-4 py-2.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              [disabled]="exporting()"
              (click)="exportPdf()"
              data-testid="export-pdf-btn"
            >
              @if (exporting()) {
                <span
                  class="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"
                ></span>
                Preparing…
              } @else {
                Export PDF
              }
            </button>
          </div>
        }
      </div>
    }
  `,
})
export class ReviewRecordComponent {
  private readonly service = inject(ReviewSignoffService);
  private readonly toastr = inject(ToastrService);

  /** The full sign-off record to render (AC-4). */
  readonly record = input.required<IReviewSignoff>();

  readonly exporting = signal(false);

  readonly statusBadge = computed(() => SIGNOFF_STATUS_BADGE[this.record().status]);
  readonly statusLabel = computed(() => SIGNOFF_STATUS_LABEL[this.record().status]);

  /** AC-4 timeline (server's if present, else derived). */
  readonly timeline = computed(() => buildSignoffTimeline(this.record()));

  /** FR-6: download the server-generated PDF; filename from Content-Disposition. */
  exportPdf(): void {
    if (this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.service.exportPdf(this.record().employeeId).subscribe({
      next: (resp) => {
        this.exporting.set(false);
        this.triggerDownload(resp);
      },
      error: () => {
        this.exporting.set(false);
        this.toastr.error('Could not export the review. Please try again.');
      },
    });
  }

  private triggerDownload(resp: HttpResponse<Blob>): void {
    const blob = resp.body;
    if (!blob) {
      this.toastr.error('The export was empty.');
      return;
    }
    const filename =
      this.filenameFromDisposition(
        resp.headers.get('Content-Disposition'),
      ) ?? `review-${this.record().reviewId}.pdf`;
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = filename;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  private filenameFromDisposition(header: string | null): string | null {
    if (!header) {
      return null;
    }
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(header);
    return match ? decodeURIComponent(match[1]) : null;
  }
}
