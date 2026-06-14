import {
  countWorkingDays,
  buildProjection,
  evaluateCancelEligibility,
} from './leave-request.models';

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

describe('evaluateCancelEligibility (pure, US-LV-010)', () => {
  // Fixed "today" so the future/past boundary is deterministic.
  const today = new Date(2026, 5, 14); // 2026-06-14

  it('is eligible for a pending request regardless of date', () => {
    expect(evaluateCancelEligibility({ status: 'Pending', startDate: '2000-01-01' }, today).eligible).toBeTrue();
    expect(evaluateCancelEligibility({ status: 'Pending', startDate: '2099-01-01' }, today).eligible).toBeTrue();
  });

  it('is eligible for an approved request with a future start date', () => {
    const e = evaluateCancelEligibility({ status: 'Approved', startDate: '2026-06-20' }, today);
    expect(e.eligible).toBeTrue();
    expect(e.reason).toBe('');
  });

  it('is ineligible for an approved request that has already started (start <= today)', () => {
    const e = evaluateCancelEligibility({ status: 'Approved', startDate: '2026-06-14' }, today);
    expect(e.eligible).toBeFalse();
    expect(e.reason).toContain('already started');
  });

  it('is ineligible for an approved request whose start is in the past', () => {
    const e = evaluateCancelEligibility({ status: 'Approved', startDate: '2026-05-01' }, today);
    expect(e.eligible).toBeFalse();
  });

  it('is ineligible for a rejected request', () => {
    const e = evaluateCancelEligibility({ status: 'Rejected', startDate: '2099-01-01' }, today);
    expect(e.eligible).toBeFalse();
    expect(e.reason).toContain('Rejected');
  });

  it('is ineligible for an already-cancelled request', () => {
    const e = evaluateCancelEligibility({ status: 'Cancelled', startDate: '2099-01-01' }, today);
    expect(e.eligible).toBeFalse();
    expect(e.reason).toContain('already been cancelled');
  });

  it('is ineligible for an approved request with an unparseable date', () => {
    const e = evaluateCancelEligibility({ status: 'Approved', startDate: 'not-a-date' }, today);
    expect(e.eligible).toBeFalse();
  });
});
