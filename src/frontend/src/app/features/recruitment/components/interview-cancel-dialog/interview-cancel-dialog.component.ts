import {
  Component,
  ChangeDetectionStrategy,
  signal,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { TrappedDialogDirective } from '../../../../shared/directives';

/**
 * US-REC-005: Confirmation prompt for cancelling an interview (AC-3).
 *
 * Centered modal with an optional cancellation reason. On confirm the parent calls
 * the cancel API (which notifies participants + removes the reminder, FR-3/BR-6);
 * on dismiss nothing changes.
 */
@Component({
  selector: 'app-interview-cancel-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, TrappedDialogDirective],
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
        animate('160ms ease-out', style({ opacity: 1, transform: 'scale(1)' })),
      ]),
    ]),
  ],
  template: `
    <div
      @backdrop
      class="fixed inset-0 z-[60] bg-neutral-900/40"
      (click)="cancel()"
      aria-hidden="true"
    ></div>
    <div class="fixed inset-0 z-[61] flex items-center justify-center p-4">
      <div
        @pop
        class="w-full max-w-md rounded-xl bg-white p-6 shadow-xl"
        role="dialog"
        aria-modal="true"
        aria-labelledby="cancel-iv-title"
        appTrappedDialog
        (dismiss)="cancel()"
      >
        <h2 id="cancel-iv-title" class="text-base font-semibold text-neutral-900">
          Cancel interview?
        </h2>
        <p class="mt-1 text-sm text-neutral-500">
          All participants will be notified and the reminder removed. This cannot
          be undone.
        </p>

        <label class="mt-4 block text-sm font-medium text-neutral-700" for="cancel-reason">
          Reason (optional)
        </label>
        <textarea
          id="cancel-reason"
          rows="2"
          class="mt-1 w-full resize-none rounded-lg border border-neutral-200 px-3 py-2 text-sm text-neutral-800 focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-200"
          placeholder="e.g. Candidate withdrew"
          [(ngModel)]="reasonValue"
        ></textarea>

        <div class="mt-5 flex justify-end gap-2">
          <button
            type="button"
            class="rounded-lg border border-neutral-200 bg-white px-4 py-2 text-sm font-medium text-neutral-600 transition hover:bg-neutral-50"
            (click)="cancel()"
          >
            Keep interview
          </button>
          <button
            type="button"
            class="rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white transition hover:bg-red-700"
            (click)="confirm()"
          >
            Cancel interview
          </button>
        </div>
      </div>
    </div>
  `,
})
export class InterviewCancelDialogComponent {
  /** Whose interview is being cancelled (for context, optional). */
  readonly applicantName = input<string>('');

  /** Emitted with the optional reason when the user confirms cancellation. */
  readonly confirmed = output<string | undefined>();
  /** Emitted when the user dismisses the dialog. */
  readonly cancelled = output<void>();

  readonly reason = signal('');

  get reasonValue(): string {
    return this.reason();
  }
  set reasonValue(v: string) {
    this.reason.set(v);
  }

  confirm(): void {
    const r = this.reason().trim();
    this.confirmed.emit(r.length ? r : undefined);
  }

  cancel(): void {
    this.cancelled.emit();
  }
}
