import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  IPortalOffer,
  canRespondToPortalOffer,
  formatPortalCompensation,
} from '../../../models/portal.models';
import {
  OFFER_STATUS_BADGE,
  offerStatusLabel,
  OfferResponse,
} from '../../../models/offer.models';

/**
 * US-REC-008: Offer card for the candidate portal (AC-3 / FR-5).
 *
 * Shows the headline offer terms (position / salary / start / expiry), a PDF
 * download button (FR-4), and prominent Accept / Decline buttons that open a
 * one-time confirmation modal (BR-2). Presentational/dumb: it emits `respond`
 * (Accepted|Declined) and `download`; the smart container performs the calls and
 * reflects the move to Hired.
 *
 * The Accept/Decline buttons + confirmation are hidden once a response is recorded
 * (BR-2 one-time) or the offer is no longer `Sent`.
 */
@Component({
  selector: 'app-portal-offer-card',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (offer(); as o) {
      <section
        class="rounded-xl border border-blue-200 bg-blue-50/40 p-4 sm:p-5"
        aria-labelledby="offer-heading"
      >
        <div class="flex items-start justify-between gap-3">
          <div>
            <h3 id="offer-heading" class="text-sm font-semibold text-neutral-900">
              Your offer
            </h3>
            <p class="text-xs text-neutral-500">{{ o.offerReferenceNumber }}</p>
          </div>
          <span
            class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
            [ngClass]="statusBadge()"
          >
            {{ statusLabel() }}
          </span>
        </div>

        <dl class="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div>
            <dt class="text-xs text-neutral-500">Position</dt>
            <dd class="text-sm font-medium text-neutral-900">{{ o.offeredPosition }}</dd>
          </div>
          <div>
            <dt class="text-xs text-neutral-500">Compensation</dt>
            <dd class="text-sm font-medium text-neutral-900">{{ compensation() }}</dd>
          </div>
          <div>
            <dt class="text-xs text-neutral-500">Start date</dt>
            <dd class="text-sm font-medium text-neutral-900">
              {{ o.startDate | date: 'mediumDate' }}
            </dd>
          </div>
          <div>
            <dt class="text-xs text-neutral-500">Respond by</dt>
            <dd class="text-sm font-medium text-neutral-900">
              {{ o.expiryDate | date: 'mediumDate' }}
            </dd>
          </div>
          @if (o.departmentName) {
            <div>
              <dt class="text-xs text-neutral-500">Department</dt>
              <dd class="text-sm font-medium text-neutral-900">{{ o.departmentName }}</dd>
            </div>
          }
        </dl>

        <div class="mt-4 flex flex-wrap items-center gap-2">
          @if (o.hasDocument) {
            <button
              type="button"
              class="inline-flex items-center gap-1.5 rounded-lg border border-neutral-300 bg-white px-3 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50 disabled:opacity-50"
              [disabled]="downloading()"
              (click)="download.emit()"
            >
              <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                <path d="M10 2.75a.75.75 0 0 1 .75.75v6.69l1.97-1.97a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L6.22 9.28a.75.75 0 1 1 1.06-1.06l1.97 1.97V3.5A.75.75 0 0 1 10 2.75Z" />
                <path d="M3.5 13.5a.75.75 0 0 1 .75.75v1.25c0 .14.11.25.25.25h11a.25.25 0 0 0 .25-.25V14.25a.75.75 0 0 1 1.5 0v1.25A1.75 1.75 0 0 1 15.5 17.25h-11A1.75 1.75 0 0 1 2.75 15.5V14.25a.75.75 0 0 1 .75-.75Z" />
              </svg>
              {{ downloading() ? 'Downloading…' : 'Download PDF' }}
            </button>
          }

          @if (canRespond()) {
            <button
              type="button"
              class="inline-flex items-center rounded-lg bg-green-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-green-700 disabled:opacity-50"
              [disabled]="responding()"
              (click)="ask('Accepted')"
            >
              Accept offer
            </button>
            <button
              type="button"
              class="inline-flex items-center rounded-lg border border-red-300 bg-white px-4 py-2 text-sm font-semibold text-red-600 transition hover:bg-red-50 disabled:opacity-50"
              [disabled]="responding()"
              (click)="ask('Declined')"
            >
              Decline
            </button>
          } @else if (o.status === 'Accepted' || o.status === 'Declined') {
            <p class="text-sm font-medium text-neutral-600">
              You {{ o.status === 'Accepted' ? 'accepted' : 'declined' }} this offer.
            </p>
          }
        </div>
      </section>

      <!-- Confirmation modal (BR-2 one-time) -->
      @if (pendingChoice(); as choice) {
        <div
          class="fixed inset-0 z-50 flex items-end justify-center bg-neutral-900/40 p-4 sm:items-center"
          role="dialog"
          aria-modal="true"
          aria-labelledby="confirm-title"
          (click)="cancel()"
        >
          <div
            class="w-full max-w-md rounded-xl bg-white p-5 shadow-lg sm:p-6"
            (click)="$event.stopPropagation()"
          >
            <h4 id="confirm-title" class="text-base font-semibold text-neutral-900">
              {{ choice === 'Accepted' ? 'Accept this offer?' : 'Decline this offer?' }}
            </h4>
            <p class="mt-2 text-sm text-neutral-600">
              @if (choice === 'Accepted') {
                By accepting, you confirm you want to join. This action cannot be undone.
              } @else {
                Declining will withdraw you from this role. This action cannot be undone.
              }
            </p>
            <div class="mt-5 flex justify-end gap-2">
              <button
                type="button"
                class="rounded-lg border border-neutral-300 bg-white px-4 py-2 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
                (click)="cancel()"
              >
                Cancel
              </button>
              <button
                type="button"
                class="rounded-lg px-4 py-2 text-sm font-semibold text-white transition disabled:opacity-50"
                [class.bg-green-600]="choice === 'Accepted'"
                [class.hover:bg-green-700]="choice === 'Accepted'"
                [class.bg-red-600]="choice === 'Declined'"
                [class.hover:bg-red-700]="choice === 'Declined'"
                [disabled]="responding()"
                (click)="confirm()"
              >
                {{ choice === 'Accepted' ? 'Yes, accept' : 'Yes, decline' }}
              </button>
            </div>
          </div>
        </div>
      }
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
export class PortalOfferCardComponent {
  readonly offer = input.required<IPortalOffer | null>();
  /** True while a respond call is in flight (disables the modal confirm). */
  readonly responding = input(false);
  /** True while the PDF is downloading. */
  readonly downloading = input(false);

  readonly respond = output<OfferResponse>();
  readonly download = output<void>();

  /** The choice awaiting confirmation in the modal, or null. */
  readonly pendingChoice = signal<OfferResponse | null>(null);

  readonly canRespond = computed(() => canRespondToPortalOffer(this.offer()));
  readonly compensation = computed(() => {
    const o = this.offer();
    return o ? formatPortalCompensation(o) : '';
  });
  readonly statusLabel = computed(() => offerStatusLabel(this.offer()?.status ?? ''));
  readonly statusBadge = computed(() => {
    const s = this.offer()?.status;
    return s ? (OFFER_STATUS_BADGE[s] ?? OFFER_STATUS_BADGE.Draft) : OFFER_STATUS_BADGE.Draft;
  });

  ask(choice: OfferResponse): void {
    this.pendingChoice.set(choice);
  }

  cancel(): void {
    if (!this.responding()) {
      this.pendingChoice.set(null);
    }
  }

  confirm(): void {
    const choice = this.pendingChoice();
    if (choice) {
      this.respond.emit(choice);
    }
  }
}
