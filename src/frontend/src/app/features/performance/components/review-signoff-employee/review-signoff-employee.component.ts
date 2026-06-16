import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { startWith } from 'rxjs/operators';
import { ToastrService } from 'ngx-toastr';
import { ReviewSignoffService } from '../../services/review-signoff.service';
import { ReviewRecordComponent } from '../review-record/review-record.component';
import {
  DISPUTE_COMMENT_MIN,
  IReviewSignoff,
  SIGNOFF_CONFIRM_MESSAGE,
  SIGNOFF_STATUS_BADGE,
  SIGNOFF_STATUS_LABEL,
  canSubmitDispute,
  employeeCanRespond,
} from '../../models/review-signoff.models';

/**
 * US-PRF-006 EMPLOYEE side (AC-3/AC-4/FR-4). Reached from the employee self-service
 * area at `/my-review/sign-off/:reviewId`. The employee reviews the meeting notes,
 * then either:
 *
 * - "Acknowledge & Sign" → a CONFIRMATION MODAL (SIGNOFF_CONFIRM_MESSAGE) → on
 *   confirm the server records the employee's signature (name+timestamp) and the
 *   review locks (AC-3).
 * - "Dispute" → a mandatory comments textarea (≥10 chars gates submit, FR-4) →
 *   submit, which notifies the manager + HR (FR-5).
 *
 * Once signed/disputed/completed the locked `app-review-record` is shown (AC-4).
 * NFR-5: large touch targets, full-width buttons + touch-friendly dialog on mobile.
 */
@Component({
  selector: 'app-review-signoff-employee',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, ReviewRecordComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl px-4 pb-28 pt-6 sm:px-6 lg:px-8">
      <div class="mb-6">
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          Review sign-off
        </h1>
        @if (record(); as r) {
          <p class="mt-1 text-sm text-neutral-500">
            {{ r.cycleName }} · reviewed by {{ r.managerName }}
          </p>
        }
      </div>

      @if (loading()) {
        <div class="space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ loadError() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (record(); as r) {
        <div class="mb-6">
          <span
            class="rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
            [class]="statusBadge()"
            data-testid="signoff-status"
          >
            {{ statusLabel() }}
          </span>
        </div>

        <!-- The full record (notes, goals, timeline, signatures) -->
        <app-review-record [record]="r" />

        <!-- AC-3: response actions, only while pending the employee's sign-off -->
        @if (canRespond()) {
          @if (!disputing()) {
            <div
              class="fixed inset-x-0 bottom-0 z-10 mt-6 border-t border-neutral-200 bg-white/95 p-4 backdrop-blur sm:static sm:border-0 sm:bg-transparent sm:p-0 sm:backdrop-blur-none"
            >
              <div
                class="mx-auto flex max-w-3xl flex-col gap-3 sm:flex-row sm:items-center sm:justify-end"
              >
                <button
                  type="button"
                  class="rounded-lg px-4 py-3 text-sm font-medium text-rose-700 ring-1 ring-rose-200 transition hover:bg-rose-50 disabled:opacity-50"
                  [disabled]="busy()"
                  (click)="startDispute()"
                  data-testid="dispute-btn"
                >
                  Dispute
                </button>
                <button
                  type="button"
                  class="rounded-lg px-5 py-3 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
                  [style.background-color]="'var(--brand-primary)'"
                  [disabled]="busy()"
                  (click)="openConfirm()"
                  data-testid="acknowledge-btn"
                >
                  Acknowledge &amp; Sign
                </button>
              </div>
            </div>
          } @else {
            <!-- FR-4: dispute comments form -->
            <div
              class="mt-6 rounded-xl border border-rose-200 bg-white p-4 shadow-sm"
              data-testid="dispute-form"
            >
              <label
                class="mb-1 block text-sm font-medium text-neutral-700"
                for="dispute-comments"
              >
                Why do you disagree with this review?
              </label>
              <textarea
                id="dispute-comments"
                rows="4"
                [formControl]="disputeControl"
                class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1"
                placeholder="Explain your concerns (minimum {{ commentMin }} characters). This is shared with your manager and HR."
              ></textarea>
              <div class="mt-1 flex items-center justify-between text-xs">
                @if (!disputeValid() && disputeLength() > 0) {
                  <span class="text-rose-600" role="alert">
                    At least {{ commentMin }} characters required.
                  </span>
                } @else {
                  <span class="text-neutral-400">
                    Shared with your manager and HR.
                  </span>
                }
                <span class="text-neutral-400">{{ disputeLength() }} chars</span>
              </div>
              <div class="mt-3 flex flex-col gap-2 sm:flex-row sm:justify-end">
                <button
                  type="button"
                  class="rounded-lg px-4 py-2.5 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
                  (click)="cancelDispute()"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  class="rounded-lg bg-rose-600 px-5 py-2.5 text-sm font-medium text-white transition hover:bg-rose-700 disabled:cursor-not-allowed disabled:opacity-50"
                  [disabled]="!disputeValid() || busy()"
                  (click)="submitDispute()"
                  data-testid="submit-dispute-btn"
                >
                  Submit Dispute
                </button>
              </div>
            </div>
          }
        }
      }
    </div>

    <!-- AC-3 confirmation modal (touch-friendly, NFR-5) -->
    @if (confirmOpen()) {
      <div
        class="fixed inset-0 z-50 flex items-end justify-center bg-black/40 p-0 sm:items-center sm:p-4"
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        data-testid="confirm-modal"
      >
        <div
          class="w-full rounded-t-2xl bg-white p-6 shadow-xl sm:max-w-md sm:rounded-2xl"
        >
          <h2 id="confirm-title" class="text-lg font-semibold text-neutral-900">
            Acknowledge &amp; sign
          </h2>
          <p class="mt-2 text-sm text-neutral-600">{{ confirmMessage }}</p>
          <div class="mt-6 flex flex-col gap-2 sm:flex-row sm:justify-end">
            <button
              type="button"
              class="rounded-lg px-4 py-3 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
              (click)="closeConfirm()"
              data-testid="confirm-cancel"
            >
              Cancel
            </button>
            <button
              type="button"
              class="rounded-lg px-5 py-3 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              [disabled]="busy()"
              (click)="acknowledge()"
              data-testid="confirm-sign"
            >
              @if (acknowledging()) {
                Signing…
              } @else {
                Acknowledge &amp; Sign
              }
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ReviewSignoffEmployeeComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(ReviewSignoffService);
  private readonly toastr = inject(ToastrService);

  readonly commentMin = DISPUTE_COMMENT_MIN;
  readonly confirmMessage = SIGNOFF_CONFIRM_MESSAGE;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly acknowledging = signal(false);
  readonly submittingDispute = signal(false);
  readonly record = signal<IReviewSignoff | null>(null);
  readonly confirmOpen = signal(false);
  readonly disputing = signal(false);

  private reviewId = '';

  readonly disputeControl = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required],
  });

  private readonly disputeValue = toSignal(
    this.disputeControl.valueChanges.pipe(
      startWith(this.disputeControl.value),
    ),
    { initialValue: this.disputeControl.value },
  );

  readonly busy = computed(
    () => this.acknowledging() || this.submittingDispute(),
  );

  readonly canRespond = computed(() => {
    const r = this.record();
    return !!r && employeeCanRespond(r.status);
  });

  readonly disputeLength = computed(() => (this.disputeValue() ?? '').trim().length);
  readonly disputeValid = computed(() => canSubmitDispute(this.disputeValue()));

  readonly statusBadge = computed(() =>
    this.record() ? SIGNOFF_STATUS_BADGE[this.record()!.status] : '',
  );
  readonly statusLabel = computed(() =>
    this.record() ? SIGNOFF_STATUS_LABEL[this.record()!.status] : '',
  );

  ngOnInit(): void {
    this.reviewId = this.route.snapshot.paramMap.get('reviewId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getSignoff(this.reviewId).subscribe({
      next: (record) => {
        this.record.set(record);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load your review for sign-off.');
        this.loading.set(false);
      },
    });
  }

  // ─── Acknowledge (AC-3) ─────────────────────────────────────

  openConfirm(): void {
    if (!this.canRespond() || this.busy()) {
      return;
    }
    this.confirmOpen.set(true);
  }

  closeConfirm(): void {
    this.confirmOpen.set(false);
  }

  acknowledge(): void {
    const r = this.record();
    if (!r || !this.canRespond() || this.busy()) {
      return;
    }
    this.acknowledging.set(true);
    this.service.acknowledge(this.reviewId).subscribe({
      next: (saved) => {
        this.acknowledging.set(false);
        this.confirmOpen.set(false);
        this.record.set(saved);
        this.toastr.success('Thank you — your sign-off has been recorded.');
      },
      error: () => {
        this.acknowledging.set(false);
        this.toastr.error('Could not record your sign-off. Please try again.');
      },
    });
  }

  // ─── Dispute (FR-4) ─────────────────────────────────────────

  startDispute(): void {
    if (!this.canRespond()) {
      return;
    }
    this.disputing.set(true);
  }

  cancelDispute(): void {
    this.disputing.set(false);
    this.disputeControl.reset('');
  }

  submitDispute(): void {
    const r = this.record();
    if (!r || !this.canRespond() || this.busy() || !this.disputeValid()) {
      return;
    }
    this.submittingDispute.set(true);
    this.service
      .dispute(this.reviewId, { comments: this.disputeControl.value.trim() })
      .subscribe({
        next: (saved) => {
          this.submittingDispute.set(false);
          this.disputing.set(false);
          this.record.set(saved);
          this.toastr.success(
            'Your dispute has been submitted. Your manager and HR have been notified.',
          );
        },
        error: () => {
          this.submittingDispute.set(false);
          this.toastr.error('Could not submit your dispute. Please try again.');
        },
      });
  }
}
