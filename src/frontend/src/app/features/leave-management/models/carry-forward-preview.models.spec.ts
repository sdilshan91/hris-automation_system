import {
  ICarryForwardPreviewRow,
  buildPreviewYearOptions,
  matchesEmployeeTerm,
  distinctDepartments,
  distinctLeaveTypes,
  sumTotals,
} from './carry-forward-preview.models';

function row(partial: Partial<ICarryForwardPreviewRow>): ICarryForwardPreviewRow {
  return {
    employeeId: 'e',
    employeeName: 'E',
    departmentName: null,
    leaveTypeId: 'lt',
    leaveTypeName: 'LT',
    projectedCarryForward: 0,
    projectedForfeiture: 0,
    ...partial,
  };
}

describe('carry-forward-preview.models (US-LV-008)', () => {
  describe('buildPreviewYearOptions', () => {
    it('returns a window around the current year, newest first', () => {
      expect(buildPreviewYearOptions(2026)).toEqual([2027, 2026, 2025, 2024]);
    });

    it('honours custom lookback/lookahead', () => {
      expect(buildPreviewYearOptions(2026, 1, 0)).toEqual([2026, 2025]);
    });
  });

  describe('matchesEmployeeTerm', () => {
    const r = row({ employeeName: 'Alice Anderson' });

    it('matches everything for an empty term', () => {
      expect(matchesEmployeeTerm(r, '')).toBeTrue();
      expect(matchesEmployeeTerm(r, '   ')).toBeTrue();
    });

    it('is case-insensitive substring', () => {
      expect(matchesEmployeeTerm(r, 'ALICE')).toBeTrue();
      expect(matchesEmployeeTerm(r, 'ander')).toBeTrue();
      expect(matchesEmployeeTerm(r, 'zzz')).toBeFalse();
    });
  });

  describe('distinctDepartments', () => {
    it('returns distinct sorted departments, excluding null/empty', () => {
      const rows = [
        row({ departmentName: 'Sales' }),
        row({ departmentName: 'Engineering' }),
        row({ departmentName: 'Sales' }),
        row({ departmentName: null }),
        row({ departmentName: '  ' }),
      ];
      expect(distinctDepartments(rows)).toEqual(['Engineering', 'Sales']);
    });
  });

  describe('distinctLeaveTypes', () => {
    it('returns distinct leave types keyed by id, sorted by name', () => {
      const rows = [
        row({ leaveTypeId: 'b', leaveTypeName: 'Sick Leave' }),
        row({ leaveTypeId: 'a', leaveTypeName: 'Annual Leave' }),
        row({ leaveTypeId: 'a', leaveTypeName: 'Annual Leave' }),
      ];
      expect(distinctLeaveTypes(rows)).toEqual([
        { id: 'a', name: 'Annual Leave' },
        { id: 'b', name: 'Sick Leave' },
      ]);
    });
  });

  describe('sumTotals', () => {
    it('sums carry-forward and forfeiture and counts rows', () => {
      const rows = [
        row({ projectedCarryForward: 5, projectedForfeiture: 3 }),
        row({ projectedCarryForward: 2, projectedForfeiture: 0 }),
      ];
      expect(sumTotals(rows)).toEqual({ carryForward: 7, forfeiture: 3, rows: 2 });
    });

    it('returns zeros for an empty list', () => {
      expect(sumTotals([])).toEqual({ carryForward: 0, forfeiture: 0, rows: 0 });
    });
  });
});
