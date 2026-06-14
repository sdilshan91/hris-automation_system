import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { LeaveReportsService } from '../../services/leave-reports.service';
import {
  REPORT_CATALOG,
  IReportCard,
  ILeaveSummaryMetrics,
} from '../../models/leave-reports.models';

/**
 * US-LV-012: Leave Reports & Analytics landing page (HR-facing).
 *
 * Layout per §8:
 *  - Dashboard summary widgets at the top: total utilization %, top leave type,
 *    absenteeism rate (the AC summary cards).
 *  - A Notion-like grid of report cards (icon, title, description) for the six
 *    pre-built reports (FR-1). Each card links to its full-page detail view.
 *
 * The "last generated" timestamp the story mentions is a per-session client
 * stamp: the backend report endpoints are on-demand queries (no stored report
 * artifacts), so there is no server "last generated" to read. We surface a
 * lightweight "Viewed {time}" stamp persisted in localStorage instead of
 * inventing a backend field. TODO(report-history): if the backend later persists
 * generated-report metadata, read it here.
 */
@Component({
  selector: 'app-leave-reports',
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
    <div class="page-container" @fadeIn>
      <!-- Header -->
      <div class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Leave Reports &amp; Analytics</h1>
        <p class="text-sm text-neutral-500 mt-1">
          Pre-built reports for leave balances, utilization, absenteeism and trends.
        </p>
      </div>

      <!-- ─── Summary widgets (AC cards, §8) ─────────────────── -->
      @if (isLoadingMetrics()) {
        <div class="summary-grid mb-8" aria-busy="true">
          @for (_ of [1,2,3]; track $index) {
            <div class="card-notion">
              <div class="skeleton-line h-3 w-24 mb-3"></div>
              <div class="skeleton-line h-7 w-20"></div>
            </div>
          }
        </div>
      } @else if (metrics()) {
        <div class="summary-grid mb-8" @fadeIn data-testid="summary-widgets">
          <div class="card-notion">
            <p class="metric-label">Total utilization</p>
            <p class="metric-value text-indigo-600" data-testid="metric-utilization">
              {{ metrics()!.totalUtilizationPct }}%
            </p>
          </div>
          <div class="card-notion">
            <p class="metric-label">Top leave type</p>
            <p class="metric-value text-neutral-900 truncate" data-testid="metric-top-type">
              {{ metrics()!.topLeaveType || '—' }}
            </p>
          </div>
          <div class="card-notion">
            <p class="metric-label">Absenteeism rate</p>
            <p class="metric-value text-rose-600" data-testid="metric-absenteeism">
              {{ metrics()!.absenteeismRatePct }}%
            </p>
          </div>
        </div>
      }

      <!-- ─── Report card grid (FR-1, §8) ────────────────────── -->
      <h2 class="text-sm font-medium text-neutral-500 uppercase tracking-wider mb-3">Reports</h2>
      <div class="report-grid" data-testid="report-grid">
        @for (card of cards; track card.type) {
          <a
            class="report-card"
            [routerLink]="['/leave/reports', card.type]"
            [attr.data-testid]="'report-card-' + card.type"
          >
            <div class="flex items-start gap-3">
              <span class="icon-chip" aria-hidden="true">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                  stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">
                  <path [attr.d]="card.iconPath" />
                </svg>
              </span>
              <div class="min-w-0">
                <h3 class="font-medium text-neutral-900">{{ card.title }}</h3>
                <p class="text-sm text-neutral-500 mt-0.5">{{ card.description }}</p>
              </div>
            </div>
            <p class="mt-4 text-xs text-neutral-400">
              {{ lastViewed(card.type) }}
            </p>
          </a>
        }
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-6xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .summary-grid { @apply grid grid-cols-1 sm:grid-cols-3 gap-4; }
    .metric-label { @apply text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .metric-value { @apply text-2xl font-semibold mt-1; }

    .report-grid { @apply grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4; }
    .report-card {
      @apply block rounded-xl bg-white border border-neutral-100 shadow-sm p-5
        transition-all duration-200 hover:shadow-md hover:-translate-y-0.5 cursor-pointer;
    }
    .icon-chip {
      @apply flex h-10 w-10 shrink-0 items-center justify-center rounded-lg
        bg-indigo-50 text-indigo-600;
    }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class LeaveReportsComponent implements OnInit, OnDestroy {
  private readonly reportsService = inject(LeaveReportsService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly cards: IReportCard[] = REPORT_CATALOG;

  readonly metrics = signal<ILeaveSummaryMetrics | null>(null);
  readonly isLoadingMetrics = signal(true);

  ngOnInit(): void {
    this.loadMetrics();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadMetrics(): void {
    this.isLoadingMetrics.set(true);
    this.reportsService
      .getSummaryMetrics()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (m) => {
          this.metrics.set(m ?? null);
          this.isLoadingMetrics.set(false);
        },
        error: () => {
          this.metrics.set(null);
          this.isLoadingMetrics.set(false);
          this.toastr.error('Failed to load the summary metrics.');
        },
      });
  }

  /** Per-session "last viewed" stamp from localStorage (no backend field exists). */
  lastViewed(type: string): string {
    const raw = this.readStamp(type);
    if (!raw) {
      return 'Not yet viewed';
    }
    const d = new Date(raw);
    if (isNaN(d.getTime())) {
      return 'Not yet viewed';
    }
    return `Last viewed ${d.toLocaleDateString()}`;
  }

  private readStamp(type: string): string | null {
    try {
      return typeof localStorage !== 'undefined'
        ? localStorage.getItem(`leave-report:lastViewed:${type}`)
        : null;
    } catch {
      return null;
    }
  }
}
