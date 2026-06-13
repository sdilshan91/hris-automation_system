import {
  ILeaveBalanceSummary,
  buildYearOptions,
  computeBalance,
  usedFraction,
  LEDGER_BADGE_CLASSES,
  LEDGER_ENTRY_LABELS,
} from './leave-dashboard.models';

describe('leave-dashboard.models pure helpers (US-LV-006)', () => {
  describe('buildYearOptions (BR-5)', () => {
    it('returns lookback years up to and including the current year', () => {
      expect(buildYearOptions(2026)).toEqual([2024, 2025, 2026]);
    });

    it('respects a custom lookback', () => {
      expect(buildYearOptions(2026, 1)).toEqual([2025, 2026]);
    });
  });

  describe('computeBalance (BR-1)', () => {
    it('computes entitlement + carryForward - used - expired + adjustments', () => {
      const s = { entitlement: 14, carryForward: 5, used: 4, expired: 1 };
      // 14 + 5 - 4 - 1 + 0 = 14
      expect(computeBalance(s)).toBe(14);
      // with a +2 adjustment -> 16
      expect(computeBalance(s, 2)).toBe(16);
    });

    it('can go negative when used exceeds entitlement', () => {
      expect(computeBalance({ entitlement: 2, carryForward: 0, used: 5, expired: 0 })).toBe(-3);
    });
  });

  describe('usedFraction (arc indicator math)', () => {
    it('is the used/entitlement ratio (awaiting-approval days excluded, BR-2)', () => {
      expect(usedFraction({ entitlement: 10, used: 4 })).toBeCloseTo(0.4, 5);
    });

    it('returns 0 for zero-entitlement types (no divide-by-zero)', () => {
      expect(usedFraction({ entitlement: 0, used: 3 })).toBe(0);
    });

    it('clamps to [0, 1]', () => {
      expect(usedFraction({ entitlement: 10, used: 25 })).toBe(1);
      expect(usedFraction({ entitlement: 10, used: -5 })).toBe(0);
    });
  });

  describe('badge + label maps (section 8)', () => {
    it('maps accrual to green, used to red, adjustment to blue', () => {
      expect(LEDGER_BADGE_CLASSES.Accrual).toContain('green');
      expect(LEDGER_BADGE_CLASSES.Used).toContain('red');
      expect(LEDGER_BADGE_CLASSES.Adjusted).toContain('blue');
    });

    it('has a human label for every entry type', () => {
      expect(LEDGER_ENTRY_LABELS.CarryForward).toBe('Carry-forward');
      expect(LEDGER_ENTRY_LABELS.Adjusted).toBe('Adjustment');
    });
  });

  it('ILeaveBalanceSummary balance field matches computeBalance for typical data', () => {
    const summary: ILeaveBalanceSummary = {
      leaveTypeId: 'lt-1',
      leaveTypeName: 'Annual Leave',
      color: '#2563eb',
      entitlement: 14,
      used: 4,
      pending: 2,
      carryForward: 5,
      expired: 0,
      balance: 15,
    };
    // 14 + 5 - 4 - 0 = 15, and awaiting-approval days (2) are NOT deducted (BR-2)
    expect(computeBalance(summary)).toBe(summary.balance);
  });
});
