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
import { trigger, transition, style, animate } from '@angular/animations';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AttendanceService } from '../../services/attendance.service';
import {
  ILatenessScore,
  latenessScoreTone,
  latenessUsedPercent,
} from '../../models/attendance.models';

/**
 * US-ATT-008 (§8, AC-4): employee self-service monthly lateness score.
 *
 * A small Notion-style progress card — "X of N allowed lates used this month" — backed by
 * GET /late-early/my-score. The bar turns amber once the allowance is reached/exceeded
 * (the late-deduction trigger, BR-4). A companion early-departure count is shown beside
 * it. Designed to be embedded on the employee dashboard or shown standalone.
 */
@Component({
  selector: 'app-lateness-score',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    @if (isLoading()) {
      <div class="score-card" aria-busy="true" data-test="skeleton">
        <div class="skeleton-line h-4 w-32 mb-3"></div>
        <div class="skeleton-line h-2.5 w-full"></div>
      </div>
    } @else if (score(); as s) {
      <div class="score-card" @fadeIn data-test="score-card">
        <div class="flex items-center justify-between gap-3">
          <p class="text-sm font-medium text-neutral-800">Lateness this month</p>
          <span class="text-xs text-neutral-400">{{ s.yearMonth }}</span>
        </div>

        <p class="mt-2 text-2xl font-semibold tabular-nums"
          [class.text-amber-700]="tone() === 'warn'" [class.text-neutral-900]="tone() === 'ok'"
          data-test="score-value">
          {{ s.lateCount }}<span class="text-base font-normal text-neutral-400"> of {{ allowedLabel() }}</span>
        </p>
        <p class="text-xs text-neutral-500">allowed lates used</p>

        <!-- Progress bar -->
        <div class="track mt-3" role="progressbar"
          [attr.aria-valuenow]="s.lateCount" [attr.aria-valuemin]="0"
          [attr.aria-valuemax]="s.allowedLates" aria-label="Allowed lates used this month">
          <div class="fill" [class.fill-warn]="tone() === 'warn'" [class.fill-ok]="tone() === 'ok'"
            [style.width.%]="usedPercent()" data-test="progress-fill"></div>
        </div>

        @if (tone() === 'warn') {
          <p class="mt-2 text-xs font-medium text-amber-700" data-test="warn-note">
            You have reached your monthly late allowance — further lates may incur a deduction.
          </p>
        }

        <p class="mt-3 text-xs text-neutral-500" data-test="early-count">
          Early departures this month: <span class="font-medium text-neutral-700">{{ s.earlyDepartureCount }}</span>
        </p>
      </div>
    } @else {
      <div class="score-card" @fadeIn data-test="error">
        <p class="text-sm text-neutral-500">Lateness score unavailable.</p>
      </div>
    }
  `,
  styles: [`
    :host { display: block; }
    .score-card { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-4 sm:p-5; }
    .track { @apply h-2.5 w-full overflow-hidden rounded-full bg-neutral-100; }
    .fill { @apply h-full rounded-full transition-all duration-300; }
    .fill-ok { @apply bg-green-500; }
    .fill-warn { @apply bg-amber-500; }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class LatenessScoreComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly destroy$ = new Subject<void>();

  readonly month = signal(currentMonthIso());
  readonly isLoading = signal(true);
  readonly score = signal<ILatenessScore | null>(null);

  readonly tone = computed(() => {
    const s = this.score();
    return s ? latenessScoreTone(s) : 'ok';
  });

  readonly usedPercent = computed(() => {
    const s = this.score();
    return s ? latenessUsedPercent(s) : 0;
  });

  /** When no allowance is configured (0), show "∞" rather than "0". */
  readonly allowedLabel = computed(() => {
    const s = this.score();
    return s && s.allowedLates > 0 ? `${s.allowedLates}` : '∞';
  });

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.isLoading.set(true);
    this.attendanceService
      .getMyLatenessScore(this.month())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (s) => {
          this.score.set(s);
          this.isLoading.set(false);
        },
        error: () => {
          this.isLoading.set(false);
          this.score.set(null);
        },
      });
  }
}

/** Current month as `yyyy-MM` in the browser's local timezone. */
function currentMonthIso(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = (now.getMonth() + 1).toString().padStart(2, '0');
  return `${y}-${m}`;
}
