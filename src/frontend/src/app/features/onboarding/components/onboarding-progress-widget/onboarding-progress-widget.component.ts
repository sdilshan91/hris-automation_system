import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { trigger, transition, style, animate } from '@angular/animations';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { OnboardingChecklistService } from '../../services/onboarding-checklist.service';
import {
  IOnboardingProgress,
  ringDashOffset,
} from '../../models/my-onboarding.models';

/**
 * US-ONB-003 (AC-1): Dashboard "Onboarding Progress" widget.
 *
 * Self-contained standalone card embedded by the dashboard. Calls
 * GET /checklists/me/progress on init and renders a circular progress ring with
 * the completion percentage and pending/completed/overdue counts, plus a link to
 * the full checklist. Hides itself entirely when the employee has no assigned
 * checklist — a 404 / empty (totalTasks === 0) response is treated as "nothing
 * to show" so non-new-hires never see the widget (additive, non-breaking).
 */
@Component({
  selector: 'app-onboarding-progress-widget',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    @if (loading()) {
      <div class="widget-card animate-pulse" aria-busy="true" aria-label="Loading onboarding progress">
        <div class="flex items-center gap-4">
          <div class="w-20 h-20 rounded-full bg-neutral-100"></div>
          <div class="flex-1 space-y-2">
            <div class="h-4 w-32 rounded bg-neutral-100"></div>
            <div class="h-3 w-48 rounded bg-neutral-100"></div>
          </div>
        </div>
      </div>
    } @else if (progress(); as p) {
      <div class="widget-card" @fadeIn>
        <div class="flex items-center justify-between mb-4">
          <h2 class="text-base font-semibold text-neutral-900">Onboarding Progress</h2>
          <a routerLink="/onboarding/my-checklist" class="text-sm font-medium text-brand-600 hover:text-brand-700">
            View checklist
          </a>
        </div>

        <div class="flex items-center gap-5">
          <!-- Circular progress ring (UI/UX §8) -->
          <div class="relative flex-shrink-0" aria-hidden="true">
            <svg width="84" height="84" viewBox="0 0 84 84" class="-rotate-90">
              <circle cx="42" cy="42" r="36" fill="none" stroke="#e5e7eb" stroke-width="8" />
              <circle
                cx="42" cy="42" r="36" fill="none"
                stroke="#4f46e5" stroke-width="8" stroke-linecap="round"
                [attr.stroke-dasharray]="circumference"
                [attr.stroke-dashoffset]="dashOffset()"
                class="transition-all duration-500 ease-out"
              />
            </svg>
            <span class="absolute inset-0 flex items-center justify-center text-lg font-semibold text-neutral-900">
              {{ p.progressPercent }}%
            </span>
          </div>

          <!-- Count summary -->
          <div class="flex-1 grid grid-cols-3 gap-2" role="group"
            [attr.aria-label]="ariaSummary()">
            <div class="count-cell">
              <span class="count-value text-green-600">{{ p.completedTasks }}</span>
              <span class="count-label">Completed</span>
            </div>
            <div class="count-cell">
              <span class="count-value text-amber-600">{{ p.pendingTasks }}</span>
              <span class="count-label">Pending</span>
            </div>
            <div class="count-cell">
              <span class="count-value" [class.text-red-600]="p.overdueTasks > 0"
                [class.text-neutral-400]="p.overdueTasks === 0">{{ p.overdueTasks }}</span>
              <span class="count-label">Overdue</span>
            </div>
          </div>
        </div>
        <p class="sr-only" aria-live="polite">{{ ariaSummary() }}</p>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .widget-card {
      @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-5;
    }
    .count-cell {
      @apply flex flex-col items-center justify-center rounded-lg bg-neutral-50 py-2 px-1;
    }
    .count-value { @apply text-xl font-semibold leading-none; }
    .count-label { @apply mt-1 text-[11px] font-medium text-neutral-400; }
  `],
})
export class OnboardingProgressWidgetComponent implements OnInit, OnDestroy {
  private readonly checklistService = inject(OnboardingChecklistService);
  private readonly destroy$ = new Subject<void>();

  /** Ring circumference for r=36 (2πr). Shared with dashOffset(). */
  readonly circumference = 2 * Math.PI * 36;

  readonly loading = signal(true);
  /** null = no checklist (widget hidden); object = render. */
  readonly progress = signal<IOnboardingProgress | null>(null);

  readonly dashOffset = computed(() =>
    ringDashOffset(this.progress()?.progressPercent ?? 0, this.circumference),
  );

  readonly ariaSummary = computed(() => {
    const p = this.progress();
    if (!p) return '';
    return `Onboarding ${p.progressPercent}% complete. ${p.completedTasks} completed, ${p.pendingTasks} pending, ${p.overdueTasks} overdue.`;
  });

  ngOnInit(): void {
    this.checklistService
      .getMyProgress()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (p) => {
          // No assigned checklist => hide (empty/zero-task response).
          this.progress.set(p && p.totalTasks > 0 ? p : null);
          this.loading.set(false);
        },
        error: (_err: HttpErrorResponse) => {
          // 404 / any error => no widget (fail closed, never block the dashboard).
          this.progress.set(null);
          this.loading.set(false);
        },
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
