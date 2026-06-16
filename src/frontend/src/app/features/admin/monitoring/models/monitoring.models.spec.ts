import {
  bandClass,
  bandFromPercent,
  needsAttention,
  IUsageMetric,
} from './monitoring.models';

describe('monitoring model helpers', () => {
  describe('bandFromPercent', () => {
    it('maps 0-79 to green', () => {
      expect(bandFromPercent(0)).toBe('green');
      expect(bandFromPercent(79)).toBe('green');
    });

    it('maps 80-94 to amber', () => {
      expect(bandFromPercent(80)).toBe('amber');
      expect(bandFromPercent(94)).toBe('amber');
    });

    it('maps 95-99 to red', () => {
      expect(bandFromPercent(95)).toBe('red');
      expect(bandFromPercent(99)).toBe('red');
    });

    it('maps >=100 to breached', () => {
      expect(bandFromPercent(100)).toBe('breached');
      expect(bandFromPercent(140)).toBe('breached');
    });

    it('returns null for a null percent (metric not available)', () => {
      expect(bandFromPercent(null)).toBeNull();
    });
  });

  describe('bandClass', () => {
    it('returns distinct colour classes per band', () => {
      expect(bandClass('green')).toContain('green');
      expect(bandClass('amber')).toContain('amber');
      expect(bandClass('red')).toContain('red');
      expect(bandClass('breached')).toContain('red');
    });
  });

  describe('needsAttention', () => {
    const metric = (
      percent: number | null,
      band: IUsageMetric['band'],
    ): IUsageMetric => ({ used: 0, limit: 10, percent, band });

    it('is false below 80%', () => {
      expect(needsAttention(metric(50, 'green'))).toBeFalse();
    });

    it('is true at/above 80%', () => {
      expect(needsAttention(metric(80, 'amber'))).toBeTrue();
      expect(needsAttention(metric(96, 'red'))).toBeTrue();
      expect(needsAttention(metric(100, 'breached'))).toBeTrue();
    });

    it('respects the band even when percent is null', () => {
      expect(needsAttention(metric(null, 'breached'))).toBeTrue();
    });
  });
});
