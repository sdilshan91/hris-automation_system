import {
  Component,
  ChangeDetectionStrategy,
  input,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { IPortalTimelineEvent } from '../../../models/portal.models';

/**
 * US-REC-008: Vertical activity timeline for an application (FR-6 / §8).
 *
 * Notion-like chronological log of sanitized status events (BR-3 — the backend only
 * sends candidate-safe labels; this component renders them as plain text, never
 * innerHTML, so there is no XSS surface). Presentational/dumb. Events come newest
 * first from the service.
 */
@Component({
  selector: 'app-portal-timeline',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (events().length > 0) {
      <section aria-labelledby="timeline-heading">
        <h3 id="timeline-heading" class="mb-3 text-sm font-semibold text-neutral-900">
          Activity
        </h3>
        <ol class="relative ml-2 border-l border-neutral-200">
          @for (e of events(); track $index) {
            <li class="mb-4 ml-4 last:mb-0">
              <span
                class="absolute -left-1.5 mt-1 h-3 w-3 rounded-full border-2 border-white bg-neutral-300"
                aria-hidden="true"
              ></span>
              <p class="text-sm text-neutral-800">{{ e.label }}</p>
              @if (e.at) {
                <time [attr.datetime]="e.at" class="text-xs text-neutral-400">
                  {{ e.at | date: 'medium' }}
                </time>
              }
            </li>
          }
        </ol>
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
export class PortalTimelineComponent {
  readonly events = input.required<IPortalTimelineEvent[]>();
}
