import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { CareersBrandingComponent } from '../../careers/careers-branding/careers-branding.component';
import { PortalStepIndicatorComponent } from '../portal-step-indicator/portal-step-indicator.component';
import { PortalInterviewCardComponent } from '../portal-interview-card/portal-interview-card.component';
import { PortalOfferCardComponent } from '../portal-offer-card/portal-offer-card.component';
import { PortalTimelineComponent } from '../portal-timeline/portal-timeline.component';
import { PortalService } from '../../../services/portal.service';
import {
  IPortalApplication,
  IPortalDashboard,
} from '../../../models/portal.models';
import { OfferResponse } from '../../../models/offer.models';

/**
 * US-REC-008: Candidate portal dashboard (smart container).
 *
 * ANONYMOUS, magic-link route (NO auth/role guard — like the public careers pages).
 * The token is read from the `?token=` query param and forwarded to the backend on
 * every call (NFR-3/NFR-4). On an expired/invalid token the page shows a "request a
 * new link" prompt (FR-8/BR-5). Tenant-branded header + step indicator + interview,
 * offer, and timeline sections per application (AC-1/AC-2/AC-3/FR-6). Skeletons
 * while loading; empty state with a careers link. Mobile-first to 360px, OnPush.
 */
@Component({
  selector: 'app-candidate-portal',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    FormsModule,
    CareersBrandingComponent,
    PortalStepIndicatorComponent,
    PortalInterviewCardComponent,
    PortalOfferCardComponent,
    PortalTimelineComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-screen bg-neutral-50">
      <app-careers-branding />

      <main class="mx-auto max-w-3xl px-4 py-8 sm:px-6 lg:py-12">
        @if (loading()) {
          <div class="space-y-4">
            <div class="h-8 w-48 animate-pulse rounded bg-neutral-100"></div>
            @for (n of [1, 2]; track n) {
              <div class="h-52 animate-pulse rounded-xl bg-neutral-100"></div>
            }
          </div>
        } @else if (tokenError()) {
          <!-- FR-8 / BR-5: expired or invalid magic link -->
          <div class="rounded-xl border border-neutral-200 bg-white p-6 text-center sm:p-8">
            <h1 class="text-lg font-semibold text-neutral-900">Your link has expired</h1>
            <p class="mx-auto mt-2 max-w-sm text-sm text-neutral-500">
              Magic links expire for your security. Enter the email you applied with
              and we'll send you a fresh link.
            </p>

            @if (linkSent()) {
              <div
                class="mx-auto mt-5 max-w-sm rounded-lg bg-green-50 px-4 py-3 text-sm text-green-700"
                role="status"
              >
                If that email matches an application, a new link is on its way.
                Please check your inbox.
              </div>
            } @else {
              <form
                class="mx-auto mt-5 flex max-w-sm flex-col gap-3 sm:flex-row"
                (ngSubmit)="requestLink()"
              >
                <label for="relink-email" class="sr-only">Email address</label>
                <input
                  id="relink-email"
                  type="email"
                  name="email"
                  required
                  autocomplete="email"
                  class="ctrl flex-1"
                  placeholder="you@example.com"
                  [ngModel]="email()"
                  (ngModelChange)="email.set($event)"
                />
                <button
                  type="submit"
                  class="rounded-lg bg-blue-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-blue-700 disabled:opacity-50"
                  [disabled]="requesting() || !email().trim()"
                >
                  {{ requesting() ? 'Sending…' : 'Send new link' }}
                </button>
              </form>
            }
          </div>
        } @else if (error()) {
          <div class="rounded-xl border border-red-200 bg-red-50 py-10 text-center">
            <p class="text-sm font-medium text-red-700">{{ error() }}</p>
            <button
              type="button"
              class="mt-3 text-sm font-medium text-blue-600 hover:text-blue-700"
              (click)="load()"
            >
              Try again
            </button>
          </div>
        } @else {
          <div class="mb-6">
            <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
              @if (dashboard()?.applicantName) {
                Welcome back, {{ dashboard()!.applicantName }}
              } @else {
                Your applications
              }
            </h1>
            <p class="mt-1 text-sm text-neutral-500">
              Track your progress and respond to offers.
            </p>
          </div>

          @if (applications().length === 0) {
            <div
              class="rounded-xl border border-dashed border-neutral-200 bg-white py-16 text-center"
            >
              <p class="text-sm font-medium text-neutral-700">No applications found</p>
              <p class="mt-1 text-sm text-neutral-500">
                Browse open roles and apply on our
                <a
                  routerLink="/careers"
                  class="font-medium text-blue-600 hover:text-blue-700"
                  >careers page</a
                >.
              </p>
            </div>
          } @else {
            <div class="space-y-5">
              @for (app of applications(); track app.id) {
                <article
                  class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm sm:p-6"
                >
                  <header class="mb-5">
                    <h2 class="text-lg font-semibold text-neutral-900">
                      {{ app.vacancyTitle }}
                    </h2>
                    <p class="mt-0.5 text-sm text-neutral-500">
                      {{ app.departmentName || 'General' }}
                      <span class="text-neutral-300">·</span>
                      Applied {{ app.appliedAt | date: 'mediumDate' }}
                    </p>
                    <p class="text-xs text-neutral-400">
                      {{ app.applicationReferenceNumber }}
                    </p>
                  </header>

                  <app-portal-step-indicator [stage]="app.stage" />

                  @if (app.interview) {
                    <div class="mt-5">
                      <app-portal-interview-card [interview]="app.interview" />
                    </div>
                  }

                  @if (app.offer) {
                    <div class="mt-5">
                      <app-portal-offer-card
                        [offer]="app.offer"
                        [responding]="respondingId() === app.offer.id"
                        [downloading]="downloadingId() === app.offer.id"
                        (respond)="onRespond(app, $event)"
                        (download)="onDownload(app)"
                      />
                    </div>
                  }

                  @if (app.timeline.length > 0) {
                    <div class="mt-6 border-t border-neutral-100 pt-5">
                      <app-portal-timeline [events]="app.timeline" />
                    </div>
                  }
                </article>
              }
            </div>
          }
        }
      </main>
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .ctrl {
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.5rem 0.75rem;
        font-size: 0.875rem;
        color: #171717;
        transition: all 150ms ease;
      }
      .ctrl:focus {
        outline: none;
        border-color: #60a5fa;
        box-shadow: 0 0 0 3px #dbeafe;
      }
      .sr-only {
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        white-space: nowrap;
        border: 0;
      }
    `,
  ],
})
export class CandidatePortalComponent implements OnInit {
  private readonly portal = inject(PortalService);
  private readonly route = inject(ActivatedRoute);
  private readonly toastr = inject(ToastrService);

  private readonly token = signal('');

  readonly dashboard = signal<IPortalDashboard | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly tokenError = signal(false);

  // Re-request link (FR-8 / BR-5)
  readonly email = signal('');
  readonly requesting = signal(false);
  readonly linkSent = signal(false);

  // Per-offer action state
  readonly respondingId = signal<string | null>(null);
  readonly downloadingId = signal<string | null>(null);

  readonly applications = computed<IPortalApplication[]>(
    () => this.dashboard()?.applications ?? [],
  );

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token') ?? '';
    this.token.set(token);
    if (!token) {
      // No token at all behaves like an expired link → request-a-new-link prompt.
      this.loading.set(false);
      this.tokenError.set(true);
      return;
    }
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.tokenError.set(false);
    this.portal.getDashboard(this.token()).subscribe({
      next: (d) => {
        this.dashboard.set(d);
        if (d.applicantEmail) {
          this.email.set(d.applicantEmail);
        }
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        if (PortalService.isTokenError(err)) {
          this.tokenError.set(true);
        } else {
          this.error.set(PortalService.parseErrorMessage(err));
        }
      },
    });
  }

  onRespond(app: IPortalApplication, response: OfferResponse): void {
    const offer = app.offer;
    if (!offer) {
      return;
    }
    this.respondingId.set(offer.id);
    this.portal.respondToOffer(offer.id, this.token(), response).subscribe({
      next: () => {
        this.respondingId.set(null);
        this.toastr.success(
          response === 'Accepted'
            ? 'Offer accepted. Congratulations!'
            : 'Offer declined.',
        );
        // Reload so the stage advances to Hired (BR-3) and the offer reflects.
        this.load();
      },
      error: (err: HttpErrorResponse) => {
        this.respondingId.set(null);
        if (PortalService.isTokenError(err)) {
          this.tokenError.set(true);
        } else {
          this.toastr.error(PortalService.parseErrorMessage(err));
        }
      },
    });
  }

  onDownload(app: IPortalApplication): void {
    const offer = app.offer;
    if (!offer) {
      return;
    }
    this.downloadingId.set(offer.id);
    this.portal.downloadOfferDocument(offer.id, this.token()).subscribe({
      next: (res) => {
        this.downloadingId.set(null);
        const blob = res.body;
        if (!blob) {
          return;
        }
        const filename = PortalService.filenameFromResponse(
          res,
          `offer-${offer.offerReferenceNumber || offer.id}.pdf`,
        );
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err: HttpErrorResponse) => {
        this.downloadingId.set(null);
        this.toastr.error(PortalService.parseErrorMessage(err));
      },
    });
  }

  requestLink(): void {
    const email = this.email().trim();
    if (!email || this.requesting()) {
      return;
    }
    this.requesting.set(true);
    this.portal.requestLink(email).subscribe({
      next: () => {
        this.requesting.set(false);
        this.linkSent.set(true);
      },
      error: () => {
        // To avoid leaking which emails exist (NFR-6), show the same confirmation.
        this.requesting.set(false);
        this.linkSent.set(true);
      },
    });
  }
}
