import {
  ILopEntry,
  lopSourceLabel,
  lopRowClasses,
  lopSourceBadgeClasses,
  canOverrideLop,
  filterLopEntries,
  expandDateRange,
  LOP_SOURCE_FILTERS,
} from './lop.models';

function entry(partial: Partial<ILopEntry> = {}): ILopEntry {
  return {
    leaveRequestId: 'lr-1',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    date: '2026-07-06',
    days: 1,
    source: 'system_generated',
    status: 'System-Generated',
    ...partial,
  };
}

describe('lop.models', () => {
  describe('lopSourceLabel', () => {
    it('maps each source to a human label', () => {
      expect(lopSourceLabel('system_generated')).toBe('Auto-generated');
      expect(lopSourceLabel('hr_assigned')).toBe('HR-assigned');
      expect(lopSourceLabel('employee_request')).toBe('Employee-requested');
      expect(lopSourceLabel('compulsory')).toBe('Compulsory');
    });
  });

  describe('lopRowClasses / badge (§8 red-orange highlight)', () => {
    it('uses red emphasis for system-generated entries', () => {
      expect(lopRowClasses('system_generated')).toContain('border-red-400');
      expect(lopSourceBadgeClasses('system_generated')).toContain('text-red-700');
    });

    it('uses orange emphasis for all other sources', () => {
      for (const src of ['hr_assigned', 'employee_request', 'compulsory'] as const) {
        expect(lopRowClasses(src)).toContain('border-orange-400');
        expect(lopSourceBadgeClasses(src)).toContain('text-orange-700');
      }
    });
  });

  describe('canOverrideLop (BR-3)', () => {
    it('allows override only for system-generated, non-locked entries', () => {
      expect(canOverrideLop(entry({ source: 'system_generated' }))).toBeTrue();
    });

    it('blocks override for non-system sources', () => {
      expect(canOverrideLop(entry({ source: 'hr_assigned' }))).toBeFalse();
      expect(canOverrideLop(entry({ source: 'employee_request' }))).toBeFalse();
      expect(canOverrideLop(entry({ source: 'compulsory' }))).toBeFalse();
    });

    it('blocks override when the payroll period is locked (BR-5)', () => {
      expect(canOverrideLop(entry({ source: 'system_generated', payrollLocked: true }))).toBeFalse();
    });
  });

  describe('filterLopEntries', () => {
    const list = [
      entry({ leaveRequestId: 'a', source: 'system_generated' }),
      entry({ leaveRequestId: 'b', source: 'hr_assigned' }),
      entry({ leaveRequestId: 'c', source: 'compulsory' }),
    ];

    it('returns all entries for the "all" filter', () => {
      expect(filterLopEntries(list, 'all').length).toBe(3);
    });

    it('filters to a single source', () => {
      const result = filterLopEntries(list, 'hr_assigned');
      expect(result.length).toBe(1);
      expect(result[0].leaveRequestId).toBe('b');
    });

    it('returns an empty array when nothing matches', () => {
      expect(filterLopEntries([], 'system_generated').length).toBe(0);
    });
  });

  describe('expandDateRange', () => {
    it('expands an inclusive range into date-only strings', () => {
      expect(expandDateRange('2026-07-06', '2026-07-08')).toEqual([
        '2026-07-06',
        '2026-07-07',
        '2026-07-08',
      ]);
    });

    it('returns a single date for a same-day range', () => {
      expect(expandDateRange('2026-07-06', '2026-07-06')).toEqual(['2026-07-06']);
    });

    it('returns [] for an inverted range', () => {
      expect(expandDateRange('2026-07-08', '2026-07-06')).toEqual([]);
    });

    it('returns [] for missing or invalid dates', () => {
      expect(expandDateRange('', '2026-07-06')).toEqual([]);
      expect(expandDateRange('not-a-date', '2026-07-06')).toEqual([]);
    });

    it('pads single-digit months and days', () => {
      expect(expandDateRange('2026-01-01', '2026-01-02')).toEqual(['2026-01-01', '2026-01-02']);
    });
  });

  describe('LOP_SOURCE_FILTERS', () => {
    it('starts with "all" and includes every source', () => {
      expect(LOP_SOURCE_FILTERS[0].value).toBe('all');
      const values = LOP_SOURCE_FILTERS.map((f) => f.value);
      expect(values).toContain('system_generated');
      expect(values).toContain('hr_assigned');
      expect(values).toContain('employee_request');
      expect(values).toContain('compulsory');
    });
  });
});
