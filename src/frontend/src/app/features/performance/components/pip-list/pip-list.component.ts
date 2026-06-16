import {
  Component,
  ChangeDetectionStrategy,
  OnInit,
  computed,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { PipService } from '../../services/pip.service';
import {
  IPipSummary,
  PIP_CONFIDENTIAL_NOTICE,
  PIP_STATUS_BADGE,
  PIP_STATUS_LABEL,
  PipStatus,
} from '../../models/pip.models';

/**
 * US-PRF-008 AC-1: the HR PIP list — the Performance > PIP entry point. A clean card
 * grid of Performance Improvement Plans with color-coded status badges and a
 * "Create PIP" CTA. Each card links into the PIP detail/timeline. A subtle
 * confidentiality banner (§8) reminds users PIP data is sensitive. HR/manager-gated
 * by the parent /performance route guard; the backend restricts visibility (FR-8).
 * Notion-like, responsive from 360px.
 */
@Component({
  selector: 'app-pip-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '220ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- §8 confidentiality banner -->
      <div
        class="mb-5 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 py-3 text-xs text-amber-800"
        role="note"
        data-testid="confidential-banner"
      >
        <span aria-hidden="true">🔒</span>
        <span>{{ confidentialNotice }}</span>
      </div>

      <!-- Header -->
      <div
        class="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
      >
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Performance Improvement Plans
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Structured, time-bound support for employees who need improvement.
          </p>
        </div>
        <a
          routerLink="/performance/pips/new"
          class="inline-flex items-center justify-center rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-offset-2"
          [style.background-color]="'var(--brand-primary)'"
          [style.--tw-ring-color]="'var(--brand-primary)'"
          data-testid="create-pip"
        >
          Create PIP
        </a>
      </div>

      @if (loading()) {
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-36 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (pips().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-12 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">No PIPs yet</p>
          <p class="mt-1 text-sm text-neutral-500">
            Create a PIP to provide structured improvement support.
          </p>
        </div>
      } @else {
        <!-- Status filter -->
        <div class="mb-4">
          <label class="block w-full sm:w-auto">
            <span class="sr-only">Filter by status</span>
            <select
              [ngModel]="statusFilter()"
              (ngModelChange)="statusFilter.set($event)"
              class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400 focus:ring-1 sm:w-auto"
              data-testid="status-filter"
            >
              <option value="all">All statuses</option>
              @for (s of statusKeys; track s) {
                <option [value]="s">{{ statusLabel(s) }}</option>
              }
            </select>
          </label>
        </div>

        @if (visiblePips().length === 0) {
          <div
            class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-10 text-center text-sm text-neutral-500"
          >
            No PIPs match this status.
          </div>
        } @else {
          <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" @fadeIn>
            @for (p of visiblePips(); track p.pipId) {
              <a
                [routerLink]="['/performance/pips', p.pipId]"
                class="group block rounded-xl border border-neutral-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus:ring-2 focus:ring-offset-2"
                [style.--tw-ring-color]="'var(--brand-primary)'"
                data-testid="pip-card"
              >
                <div class="flex items-start justify-between gap-3">
                  <div>
                    <h2
                      class="font-medium text-neutral-900 group-hover:text-neutral-700"
                    >
                      {{ p.employeeName }}
                    </h2>
                    @if (p.jobTitle) {
                      <p class="text-xs text-neutral-500">{{ p.jobTitle }}</p>
                    }
                  </div>
                  <span
                    class="inline-flex shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                    [class]="statusBadge(p.status)"
                    data-testid="status-badge"
                  >
                    {{ statusLabel(p.status) }}
                  </span>
                </div>
                <dl class="mt-4 space-y-1 text-sm text-neutral-600">
                  <div class="flex items-center justify-between">
                    <dt class="text-neutral-400">Period</dt>
                    <dd>{{ p.startDate }} → {{ p.endDate }}</dd>
                  </div>
                  <div class="flex items-center justify-between">
                    <dt class="text-neutral-400">Objectives</dt>
                    <dd>{{ p.objectiveCount }}</dd>
                  </div>
                  <div class="flex items-center justify-between">
                    <dt class="text-neutral-400">Checkpoints</dt>
                    <dd>
                      {{ p.checkpointsRecorded }} of {{ p.checkpointsTotal }}
                    </dd>
                  </div>
                </dl>
                @if (p.acknowledgement === 'NotAcknowledged') {
                  <p class="mt-3 text-xs font-medium text-rose-600">
                    Not acknowledged
                  </p>
                } @else if (p.acknowledgement === 'Pending') {
                  <p class="mt-3 text-xs font-medium text-amber-600">
                    Awaiting acknowledgement
                  </p>
                }
              </a>
            }
          </div>
        }
      }
    </div>
  `,
})
export class PipListComponent implements OnInit {
  private readonly service = inject(PipService);

  readonly confidentialNotice = PIP_CONFIDENTIAL_NOTICE;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly pips = signal<IPipSummary[]>([]);
  readonly statusFilter = signal<PipStatus | 'all'>('all');

  readonly statusKeys: PipStatus[] = [
    'Draft',
    'Active',
    'Extended',
    'SuccessfullyCompleted',
    'NotMet',
    'Cancelled',
  ];

  readonly visiblePips = computed(() => {
    const status = this.statusFilter();
    if (status === 'all') {
      return this.pips();
    }
    return this.pips().filter((p) => p.status === status);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.listPips().subscribe({
      next: (pips) => {
        this.pips.set(pips);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load Performance Improvement Plans.');
        this.loading.set(false);
      },
    });
  }

  statusBadge(status: PipStatus): string {
    return PIP_STATUS_BADGE[status];
  }

  statusLabel(status: PipStatus): string {
    return PIP_STATUS_LABEL[status];
  }
}
