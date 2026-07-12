import {
  Component,
  ChangeDetectionStrategy,
  ElementRef,
  OnInit,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { ReviewSignoffService } from '../../services/review-signoff.service';
import { ReviewRecordComponent } from '../review-record/review-record.component';
import {
  IReviewSignoff,
  SIGNOFF_STATUS_BADGE,
  SIGNOFF_STATUS_LABEL,
  buildMeetingNotesTemplate,
  notesAreEmpty,
  notesEditable,
} from '../../models/review-signoff.models';

type NotesCommand =
  | 'bold'
  | 'italic'
  | 'underline'
  | 'insertUnorderedList'
  | 'insertOrderedList';

/**
 * US-PRF-006 MANAGER side (AC-1/AC-2/AC-4). Attached to the manager-review area at
 * `/performance/reviews/:employeeId/signoff` (the per-employee manager review is the
 * `:employeeId` route, US-PRF-003). After the manager review is submitted the manager:
 *
 * - AC-1: edits MEETING NOTES in a dependency-free contenteditable editor
 *   pre-populated with a TEMPLATE (strengths, areas for improvement, agreed actions
 *   with deadlines, overall summary) referencing the goal titles + manager ratings.
 *   No heavy RTE dependency is added (zero-dep `contenteditable` + execCommand); the
 *   stored HTML is sanitized on display via Angular `[innerHTML]` (NFR-4).
 * - AC-2: "Request Employee Sign-Off" persists the notes, records the manager
 *   signature server-side, and flips status → PendingEmployeeSignOff.
 * - AC-4: once locked, the completed `app-review-record` is shown (timeline,
 *   signatures, export). HR can resolve a dispute from here (BR-4).
 *
 * NFR-5: full-screen editor + large touch targets on mobile; WCAG 2.1 AA toolbar.
 */
@Component({
  selector: 'app-review-signoff',
  standalone: true,
  imports: [CommonModule, RouterLink, ReviewRecordComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl px-4 pb-28 pt-6 sm:px-6 lg:px-8">
      <a
        [routerLink]="['/performance/reviews', employeeId]"
        class="mb-4 inline-flex items-center gap-1 text-sm text-neutral-500 transition hover:text-neutral-800"
      >
        <span aria-hidden="true">←</span> Back to review
      </a>

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
        <!-- Header -->
        <div class="mb-6 flex flex-wrap items-start justify-between gap-3">
          <div class="min-w-0">
            <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
              Meeting notes &amp; sign-off
            </h1>
            <p class="mt-1 text-sm text-neutral-500">
              {{ r.employeeName }} · {{ r.cycleName }}
            </p>
          </div>
          <span
            class="rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
            [class]="statusBadge()"
            data-testid="signoff-status"
          >
            {{ statusLabel() }}
          </span>
        </div>

        <!-- Dispute escalation banner + HR resolve (BR-4/FR-5) -->
        @if (r.status === 'Disputed') {
          <div
            class="mb-6 rounded-xl border border-rose-200 bg-rose-50 p-4 text-sm text-rose-800"
            role="alert"
            data-testid="dispute-banner"
          >
            <p class="font-medium">The employee disputed this review.</p>
            <p class="mt-1 whitespace-pre-line text-rose-700/90">
              {{ r.disputeComments }}
            </p>
            <div class="mt-3 flex flex-wrap gap-2">
              <button
                type="button"
                class="rounded-lg bg-white px-3 py-1.5 text-xs font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50 disabled:opacity-50"
                [disabled]="busy()"
                (click)="resolve('Amend')"
                data-testid="resolve-amend"
              >
                Amend &amp; reopen notes
              </button>
              <button
                type="button"
                class="rounded-lg bg-rose-600 px-3 py-1.5 text-xs font-medium text-white transition hover:bg-rose-700 disabled:opacity-50"
                [disabled]="busy()"
                (click)="resolve('Confirm')"
                data-testid="resolve-confirm"
              >
                Confirm review (re-request sign-off)
              </button>
            </div>
          </div>
        }

        <!-- AC-1: meeting-notes editor (only while editable) -->
        @if (editable()) {
          <div
            class="mb-6 rounded-xl border border-neutral-200 bg-white shadow-sm"
          >
            <div
              class="flex flex-wrap items-center gap-1 border-b border-neutral-100 px-2 py-2"
              role="toolbar"
              aria-label="Meeting notes formatting"
            >
              <button
                type="button"
                class="notes-btn font-bold"
                aria-label="Bold"
                (mousedown)="$event.preventDefault()"
                (click)="exec('bold')"
              >
                B
              </button>
              <button
                type="button"
                class="notes-btn italic"
                aria-label="Italic"
                (mousedown)="$event.preventDefault()"
                (click)="exec('italic')"
              >
                I
              </button>
              <button
                type="button"
                class="notes-btn underline"
                aria-label="Underline"
                (mousedown)="$event.preventDefault()"
                (click)="exec('underline')"
              >
                U
              </button>
              <span class="mx-1 h-5 w-px bg-neutral-200" aria-hidden="true"></span>
              <button
                type="button"
                class="notes-btn"
                aria-label="Bulleted list"
                (mousedown)="$event.preventDefault()"
                (click)="exec('insertUnorderedList')"
              >
                &bull; List
              </button>
              <button
                type="button"
                class="notes-btn"
                aria-label="Numbered list"
                (mousedown)="$event.preventDefault()"
                (click)="exec('insertOrderedList')"
              >
                1. List
              </button>
            </div>
            <div
              #editor
              class="notes-content prose prose-sm max-w-none px-4 py-3 min-h-[16rem] focus:outline-none"
              contenteditable="true"
              role="textbox"
              aria-multiline="true"
              aria-label="Meeting notes"
              (input)="onNotesInput()"
            ></div>
          </div>

          @if (notesEmpty()) {
            <p class="mb-4 text-sm text-rose-600" role="alert" data-testid="notes-required">
              Add meeting notes before requesting sign-off.
            </p>
          }

          <!-- Actions (AC-2) — sticky on mobile -->
          <div
            class="fixed inset-x-0 bottom-0 z-10 border-t border-neutral-200 bg-white/95 p-4 backdrop-blur sm:static sm:border-0 sm:bg-transparent sm:p-0 sm:backdrop-blur-none"
          >
            <div
              class="mx-auto flex max-w-3xl flex-col-reverse gap-3 sm:flex-row sm:items-center sm:justify-end"
            >
              <button
                type="button"
                class="rounded-lg px-4 py-2.5 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50 disabled:opacity-50"
                [disabled]="busy()"
                (click)="saveNotes()"
                data-testid="save-notes-btn"
              >
                Save notes
              </button>
              <button
                type="button"
                class="rounded-lg px-5 py-2.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
                [style.background-color]="'var(--brand-primary)'"
                [disabled]="busy() || notesEmpty()"
                (click)="requestSignoff()"
                data-testid="request-signoff-btn"
              >
                @if (requesting()) {
                  <span class="inline-flex items-center gap-2">
                    <span
                      class="h-4 w-4 animate-spin rounded-full border-2 border-white/40 border-t-white"
                    ></span>
                    Requesting…
                  </span>
                } @else {
                  Request Employee Sign-Off
                }
              </button>
            </div>
          </div>
        } @else {
          <!-- AC-4: locked / completed record view -->
          <app-review-record [record]="r" />
        }
      }
    </div>
  `,
  styles: [
    `
      .notes-btn {
        min-width: 2.25rem;
        min-height: 2.25rem;
        padding: 0.25rem 0.6rem;
        border-radius: 0.375rem;
        font-size: 0.875rem;
        color: #525252;
        transition:
          background-color 150ms ease,
          color 150ms ease;
      }
      .notes-btn:hover {
        background-color: #f5f5f5;
      }
      .notes-content:empty::before {
        content: 'Document the review discussion…';
        color: #a3a3a3;
        pointer-events: none;
      }
    `,
  ],
})
export class ReviewSignoffComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(ReviewSignoffService);
  private readonly toastr = inject(ToastrService);

  private readonly editorRef =
    viewChild<ElementRef<HTMLDivElement>>('editor');

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly savingNotes = signal(false);
  readonly requesting = signal(false);
  readonly resolving = signal(false);
  readonly record = signal<IReviewSignoff | null>(null);
  /** Live snapshot of the editor HTML (drives the empty-notes gate). */
  readonly notesHtml = signal('');

  employeeId = '';

  readonly busy = computed(
    () => this.savingNotes() || this.requesting() || this.resolving(),
  );

  readonly editable = computed(() => {
    const r = this.record();
    return !!r && notesEditable(r.status);
  });

  readonly notesEmpty = computed(() => notesAreEmpty(this.notesHtml()));

  readonly statusBadge = computed(() =>
    this.record() ? SIGNOFF_STATUS_BADGE[this.record()!.status] : '',
  );
  readonly statusLabel = computed(() =>
    this.record() ? SIGNOFF_STATUS_LABEL[this.record()!.status] : '',
  );

  ngOnInit(): void {
    this.employeeId = this.route.snapshot.paramMap.get('employeeId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getSignoff(this.employeeId).subscribe({
      next: (record) => {
        this.record.set(record);
        this.hydrateNotes(record);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load the sign-off record.');
        this.loading.set(false);
      },
    });
  }

  /** AC-1: seed the editor with saved notes, or the pre-populated template. */
  private hydrateNotes(record: IReviewSignoff): void {
    const html =
      record.meetingNotesHtml && record.meetingNotesHtml.trim().length > 0
        ? record.meetingNotesHtml
        : buildMeetingNotesTemplate(record.goals, record.ratingScaleMax);
    this.notesHtml.set(html);
    // The editor element may not be in the DOM until change detection runs; set it
    // on the next macrotask once contenteditable has rendered.
    queueMicrotask(() => {
      const el = this.editorRef()?.nativeElement;
      if (el) {
        el.innerHTML = html;
      }
    });
  }

  onNotesInput(): void {
    const el = this.editorRef()?.nativeElement;
    this.notesHtml.set(el ? el.innerHTML : '');
  }

  exec(command: NotesCommand): void {
    const el = this.editorRef()?.nativeElement;
    el?.focus();
    document.execCommand(command, false);
    this.onNotesInput();
  }

  saveNotes(): void {
    const r = this.record();
    if (!r || !this.editable() || this.busy()) {
      return;
    }
    this.savingNotes.set(true);
    this.service
      .saveNotes(this.employeeId, { meetingNotesHtml: this.notesHtml() })
      .subscribe({
        next: (saved) => {
          this.savingNotes.set(false);
          this.record.set(saved);
          this.toastr.success('Meeting notes saved.');
        },
        error: () => {
          this.savingNotes.set(false);
          this.toastr.error('Could not save the notes. Please try again.');
        },
      });
  }

  /** AC-2: persist notes + request the employee's sign-off. */
  requestSignoff(): void {
    const r = this.record();
    if (!r || !this.editable() || this.busy()) {
      return;
    }
    if (this.notesEmpty()) {
      this.toastr.error('Add meeting notes before requesting sign-off.');
      return;
    }
    this.requesting.set(true);
    this.service
      .requestSignoff(this.employeeId, { meetingNotesHtml: this.notesHtml() })
      .subscribe({
        next: (saved) => {
          this.requesting.set(false);
          this.record.set(saved);
          this.toastr.success(
            'Sign-off requested. The employee has been notified.',
          );
        },
        error: () => {
          this.requesting.set(false);
          this.toastr.error('Could not request sign-off. Please try again.');
        },
      });
  }

  /** BR-4: HR resolves the dispute. */
  resolve(resolution: 'Amend' | 'Confirm'): void {
    const r = this.record();
    if (!r || this.busy()) {
      return;
    }
    this.resolving.set(true);
    this.service.resolveDispute(this.employeeId, { resolution }).subscribe({
      next: (saved) => {
        this.resolving.set(false);
        this.record.set(saved);
        this.hydrateNotes(saved);
        this.toastr.success(
          resolution === 'Amend'
            ? 'Notes reopened for amendment.'
            : 'Review confirmed; sign-off re-requested.',
        );
      },
      error: () => {
        this.resolving.set(false);
        this.toastr.error('Could not resolve the dispute. Please try again.');
      },
    });
  }
}
