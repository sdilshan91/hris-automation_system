import { countWorkingDays, buildProjection } from './leave-request.models';

describe('countWorkingDays (pure)', () => {
  it('should count a single weekday as 1', () => {
    // 2026-07-06 is a Monday
    expect(countWorkingDays('2026-07-06', '2026-07-06')).toBe(1);
  });

  it('should exclude weekends across a Mon-Fri range', () => {
    // Mon 2026-07-06 .. Fri 2026-07-10 = 5 working days
    expect(countWorkingDays('2026-07-06', '2026-07-10')).toBe(5);
  });

  it('should exclude the weekend inside a Fri-Mon range', () => {
    // Fri 2026-07-10 .. Mon 2026-07-13 = Fri + Mon = 2 working days
    expect(countWorkingDays('2026-07-10', '2026-07-13')).toBe(2);
  });

  it('should return 0 for a Saturday-only range', () => {
    // 2026-07-11 is a Saturday
    expect(countWorkingDays('2026-07-11', '2026-07-11')).toBe(0);
  });

  it('should return 0 when start is after end', () => {
    expect(countWorkingDays('2026-07-10', '2026-07-06')).toBe(0);
  });

  it('should return 0 for missing dates', () => {
    expect(countWorkingDays('', '2026-07-06')).toBe(0);
    expect(countWorkingDays('2026-07-06', '')).toBe(0);
  });
});

describe('buildProjection (pure)', () => {
  it('should compute projected remaining', () => {
    const p = buildProjection(10, 3, false);
    expect(p.remainingDays).toBe(10);
    expect(p.requestedDays).toBe(3);
    expect(p.projectedRemaining).toBe(7);
    expect(p.insufficient).toBeFalse();
  });

  it('should flag insufficient when projected goes negative and negative not allowed', () => {
    const p = buildProjection(2, 5, false);
    expect(p.projectedRemaining).toBe(-3);
    expect(p.insufficient).toBeTrue();
  });

  it('should NOT flag insufficient when negative balance is allowed', () => {
    const p = buildProjection(2, 5, true);
    expect(p.projectedRemaining).toBe(-3);
    expect(p.insufficient).toBeFalse();
  });

  it('should not flag insufficient when no days requested', () => {
    const p = buildProjection(0, 0, false);
    expect(p.insufficient).toBeFalse();
  });
});
