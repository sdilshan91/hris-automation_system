import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  IInterview,
  interviewStatusBadge,
  interviewStatusLabel,
  interviewTypeBadge,
  interviewTypeLabel,
  endTimeLabel,
  nameInitials,
} from '../../models/interview.models';

/**
 * US-REC-005: Presentational interview card (AC-4 / §8).
 *
 * Renders one interview: round number, date/time, interviewer avatars, a type
 * badge and a color-coded status badge (Scheduled=blue / Completed=green /
 * Cancelled=gray / NoShow=amber). Pure/dumb — actions (reschedule, cancel) are
 * emitted to the parent. Used on the applicant detail timeline and (with the
 * applicant/vacancy context shown) on the tenant-wide agenda.
 */
@Component({
  selector: 'app-interview-card',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (interview(); as iv) {
      <div
        class="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm transition hover:shadow-md"
      >
        <!-- Top row: round + badges -->
        <div class="flex items-start justify-between gap-3">
          <div class="flex flex-wrap items-center gap-2">
            <span class="text-sm font-semibold text-neutral-800">
              Round {{ iv.roundNumber }}
            </span>
            <span
              class="badge ring-1 ring-inset"
              [class]="typeBadge(iv.interviewType)"
            >
              {{ typeLabel(iv.interviewType) }}
            </span>
            <span
              class="badge ring-1 ring-inset"
              [class]="statusBadge(iv.status)"
            >
              {{ statusLabel(iv.status) }}
            </span>
          </div>

          @if (showActions() && iv.status === 'Scheduled') {
            <div class="flex shrink-0 items-center gap-1">
              <button
                type="button"
                class="rounded-md px-2 py-1 text-xs font-medium text-neutral-500 transition hover:bg-neutral-100 hover:text-neutral-800"
                (click)="reschedule.emit(iv)"
              >
                Reschedule
              </button>
              <button
                type="button"
                class="rounded-md px-2 py-1 text-xs font-medium text-red-500 transition hover:bg-red-50 hover:text-red-700"
                (click)="cancel.emit(iv)"
              >
                Cancel
              </button>
            </div>
          }
        </div>

        <!-- Optional applicant / vacancy context (agenda view) -->
        @if (showContext() && (iv.applicantName || iv.vacancyTitle)) {
          <p class="mt-1 text-sm font-medium text-neutral-700">
            {{ iv.applicantName }}
            @if (iv.vacancyTitle) {
              <span class="font-normal text-neutral-400">· {{ iv.vacancyTitle }}</span>
            }
          </p>
        }

        <!-- Date / time -->
        <div class="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-neutral-600">
          <span class="inline-flex items-center gap-1.5">
            <svg
              class="h-4 w-4 text-neutral-400"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
            >
              <rect x="3" y="4" width="18" height="18" rx="2" />
              <path d="M16 2v4M8 2v4M3 10h18" />
            </svg>
            {{ iv.scheduledDate | date: 'EEE, MMM d' }}
          </span>
          <span class="inline-flex items-center gap-1.5">
            <svg
              class="h-4 w-4 text-neutral-400"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              aria-hidden="true"
            >
              <circle cx="12" cy="12" r="9" />
              <path d="M12 7v5l3 2" />
            </svg>
            {{ iv.startTime }}@if (endLabel()) { – {{ endLabel() }} }
            <span class="text-neutral-400">({{ iv.durationMinutes }} min)</span>
          </span>
        </div>

        <!-- Location / video -->
        @if (iv.interviewType === 'InPerson' && iv.location) {
          <p class="mt-1.5 text-sm text-neutral-500">📍 {{ iv.location }}</p>
        }
        @if (iv.interviewType === 'Video' && iv.videoLink) {
          <a
            class="mt-1.5 inline-block break-all text-sm text-indigo-600 hover:underline"
            [href]="iv.videoLink"
            target="_blank"
            rel="noopener noreferrer"
            >🔗 Join video meeting</a
          >
        }

        <!-- Interviewer avatars -->
        @if (iv.interviewers.length) {
          <div class="mt-3 flex items-center gap-2">
            <div class="flex -space-x-1.5">
              @for (p of iv.interviewers; track p.employeeId) {
                <span
                  class="flex h-7 w-7 items-center justify-center rounded-full border-2 border-white bg-indigo-100 text-[0.6rem] font-semibold text-indigo-700"
                  [title]="p.name + (p.department ? ' · ' + p.department : '')"
                  >{{ initials(p.name) }}</span
                >
              }
            </div>
            <span class="text-xs text-neutral-400">
              {{ interviewerNames() }}
            </span>
          </div>
        }

        <!-- Notes -->
        @if (iv.notes) {
          <p class="mt-2 whitespace-pre-wrap text-sm text-neutral-500">
            {{ iv.notes }}
          </p>
        }
      </div>
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .badge {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        padding: 0.125rem 0.5rem;
        font-size: 0.6875rem;
        font-weight: 500;
      }
    `,
  ],
})
export class InterviewCardComponent {
  readonly interview = input.required<IInterview>();
  /** Show reschedule/cancel buttons (timeline). Off for read-only agenda. */
  readonly showActions = input<boolean>(true);
  /** Show applicant/vacancy context line (agenda view). */
  readonly showContext = input<boolean>(false);

  readonly reschedule = output<IInterview>();
  readonly cancel = output<IInterview>();

  readonly endLabel = computed(() => {
    const iv = this.interview();
    return endTimeLabel(iv.startTime, iv.durationMinutes);
  });

  readonly interviewerNames = computed(() =>
    this.interview()
      .interviewers.map((p) => p.name)
      .join(', '),
  );

  initials(name: string): string {
    return nameInitials(name);
  }

  typeBadge(type: string): string {
    return interviewTypeBadge(type);
  }
  typeLabel(type: string): string {
    return interviewTypeLabel(type);
  }
  statusBadge(status: string): string {
    return interviewStatusBadge(status);
  }
  statusLabel(status: string): string {
    return interviewStatusLabel(status);
  }
}
