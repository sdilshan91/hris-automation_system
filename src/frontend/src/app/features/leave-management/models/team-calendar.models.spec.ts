import {
  ITeamCalendarEntry,
  ITeamCalendarHoliday,
  buildHolidayMap,
  buildMonthGrid,
  buildWeekDays,
  buildWeekRows,
  buildListGroups,
  buildLegend,
  resolveEntryColor,
  entryLabel,
  distinctEmployees,
  distinctLeaveTypes,
  hasStatusScope,
  entryCoversDate,
  isWithin,
  toIsoDate,
  yearOf,
  TEAM_CALENDAR_NEUTRAL_COLOR,
  TEAM_CALENDAR_GENERIC_LABEL,
  TEAM_CALENDAR_PALETTE,
} from './team-calendar.models';

function entry(p: Partial<ITeamCalendarEntry>): ITeamCalendarEntry {
  return {
    employeeId: 'e-1',
    employeeName: 'Ada',
    startDate: '2026-06-10',
    endDate: '2026-06-10',
    totalDays: 1,
    ...p,
  };
}

describe('team-calendar.models (US-LV-009)', () => {
  describe('date helpers', () => {
    it('toIsoDate pads month/day', () => {
      expect(toIsoDate(2026, 0, 5)).toBe('2026-01-05');
      expect(toIsoDate(2026, 11, 31)).toBe('2026-12-31');
    });

    it('yearOf slices the year', () => {
      expect(yearOf('2026-06-10')).toBe(2026);
    });

    it('isWithin is inclusive', () => {
      expect(isWithin('2026-06-10', '2026-06-01', '2026-06-30')).toBeTrue();
      expect(isWithin('2026-07-01', '2026-06-01', '2026-06-30')).toBeFalse();
    });

    it('entryCoversDate spans the range inclusively', () => {
      const e = entry({ startDate: '2026-06-10', endDate: '2026-06-12' });
      expect(entryCoversDate(e, '2026-06-09')).toBeFalse();
      expect(entryCoversDate(e, '2026-06-10')).toBeTrue();
      expect(entryCoversDate(e, '2026-06-11')).toBeTrue();
      expect(entryCoversDate(e, '2026-06-12')).toBeTrue();
      expect(entryCoversDate(e, '2026-06-13')).toBeFalse();
    });
  });

  describe('buildHolidayMap', () => {
    it('groups holiday names by date', () => {
      const holidays: ITeamCalendarHoliday[] = [
        { date: '2026-06-15', name: 'A' },
        { date: '2026-06-15', name: 'B' },
        { date: '2026-06-20', name: 'C' },
      ];
      const map = buildHolidayMap(holidays);
      expect(map.get('2026-06-15')).toEqual(['A', 'B']);
      expect(map.get('2026-06-20')).toEqual(['C']);
    });
  });

  describe('buildLegend', () => {
    it('manager scope: one item per leave type, API color preferred', () => {
      const entries = [
        entry({ leaveTypeName: 'Annual Leave', color: '#111111' }),
        entry({ leaveTypeName: 'Sick Leave' }), // no color → palette
        entry({ leaveTypeName: 'Annual Leave', color: '#111111' }),
      ];
      const { legend, paletteIndex } = buildLegend(entries);
      expect(legend.length).toBe(2);
      expect(legend[0]).toEqual({ label: 'Annual Leave', color: '#111111' });
      expect(paletteIndex.get('Annual Leave')).toBe('#111111');
      expect(paletteIndex.get('Sick Leave')).toBe(TEAM_CALENDAR_PALETTE[0]);
    });

    it('employee scope: single generic legend when no leave-type detail (BR-1)', () => {
      const entries = [entry({}), entry({ employeeId: 'e-2', employeeName: 'Bob' })];
      const { legend } = buildLegend(entries);
      expect(legend.length).toBe(1);
      expect(legend[0]).toEqual({
        label: TEAM_CALENDAR_GENERIC_LABEL,
        color: TEAM_CALENDAR_NEUTRAL_COLOR,
      });
    });

    it('empty entries → empty legend', () => {
      expect(buildLegend([]).legend).toEqual([]);
    });
  });

  describe('resolveEntryColor / entryLabel', () => {
    it('prefers API color, then palette index, then neutral', () => {
      const { paletteIndex } = buildLegend([
        entry({ leaveTypeName: 'Annual Leave' }),
      ]);
      expect(resolveEntryColor(entry({ color: '#abcdef' }), paletteIndex)).toBe('#abcdef');
      expect(
        resolveEntryColor(entry({ leaveTypeName: 'Annual Leave' }), paletteIndex)
      ).toBe(TEAM_CALENDAR_PALETTE[0]);
      expect(resolveEntryColor(entry({}), paletteIndex)).toBe(
        TEAM_CALENDAR_NEUTRAL_COLOR
      );
    });

    it('entryLabel falls back to the generic label (BR-1)', () => {
      expect(entryLabel(entry({ leaveTypeName: 'Annual Leave' }))).toBe('Annual Leave');
      expect(entryLabel(entry({}))).toBe(TEAM_CALENDAR_GENERIC_LABEL);
    });
  });

  describe('buildMonthGrid (AC-1)', () => {
    const holidayMap = buildHolidayMap([{ date: '2026-06-15', name: 'Holiday' }]);

    it('produces a 42-cell Sunday-first grid', () => {
      const grid = buildMonthGrid(2026, 5, [], holidayMap, '2026-06-10');
      expect(grid.length).toBe(42);
      // June 2026 starts on a Monday → 1 leading padding cell.
      expect(grid[0].inMonth).toBeFalse();
      expect(grid[1].inMonth).toBeTrue();
      expect(grid[1].day).toBe(1);
    });

    it('buckets a multi-day entry onto each covered date + colors via legend', () => {
      const e = entry({ startDate: '2026-06-10', endDate: '2026-06-12' });
      const grid = buildMonthGrid(2026, 5, [e], holidayMap, '2026-06-01');
      const c10 = grid.find((c) => c.date === '2026-06-10')!;
      const c11 = grid.find((c) => c.date === '2026-06-11')!;
      const c13 = grid.find((c) => c.date === '2026-06-13')!;
      expect(c10.entries.length).toBe(1);
      expect(c11.entries.length).toBe(1);
      expect(c13.entries.length).toBe(0);
    });

    it('marks today and public-holiday cells (FR-7)', () => {
      const grid = buildMonthGrid(2026, 5, [], holidayMap, '2026-06-10');
      const today = grid.find((c) => c.date === '2026-06-10')!;
      const holiday = grid.find((c) => c.date === '2026-06-15')!;
      expect(today.isToday).toBeTrue();
      expect(holiday.isHoliday).toBeTrue();
      expect(holiday.holidayNames).toEqual(['Holiday']);
    });
  });

  describe('buildWeekDays / buildWeekRows (AC-3)', () => {
    const holidayMap = buildHolidayMap([{ date: '2026-06-10', name: 'H' }]);

    it('builds 7 Sunday-first day columns', () => {
      const days = buildWeekDays('2026-06-10', holidayMap, '2026-06-10');
      expect(days.length).toBe(7);
      expect(days[0].label).toBe('Sun');
      // The week of Wed 2026-06-10 starts Sun 2026-06-07.
      expect(days[0].date).toBe('2026-06-07');
      expect(days[3].date).toBe('2026-06-10');
      expect(days[3].isToday).toBeTrue();
      expect(days[3].isHoliday).toBeTrue();
    });

    it('builds one Gantt row per employee with per-day cells', () => {
      const days = buildWeekDays('2026-06-10', holidayMap, '2026-06-10');
      const entries = [
        entry({ employeeId: 'e-1', employeeName: 'Ada', startDate: '2026-06-09', endDate: '2026-06-10' }),
        entry({ employeeId: 'e-2', employeeName: 'Bob', startDate: '2026-06-11', endDate: '2026-06-11' }),
      ];
      const rows = buildWeekRows(entries, days);
      expect(rows.length).toBe(2);
      // Sorted by name → Ada first.
      expect(rows[0].employeeName).toBe('Ada');
      expect(rows[0].cells.length).toBe(7);
      // Ada covered on Mon(09) + Tue(10) → indices 2 and 3.
      expect(rows[0].cells[2]).not.toBeNull();
      expect(rows[0].cells[3]).not.toBeNull();
      expect(rows[0].cells[0]).toBeNull();
    });

    it('drops employees with no coverage in the week', () => {
      const days = buildWeekDays('2026-06-10', holidayMap, '2026-06-10');
      const entries = [
        entry({ employeeId: 'e-3', employeeName: 'Far', startDate: '2026-07-01', endDate: '2026-07-02' }),
      ];
      expect(buildWeekRows(entries, days).length).toBe(0);
    });
  });

  describe('buildListGroups (AC-4)', () => {
    it('groups by start date chronologically and sorts entries by name', () => {
      const holidayMap = buildHolidayMap([{ date: '2026-06-12', name: 'H' }]);
      const entries = [
        entry({ employeeId: 'e-2', employeeName: 'Bob', startDate: '2026-06-12' }),
        entry({ employeeId: 'e-1', employeeName: 'Ada', startDate: '2026-06-12' }),
        entry({ employeeId: 'e-3', employeeName: 'Cy', startDate: '2026-06-10' }),
      ];
      const groups = buildListGroups(entries, holidayMap);
      expect(groups.map((g) => g.date)).toEqual(['2026-06-10', '2026-06-12']);
      expect(groups[1].entries.map((e) => e.employeeName)).toEqual(['Ada', 'Bob']);
      expect(groups[1].isHoliday).toBeTrue();
      expect(groups[0].isHoliday).toBeFalse();
    });
  });

  describe('filter helpers (FR-6)', () => {
    it('distinctEmployees returns unique employees sorted by name', () => {
      const entries = [
        entry({ employeeId: 'e-2', employeeName: 'Bob' }),
        entry({ employeeId: 'e-1', employeeName: 'Ada' }),
        entry({ employeeId: 'e-2', employeeName: 'Bob' }),
      ];
      expect(distinctEmployees(entries)).toEqual([
        { id: 'e-1', name: 'Ada' },
        { id: 'e-2', name: 'Bob' },
      ]);
    });

    it('distinctLeaveTypes only from entries that carry detail', () => {
      const entries = [
        entry({ leaveTypeName: 'Sick Leave' }),
        entry({ leaveTypeName: 'Annual Leave' }),
        entry({}), // employee-scope row, no detail
      ];
      expect(distinctLeaveTypes(entries)).toEqual(['Annual Leave', 'Sick Leave']);
    });

    it('hasStatusScope true only when any entry has a status (manager scope)', () => {
      expect(hasStatusScope([entry({ status: 'Pending' })])).toBeTrue();
      expect(hasStatusScope([entry({})])).toBeFalse();
    });
  });
});
