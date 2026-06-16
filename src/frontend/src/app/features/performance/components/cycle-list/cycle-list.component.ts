import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { CycleService } from '../../services/cycle.service';
import {
  CYCLE_STATUS_BADGE,
  CYCLE_STATUS_LABEL,
  CYCLE_STATUS_OPTIONS,
  CYCLE_TYPE_OPTIONS,
  CycleStatus,
  CycleType,
  ICycleSummary,
} from '../../models/cycle.models';

/**
 * US-PRF-004 (FR-7 / §8): Cycles list — the default HR Cycle Management screen.
 * A clean card grid of appraisal cycles with color-coded status badges
 * (Draft gray / Active green / Paused amber / Completed blue / Cancelled red),
 * a status filter, and a "Create New Cycle" CTA (AC-1). Each card links into the
 * cycle dashboard. Notion-like, responsive from 360px.
 */
@Component({
  selector: 'app-cycle-list',
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
      <!-- Header -->
      <div
        class="mb-6 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
      >
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Appraisal cycles
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Create and manage time-bound performance review cycles.
          </p>
        </div>
        <a
          routerLink="/performance/cycles/new"
          class="inline-flex items-center justify-center rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-offset-2"
          [style.background-color]="'var(--brand-primary)'"
          [style.--tw-ring-color]="'var(--brand-primary)'"
          data-testid="create-cycle"
        >
          Create New Cycle
        </a>
      </div>

      @if (loading()) {
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          @for (i of [1, 2, 3, 4, 5, 6]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
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
      } @else if (cycles().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-12 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">No cycles yet</p>
          <p class="mt-1 text-sm text-neutral-500">
            Create your first appraisal cycle to kick off a review period.
          </p>
        </div>
      } @else {
        <!-- Status filter (FR-7) -->
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

        @if (visibleCycles().length === 0) {
          <div
            class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-10 text-center text-sm text-neutral-500"
          >
            No cycles match this status.
          </div>
        } @else {
          <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" @fadeIn>
            @for (c of visibleCycles(); track c.id) {
              <a
                [routerLink]="['/performance/cycles', c.id]"
                class="group block rounded-xl border border-neutral-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus:ring-2 focus:ring-offset-2"
                [style.--tw-ring-color]="'var(--brand-primary)'"
                data-testid="cycle-card"
              >
                <div class="flex items-start justify-between gap-3">
                  <h2
                    class="font-medium text-neutral-900 group-hover:text-neutral-700"
                    [title]="c.name"
                  >
                    {{ c.name }}
                  </h2>
                  <span
                    class="inline-flex shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                    [class]="statusBadge(c.status)"
                    data-testid="status-badge"
                  >
                    {{ statusLabel(c.status) }}
                  </span>
                </div>
                <p class="mt-1 text-xs font-medium text-neutral-500">
                  {{ typeLabel(c.type) }}
                </p>
                <dl class="mt-4 space-y-1 text-sm text-neutral-600">
                  <div class="flex items-center justify-between">
                    <dt class="text-neutral-400">Period</dt>
                    <dd>{{ c.startDate }} → {{ c.endDate }}</dd>
                  </div>
                  <div class="flex items-center justify-between">
                    <dt class="text-neutral-400">Participants</dt>
                    <dd>{{ c.participantCount }}</dd>
                  </div>
                </dl>
              </a>
            }
          </div>
        }
      }
    </div>
  `,
})
export class CycleListComponent implements OnInit {
  private readonly service = inject(CycleService);

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly cycles = signal<ICycleSummary[]>([]);
  readonly statusFilter = signal<CycleStatus | 'all'>('all');

  readonly statusKeys: CycleStatus[] = CYCLE_STATUS_OPTIONS;

  /** Cycles filtered by the status selector (FR-7). */
  readonly visibleCycles = computed(() => {
    const status = this.statusFilter();
    if (status === 'all') {
      return this.cycles();
    }
    return this.cycles().filter((c) => c.status === status);
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.service.list().subscribe({
      next: (cycles) => {
        this.cycles.set(cycles);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load appraisal cycles.');
        this.loading.set(false);
      },
    });
  }

  statusBadge(status: CycleStatus): string {
    return CYCLE_STATUS_BADGE[status];
  }

  statusLabel(status: CycleStatus): string {
    return CYCLE_STATUS_LABEL[status];
  }

  typeLabel(type: CycleType): string {
    return (
      CYCLE_TYPE_OPTIONS.find((o) => o.value === type)?.label ?? type
    );
  }
}
