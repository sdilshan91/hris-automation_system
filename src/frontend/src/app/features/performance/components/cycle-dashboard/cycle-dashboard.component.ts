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
import { RouterLink, ActivatedRoute, Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { forkJoin } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { CycleService } from '../../services/cycle.service';
import {
  CANCEL_REASON_MIN,
  CYCLE_STATUS_BADGE,
  CYCLE_STATUS_LABEL,
  CycleStatus,
  CycleTransitionAction,
  ICycle,
  ICycleDashboard,
  IPhaseStat,
  PHASE_LABEL,
  TRANSITION_LABEL,
  allowedTransitions,
  phaseCompletionPercent,
  progressFillClass,
} from '../../models/cycle.models';

/**
 * US-PRF-004 (AC-3 / AC-5 / FR-7 / FR-8): the cycle dashboard. Shows a vertical
 * phase-timeline stepper with per-phase completion stats + overdue counts (AC-3),
 * the status-transition toolbar gated to the legal actions for the current status
 * (Activate / Pause / Resume / Complete / Cancel-with-reason, FR-7), a clone action
 * (FR-8), and an "Edit cycle" link (AC-5).
 *
 * No chart library is a FE dependency, so completion is drawn with CSS/Tailwind
 * progress bars (not chart.js donuts — §8 nice-to-have). Mobile: the timeline is the
 * vertical stepper and the stats cards stack/swipe (overflow-x scroll).
 */
@Component({
  selector: 'app-cycle-dashboard',
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
    <div class="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
      <a
        routerLink="/performance/cycles"
        class="text-sm text-neutral-500 transition hover:text-neutral-800"
      >
        ← Back to cycles
      </a>

      @if (loading()) {
        <div class="mt-4 space-y-4">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="mt-4 rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (cycle(); as c) {
        <!-- Header + status -->
        <div
          class="mt-2 flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between"
        >
          <div>
            <div class="flex items-center gap-3">
              <h1
                class="text-2xl font-semibold tracking-tight text-neutral-900"
              >
                {{ c.name }}
              </h1>
              <span
                class="inline-flex rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                [class]="statusBadge(c.status)"
                data-testid="status-badge"
              >
                {{ statusLabel(c.status) }}
              </span>
            </div>
            <p class="mt-1 text-sm text-neutral-500">
              {{ c.startDate }} → {{ c.endDate }} ·
              {{ c.participantCount }} participants
            </p>
          </div>

          <!-- Status transition toolbar (FR-7) -->
          <div class="flex flex-wrap items-center gap-2" data-testid="actions">
            @if (canEdit()) {
              <a
                [routerLink]="['/performance/cycles', c.id, 'edit']"
                class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
                data-testid="edit-link"
              >
                Edit
              </a>
            }
            <button
              type="button"
              (click)="startClone()"
              class="rounded-lg border border-neutral-200 px-3 py-1.5 text-sm font-medium text-neutral-700 transition hover:bg-neutral-50"
              data-testid="clone-btn"
            >
              Clone
            </button>
            @for (action of transitions(); track action) {
              <button
                type="button"
                (click)="onTransition(action)"
                [disabled]="busy()"
                class="rounded-lg px-3 py-1.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 focus:outline-none focus:ring-2 focus:ring-offset-2 disabled:opacity-50"
                [class]="actionClasses(action)"
                [attr.data-testid]="'transition-' + action"
              >
                {{ transitionLabel(action) }}
              </button>
            }
          </div>
        </div>

        @if (c.status === 'Cancelled' && c.cancelledReason) {
          <p
            class="mt-3 rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700"
          >
            Cancelled: {{ c.cancelledReason }}
          </p>
        }

        <!-- Phase timeline + stats (AC-3) -->
        <h2 class="mt-8 text-sm font-semibold text-neutral-700">
          Phase progress
        </h2>
        @if (dashboard(); as d) {
          <ol class="mt-4 space-y-4" data-testid="timeline" @fadeIn>
            @for (stat of d.phases; track stat.phaseType; let last = $last) {
              <li class="relative flex gap-4" data-testid="phase-step">
                <!-- Stepper rail -->
                <div class="flex flex-col items-center">
                  <span
                    class="flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold text-white"
                    [class]="stepDotClass(stat)"
                  >
                    {{ completion(stat) }}%
                  </span>
                  @if (!last) {
                    <span class="mt-1 w-px flex-1 bg-neutral-200"></span>
                  }
                </div>

                <!-- Stat card -->
                <div
                  class="mb-2 flex-1 rounded-xl border border-neutral-200 bg-white p-4 shadow-sm"
                  data-testid="phase-card"
                >
                  <div
                    class="flex flex-wrap items-center justify-between gap-2"
                  >
                    <p class="font-medium text-neutral-900">
                      {{ phaseLabel(stat.phaseType) }}
                    </p>
                    <p class="text-xs text-neutral-500">
                      {{ stat.startDate }} → {{ stat.endDate }}
                    </p>
                  </div>
                  <div class="mt-3 h-2 overflow-hidden rounded-full bg-neutral-100">
                    <div
                      class="h-full rounded-full transition-all"
                      [class]="fillClass(stat)"
                      [style.width.%]="completion(stat)"
                      [attr.data-testid]="'fill-' + stat.phaseType"
                    ></div>
                  </div>
                  <div
                    class="mt-2 flex items-center justify-between text-xs text-neutral-600"
                  >
                    <span>
                      {{ stat.completedCount }} / {{ stat.totalParticipants }} completed
                    </span>
                    @if (stat.overdueCount > 0) {
                      <span
                        class="font-medium text-rose-600"
                        [attr.data-testid]="'overdue-' + stat.phaseType"
                      >
                        {{ stat.overdueCount }} overdue
                      </span>
                    }
                  </div>
                </div>
              </li>
            }
          </ol>
        }
      }
    </div>

    <!-- Cancel-with-reason modal (BR-6) -->
    @if (cancelOpen()) {
      <div
        class="fixed inset-0 z-40 flex items-center justify-center bg-neutral-900/40 px-4"
        role="dialog"
        aria-modal="true"
        aria-label="Cancel cycle"
      >
        <div
          class="w-full max-w-md rounded-xl bg-white p-5 shadow-xl"
          @fadeIn
        >
          <h3 class="text-lg font-semibold text-neutral-900">Cancel cycle</h3>
          <p class="mt-1 text-sm text-neutral-500">
            Cancelling notifies all participants and cannot be undone.
          </p>
          <label class="mt-4 block">
            <span class="mb-1 block text-sm font-medium text-neutral-700"
              >Reason</span
            >
            <textarea
              rows="3"
              [ngModel]="cancelReason()"
              (ngModelChange)="cancelReason.set($event)"
              class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none focus:border-neutral-400 focus:ring-1"
              data-testid="cancel-reason"
            ></textarea>
          </label>
          <div class="mt-4 flex justify-end gap-2">
            <button
              type="button"
              (click)="cancelOpen.set(false)"
              class="rounded-lg px-3 py-1.5 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
            >
              Keep cycle
            </button>
            <button
              type="button"
              (click)="confirmCancel()"
              [disabled]="!cancelReasonValid() || busy()"
              class="rounded-lg bg-rose-600 px-3 py-1.5 text-sm font-medium text-white transition hover:bg-rose-700 disabled:opacity-50"
              data-testid="confirm-cancel"
            >
              Cancel cycle
            </button>
          </div>
        </div>
      </div>
    }

    <!-- Clone modal (FR-8) -->
    @if (cloneOpen()) {
      <div
        class="fixed inset-0 z-40 flex items-center justify-center bg-neutral-900/40 px-4"
        role="dialog"
        aria-modal="true"
        aria-label="Clone cycle"
      >
        <div class="w-full max-w-md rounded-xl bg-white p-5 shadow-xl" @fadeIn>
          <h3 class="text-lg font-semibold text-neutral-900">Clone cycle</h3>
          <p class="mt-1 text-sm text-neutral-500">
            Copy this cycle's settings into a new Draft for the next period.
          </p>
          <label class="mt-4 block">
            <span class="mb-1 block text-sm font-medium text-neutral-700"
              >New cycle name</span
            >
            <input
              type="text"
              [ngModel]="cloneName()"
              (ngModelChange)="cloneName.set($event)"
              class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none focus:border-neutral-400 focus:ring-1"
              data-testid="clone-name"
            />
          </label>
          <div class="mt-3 grid grid-cols-2 gap-3">
            <label class="block">
              <span class="mb-1 block text-xs text-neutral-500">Start</span>
              <input
                type="date"
                [ngModel]="cloneStart()"
                (ngModelChange)="cloneStart.set($event)"
                class="w-full rounded-lg border border-neutral-200 px-2 py-1.5 text-sm outline-none focus:border-neutral-400 focus:ring-1"
                data-testid="clone-start"
              />
            </label>
            <label class="block">
              <span class="mb-1 block text-xs text-neutral-500">End</span>
              <input
                type="date"
                [ngModel]="cloneEnd()"
                (ngModelChange)="cloneEnd.set($event)"
                class="w-full rounded-lg border border-neutral-200 px-2 py-1.5 text-sm outline-none focus:border-neutral-400 focus:ring-1"
                data-testid="clone-end"
              />
            </label>
          </div>
          <div class="mt-4 flex justify-end gap-2">
            <button
              type="button"
              (click)="cloneOpen.set(false)"
              class="rounded-lg px-3 py-1.5 text-sm font-medium text-neutral-600 transition hover:bg-neutral-100"
            >
              Cancel
            </button>
            <button
              type="button"
              (click)="confirmClone()"
              [disabled]="!cloneValid() || busy()"
              class="rounded-lg px-3 py-1.5 text-sm font-medium text-white shadow-sm transition hover:opacity-90 disabled:opacity-50"
              [style.background-color]="'var(--brand-primary)'"
              data-testid="confirm-clone"
            >
              Create clone
            </button>
          </div>
        </div>
      </div>
    }
  `,
})
export class CycleDashboardComponent implements OnInit {
  private readonly service = inject(CycleService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly cycle = signal<ICycle | null>(null);
  readonly dashboard = signal<ICycleDashboard | null>(null);

  // Cancel modal state (BR-6).
  readonly cancelOpen = signal(false);
  readonly cancelReason = signal('');
  readonly cancelReasonValid = computed(
    () => this.cancelReason().trim().length >= CANCEL_REASON_MIN,
  );

  // Clone modal state (FR-8).
  readonly cloneOpen = signal(false);
  readonly cloneName = signal('');
  readonly cloneStart = signal('');
  readonly cloneEnd = signal('');
  readonly cloneValid = computed(
    () =>
      this.cloneName().trim().length > 0 &&
      !!this.cloneStart() &&
      !!this.cloneEnd() &&
      this.cloneStart() <= this.cloneEnd(),
  );

  /** Legal transitions for the current status (FR-7). */
  readonly transitions = computed<CycleTransitionAction[]>(() => {
    const c = this.cycle();
    return c ? allowedTransitions(c.status) : [];
  });

  /** A Draft cycle can still be edited (AC-5 edit). */
  readonly canEdit = computed(() => {
    const c = this.cycle();
    return !!c && (c.status === 'Draft' || c.status === 'Active' || c.status === 'Paused');
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    const id = this.route.snapshot.paramMap.get('cycleId');
    if (!id) {
      this.error.set('Cycle not found.');
      this.loading.set(false);
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      cycle: this.service.get(id),
      dashboard: this.service.dashboard(id),
    }).subscribe({
      next: ({ cycle, dashboard }) => {
        this.cycle.set(cycle);
        this.dashboard.set(dashboard);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load this cycle.');
        this.loading.set(false);
      },
    });
  }

  onTransition(action: CycleTransitionAction): void {
    if (action === 'Cancel') {
      this.cancelReason.set('');
      this.cancelOpen.set(true);
      return;
    }
    this.runTransition(action);
  }

  confirmCancel(): void {
    if (!this.cancelReasonValid()) {
      return;
    }
    this.runTransition('Cancel', this.cancelReason().trim());
  }

  private runTransition(action: CycleTransitionAction, reason?: string): void {
    const c = this.cycle();
    if (!c) {
      return;
    }
    this.busy.set(true);
    this.service.transition(c.id, { action, reason }).subscribe({
      next: (updated) => {
        this.cycle.set(updated);
        this.busy.set(false);
        this.cancelOpen.set(false);
        this.toastr.success(`Cycle ${action.toLowerCase()}d.`);
        this.load();
      },
      error: () => {
        this.busy.set(false);
        this.toastr.error(`Unable to ${action.toLowerCase()} the cycle.`);
      },
    });
  }

  startClone(): void {
    const c = this.cycle();
    this.cloneName.set(c ? `${c.name} (copy)` : '');
    this.cloneStart.set('');
    this.cloneEnd.set('');
    this.cloneOpen.set(true);
  }

  confirmClone(): void {
    const c = this.cycle();
    if (!c || !this.cloneValid()) {
      return;
    }
    this.busy.set(true);
    this.service
      .clone(c.id, {
        name: this.cloneName().trim(),
        startDate: this.cloneStart(),
        endDate: this.cloneEnd(),
      })
      .subscribe({
        next: (created) => {
          this.busy.set(false);
          this.cloneOpen.set(false);
          this.toastr.success('Cycle cloned.');
          this.router.navigate(['/performance/cycles', created.id]);
        },
        error: () => {
          this.busy.set(false);
          this.toastr.error('Unable to clone the cycle.');
        },
      });
  }

  completion(stat: IPhaseStat): number {
    return phaseCompletionPercent(stat);
  }

  fillClass(stat: IPhaseStat): string {
    return progressFillClass(this.completion(stat));
  }

  stepDotClass(stat: IPhaseStat): string {
    return this.completion(stat) >= 100
      ? 'bg-emerald-500'
      : this.completion(stat) > 0
        ? 'bg-sky-500'
        : 'bg-neutral-400';
  }

  actionClasses(action: CycleTransitionAction): string {
    if (action === 'Cancel') {
      return 'bg-rose-600 hover:bg-rose-700 focus:ring-rose-500';
    }
    if (action === 'Pause') {
      return 'bg-amber-500 hover:bg-amber-600 focus:ring-amber-400';
    }
    return 'bg-emerald-600 hover:bg-emerald-700 focus:ring-emerald-500';
  }

  statusBadge(status: CycleStatus): string {
    return CYCLE_STATUS_BADGE[status];
  }

  statusLabel(status: CycleStatus): string {
    return CYCLE_STATUS_LABEL[status];
  }

  phaseLabel = (phaseType: IPhaseStat['phaseType']): string =>
    PHASE_LABEL[phaseType];

  transitionLabel(action: CycleTransitionAction): string {
    return TRANSITION_LABEL[action];
  }
}
