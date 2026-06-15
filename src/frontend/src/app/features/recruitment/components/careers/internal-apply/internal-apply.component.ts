import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { AuthService } from '../../../../../core/auth/auth.service';
import {
  ApplicationFormComponent,
  IApplicationPrefill,
} from '../application-form/application-form.component';
import { IApplicant } from '../../../models/applicant.models';

/**
 * US-REC-002: Internal-application slide-over for authenticated employees
 * (AC-4 / FR-8).
 *
 * A right-side drawer (full-screen on mobile) that hosts the shared application
 * form in `internal` mode with the employee's name/email pre-filled from the auth
 * signals. The backend links the application to the employee record and flags it
 * `internal` (BR-5). On success it swaps to an inline confirmation with the
 * reference number (AC-1) and lets the user close.
 *
 * Mirrors the right-drawer pattern used elsewhere (fixed wrap justify-end,
 * pointer-events split, translateX animation, separate backdrop).
 */
@Component({
  selector: 'app-internal-apply',
  standalone: true,
  imports: [CommonModule, ApplicationFormComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('drawer', [
      transition(':enter', [
        style({ transform: 'translateX(100%)' }),
        animate('260ms cubic-bezier(0.22, 1, 0.36, 1)', style({ transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('backdrop', [
      transition(':enter', [style({ opacity: 0 }), animate('200ms ease-out', style({ opacity: 1 }))]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  template: `
    <!-- Backdrop -->
    <div
      @backdrop
      class="fixed inset-0 z-40 bg-neutral-900/30"
      (click)="close()"
      aria-hidden="true"
    ></div>

    <!-- Drawer wrap -->
    <div class="pointer-events-none fixed inset-0 z-50 flex justify-end">
      <div
        @drawer
        class="pointer-events-auto flex h-full w-full max-w-lg flex-col bg-white shadow-xl"
        role="dialog"
        aria-modal="true"
        aria-labelledby="internal-apply-title"
      >
        <!-- Header -->
        <div class="flex items-center justify-between border-b border-neutral-100 px-6 py-4">
          <div>
            <h2 id="internal-apply-title" class="text-base font-semibold text-neutral-900">
              Apply internally
            </h2>
            <p class="mt-0.5 text-sm text-neutral-500">{{ vacancyTitle() }}</p>
          </div>
          <button
            type="button"
            class="rounded-md p-1.5 text-neutral-400 transition hover:bg-neutral-100 hover:text-neutral-700"
            (click)="close()"
            aria-label="Close"
          >
            <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
              <path d="M18 6 6 18M6 6l12 12" />
            </svg>
          </button>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-6 py-5">
          @if (confirmation(); as appn) {
            <div class="py-8 text-center">
              <div class="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-green-100">
                <svg class="h-6 w-6 text-green-600" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
                  <path d="m5 13 4 4L19 7" />
                </svg>
              </div>
              <h3 class="mt-4 text-lg font-semibold text-neutral-900">Application submitted</h3>
              <div class="mt-3 inline-flex flex-col items-center rounded-lg bg-neutral-50 px-4 py-2">
                <span class="text-xs uppercase tracking-wide text-neutral-400">Reference</span>
                <span class="font-mono text-sm font-semibold text-neutral-800">
                  {{ appn.applicationReferenceNumber }}
                </span>
              </div>
              <p class="mt-3 text-sm text-neutral-500">
                Your application is linked to your employee profile.
              </p>
              <button
                type="button"
                class="mt-5 rounded-lg bg-indigo-600 px-5 py-2 text-sm font-medium text-white transition hover:bg-indigo-700"
                (click)="close()"
              >
                Done
              </button>
            </div>
          } @else {
            <p class="mb-5 text-sm text-neutral-500">
              We've pre-filled your details from your profile. Review and submit.
            </p>
            <app-application-form
              [vacancyId]="vacancyId()"
              [internal]="true"
              [prefill]="prefill()"
              [showCancel]="true"
              submitLabel="Submit application"
              (submitted)="onSubmitted($event)"
              (cancelled)="close()"
            />
          }
        </div>
      </div>
    </div>
  `,
  styles: [
    `
      :host {
        display: contents;
      }
    `,
  ],
})
export class InternalApplyComponent {
  private readonly auth = inject(AuthService);

  readonly vacancyId = input.required<string>();
  readonly vacancyTitle = input('');

  readonly closed = output<void>();
  /** Emits when the internal application succeeds (host may refresh). */
  readonly submitted = output<IApplicant>();

  readonly confirmation = signal<IApplicant | null>(null);

  /** Pre-fill from the authenticated user's profile (AC-4 / FR-8). */
  readonly prefill = computed<IApplicationPrefill>(() => {
    const user = this.auth.currentUser();
    const display = (user?.displayName ?? '').trim();
    const parts = display.split(/\s+/).filter(Boolean);
    const firstName = parts.length ? parts[0] : '';
    const lastName = parts.length > 1 ? parts.slice(1).join(' ') : '';
    return {
      firstName,
      lastName,
      email: user?.email ?? '',
    };
  });

  onSubmitted(applicant: IApplicant): void {
    this.confirmation.set(applicant);
    this.submitted.emit(applicant);
  }

  close(): void {
    this.closed.emit();
  }
}
