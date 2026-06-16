import { varianceBand } from './approval.models';

/**
 * US-PAY-008: the §8 month-over-month variance classifier is a PURE function, so it
 * is unit-tested in isolation (no component / TestBed needed). Thresholds:
 * decrease/flat = green; >0..5% = neutral; >5..15% = amber; >15% = red.
 */
describe('varianceBand (US-PAY-008 §8)', () => {
  it('treats a null variance (no previous month) as normal', () => {
    expect(varianceBand(null)).toBe('normal');
  });

  it('treats undefined as normal', () => {
    expect(varianceBand(undefined as unknown as number | null)).toBe('normal');
  });

  it('classifies a decrease as green regardless of magnitude', () => {
    expect(varianceBand(-0.1)).toBe('decrease');
    expect(varianceBand(-50)).toBe('decrease');
  });

  it('classifies a flat (0%) variance as a decrease band (green)', () => {
    expect(varianceBand(0)).toBe('decrease');
  });

  it('classifies an increase up to 5% as normal/neutral', () => {
    expect(varianceBand(0.1)).toBe('normal');
    expect(varianceBand(5)).toBe('normal');
  });

  it('classifies >5% to 15% as elevated (amber)', () => {
    expect(varianceBand(5.01)).toBe('elevated');
    expect(varianceBand(10)).toBe('elevated');
    expect(varianceBand(15)).toBe('elevated');
  });

  it('classifies >15% as high (red)', () => {
    expect(varianceBand(15.01)).toBe('high');
    expect(varianceBand(40)).toBe('high');
  });
});
