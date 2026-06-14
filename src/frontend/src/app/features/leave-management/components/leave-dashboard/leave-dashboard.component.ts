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
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

import { LeaveDashboardService } from '../../services/leave-dashboard.service';
import { LeaveRequestService } from '../../services/leave-request.service';
import {
  ILeaveBalanceSummary,
  ILeaveLedgerEntry,
  LedgerEntryType,
  buildYearOptions,
  usedFraction,
  LEDGER_BADGE_CLASSES,
  LEDGER_ENTRY_LABELS,
} from '../../models/leave-dashboard.models';
import {
  ILeaveRequest,
  LeaveRequestStatus,
  STATUS_BADGE_CLASSES,
} from '../../models/leave-request.models';

/** Filter chips for the leave-history section (FR-6). */
type HistoryFilter = 'All' | 'Approved' | 'Rejected' | 'Cancelled';

/**
 * US-LV-006: Leave Balance Dashboard for the Employee.
 *
 * The default landing view of the Leave module for the Employee persona (§10).
 * Layout per §8:
 *  - Year-selector pill group (BR-5) that re-fetches the selected year's data.
 *  - Grid of per-leave-type balance cards (2-3/row desktop, 1/row mobile, AC-4),
 *    each with a custom SVG arc progress indicator + numeric entitlement/used/
 *    pending/remaining (AC-1). Color is an accent only; text values are always
 *    shown and the arc carries an aria-label (NFR-4).
 *  - Click a card -> ledger detail view showing the transaction history for the
 *    year (accruals/usages/adjustments/carry-forwards/expirations) (AC-2).
 *  - "Upcoming Leaves" timeline of approved + pending future leaves (AC-3).
 *  - Leave history: a filterable list of past requests (FR-6).
 *  - Empty state when the balance list is empty (AC-5).
 *  - Loading skeletons while fetching (§8).
 *
 * Charting: custom SVG arc (no new dependency, §10). Balance math (BR-1) and the
 * arc fraction are pure helpers in leave-dashboard.models.ts so they are testable.
 *
 * DEFER (seam only, not built): Redis-cached balance source (backend concern,
 * FR-5) and real-time balance push -- the dashboard re-fetches on year change /
 * manual reload. TODO(realtime-balance-push) marker below.
 */
@Component({
  selector: 'app-leave-dashboard',
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
    trigger('slideOver', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(16px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('160ms ease-in', style({ opacity: 0, transform: 'translateX(16px)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container" @fadeIn>
      <!-- Header + year selector -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Leave Dashboard</h1>
          <p class="text-sm text-neutral-500 mt-1">Your balances, upcoming leaves and history.</p>
        </div>
        <div class="flex items-center gap-3">
          <a routerLink="/leave/apply" class="btn-primary text-sm">+ Apply for Leave</a>
        </div>
      </div>

      <!-- Year selector pill-group (BR-5, §8) -->
      <div
        class="inline-flex items-center rounded-lg bg-neutral-100 p-1 mb-6"
        role="group"
        aria-label="Select leave year"
      >
        @for (y of yearOptions; track y) {
          <button
            type="button"
            class="year-pill"
            [class.year-pill--active]="y === selectedYear()"
            [attr.aria-pressed]="y === selectedYear()"
            (click)="selectYear(y)"
          >
            {{ y }}
          </button>
        }
      </div>

      <!-- ─── Balance cards ─────────────────────────────────── -->
      @if (isLoadingBalances()) {
        <div class="balance-grid mb-10" aria-live="polite" aria-busy="true">
          @for (_ of [1,2,3]; track $index) {
            <div class="card-notion">
              <div class="skeleton-line h-4 w-24 mb-4"></div>
              <div class="skeleton-circle mx-auto mb-4"></div>
              <div class="skeleton-line h-3 w-full mb-2"></div>
              <div class="skeleton-line h-3 w-2/3"></div>
            </div>
          }
        </div>
      } @else if (activeBalances().length === 0 && archivedBalances().length === 0) {
        <!-- Empty state (AC-5) -->
        <div @fadeIn class="card-notion text-center py-16 mb-10" data-testid="empty-state">
          <svg class="mx-auto mb-4 text-neutral-300" width="72" height="72" viewBox="0 0 24 24"
            fill="none" stroke="currentColor" stroke-width="1.5" aria-hidden="true">
            <rect x="3" y="4" width="18" height="17" rx="2" />
            <path d="M16 2v4M8 2v4M3 10h18" />
          </svg>
          <h3 class="text-lg font-semibold text-neutral-900 mb-1">No leave balances yet</h3>
          <p class="text-sm text-neutral-500 max-w-md mx-auto">
            Your leave balances are being set up. Please check back soon.
          </p>
        </div>
      } @else {
        <!-- Active leave type cards (AC-1) -->
        <div class="balance-grid mb-6" @fadeIn>
          @for (b of activeBalances(); track b.leaveTypeId) {
            <button
              type="button"
              class="card-notion balance-card text-left"
              (click)="openLedger(b)"
              [attr.aria-label]="cardAriaLabel(b)"
            >
              <!-- Color accent stripe + type name (color not sole indicator, NFR-4) -->
              <div class="flex items-center gap-2 mb-4">
                <span class="h-3 w-3 rounded-full shrink-0"
                  [style.background-color]="b.color || '#9ca3af'" aria-hidden="true"></span>
                <span class="font-medium text-neutral-900 truncate">{{ b.leaveTypeName }}</span>
              </div>

              <!-- Custom SVG arc indicator (§10) -->
              <div class="relative flex items-center justify-center mb-4">
                <svg width="120" height="120" viewBox="0 0 120 120"
                  [attr.aria-label]="arcAriaLabel(b)" role="img">
                  <circle cx="60" cy="60" r="52" fill="none" stroke="#f1f1f0" stroke-width="10" />
                  <circle cx="60" cy="60" r="52" fill="none"
                    [attr.stroke]="b.color || '#9ca3af'" stroke-width="10" stroke-linecap="round"
                    transform="rotate(-90 60 60)"
                    [attr.stroke-dasharray]="circumference"
                    [attr.stroke-dashoffset]="dashOffset(b)" />
                </svg>
                <div class="absolute inset-0 flex flex-col items-center justify-center">
                  <span class="text-2xl font-semibold text-neutral-900">{{ b.balance }}</span>
                  <span class="text-[11px] text-neutral-400 uppercase tracking-wide">left</span>
                </div>
              </div>

              <!-- Numeric values (always visible, NFR-4) -->
              <dl class="grid grid-cols-2 gap-x-4 gap-y-1.5 text-sm">
                <dt class="text-neutral-500">Entitlement</dt>
                <dd class="text-right font-medium text-neutral-900">{{ b.entitlement }}</dd>
                <dt class="text-neutral-500">Used</dt>
                <dd class="text-right font-medium text-neutral-900">{{ b.used }}</dd>
                <dt class="text-neutral-500">Pending</dt>
                <dd class="text-right font-medium text-amber-600" data-testid="pending-value">{{ b.pending }}</dd>
                <dt class="text-neutral-500">Remaining</dt>
                <dd class="text-right font-semibold text-neutral-900">{{ b.balance }}</dd>
              </dl>
              <!-- US-LV-008 (§8): carry-forward + expired as separate line items.
                   Color coding: carry-forward = blue, expired = gray strikethrough. -->
              @if (b.carryForward > 0 || b.expired > 0) {
                <dl class="grid grid-cols-2 gap-x-4 gap-y-1 text-xs mt-3 pt-3 border-t border-neutral-100">
                  @if (b.carryForward > 0) {
                    <dt class="text-neutral-500">Carry-forward</dt>
                    <dd class="text-right font-medium text-blue-600" data-testid="carry-forward-value">
                      +{{ b.carryForward }}
                    </dd>
                  }
                  @if (b.expired > 0) {
                    <dt class="text-neutral-500">Expired</dt>
                    <dd class="text-right font-medium text-neutral-400 line-through" data-testid="expired-value">
                      {{ b.expired }}
                    </dd>
                  }
                </dl>
                <!-- Expiring-soon amber indicator (§8) — only when an expiry date is supplied. -->
                @if (b.carryForward > 0 && b.carryForwardExpiry) {
                  <p class="mt-2 inline-flex items-center gap-1.5 rounded-md bg-amber-50 px-2 py-1
                      text-xs font-medium text-amber-700 ring-1 ring-inset ring-amber-200"
                    data-testid="expiring-soon">
                    <span class="h-1.5 w-1.5 rounded-full bg-amber-500" aria-hidden="true"></span>
                    {{ b.carryForward }} carry-forward day(s) expiring on {{ b.carryForwardExpiry | date:'mediumDate' }}
                  </p>
                }
              }
            </button>
          }
        </div>

        <!-- Archived (deactivated types with remaining balance, BR-3) -->
        @if (archivedBalances().length > 0) {
          <div class="mb-10">
            <button type="button" class="text-sm text-neutral-500 hover:text-neutral-800 transition-colors mb-3"
              [attr.aria-expanded]="showArchived()" (click)="toggleArchived()">
              {{ showArchived() ? '▾' : '▸' }} Archived ({{ archivedBalances().length }})
            </button>
            @if (showArchived()) {
              <div class="balance-grid" @fadeIn>
                @for (b of archivedBalances(); track b.leaveTypeId) {
                  <button type="button" class="card-notion balance-card text-left opacity-75"
                    (click)="openLedger(b)" [attr.aria-label]="cardAriaLabel(b)">
                    <div class="flex items-center gap-2 mb-2">
                      <span class="h-3 w-3 rounded-full shrink-0"
                        [style.background-color]="b.color || '#9ca3af'" aria-hidden="true"></span>
                      <span class="font-medium text-neutral-700 truncate">{{ b.leaveTypeName }}</span>
                      <span class="ml-auto text-[10px] uppercase text-neutral-400 ring-1 ring-neutral-200 rounded px-1.5">Archived</span>
                    </div>
                    <p class="text-sm text-neutral-500">Remaining: <span class="font-semibold text-neutral-900">{{ b.balance }}</span></p>
                  </button>
                }
              </div>
            }
          </div>
        }
      }

      <!-- ─── Upcoming leaves timeline (AC-3) ───────────────── -->
      <section class="mb-10" aria-labelledby="upcoming-heading">
        <h2 id="upcoming-heading" class="text-lg font-semibold text-neutral-900 mb-3">Upcoming Leaves</h2>
        @if (isLoadingUpcoming()) {
          <div class="card-notion space-y-3" aria-busy="true">
            @for (_ of [1,2]; track $index) { <div class="skeleton-line h-10 w-full"></div> }
          </div>
        } @else if (upcoming().length === 0) {
          <div class="card-notion text-center py-8 text-sm text-neutral-500">No upcoming leaves scheduled.</div>
        } @else {
          <ol class="relative border-l border-neutral-200 ml-2 space-y-4" data-testid="upcoming-list">
            @for (u of upcoming(); track u.leaveRequestId) {
              <li class="ml-4">
                <span class="absolute -left-1.5 mt-1.5 h-3 w-3 rounded-full ring-2 ring-white"
                  [style.background-color]="u.leaveTypeColor || '#9ca3af'" aria-hidden="true"></span>
                <div class="card-notion flex flex-col sm:flex-row sm:items-center gap-2 sm:gap-4">
                  <span class="date-chip">{{ u.startDate | date:'MMM d' }} – {{ u.endDate | date:'MMM d, y' }}</span>
                  <span class="font-medium text-neutral-800">{{ u.leaveTypeName }}</span>
                  <span class="text-sm text-neutral-500">{{ u.totalDays }} day(s)</span>
                  <span class="status-badge sm:ml-auto" [class]="statusBadgeClass(u.status)">{{ u.status }}</span>
                </div>
              </li>
            }
          </ol>
        }
      </section>

      <!-- ─── Leave history (FR-6) ──────────────────────────── -->
      <section aria-labelledby="history-heading">
        <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 mb-3">
          <h2 id="history-heading" class="text-lg font-semibold text-neutral-900">Leave History</h2>
          <div class="inline-flex items-center rounded-lg bg-neutral-100 p-1" role="group" aria-label="Filter history">
            @for (f of historyFilters; track f) {
              <button type="button" class="year-pill"
                [class.year-pill--active]="f === historyFilter()"
                [attr.aria-pressed]="f === historyFilter()"
                (click)="setHistoryFilter(f)">{{ f }}</button>
            }
          </div>
        </div>
        @if (isLoadingHistory()) {
          <div class="card-notion space-y-3" aria-busy="true">
            @for (_ of [1,2,3]; track $index) { <div class="skeleton-line h-10 w-full"></div> }
          </div>
        } @else if (filteredHistory().length === 0) {
          <div class="card-notion text-center py-8 text-sm text-neutral-500">No past leave requests for this filter.</div>
        } @else {
          <div class="card-notion overflow-x-auto" data-testid="history-list">
            <table class="w-full text-sm" aria-label="Leave history">
              <thead>
                <tr class="border-b border-neutral-100">
                  <th class="th">Type</th>
                  <th class="th">Dates</th>
                  <th class="th text-center">Days</th>
                  <th class="th text-center">Status</th>
                </tr>
              </thead>
              <tbody>
                @for (h of filteredHistory(); track h.leaveRequestId; let odd = $odd) {
                  <tr class="border-b border-neutral-50" [class.bg-neutral-50/40]="odd">
                    <td class="td">
                      <span class="inline-flex items-center gap-1.5">
                        <span class="h-2.5 w-2.5 rounded-full" [style.background-color]="h.leaveTypeColor || '#9ca3af'" aria-hidden="true"></span>
                        {{ h.leaveTypeName }}
                      </span>
                    </td>
                    <td class="td text-neutral-600">{{ h.startDate | date:'mediumDate' }} – {{ h.endDate | date:'mediumDate' }}</td>
                    <td class="td text-center font-medium text-neutral-900">{{ h.totalDays }}</td>
                    <td class="td text-center"><span class="status-badge" [class]="statusBadgeClass(h.status)">{{ h.status }}</span></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>

    <!-- ─── Ledger detail slide-over (AC-2) ─────────────────── -->
    @if (ledgerOpen()) {
      <div class="overlay" (click)="closeLedger()" aria-hidden="true"></div>
      <aside class="detail-panel" @slideOver role="dialog" aria-modal="true" aria-labelledby="ledger-title">
        <div class="flex items-center justify-between mb-5">
          <h2 id="ledger-title" class="text-lg font-semibold text-neutral-900 flex items-center gap-2">
            <span class="h-3 w-3 rounded-full" [style.background-color]="selectedBalance()?.color || '#9ca3af'" aria-hidden="true"></span>
            {{ selectedBalance()?.leaveTypeName }} · {{ selectedYear() }}
          </h2>
          <button type="button" class="icon-btn" (click)="closeLedger()" aria-label="Close ledger">✕</button>
        </div>

        @if (isLoadingLedger()) {
          <div class="space-y-3" aria-busy="true">
            @for (_ of [1,2,3,4]; track $index) { <div class="skeleton-line h-10 w-full"></div> }
          </div>
        } @else if (ledger().length === 0) {
          <p class="text-sm text-neutral-500 text-center py-10">No transactions recorded for this year.</p>
        } @else {
          <table class="w-full text-sm" aria-label="Leave ledger transactions" data-testid="ledger-table">
            <thead>
              <tr class="border-b border-neutral-100">
                <th class="th">Date</th>
                <th class="th">Type</th>
                <th class="th text-right">Amount</th>
                <th class="th text-right">Balance</th>
              </tr>
            </thead>
            <tbody>
              @for (e of ledger(); track e.ledgerId; let odd = $odd) {
                <tr class="border-b border-neutral-50" [class.bg-neutral-50/40]="odd">
                  <td class="td text-neutral-500 whitespace-nowrap">{{ e.occurredAt | date:'mediumDate' }}</td>
                  <td class="td">
                    <span class="status-badge" [class]="ledgerBadgeClass(e.entryType)">{{ ledgerLabel(e.entryType) }}</span>
                    @if (e.description) { <span class="block text-xs text-neutral-400 mt-0.5">{{ e.description }}</span> }
                  </td>
                  <td class="td text-right font-medium" [class.text-green-700]="e.amount > 0" [class.text-red-700]="e.amount < 0">
                    {{ e.amount > 0 ? '+' : '' }}{{ e.amount }}
                  </td>
                  <td class="td text-right font-semibold text-neutral-900">{{ e.balanceAfter }}</td>
                </tr>
              }
            </tbody>
          </table>
        }
      </aside>
    }
  `,
  styles: [`
    :host { display: block; }
    .page-container { @apply max-w-5xl mx-auto pb-12; }
    .card-notion { @apply rounded-xl bg-white border border-neutral-100 shadow-sm p-5; }
    .balance-grid { @apply grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4; }
    .balance-card { @apply transition-all duration-200 hover:shadow-md hover:-translate-y-0.5 cursor-pointer w-full; }

    .year-pill {
      @apply px-3 py-1.5 rounded-md text-sm font-medium text-neutral-500 transition-colors duration-200;
    }
    .year-pill--active { @apply bg-white text-neutral-900 shadow-sm; }

    .date-chip { @apply inline-flex items-center px-2.5 py-1 rounded-md text-xs font-medium bg-neutral-100 text-neutral-700 whitespace-nowrap; }
    .status-badge { @apply inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ring-1 ring-inset; }

    .th { @apply text-left py-3 px-3 text-xs font-medium text-neutral-400 uppercase tracking-wider; }
    .td { @apply py-3 px-3; }

    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }
    .skeleton-circle { @apply rounded-full bg-neutral-200 h-[120px] w-[120px]; animation: shimmer 1.5s ease-in-out infinite; }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200 hover:bg-brand-700;
    }

    .overlay { @apply fixed inset-0 bg-black/20 z-40; }
    .detail-panel {
      @apply fixed top-0 right-0 z-50 h-full w-full sm:w-[28rem] bg-white shadow-xl
        border-l border-neutral-100 p-6 overflow-y-auto;
    }
    .icon-btn { @apply h-8 w-8 inline-flex items-center justify-center rounded-lg text-neutral-400 hover:bg-neutral-100 hover:text-neutral-700 transition-colors; }
  `],
})
export class LeaveDashboardComponent implements OnInit, OnDestroy {
  private readonly dashboardService = inject(LeaveDashboardService);
  private readonly leaveRequestService = inject(LeaveRequestService);
  private readonly toastr = inject(ToastrService);
  private readonly destroy$ = new Subject<void>();

  // SVG arc geometry (r = 52)
  readonly circumference = 2 * Math.PI * 52;

  readonly yearOptions = buildYearOptions(new Date().getFullYear());
  readonly historyFilters: HistoryFilter[] = ['All', 'Approved', 'Rejected', 'Cancelled'];

  // ─── State signals ─────────────────────────────────────
  readonly selectedYear = signal(new Date().getFullYear());
  readonly balances = signal<ILeaveBalanceSummary[]>([]);
  readonly upcoming = signal<ILeaveRequest[]>([]);
  readonly history = signal<ILeaveRequest[]>([]);
  readonly ledger = signal<ILeaveLedgerEntry[]>([]);
  readonly selectedBalance = signal<ILeaveBalanceSummary | null>(null);

  readonly isLoadingBalances = signal(true);
  readonly isLoadingUpcoming = signal(true);
  readonly isLoadingHistory = signal(true);
  readonly isLoadingLedger = signal(false);
  readonly ledgerOpen = signal(false);
  readonly showArchived = signal(false);
  readonly historyFilter = signal<HistoryFilter>('All');

  // ─── Derived state ─────────────────────────────────────
  readonly activeBalances = computed(() => this.balances().filter((b) => !b.isArchived));
  readonly archivedBalances = computed(() => this.balances().filter((b) => b.isArchived));

  readonly filteredHistory = computed(() => {
    const f = this.historyFilter();
    const all = this.history();
    if (f === 'All') {
      return all;
    }
    return all.filter((h) => h.status === f);
  });

  ngOnInit(): void {
    this.loadYear(this.selectedYear());
    this.loadUpcoming();
    this.loadHistory();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Data loading ──────────────────────────────────────

  /**
   * Load (or reload) balances for a given year (BR-5).
   * TODO(realtime-balance-push): balances are fetched on load / year-change only;
   * real-time push (e.g. SignalR on approval) is deferred. Manual reload suffices.
   */
  loadYear(year: number): void {
    this.isLoadingBalances.set(true);
    this.dashboardService
      .getMyBalance(year)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (balances) => {
          this.balances.set(balances ?? []);
          this.isLoadingBalances.set(false);
        },
        error: () => {
          this.balances.set([]);
          this.isLoadingBalances.set(false);
          this.toastr.error('Failed to load your leave balances.');
        },
      });
  }

  private loadUpcoming(): void {
    this.isLoadingUpcoming.set(true);
    this.dashboardService
      .getMyUpcoming()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (items) => {
          this.upcoming.set(items ?? []);
          this.isLoadingUpcoming.set(false);
        },
        error: () => {
          this.upcoming.set([]);
          this.isLoadingUpcoming.set(false);
        },
      });
  }

  private loadHistory(): void {
    this.isLoadingHistory.set(true);
    // FR-6: history reuses the US-LV-003 /leaves/mine endpoint; we keep only
    // terminal-state requests (approved / rejected / cancelled) for "past" history.
    this.leaveRequestService
      .getMyLeaveRequests()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (items) => {
          const past = (items ?? []).filter(
            (r) => r.status === 'Approved' || r.status === 'Rejected' || r.status === 'Cancelled',
          );
          this.history.set(past);
          this.isLoadingHistory.set(false);
        },
        error: () => {
          this.history.set([]);
          this.isLoadingHistory.set(false);
        },
      });
  }

  // ─── Year selector (BR-5) ──────────────────────────────

  selectYear(year: number): void {
    if (year === this.selectedYear()) {
      return;
    }
    this.selectedYear.set(year);
    this.loadYear(year);
    // If a ledger is open, refresh it for the new year too.
    const sel = this.selectedBalance();
    if (this.ledgerOpen() && sel) {
      this.loadLedger(sel);
    }
  }

  // ─── Ledger detail (AC-2) ──────────────────────────────

  openLedger(balance: ILeaveBalanceSummary): void {
    this.selectedBalance.set(balance);
    this.ledgerOpen.set(true);
    this.loadLedger(balance);
  }

  private loadLedger(balance: ILeaveBalanceSummary): void {
    this.isLoadingLedger.set(true);
    this.ledger.set([]);
    this.dashboardService
      .getMyLedger(balance.leaveTypeId, this.selectedYear())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (entries) => {
          this.ledger.set(entries ?? []);
          this.isLoadingLedger.set(false);
        },
        error: () => {
          this.ledger.set([]);
          this.isLoadingLedger.set(false);
          this.toastr.error('Failed to load the leave ledger.');
        },
      });
  }

  closeLedger(): void {
    this.ledgerOpen.set(false);
    this.selectedBalance.set(null);
  }

  // ─── History filter (FR-6) ─────────────────────────────

  setHistoryFilter(f: HistoryFilter): void {
    this.historyFilter.set(f);
  }

  toggleArchived(): void {
    this.showArchived.update((v) => !v);
  }

  // ─── View helpers ──────────────────────────────────────

  /** Stroke-dashoffset for the arc: progresses from full (empty) to 0 (fully used). */
  dashOffset(b: ILeaveBalanceSummary): number {
    return this.circumference * (1 - usedFraction(b));
  }

  arcAriaLabel(b: ILeaveBalanceSummary): string {
    // Color is never the sole indicator (NFR-4): the arc has an explicit text label.
    return `${b.leaveTypeName}: ${b.used} of ${b.entitlement} days used, ${b.balance} remaining, ${b.pending} pending`;
  }

  cardAriaLabel(b: ILeaveBalanceSummary): string {
    return `View ledger for ${b.leaveTypeName}. ${b.balance} days remaining of ${b.entitlement}.`;
  }

  ledgerBadgeClass(t: LedgerEntryType): string {
    return LEDGER_BADGE_CLASSES[t] ?? LEDGER_BADGE_CLASSES.Adjusted;
  }

  ledgerLabel(t: LedgerEntryType): string {
    return LEDGER_ENTRY_LABELS[t] ?? t;
  }

  statusBadgeClass(status: LeaveRequestStatus): string {
    return STATUS_BADGE_CLASSES[status] ?? STATUS_BADGE_CLASSES.Pending;
  }
}
