import {
  Component,
  ChangeDetectionStrategy,
  input,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApplicantStage } from '../../../models/applicant.models';
import {
  IPortalStep,
  buildSteps,
  isRejected,
  stepCircleClass,
  stepConnectorClass,
} from '../../../models/portal.models';

/**
 * US-REC-008: Horizontal pipeline step indicator (AC-1 / §8).
 *
 * Applied → Screening → Interview → Offer → Hired. Completed steps are green, the
 * current step blue, future steps gray. A `Rejected` application renders a distinct
 * "no longer active" banner instead of the linear bar (BR-3 sanitized view).
 *
 * Presentational/dumb — driven entirely by the `stage` input. Mobile (<sm) the bar
 * scrolls horizontally with large touch targets (§8 / NFR-2).
 */
@Component({
  selector: 'app-portal-step-indicator',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rejected()) {
      <div
        class="rounded-lg bg-neutral-100 px-4 py-3 text-sm text-neutral-600"
        role="status"
      >
        This application is no longer active.
      </div>
    } @else {
      <ol
        class="flex items-center overflow-x-auto pb-1"
        [attr.aria-label]="'Application progress'"
      >
        @for (step of steps(); track step.stage; let i = $index; let first = $first) {
          <li class="flex min-w-0 flex-1 items-center" [class.flex-none]="first">
            @if (!first) {
              <span
                class="mx-1 h-0.5 flex-1 rounded sm:mx-2"
                [ngClass]="connectorClass(step)"
                aria-hidden="true"
              ></span>
            }
            <div class="flex flex-col items-center gap-1.5">
              <span
                class="flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold transition"
                [ngClass]="circleClass(step)"
                [attr.aria-current]="step.state === 'current' ? 'step' : null"
              >
                @if (step.state === 'completed') {
                  <svg
                    class="h-4 w-4"
                    viewBox="0 0 20 20"
                    fill="currentColor"
                    aria-hidden="true"
                  >
                    <path
                      fill-rule="evenodd"
                      d="M16.7 5.3a1 1 0 0 1 0 1.4l-7.5 7.5a1 1 0 0 1-1.4 0L3.3 9.7a1 1 0 1 1 1.4-1.4l3.3 3.3 6.8-6.8a1 1 0 0 1 1.4 0Z"
                      clip-rule="evenodd"
                    />
                  </svg>
                } @else {
                  {{ i + 1 }}
                }
              </span>
              <span
                class="whitespace-nowrap text-[11px] font-medium sm:text-xs"
                [class.text-neutral-900]="step.state !== 'future'"
                [class.text-neutral-400]="step.state === 'future'"
              >
                {{ step.label }}
              </span>
            </div>
          </li>
        }
      </ol>
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
export class PortalStepIndicatorComponent {
  readonly stage = input.required<ApplicantStage>();

  readonly rejected = computed(() => isRejected(this.stage()));
  readonly steps = computed<IPortalStep[]>(() => buildSteps(this.stage()));

  circleClass(step: IPortalStep): string {
    return stepCircleClass(step.state);
  }

  connectorClass(step: IPortalStep): string {
    return stepConnectorClass(step.state);
  }
}
