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
import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { TeamCalendarService } from '../../services/team-calendar.service';
import {
  ITeamCalendarEntry,
  ITeamCalendarFilters,
  TeamCalendarStatus,
  TeamCalendarView,
  ICalendarCell,
  IWeekDay,
  IWeekRow,
  IListGroup,
  ITeamCalendarLegendItem,
  buildMonthGrid,
  buildWeekDays,
  buildWeekRows,
  buildListGroups,
  buildHolidayMap,
  buildLegend,
  distinctEmployees,
  distinctLeaveTypes,
  hasStatusScope,
  resolveEntryColor,
  entryLabel,
  toIsoDate,
  MONTH_NAMES,
  WEEKDAY_LABELS,
  TEAM_CALENDAR_HOLIDAY_BG,
} from '../../models/team-calendar.models';

/**
 * US-LV-009: Team Leave Calendar View.
 *
 * Three view modes via a segmented control (FR-5, §8):
 *   - Month (AC-1): CSS-grid month calendar with color-coded leave blocks per
 *     employee, today highlighted, public holidays as light-gray backgrounds
 *     (FR-7), hover tooltips.
 *   - Week (AC-3): Gantt-style horizontal bars, employee rows × day columns.
 *   - List (AC-4, mobile default): chronological list grouped by date with
 *     employee cards (name, leave type, status).
 *
 * Half-day leaves are visually differentiated (AM/PM indicator, BR-5).
 * A color legend + Notion-style chip filter bar (employee / leave type /
 * status) sit at the top (FR-6). The status filter only appears when the API
 * returns status (manager scope).
 *
 * SCOPE-AWARE: the component renders whatever the API returns. For employee
 * scope the API suppresses pending + leave-type detail; entries then render
 * generically as "On leave" with no status/type. The component never assumes
 * manager-only fields nor requests hidden data (BR-1, BR-2).
 *
 * Open to any authenticated employee (Manager + Employee), matching the
 * dashboard route guard.
 */
@Component({
  selector: 'app-team-leave-calendar',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  templateUrl: './team-leave-calendar.component.html',
  styleUrls: ['./team-leave-calendar.component.css'],
})
export class TeamLeaveCalendarComponent implements OnInit, OnDestroy {
  private readonly teamCalendarService = inject(TeamCalendarService);
  private readonly destroy$ = new Subject<void>();

  // --- Data state -------------------------------------------------
  readonly entries = signal<ITeamCalendarEntry[]>([]);
  readonly holidays = signal<{ date: string; name: string }[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');

  // --- View state -------------------------------------------------
  /** Default view depends on viewport; list on mobile (§8, AC-4). */
  readonly viewMode = signal<TeamCalendarView>(this.defaultView());
  readonly year = signal<number>(new Date().getFullYear());
  /** Month being shown in the month grid (0-based). */
  readonly activeMonth = signal<number>(new Date().getMonth());
  /** Anchor ISO date for the week view (any date within the shown week). */
  readonly weekAnchor = signal<string>(this.todayIso());

  // --- Filter state (FR-6) ---------------------------------------
  readonly employeeFilter = signal<string>('');
  readonly leaveTypeFilter = signal<string>('');
  readonly statusFilter = signal<TeamCalendarStatus | ''>('');

  // --- Template constants ----------------------------------------
  readonly monthNames = MONTH_NAMES;
  readonly weekdayLabels = WEEKDAY_LABELS;
  readonly holidayBg = TEAM_CALENDAR_HOLIDAY_BG;
  readonly skeletonItems = [1, 2, 3, 4, 5, 6];
  readonly statusOptions: TeamCalendarStatus[] = ['Approved', 'Pending'];

  // --- Derived: holiday lookup -----------------------------------
  readonly holidayMap = computed(() => buildHolidayMap(this.holidays()));

  /** Distinct employees for the filter bar (FR-6). */
  readonly employeeOptions = computed(() => distinctEmployees(this.entries()));

  /** Distinct leave types for the filter bar (manager scope only, FR-6). */
  readonly leaveTypeOptions = computed(() => distinctLeaveTypes(this.entries()));

  /**
   * Whether the API returned status info (manager scope). The status filter
   * chip group is only shown when true (FR-6 — status filter manager only).
   */
  readonly hasStatus = computed(() => hasStatusScope(this.entries()));

  /** Color legend + name→color index for the leave-type blocks (§8). */
  readonly legendData = computed(() => buildLegend(this.entries()));
  readonly legend = computed<ITeamCalendarLegendItem[]>(() => this.legendData().legend);

  /** Entries after applying the chip filters (FR-6). */
  readonly filteredEntries = computed(() => {
    const emp = this.employeeFilter();
    const type = this.leaveTypeFilter();
    const status = this.statusFilter();
    return this.entries().filter((e) => {
      if (emp && e.employeeId !== emp) return false;
      if (type && e.leaveTypeName !== type) return false;
      if (status && e.status !== status) return false;
      return true;
    });
  });

  readonly hasEntries = computed(() => this.filteredEntries().length > 0);

  // --- Month view (AC-1) -----------------------------------------
  readonly monthGrid = computed<ICalendarCell[]>(() =>
    buildMonthGrid(
      this.year(),
      this.activeMonth(),
      this.filteredEntries(),
      this.holidayMap(),
      this.todayIso()
    )
  );

  // --- Week view (AC-3) ------------------------------------------
  readonly weekDays = computed<IWeekDay[]>(() =>
    buildWeekDays(this.weekAnchor(), this.holidayMap(), this.todayIso())
  );

  readonly weekRows = computed<IWeekRow[]>(() => {
    const days = this.weekDays();
    // Only entries that overlap the visible week, to keep the Gantt focused.
    const from = days[0].date;
    const to = days[days.length - 1].date;
    const inWeek = this.filteredEntries().filter(
      (e) => e.startDate <= to && e.endDate >= from
    );
    return buildWeekRows(inWeek, days);
  });

  // --- List view (AC-4) ------------------------------------------
  readonly listGroups = computed<IListGroup[]>(() =>
    buildListGroups(this.filteredEntries(), this.holidayMap())
  );

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // --- Loading ----------------------------------------------------

  /**
   * Load the calendar for the currently active month's date range. We always
   * fetch a generous range (the whole active month plus its grid padding) so
   * the month grid, the week view, and the list all have data to render.
   */
  load(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    const { from, to } = this.activeRange();
    const filters: ITeamCalendarFilters = {
      employeeId: this.employeeFilter() || null,
      leaveTypeId: null, // leaveType filter is client-side (by name)
      status: this.statusFilter() || null,
    };

    this.teamCalendarService
      .getTeamCalendar(from, to, filters)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (res) => {
          this.entries.set(res.entries ?? []);
          this.holidays.set(res.holidays ?? []);
          this.isLoading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          const body = TeamCalendarService.parseError(err);
          this.loadError.set(
            body?.message ||
              'Failed to load the team leave calendar. Please try again.'
          );
        },
      });
  }

  /**
   * The from/to range to request. Covers the active month with one week of
   * padding on each side so multi-day leaves spilling across month boundaries
   * (and the week view) still render.
   */
  private activeRange(): { from: string; to: string } {
    const y = this.year();
    const m = this.activeMonth();
    const first = new Date(y, m, 1);
    const last = new Date(y, m + 1, 0);
    first.setDate(first.getDate() - 7);
    last.setDate(last.getDate() + 7);
    return {
      from: toIsoDate(first.getFullYear(), first.getMonth(), first.getDate()),
      to: toIsoDate(last.getFullYear(), last.getMonth(), last.getDate()),
    };
  }

  // --- View toggle + navigation ----------------------------------

  setView(mode: TeamCalendarView): void {
    this.viewMode.set(mode);
  }

  changeMonth(delta: number): void {
    let m = this.activeMonth() + delta;
    let y = this.year();
    if (m < 0) {
      m = 11;
      y -= 1;
    } else if (m > 11) {
      m = 0;
      y += 1;
    }
    this.activeMonth.set(m);
    this.year.set(y);
    // Keep the week anchor inside the newly shown month.
    this.weekAnchor.set(toIsoDate(y, m, 1));
    this.load();
  }

  changeWeek(delta: number): void {
    const days = this.weekDays();
    const anchor = days[0].date;
    const d = new Date(
      parseInt(anchor.substring(0, 4), 10),
      parseInt(anchor.substring(5, 7), 10) - 1,
      parseInt(anchor.substring(8, 10), 10)
    );
    d.setDate(d.getDate() + delta * 7);
    const newAnchor = toIsoDate(d.getFullYear(), d.getMonth(), d.getDate());
    this.weekAnchor.set(newAnchor);

    // If the week moved into a different month, re-fetch that month's range.
    const m = d.getMonth();
    const y = d.getFullYear();
    if (m !== this.activeMonth() || y !== this.year()) {
      this.activeMonth.set(m);
      this.year.set(y);
      this.load();
    }
  }

  goToToday(): void {
    const now = new Date();
    this.year.set(now.getFullYear());
    this.activeMonth.set(now.getMonth());
    this.weekAnchor.set(this.todayIso());
    this.load();
  }

  // --- Filters (FR-6) --------------------------------------------

  setEmployeeFilter(value: string): void {
    this.employeeFilter.set(value);
    // Employee filter is also passed server-side; re-fetch to narrow the range.
    this.load();
  }

  onEmployeeFilterChange(event: Event): void {
    this.setEmployeeFilter((event.target as HTMLSelectElement).value);
  }

  setLeaveTypeFilter(value: string): void {
    // Leave-type filter is purely client-side (by denormalized name).
    this.leaveTypeFilter.update((cur) => (cur === value ? '' : value));
  }

  setStatusFilter(value: TeamCalendarStatus | ''): void {
    this.statusFilter.update((cur) => (cur === value ? '' : value));
    this.load();
  }

  clearFilters(): void {
    this.employeeFilter.set('');
    this.leaveTypeFilter.set('');
    this.statusFilter.set('');
    this.load();
  }

  readonly hasActiveFilters = computed(
    () =>
      !!this.employeeFilter() ||
      !!this.leaveTypeFilter() ||
      !!this.statusFilter()
  );

  // --- Color / label helpers (template) --------------------------

  entryColor(entry: ITeamCalendarEntry): string {
    return resolveEntryColor(entry, this.legendData().paletteIndex);
  }

  /** A faint background tint derived from the entry color (block fill). */
  entryBg(entry: ITeamCalendarEntry): string {
    return this.entryColor(entry) + '1a'; // ~10% alpha hex suffix
  }

  label(entry: ITeamCalendarEntry): string {
    return entryLabel(entry);
  }

  /** Tooltip text for a leave block (manager scope shows full detail). */
  tooltip(entry: ITeamCalendarEntry): string {
    const parts = [entry.employeeName, this.label(entry)];
    if (entry.status) parts.push(entry.status);
    if (entry.isHalfDay) {
      parts.push(`Half day${entry.halfDaySession ? ' (' + entry.halfDaySession + ')' : ''}`);
    }
    parts.push(`${entry.startDate} → ${entry.endDate}`);
    return parts.join(' · ');
  }

  /** Half-day session label for the AM/PM indicator (BR-5). */
  halfDayLabel(entry: ITeamCalendarEntry): string {
    if (!entry.isHalfDay) return '';
    return entry.halfDaySession ? entry.halfDaySession : '½';
  }

  // --- Internal: date helpers ------------------------------------

  /** Today's date as 'YYYY-MM-DD'. Extracted so tests can rely on real today. */
  private todayIso(): string {
    const now = new Date();
    return toIsoDate(now.getFullYear(), now.getMonth(), now.getDate());
  }

  /**
   * Default view mode by viewport width. Mobile (< 768px) defaults to the
   * list view (§8, AC-4). Guarded for non-browser environments.
   */
  private defaultView(): TeamCalendarView {
    if (typeof window !== 'undefined' && window.innerWidth < 768) {
      return 'list';
    }
    return 'month';
  }
}
