import {
  Component,
  ChangeDetectionStrategy,
  input,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { IPortalInterview, portalInterviewTypeLabel } from '../../../models/portal.models';
import {
  endTimeLabel,
  INTERVIEW_TYPE_BADGE,
} from '../../../models/interview.models';

/**
 * US-REC-008: Upcoming interview card for the candidate portal (AC-2 / FR-3).
 *
 * Shows date/time prominently, a type badge, location (in-person) or a "Join
 * Meeting" link (video), and the interviewer name(s). Sanitized view — names only,
 * no internal notes (NFR-5). Presentational/dumb.
 */
@Component({
  selector: 'app-portal-interview-card',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (interview(); as iv) {
      <section
        class="rounded-xl border border-neutral-200 bg-white p-4 sm:p-5"
        aria-labelledby="iv-heading"
      >
        <div class="flex items-start gap-3">
          <span
            class="flex h-10 w-10 flex-none items-center justify-center rounded-lg bg-blue-50 text-blue-600"
            aria-hidden="true"
          >
            <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
              <path
                d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2ZM3.5 8.5v6.75c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25V8.5h-13Z"
              />
            </svg>
          </span>
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <h3 id="iv-heading" class="text-sm font-semibold text-neutral-900">
                Upcoming interview
              </h3>
              <span
                class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                [ngClass]="typeBadge()"
              >
                {{ typeLabel() }}
              </span>
              @if (iv.roundNumber > 1) {
                <span class="text-xs text-neutral-400">Round {{ iv.roundNumber }}</span>
              }
            </div>

            <p class="mt-1 text-base font-semibold text-neutral-900">
              {{ iv.scheduledDate | date: 'EEEE, MMM d, y' }}
            </p>
            <p class="text-sm text-neutral-600">
              {{ iv.startTime }}@if (endLabel()) {<span> – {{ endLabel() }}</span>}
              <span class="text-neutral-400">({{ iv.durationMinutes }} min)</span>
            </p>

            @if (iv.interviewType === 'Video' && iv.videoLink) {
              <a
                [href]="iv.videoLink"
                target="_blank"
                rel="noopener noreferrer"
                class="mt-3 inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-2 text-sm font-medium text-white transition hover:bg-blue-700"
              >
                <svg class="h-4 w-4" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                  <path d="M3.25 4A2.25 2.25 0 0 0 1 6.25v7.5A2.25 2.25 0 0 0 3.25 16h8.5A2.25 2.25 0 0 0 14 13.75v-1.19l3.13 1.74A.75.75 0 0 0 18.25 13V7a.75.75 0 0 0-1.12-.65L14 8.09V6.25A2.25 2.25 0 0 0 11.75 4h-8.5Z" />
                </svg>
                Join Meeting
              </a>
            } @else if (iv.interviewType === 'InPerson' && iv.location) {
              <p class="mt-2 flex items-start gap-1.5 text-sm text-neutral-600">
                <svg class="mt-0.5 h-4 w-4 flex-none text-neutral-400" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                  <path fill-rule="evenodd" d="M10 2a5.5 5.5 0 0 0-5.5 5.5c0 3.6 4.4 8.5 5 9.16a.75.75 0 0 0 1.06 0c.55-.66 4.94-5.56 4.94-9.16A5.5 5.5 0 0 0 10 2Zm0 7.5a2 2 0 1 1 0-4 2 2 0 0 1 0 4Z" clip-rule="evenodd" />
                </svg>
                <span>{{ iv.location }}</span>
              </p>
            }

            @if (interviewerNames()) {
              <p class="mt-2 text-sm text-neutral-500">
                With {{ interviewerNames() }}
              </p>
            }
          </div>
        </div>
      </section>
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
export class PortalInterviewCardComponent {
  readonly interview = input.required<IPortalInterview | null>();

  readonly typeLabel = computed(() =>
    portalInterviewTypeLabel(this.interview()?.interviewType ?? ''),
  );

  readonly typeBadge = computed(() => {
    const type = this.interview()?.interviewType;
    return type
      ? (INTERVIEW_TYPE_BADGE[type] ?? INTERVIEW_TYPE_BADGE.InPerson)
      : INTERVIEW_TYPE_BADGE.InPerson;
  });

  readonly endLabel = computed(() => {
    const iv = this.interview();
    return iv ? endTimeLabel(iv.startTime, iv.durationMinutes) : '';
  });

  readonly interviewerNames = computed(() =>
    (this.interview()?.interviewers ?? [])
      .map((i) => i.name)
      .filter(Boolean)
      .join(', '),
  );
}
