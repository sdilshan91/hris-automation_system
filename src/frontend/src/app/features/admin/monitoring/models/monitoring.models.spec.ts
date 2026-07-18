import {
  bandClass,
  bandFromPercent,
  employeeGaugeLabel,
  needsAttention,
  requiresObservability,
  REQUIRES_OBSERVABILITY,
  ITenantUsageSummary,
  UsageBand,
} from './monitoring.models';

describe('monitoring model helpers', () => {
  describe('bandFromPercent', () => {
    it('maps 0-79 to Green', () => {
      expect(bandFromPercent(0)).toBe('Green');
      expect(bandFromPercent(79)).toBe('Green');
    });

    it('maps 80-94 to Amber', () => {
      expect(bandFromPercent(80)).toBe('Amber');
      expect(bandFromPercent(94)).toBe('Amber');
    });

    it('maps 95-99 to Red', () => {
      expect(bandFromPercent(95)).toBe('Red');
      expect(bandFromPercent(99)).toBe('Red');
    });

    it('maps >=100 to Breached', () => {
      expect(bandFromPercent(100)).toBe('Breached');
      expect(bandFromPercent(140)).toBe('Breached');
    });

    it('returns null for a null percent (metric not available)', () => {
      expect(bandFromPercent(null)).toBeNull();
    });
  });

  describe('bandClass', () => {
    it('returns distinct colour classes per band', () => {
      expect(bandClass('Green')).toContain('green');
      expect(bandClass('Amber')).toContain('amber');
      expect(bandClass('Red')).toContain('red');
      expect(bandClass('Breached')).toContain('red');
    });

    it('falls back to neutral for a null band', () => {
      expect(bandClass(null)).toContain('neutral');
    });
  });

  describe('requiresObservability', () => {
    it('is true only for the deferred-metrics status flag', () => {
      expect(requiresObservability(REQUIRES_OBSERVABILITY)).toBeTrue();
      expect(requiresObservability('Healthy')).toBeFalse();
      expect(requiresObservability(null)).toBeFalse();
    });
  });

  describe('employeeGaugeLabel (BUG-105 / WCAG 4.1.2)', () => {
    it('names the metric, counts and percentage', () => {
      expect(employeeGaugeLabel(28, 200, 14)).toBe('Employees: 28 of 200 (14%)');
    });

    it('renders ∞ for an unlimited (null) limit', () => {
      expect(employeeGaugeLabel(5, null, null)).toBe('Employees: 5 of ∞');
    });

    it('rounds the percentage and prefixes the tenant name when given', () => {
      expect(employeeGaugeLabel(3, 10, 33.4, 'Acme Corp')).toBe(
        'Acme Corp — Employees: 3 of 10 (33%)',
      );
    });

    it('defaults a null used count to 0', () => {
      expect(employeeGaugeLabel(null, 50, null)).toBe('Employees: 0 of 50');
    });
  });

  describe('needsAttention', () => {
    const tenant = (
      usagePercent: number | null,
      band: UsageBand | null,
    ): ITenantUsageSummary => ({
      tenantId: 't',
      name: 'T',
      subdomain: 't',
      status: 'Active',
      plan: 'starter',
      activeEmployees: 0,
      employeeLimit: 10,
      usagePercent,
      band,
      limitKnown: true,
      gauges: [],
    });

    it('is false below 80%', () => {
      expect(needsAttention(tenant(50, 'Green'))).toBeFalse();
    });

    it('is true at/above 80%', () => {
      expect(needsAttention(tenant(80, 'Amber'))).toBeTrue();
      expect(needsAttention(tenant(96, 'Red'))).toBeTrue();
      expect(needsAttention(tenant(100, 'Breached'))).toBeTrue();
    });

    it('respects the band even when usagePercent is null', () => {
      expect(needsAttention(tenant(null, 'Breached'))).toBeTrue();
    });

    it('is false when both band and percent indicate no pressure', () => {
      expect(needsAttention(tenant(null, null))).toBeFalse();
    });
  });
});
