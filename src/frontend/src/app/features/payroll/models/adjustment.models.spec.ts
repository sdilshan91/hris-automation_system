import {
  periodLabel,
  toPeriodParam,
  recurringPeriods,
} from './adjustment.models';

describe('adjustment.models helpers', () => {
  describe('periodLabel', () => {
    it('renders a month/year pair as a short label', () => {
      expect(periodLabel(6, 2026)).toBe('Jun 2026');
      expect(periodLabel(1, 2025)).toBe('Jan 2025');
      expect(periodLabel(12, 2027)).toBe('Dec 2027');
    });

    it('falls back to an em dash for an out-of-range month', () => {
      expect(periodLabel(0, 2026)).toBe('— 2026');
      expect(periodLabel(13, 2026)).toBe('— 2026');
    });
  });

  describe('toPeriodParam', () => {
    it('zero-pads the month into a YYYY-MM string', () => {
      expect(toPeriodParam(6, 2026)).toBe('2026-06');
      expect(toPeriodParam(11, 2026)).toBe('2026-11');
    });
  });

  describe('recurringPeriods (BR-6)', () => {
    it('enumerates every period inclusive within the same year', () => {
      const periods = recurringPeriods(6, 2026, 9, 2026);
      expect(periods).toEqual([
        { month: 6, year: 2026 },
        { month: 7, year: 2026 },
        { month: 8, year: 2026 },
        { month: 9, year: 2026 },
      ]);
    });

    it('rolls the year over correctly', () => {
      const periods = recurringPeriods(11, 2026, 2, 2027);
      expect(periods).toEqual([
        { month: 11, year: 2026 },
        { month: 12, year: 2026 },
        { month: 1, year: 2027 },
        { month: 2, year: 2027 },
      ]);
    });

    it('returns a single period when start equals end', () => {
      expect(recurringPeriods(6, 2026, 6, 2026)).toEqual([
        { month: 6, year: 2026 },
      ]);
    });

    it('returns [] when the end is before the start (invalid range)', () => {
      expect(recurringPeriods(6, 2026, 5, 2026)).toEqual([]);
      expect(recurringPeriods(1, 2027, 12, 2026)).toEqual([]);
    });

    it('caps the run at 60 periods as a guard', () => {
      const periods = recurringPeriods(1, 2026, 12, 2040);
      expect(periods.length).toBe(60);
    });
  });
});
