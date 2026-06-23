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
    source: 'SystemGenerated',
    status: 'System-Generated',
    ...partial,
  };
}

describe('lop.models', () => {
  describe('lopSourceLabel', () => {
    it('maps each source to a human label', () => {
      expect(lopSourceLabel('SystemGenerated')).toBe('Auto-generated');
      expect(lopSourceLabel('HrAssigned')).toBe('HR-assigned');
      expect(lopSourceLabel('EmployeeRequest')).toBe('Employee-requested');
      expect(lopSourceLabel('Compulsory')).toBe('Compulsory');
    });
  });

  describe('lopRowClasses / badge (§8 red-orange highlight)', () => {
    it('uses red emphasis for system-generated entries', () => {
      expect(lopRowClasses('SystemGenerated')).toContain('border-red-400');
      expect(lopSourceBadgeClasses('SystemGenerated')).toContain('text-red-700');
    });

    it('uses orange emphasis for all other sources', () => {
      for (const src of ['HrAssigned', 'EmployeeRequest', 'Compulsory'] as const) {
        expect(lopRowClasses(src)).toContain('border-orange-400');
        expect(lopSourceBadgeClasses(src)).toContain('text-orange-700');
      }
    });
  });

  describe('canOverrideLop (BR-3)', () => {
    it('allows override only for system-generated, non-locked entries', () => {
      expect(canOverrideLop(entry({ source: 'SystemGenerated' }))).toBeTrue();
    });

    it('blocks override for non-system sources', () => {
      expect(canOverrideLop(entry({ source: 'HrAssigned' }))).toBeFalse();
      expect(canOverrideLop(entry({ source: 'EmployeeRequest' }))).toBeFalse();
      expect(canOverrideLop(entry({ source: 'Compulsory' }))).toBeFalse();
    });

    it('blocks override when the payroll period is locked (BR-5)', () => {
      expect(canOverrideLop(entry({ source: 'SystemGenerated', payrollLocked: true }))).toBeFalse();
    });
  });

  describe('filterLopEntries', () => {
    const list = [
      entry({ leaveRequestId: 'a', source: 'SystemGenerated' }),
      entry({ leaveRequestId: 'b', source: 'HrAssigned' }),
      entry({ leaveRequestId: 'c', source: 'Compulsory' }),
    ];

    it('returns all entries for the "all" filter', () => {
      expect(filterLopEntries(list, 'all').length).toBe(3);
    });

    it('filters to a single source', () => {
      const result = filterLopEntries(list, 'HrAssigned');
      expect(result.length).toBe(1);
      expect(result[0].leaveRequestId).toBe('b');
    });

    it('returns an empty array when nothing matches', () => {
      expect(filterLopEntries([], 'SystemGenerated').length).toBe(0);
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
      expect(values).toContain('SystemGenerated');
      expect(values).toContain('HrAssigned');
      expect(values).toContain('EmployeeRequest');
      expect(values).toContain('Compulsory');
    });
  });
});
