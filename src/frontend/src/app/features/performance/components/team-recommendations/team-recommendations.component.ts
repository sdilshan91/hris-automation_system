import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { RecommendationService } from '../../services/recommendation.service';
import {
  ICompletedCycleOption,
  IRecommendationRow,
  RECOMMENDATION_STATUS_BADGE,
  RECOMMENDATION_STATUS_LABEL,
  RECOMMENDATION_TYPE_LABEL,
  RecommendationStatus,
  RecommendationType,
  formatTenure,
} from '../../models/recommendation.models';

/**
 * US-PRF-010 AC-5: the MANAGER "Team Recommendations" view. Shows ONLY the manager's
 * direct reports with their final score and recommendation STATUS — read-only, no
 * compensation editing, no other teams. Deliberately DISTINCT from the HR workspace
 * (RecommendationWorkspaceComponent): the manager cannot auto-generate, override, or
 * submit; the backend hard-restricts the `/team` endpoint to direct reports
 * (Performance.Read.Team + RLS). A clean card grid (Notion-like), OnPush + signals.
 */
@Component({
  selector: 'app-team-recommendations',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-5xl px-4 py-6 sm:px-6 lg:px-8">
      <div class="mb-6">
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          Team Recommendations
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Your direct reports' final scores and the status of any
          performance-based recommendation.
        </p>
      </div>

      @if (cycles().length > 0) {
        <label class="mb-5 block sm:w-72">
          <span class="mb-1 block text-xs font-medium text-neutral-500"
            >Cycle</span
          >
          <select
            [ngModel]="cycleId()"
            (ngModelChange)="onCycleChange($event)"
            class="w-full rounded-lg border border-neutral-200 px-3 py-2 text-sm outline-none transition focus:border-neutral-400"
            data-testid="cycle-select"
          >
            @for (c of cycles(); track c.cycleId) {
              <option [value]="c.cycleId">{{ c.cycleName }}</option>
            }
          </select>
        </label>
      }

      @if (loading()) {
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-32 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="reload()">
            Retry
          </button>
        </div>
      } @else if (rows().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-neutral-50 px-6 py-12 text-center text-sm text-neutral-500"
          data-testid="empty"
        >
          No recommendations for your team in this cycle yet.
        </div>
      } @else {
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3" @fadeIn>
          @for (row of rows(); track row.recommendationId) {
            <div
              class="rounded-xl border border-neutral-200 bg-white p-5 shadow-sm transition hover:shadow-md"
              data-testid="team-card"
            >
              <div class="flex items-start justify-between gap-2">
                <div>
                  <h2 class="font-medium text-neutral-900">
                    {{ row.employeeName }}
                  </h2>
                  @if (row.jobTitle) {
                    <p class="text-xs text-neutral-500">{{ row.jobTitle }}</p>
                  }
                </div>
                <span
                  class="inline-flex shrink-0 rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="statusBadge(row.status)"
                  data-testid="status-chip"
                  >{{ statusLabel(row.status) }}</span
                >
              </div>
              <dl class="mt-4 space-y-1 text-sm text-neutral-600">
                <div class="flex items-center justify-between">
                  <dt class="text-neutral-400">Final score</dt>
                  <dd class="font-medium text-neutral-700">
                    {{ row.finalScore ?? '—' }}
                  </dd>
                </div>
                <div class="flex items-center justify-between">
                  <dt class="text-neutral-400">Tenure</dt>
                  <dd>{{ tenure(row.tenureMonths) }}</dd>
                </div>
                <div class="flex items-center justify-between">
                  <dt class="text-neutral-400">Recommendation</dt>
                  <dd>{{ typeLabel(row.detail.type) }}</dd>
                </div>
              </dl>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class TeamRecommendationsComponent implements OnInit {
  private readonly service = inject(RecommendationService);

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly cycles = signal<ICompletedCycleOption[]>([]);
  readonly cycleId = signal<string>('');
  readonly rows = signal<IRecommendationRow[]>([]);

  ngOnInit(): void {
    this.loadCycles();
  }

  private loadCycles(): void {
    this.service.getCompletedCycles().subscribe({
      next: (cycles) => {
        this.cycles.set(cycles);
        if (cycles.length > 0) {
          this.cycleId.set(cycles[0].cycleId);
          this.reload();
        }
      },
      error: () => this.error.set('Unable to load completed cycles.'),
    });
  }

  onCycleChange(cycleId: string): void {
    this.cycleId.set(cycleId);
    this.reload();
  }

  reload(): void {
    if (!this.cycleId()) return;
    this.loading.set(true);
    this.error.set(null);
    this.service.getTeamRecommendations(this.cycleId()).subscribe({
      next: (rows) => {
        this.rows.set(rows);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load your team recommendations.');
        this.loading.set(false);
      },
    });
  }

  typeLabel(t: RecommendationType): string {
    return RECOMMENDATION_TYPE_LABEL[t];
  }
  statusLabel(s: RecommendationStatus): string {
    return RECOMMENDATION_STATUS_LABEL[s];
  }
  statusBadge(s: RecommendationStatus): string {
    return RECOMMENDATION_STATUS_BADGE[s];
  }
  tenure(months: number | null): string {
    return formatTenure(months);
  }
}
