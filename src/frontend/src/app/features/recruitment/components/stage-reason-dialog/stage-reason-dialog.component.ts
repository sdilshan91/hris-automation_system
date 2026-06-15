import {
  Component,
  ChangeDetectionStrategy,
  signal,
  computed,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { ApplicantStage } from '../../models/applicant.models';
import { REJECTION_REASONS } from '../../models/pipeline.models';

/** Result emitted when the user confirms a stage move that needs a reason. */
export interface IStageReasonResult {
  reason: string;
  notes?: string;
}

/**
 * US-REC-003: Reason prompt for stage moves that require one (BR-3 Rejected, BR-4
 * backward move). A centered modal — for Rejected it offers the canned reasons
 * dropdown + optional free text (BR-3); for a backward move it asks for a free-text
 * reason. The board calls the stage API on confirm and rolls back on cancel.
 */
@Component({
  selector: 'app-stage-reason-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
    trigger('pop', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.96)' }),
        animate(
          '160ms ease-out',
          style({ opacity: 1, transform: 'scale(1)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div
      @backdrop
      class="fixed inset-0 z-[60] flex items-center justify-center bg-neutral-900/40 p-4"
      (click)="cancel()"
    >
      <div
        @pop
        class="w-full max-w-md rounded-xl bg-white p-6 shadow-xl"
        role="dialog"
        aria-modal="true"
        aria-labelledby="stage-reason-title"
        (click)="$event.stopPropagation()"
      >
        <h2 id="stage-reason-title" class="text-base font-semibold text-neutral-900">
          {{ title() }}
        </h2>
        <p class="mt-1 text-sm text-neutral-500">{{ subtitle() }}</p>

        <div class="mt-4 space-y-4">
          @if (isRejection()) {
            <div>
              <label
                for="reason-select"
                class="mb-1 block text-sm font-medium text-neutral-700"
              >
                Reason <span class="text-red-500">*</span>
              </label>
              <select
                id="reason-select"
                class="field"
                [(ngModel)]="selectedReason"
                (ngModelChange)="onInput()"
              >
                <option value="" disabled>Select a reason…</option>
                @for (r of reasons; track r) {
                  <option [value]="r">{{ r }}</option>
                }
              </select>
            </div>
            <div>
              <label
                for="reason-notes"
                class="mb-1 block text-sm font-medium text-neutral-700"
              >
                Notes (optional)
              </label>
              <textarea
                id="reason-notes"
                rows="3"
                class="field resize-none"
                placeholder="Add any additional context…"
                [(ngModel)]="notes"
              ></textarea>
            </div>
          } @else {
            <div>
              <label
                for="reason-free"
                class="mb-1 block text-sm font-medium text-neutral-700"
              >
                Reason <span class="text-red-500">*</span>
              </label>
              <textarea
                id="reason-free"
                rows="3"
                class="field resize-none"
                placeholder="Why are you moving this applicant back?"
                [(ngModel)]="freeReason"
                (ngModelChange)="onInput()"
              ></textarea>
            </div>
          }
        </div>

        <div class="mt-6 flex justify-end gap-2">
          <button
            type="button"
            class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
            (click)="cancel()"
          >
            Cancel
          </button>
          <button
            type="button"
            class="rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition disabled:cursor-not-allowed disabled:opacity-50"
            [class]="isRejection() ? 'bg-red-600 hover:bg-red-700' : 'bg-indigo-600 hover:bg-indigo-700'"
            [disabled]="!canConfirm()"
            (click)="confirm()"
          >
            {{ isRejection() ? 'Reject applicant' : 'Move applicant' }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .field {
        width: 100%;
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #171717;
      }
      .field:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 2px #e0e7ff;
      }
    `,
  ],
})
export class StageReasonDialogComponent {
  /** Applicant display name, for the dialog copy. */
  readonly applicantName = input.required<string>();
  /** Target stage being moved to. */
  readonly toStage = input.required<ApplicantStage>();

  readonly confirmed = output<IStageReasonResult>();
  readonly cancelled = output<void>();

  readonly reasons = REJECTION_REASONS;

  // ngModel-backed state (FormsModule).
  selectedReason = '';
  notes = '';
  freeReason = '';

  readonly isRejection = computed(() => this.toStage() === 'Rejected');

  readonly title = computed(() =>
    this.isRejection() ? 'Reject applicant' : 'Move applicant back',
  );

  readonly subtitle = computed(() =>
    this.isRejection()
      ? `A reason is required to reject ${this.applicantName()}.`
      : `Moving ${this.applicantName()} back to ${this.toStage()} requires a reason.`,
  );

  /** Track the live ngModel values so the confirm button reactively enables. */
  private readonly tick = signal(0);

  readonly canConfirm = computed(() => {
    this.tick(); // re-evaluate on input
    if (this.isRejection()) {
      return this.selectedReason.trim().length > 0;
    }
    return this.freeReason.trim().length > 0;
  });

  /** Bump the reactive tick so `canConfirm` re-evaluates on every keystroke. */
  onInput(): void {
    this.tick.update((n) => n + 1);
  }

  confirm(): void {
    this.tick.update((n) => n + 1);
    if (this.isRejection()) {
      const reason = this.selectedReason.trim();
      if (!reason) {
        return;
      }
      this.confirmed.emit({
        reason,
        notes: this.notes.trim() || undefined,
      });
      return;
    }
    const reason = this.freeReason.trim();
    if (!reason) {
      return;
    }
    this.confirmed.emit({ reason });
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
