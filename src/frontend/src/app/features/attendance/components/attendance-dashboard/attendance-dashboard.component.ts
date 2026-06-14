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
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { Subject, interval } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { AttendanceService } from '../../services/attendance.service';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  AttendanceScope,
  IDashboardKpi,
  ILiveBoardRow,
  LiveBoardStatus,
  IDonutSegment,
  buildDonutSegments,
  initialsOf,
  todayIso,
} from '../../models/attendance.models';

/** Polling cadence (§10 SignalR fallback): refresh KPIs + live board every 30s. */
const POLL_INTERVAL_MS = 30_000;

interface IKpiCard {
  key: keyof Pick<
    IDashboardKpi,
    'expectedHeadcount' | 'clockedIn' | 'pendingClockIn' | 'onLeave' | 'absent'
  >;
  label: string;
  accent: string;
}

const KPI_CARDS: IKpiCard[] = [
  { key: 'expectedHeadcount', label: 'Expected today', accent: 'text-neutral-900' },
  { key: 'clockedIn', label: 'Clocked in', accent: 'text-emerald-600' },
  { key: 'pendingClockIn', label: 'Pending clock-in', accent: 'text-amber-600' },
  { key: 'onLeave', label: 'On leave', accent: 'text-sky-600' },
  { key: 'absent', label: 'Absent', accent: 'text-rose-600' },
];

interface IStatusMeta {
  label: string;
  pill: string;
}

const STATUS_META: Record<LiveBoardStatus, IStatusMeta> = {
  CLOCKED_IN: { label: 'Clocked In', pill: 'bg-emerald-100 text-emerald-700' },
  NOT_CLOCKED_IN: { label: 'Not Clocked In', pill: 'bg-neutral-100 text-neutral-600' },
  ON_LEAVE: { label: 'On Leave', pill: 'bg-sky-100 text-sky-700' },
  HOLIDAY: { label: 'Holiday', pill: 'bg-violet-100 text-violet-700' },
};

/** Donut palette for the today's-breakdown segments (§8). */
const DONUT_COLORS = {
  clockedIn: '#10b981', // emerald-500
  onLeave: '#0ea5e9', // sky-500
  absent: '#f43f5e', // rose-500
  pending: '#f59e0b', // amber-500
};

/**
 * US-ATT-010 (AC-1/AC-2, §8): the HR attendance dashboard + live board.
 *
 * A Notion-style page:
 *  - KPI widget cards (expected / clocked-in / pending / on-leave / absent / attendance %).
 *  - A CSS/SVG donut for today's breakdown (Clocked In / On Leave / Absent / Pending),
 *    rendered with the pure `buildDonutSegments` helper — NO charting library.
 *  - A live attendance board (avatars/initials, status pills, clock-in time) with a
 *    desktop table + mobile card layout, and a subtle highlight on rows that changed.
 *
 * SignalR is unavailable (§10), so the KPIs + live board POLL every 30s via an
 * `interval` subscription torn down on destroy. A scope toggle (all / team) lets a
 * manager scope to their own team (BR-4); only HR sees the All-scope segment.
 */
@Component({
  selector: 'app-attendance-dashboard',
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
      <!-- Header + scope toggle -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Attendance Dashboard</h1>
          <p class="text-sm text-neutral-500 mt-1">
            Real-time workforce attendance for {{ date() }} ·
            <span class="text-neutral-400">auto-refreshing</span>
          </p>
        </div>
        <div class="flex items-center gap-2">
          @if (canScopeTeam()) {
            <div class="scope-toggle" role="group" aria-label="Scope">
              <button type="button" class="scope-btn" [class.scope-active]="scope() === 'all'"
                (click)="setScope('all')" data-test="scope-all">All</button>
              <button type="button" class="scope-btn" [class.scope-active]="scope() === 'team'"
                (click)="setScope('team')" data-test="scope-team">My team</button>
            </div>
          }
          <a routerLink="/attendance/reports" class="btn-secondary text-sm" data-test="reports-link">Reports</a>
        </div>
      </div>

      <!-- ─── KPI widget cards (AC-1) ───────────────────────────── -->
      @if (isLoadingKpi()) {
        <div class="kpi-grid mb-6" aria-busy="true" data-test="kpi-skeleton">
          @for (_ of [1,2,3,4,5,6]; track $index) {
            <div class="card-notion">
              <div class="skeleton-line h-3 w-20 mb-3"></div>
              <div class="skeleton-line h-8 w-16"></div>
            </div>
          }
        </div>
      } @else if (kpi(); as k) {
        <div class="kpi-grid mb-6" data-test="kpi-cards">
          @for (card of kpiCards; track card.key) {
            <div class="card-notion" [attr.data-test]="'kpi-' + card.key">
              <p class="metric-label">{{ card.label }}</p>
              <p class="metric-value" [ngClass]="card.accent">{{ k[card.key] }}</p>
            </div>
          }
          <div class="card-notion" data-test="kpi-attendancePercent">
            <p class="metric-label">Attendance %</p>
            <p class="metric-value text-indigo-600">{{ k.attendancePercent | number:'1.0-1' }}%</p>
          </div>
        </div>
      } @else {
        <div class="card-notion text-center py-10 mb-6" data-test="kpi-empty">
          <p class="text-sm text-neutral-500">Dashboard data is unavailable right now.</p>
        </div>
      }

      <div class="lg:grid lg:grid-cols-[20rem_1fr] lg:gap-6">
        <!-- ─── Donut: today's breakdown (§8) ───────────────────── -->
        <section class="card-notion mb-6 lg:mb-0" data-test="donut-section">
          <h2 class="text-sm font-medium text-neutral-700 mb-4">Today's breakdown</h2>
          @if (donutSegments().length === 0) {
            <p class="text-sm text-neutral-400 py-8 text-center" data-test="donut-empty">No data yet.</p>
          } @else {
            <div class="flex items-center gap-5">
              <svg viewBox="0 0 120 120" width="120" height="120" role="img"
                aria-label="Donut chart of today's attendance breakdown" data-test="donut-chart">
                @for (seg of donutSegments(); track seg.label) {
                  <path [attr.d]="seg.path" fill="none" [attr.stroke]="seg.color"
                    stroke-width="14" stroke-linecap="butt" />
                }
                <text x="60" y="58" text-anchor="middle" class="donut-num">
                  {{ kpi()?.attendancePercent | number:'1.0-0' }}%
                </text>
                <text x="60" y="72" text-anchor="middle" class="donut-cap">present</text>
              </svg>
              <ul class="text-xs space-y-1.5 flex-1">
                @for (seg of donutSegments(); track seg.label) {
                  <li class="flex items-center justify-between gap-2" [attr.data-test]="'legend-' + seg.label">
                    <span class="flex items-center gap-1.5">
                      <span class="h-2.5 w-2.5 rounded-sm" [style.background-color]="seg.color"></span>
                      {{ seg.label }}
                    </span>
                    <span class="font-medium text-neutral-700">{{ seg.value }}</span>
                  </li>
                }
              </ul>
            </div>
          }
        </section>

        <!-- ─── Live attendance board (AC-2) ────────────────────── -->
        <section class="min-w-0" data-test="live-board">
          <div class="flex items-center justify-between mb-3">
            <h2 class="text-sm font-medium text-neutral-700">Live board</h2>
            <span class="text-xs text-neutral-400">{{ liveRows().length }} employees</span>
          </div>

          @if (isLoadingBoard()) {
            <div class="card-notion space-y-3" aria-busy="true" data-test="board-skeleton">
              @for (_ of [1,2,3,4,5]; track $index) { <div class="skeleton-line h-12 w-full"></div> }
            </div>
          } @else if (liveRows().length === 0) {
            <div class="card-notion text-center py-12" data-test="board-empty">
              <p class="text-sm text-neutral-500">No employees in scope for {{ date() }}.</p>
            </div>
          } @else {
            <!-- Desktop table -->
            <div class="hidden md:block card-notion !p-0 overflow-x-auto">
              <table class="w-full text-sm" aria-label="Live attendance board">
                <thead>
                  <tr class="border-b border-neutral-100">
                    <th class="th">Employee</th>
                    <th class="th">Department</th>
                    <th class="th">Status</th>
                    <th class="th text-right">Clock-in</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of liveRows(); track row.employeeId) {
                    <tr class="row" [class.row-changed]="isChanged(row.employeeId)" data-test="board-row">
                      <td class="td">
                        <div class="flex items-center gap-2.5">
                          <span class="avatar" aria-hidden="true">{{ initials(row.employeeName) }}</span>
                          <div class="min-w-0">
                            <p class="font-medium text-neutral-900 truncate">{{ row.employeeName }}</p>
                            @if (row.employeeNumber) {
                              <p class="text-xs text-neutral-400">{{ row.employeeNumber }}</p>
                            }
                          </div>
                        </div>
                      </td>
                      <td class="td text-neutral-600">{{ row.departmentName || '—' }}</td>
                      <td class="td">
                        <span class="pill" [ngClass]="statusPill(row.status)" data-test="status-pill">
                          {{ statusLabel(row.status) }}
                        </span>
                      </td>
                      <td class="td text-right text-neutral-600" data-test="clock-in-cell">
                        {{ clockInTime(row) }}
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <!-- Mobile card layout -->
            <div class="md:hidden space-y-3" data-test="board-cards">
              @for (row of liveRows(); track row.employeeId) {
                <div class="card-notion flex items-center gap-3" [class.row-changed]="isChanged(row.employeeId)">
                  <span class="avatar" aria-hidden="true">{{ initials(row.employeeName) }}</span>
                  <div class="min-w-0 flex-1">
                    <p class="font-medium text-neutral-900 truncate">{{ row.employeeName }}</p>
                    <p class="text-xs text-neutral-400">{{ row.departmentName || '—' }}</p>
                  </div>
                  <div class="text-right">
                    <span class="pill" [ngClass]="statusPill(row.status)">{{ statusLabel(row.status) }}</span>
                    <p class="text-xs text-neutral-500 mt-1">{{ clockInTime(row) }}</p>
                  </div>
                </div>
              }
            </div>
          }
        </section>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-7xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }

    .kpi-grid { @apply grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3; }
    .metric-label { @apply text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .metric-value { @apply text-2xl font-semibold mt-1; }

    .scope-toggle { @apply inline-flex rounded-lg border border-neutral-200 bg-neutral-50 p-0.5; }
    .scope-btn { @apply rounded-md px-3 py-1.5 text-sm font-medium text-neutral-500 transition-colors; }
    .scope-active { @apply bg-white text-neutral-900 shadow-sm; }

    .th { @apply text-left py-3 px-4 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .td { @apply py-3 px-4 align-middle; }
    .row { @apply border-b border-neutral-50 transition-colors hover:bg-neutral-50/60; }
    .row-changed { animation: rowPulse 1.2s ease-out; }
    @keyframes rowPulse {
      0% { background-color: rgba(16, 185, 129, 0.18); }
      100% { background-color: transparent; }
    }

    .avatar {
      @apply flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-indigo-50
        text-xs font-semibold text-indigo-600;
    }
    .pill { @apply inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium; }

    .donut-num { @apply text-base font-semibold; fill: #1f2937; }
    .donut-cap { @apply text-[8px] uppercase tracking-wider; fill: #9ca3af; }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg border border-neutral-200 bg-white px-4 py-2
        text-sm font-medium text-neutral-700 transition-all duration-200 hover:bg-neutral-50;
    }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class AttendanceDashboardComponent implements OnInit, OnDestroy {
  private readonly attendanceService = inject(AttendanceService);
  private readonly authService = inject(AuthService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  readonly kpiCards = KPI_CARDS;

  // ─── State ──────────────────────────────────────────────────
  readonly date = signal(todayIso());
  readonly scope = signal<AttendanceScope>('all');
  readonly kpi = signal<IDashboardKpi | null>(null);
  readonly liveRows = signal<ILiveBoardRow[]>([]);
  readonly isLoadingKpi = signal(true);
  readonly isLoadingBoard = signal(true);
  /** employeeIds whose status changed since the last poll (drives the row highlight). */
  readonly changedIds = signal<Set<string>>(new Set());

  /** A manager (not a sole HR-all viewer) can scope to their team (BR-4). */
  readonly canScopeTeam = computed(() => this.authService.hasRole('Manager'));

  // ─── Donut geometry (§8) ────────────────────────────────────
  readonly donutSegments = computed<IDonutSegment[]>(() => {
    const k = this.kpi();
    if (!k) {
      return [];
    }
    return buildDonutSegments(
      [
        { label: 'Clocked In', value: k.clockedIn, color: DONUT_COLORS.clockedIn },
        { label: 'On Leave', value: k.onLeave, color: DONUT_COLORS.onLeave },
        { label: 'Absent', value: k.absent, color: DONUT_COLORS.absent },
        { label: 'Pending', value: k.pendingClockIn, color: DONUT_COLORS.pending },
      ],
      60,
      60,
      46,
    );
  });

  // ─── Lifecycle ──────────────────────────────────────────────
  ngOnInit(): void {
    this.refresh(true);
    // §10 polling fallback (no SignalR): refresh KPIs + live board every 30s.
    interval(POLL_INTERVAL_MS)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.refresh(false));
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Load KPIs + live board. `showSkeleton` only on first load / scope change. */
  refresh(showSkeleton: boolean): void {
    if (showSkeleton) {
      this.isLoadingKpi.set(true);
      this.isLoadingBoard.set(true);
    }
    const date = this.date();
    const scope = this.scope();

    this.attendanceService
      .getDashboardKpi(date, scope)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (k) => {
          this.kpi.set(k);
          this.isLoadingKpi.set(false);
        },
        error: () => {
          this.isLoadingKpi.set(false);
          if (showSkeleton) {
            this.kpi.set(null);
            this.toastr.error('Failed to load the dashboard KPIs.');
          }
        },
      });

    this.attendanceService
      .getLiveBoard(date, scope)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.applyLiveBoard(res.rows ?? []);
          this.isLoadingBoard.set(false);
        },
        error: () => {
          this.isLoadingBoard.set(false);
          if (showSkeleton) {
            this.liveRows.set([]);
            this.toastr.error('Failed to load the live board.');
          }
        },
      });
  }

  /** Diff incoming rows against the current board to highlight status changes (§8). */
  private applyLiveBoard(rows: ILiveBoardRow[]): void {
    const prev = new Map(this.liveRows().map((r) => [r.employeeId, r.status]));
    const changed = new Set<string>();
    for (const r of rows) {
      const before = prev.get(r.employeeId);
      if (before !== undefined && before !== r.status) {
        changed.add(r.employeeId);
      }
    }
    this.changedIds.set(changed);
    this.liveRows.set(rows);
  }

  setScope(scope: AttendanceScope): void {
    if (this.scope() === scope) {
      return;
    }
    this.scope.set(scope);
    this.refresh(true);
  }

  // ─── View helpers ───────────────────────────────────────────
  isChanged(employeeId: string): boolean {
    return this.changedIds().has(employeeId);
  }

  initials(name: string): string {
    return initialsOf(name);
  }

  statusLabel(status: LiveBoardStatus): string {
    return STATUS_META[status]?.label ?? status;
  }

  statusPill(status: LiveBoardStatus): string {
    return STATUS_META[status]?.pill ?? 'bg-neutral-100 text-neutral-600';
  }

  clockInTime(row: ILiveBoardRow): string {
    if (row.status !== 'CLOCKED_IN' || !row.clockInAt) {
      return '—';
    }
    const d = new Date(row.clockInAt);
    if (Number.isNaN(d.getTime())) {
      return '—';
    }
    return d.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  }
}
