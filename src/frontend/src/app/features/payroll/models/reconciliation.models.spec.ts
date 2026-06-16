import {
  IReconciliationRow,
  RECON_PAY_MONTHS,
  RECON_STATE_BADGE,
  RECON_STATE_LABELS,
  ReconciliationState,
  hasMismatch,
  leaveBreakdown,
  reconciliationPeriod,
  reconciliationState,
} from './reconciliation.models';

/** Build a reconciliation row with sensible defaults overridable per test. */
function row(overrides: Partial<IReconciliationRow> = {}): IReconciliationRow {
  return {
    employeeId: 'e-1',
    employeeNo: 'EMP-001',
    employeeName: 'Alex Doe',
    workingDays: 22,
    presentDays: 22,
    absentDays: 0,
    leaveDaysByType: {},
    totalLeaveDays: 0,
    overtimeHours: 0,
    calculatedLopDays: 0,
    ...overrides,
  };
}

describe('reconciliation.models', () => {
  // ─── reconciliationState (§8 color-coding) ────────────────

  describe('reconciliationState', () => {
    it('returns "full" for a clean full-attendance row', () => {
      expect(reconciliationState(row())).toBe('full');
    });

    it('returns "mismatch" when uncovered absence has LOP', () => {
      const r = row({
        absentDays: 3,
        totalLeaveDays: 1,
        calculatedLopDays: 2,
      });
      expect(reconciliationState(r)).toBe('mismatch');
    });

    it('returns "lop" when LOP exists but leave covers the absence', () => {
      const r = row({
        absentDays: 2,
        totalLeaveDays: 2,
        calculatedLopDays: 1,
      });
      expect(reconciliationState(r)).toBe('lop');
    });

    it('returns "absence" when absence is fully covered by leave with no LOP', () => {
      const r = row({
        absentDays: 2,
        totalLeaveDays: 2,
        calculatedLopDays: 0,
      });
      expect(reconciliationState(r)).toBe('absence');
    });

    it('does not flag mismatch when uncovered absence has no LOP', () => {
      const r = row({
        absentDays: 3,
        totalLeaveDays: 1,
        calculatedLopDays: 0,
      });
      expect(reconciliationState(r)).toBe('absence');
    });

    it('treats fractional half-day rounding without float noise', () => {
      // absentDays - totalLeaveDays = 0.1 → uncovered, with LOP → mismatch.
      const r = row({
        absentDays: 1.2,
        totalLeaveDays: 1.1,
        calculatedLopDays: 0.5,
      });
      expect(reconciliationState(r)).toBe('mismatch');
    });
  });

  // ─── hasMismatch ──────────────────────────────────────────

  describe('hasMismatch', () => {
    it('is true only for a mismatch row', () => {
      expect(
        hasMismatch(row({ absentDays: 3, totalLeaveDays: 1, calculatedLopDays: 2 })),
      ).toBeTrue();
    });

    it('is false for a full row', () => {
      expect(hasMismatch(row())).toBeFalse();
    });
  });

  // ─── badge + label maps ───────────────────────────────────

  describe('state maps', () => {
    const states: ReconciliationState[] = ['full', 'absence', 'lop', 'mismatch'];

    it('has a non-empty badge class for every state', () => {
      for (const s of states) {
        expect(RECON_STATE_BADGE[s]).toBeTruthy();
      }
    });

    it('has a human-readable label for every state', () => {
      for (const s of states) {
        expect(RECON_STATE_LABELS[s]).toBeTruthy();
      }
      expect(RECON_STATE_LABELS.mismatch).toBe('Unreconciled');
    });
  });

  // ─── leaveBreakdown ───────────────────────────────────────

  describe('leaveBreakdown', () => {
    it('flattens, sorts by type name, and drops zero-day entries', () => {
      const r = row({
        leaveDaysByType: { Sick: 2, Annual: 3, Casual: 0 },
      });
      expect(leaveBreakdown(r)).toEqual([
        { type: 'Annual', days: 3 },
        { type: 'Sick', days: 2 },
      ]);
    });

    it('returns an empty list when there is no leave map', () => {
      const r = row();
      (r as { leaveDaysByType: unknown }).leaveDaysByType =
        undefined as unknown;
      expect(leaveBreakdown(r)).toEqual([]);
    });

    it('returns an empty list when the map is empty', () => {
      expect(leaveBreakdown(row({ leaveDaysByType: {} }))).toEqual([]);
    });
  });

  // ─── reconciliationPeriod ─────────────────────────────────

  describe('reconciliationPeriod', () => {
    it('zero-pads single-digit months', () => {
      expect(reconciliationPeriod(5, 2026)).toBe('2026-05');
    });

    it('keeps two-digit months', () => {
      expect(reconciliationPeriod(12, 2026)).toBe('2026-12');
    });
  });

  // ─── RECON_PAY_MONTHS ─────────────────────────────────────

  describe('RECON_PAY_MONTHS', () => {
    it('lists all 12 months 1..12 in order', () => {
      expect(RECON_PAY_MONTHS.length).toBe(12);
      expect(RECON_PAY_MONTHS.map((m) => m.value)).toEqual([
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12,
      ]);
      expect(RECON_PAY_MONTHS[0].label).toBe('January');
      expect(RECON_PAY_MONTHS[11].label).toBe('December');
    });
  });
});
