import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { PipService } from '../../services/pip.service';
import {
  CHECKPOINT_STATUS_BADGE,
  CHECKPOINT_STATUS_LABEL,
  CheckpointStatus,
  IPip,
  PIP_ACKNOWLEDGE_MESSAGE,
  PIP_CONFIDENTIAL_NOTICE,
  PIP_STATUS_BADGE,
  PIP_STATUS_LABEL,
} from '../../models/pip.models';

/**
 * US-PRF-008 EMPLOYEE side (BR-4 / FR-8). Reached from the employee self-service area
 * at `/my-review/pip/:pipId`. The employee reviews their own PIP (read-only — reason,
 * objectives + checkpoints, timeline dates) and, while acknowledgement is Pending,
 * acknowledges PIP initiation via a touch-friendly confirmation modal (reusing the
 * US-PRF-006 sign-off acknowledge UX). The backend enforces PIP ownership + RLS so an
 * employee only ever sees their OWN PIP. PIP is NOT surfaced in the general dashboard.
 */
@Component({
  selector: 'app-my-pip',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-3xl px-4 pb-28 pt-6 sm:px-6 lg:px-8">
      <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
        My improvement plan
      </h1>

      <!-- §8 confidentiality banner -->
      <div
        class="mt-4 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-800"
        role="note"
        data-testid="confidential-banner"
      >
        <span aria-hidden="true">🔒</span>
        <span>{{ confidentialNotice }}</span>
      </div>

      @if (loading()) {
        <div class="mt-6 space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-28 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (loadError()) {
        <div
          class="mt-6 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ loadError() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (pip(); as p) {
        <div class="mt-5 flex items-center justify-between">
          <p class="text-sm text-neutral-500">
            {{ p.startDate }} → {{ p.endDate }}
          </p>
          <span
            class="rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
            [class]="statusBadge(p.status)"
            data-testid="pip-status"
          >
            {{ statusLabel(p.status) }}
          </span>
        </div>

        <section
          class="mt-5 rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
        >
          <h2 class="text-sm font-semibold text-neutral-800">Why this plan</h2>
          <p class="mt-1 whitespace-pre-line text-sm text-neutral-600">
            {{ p.reason }}
          </p>
          @if (p.mentorName) {
            <p class="mt-3 text-xs text-neutral-400">
              Your assigned mentor: {{ p.mentorName }}
            </p>
          }
        </section>

        <section class="mt-5 space-y-3">
          <h2 class="text-sm font-semibold text-neutral-800">Objectives</h2>
          @for (obj of p.objectives; track obj.objectiveId) {
            <div
              class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm"
              data-testid="objective"
            >
              <p class="font-medium text-neutral-900">{{ obj.title }}</p>
              <p class="mt-1 text-sm text-neutral-600">{{ obj.description }}</p>
              <p class="mt-2 text-xs font-medium text-neutral-400">
                Success criteria
              </p>
              <p class="text-sm text-neutral-600">{{ obj.successCriteria }}</p>
              <p class="mt-2 text-xs text-neutral-400">Due {{ obj.dueDate }}</p>

              @for (cp of obj.checkpoints; track cp.checkpointId) {
                <div
                  class="mt-3 flex items-center justify-between rounded-lg border border-neutral-100 px-3 py-2"
                >
                  <span class="text-xs text-neutral-500">
                    Due {{ cp.dueDate }}
                  </span>
                  @if (cp.status) {
                    <span
                      class="rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                      [class]="checkpointBadge(cp.status)"
                    >
                      {{ checkpointLabel(cp.status) }}
                    </span>
                  } @else {
                    <span class="text-xs text-neutral-400">Scheduled</span>
                  }
                </div>
              }
            </div>
          }
        </section>

        <!-- BR-4 acknowledgement state -->
        @if (p.acknowledgement === 'Acknowledged') {
          <div
            class="mt-6 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-700"
            data-testid="acknowledged-banner"
          >
            You acknowledged this plan
            @if (p.acknowledgedSignature) {
              on {{ p.acknowledgedSignature.signedOn }}.
            }
          </div>
        } @else if (p.acknowledgement === 'NotAcknowledged') {
          <div
            class="mt-6 rounded-lg border border-neutral-200 bg-neutral-50 px-4 py-3 text-sm text-neutral-600"
          >
            This plan proceeded without your acknowledgement.
          </div>
        }
      }
    </div>

    <!-- BR-4 acknowledge action bar (only while Pending) -->
    @if (canAcknowledge()) {
      <div
        class="fixed inset-x-0 bottom-0 z-10 border-t border-neutral-200 bg-white/95 p-4 backdrop-blur"
      >
        <div class="mx-auto flex max-w-3xl justify-end">
          <button
            type="button"
            class="w-full rounded-lg px-5 py-3 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50 sm:w-auto"
            [style.background-color]="'var(--brand-primary)'"
            [disabled]="acknowledging()"
            (click)="openConfirm()"
            data-testid="acknowledge-btn"
          >
            Acknowledge plan
          </button>
        </div>
      </div>
    }

    <!-- BR-4 confirmation modal (touch-friendly, NFR-5) -->
    @if (confirmOpen()) {
      <div
        class="fixed inset-0 z-50 flex items-end justify-center bg-black/40 p-0 sm:items-center sm:p-4"
        role="dialog"
        aria-modal="true"
        aria-labelledby="ack-title"
        data-testid="confirm-modal"
      >
        <div
          class="w-full rounded-t-2xl bg-white p-6 shadow-xl sm:max-w-md sm:rounded-2xl"
        >
          <h2 id="ack-title" class="text-lg font-semibold text-neutral-900">
            Acknowledge plan
          </h2>
          <p class="mt-2 text-sm text-neutral-600">{{ acknowledgeMessage }}</p>
          <div class="mt-6 flex flex-col gap-2 sm:flex-row sm:justify-end">
            <button
              type="button"
              class="rounded-lg px-4 py-3 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
              (click)="closeConfirm()"
              data-testid="confirm-cancel"
            >
              Cancel
            </button>
            <button
              type="button"
              class="rounded-lg px-5 py-3 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              [disabled]="acknowledging()"
              (click)="acknowledge()"
              data-testid="confirm-acknowledge"
            >
              @if (acknowledging()) {
                Acknowledging…
              } @else {
                Acknowledge
              }
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class MyPipComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly service = inject(PipService);
  private readonly toastr = inject(ToastrService);

  readonly confidentialNotice = PIP_CONFIDENTIAL_NOTICE;
  readonly acknowledgeMessage = PIP_ACKNOWLEDGE_MESSAGE;

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly pip = signal<IPip | null>(null);
  readonly confirmOpen = signal(false);
  readonly acknowledging = signal(false);

  private pipId = '';

  readonly canAcknowledge = computed(() => {
    const p = this.pip();
    return !!p && p.acknowledgement === 'Pending';
  });

  ngOnInit(): void {
    this.pipId = this.route.snapshot.paramMap.get('pipId') ?? '';
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service.getPip(this.pipId).subscribe({
      next: (pip) => {
        this.pip.set(pip);
        this.loading.set(false);
      },
      error: () => {
        this.loadError.set('Unable to load your improvement plan.');
        this.loading.set(false);
      },
    });
  }

  openConfirm(): void {
    if (!this.canAcknowledge() || this.acknowledging()) {
      return;
    }
    this.confirmOpen.set(true);
  }

  closeConfirm(): void {
    this.confirmOpen.set(false);
  }

  acknowledge(): void {
    if (!this.canAcknowledge() || this.acknowledging()) {
      return;
    }
    this.acknowledging.set(true);
    this.service.acknowledge(this.pipId).subscribe({
      next: (pip) => {
        this.acknowledging.set(false);
        this.confirmOpen.set(false);
        this.pip.set(pip);
        this.toastr.success('Thank you — your acknowledgement has been recorded.');
      },
      error: () => {
        this.acknowledging.set(false);
        this.toastr.error('Could not record your acknowledgement. Please try again.');
      },
    });
  }

  statusBadge = (status: IPip['status']): string => PIP_STATUS_BADGE[status];
  statusLabel = (status: IPip['status']): string => PIP_STATUS_LABEL[status];
  checkpointBadge = (status: CheckpointStatus): string =>
    CHECKPOINT_STATUS_BADGE[status];
  checkpointLabel = (status: CheckpointStatus): string =>
    CHECKPOINT_STATUS_LABEL[status];
}
