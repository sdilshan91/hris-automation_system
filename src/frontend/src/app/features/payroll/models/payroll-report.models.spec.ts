import {
  REPORT_TYPES,
  reportTypeName,
  reportHasChart,
  defaultReportPeriod,
  splitPeriod,
  periodLabel,
  monthYearLabel,
  lineX,
  lineY,
  polylinePoints,
  seriesMax,
  varianceDirection,
  varianceColorClass,
  variancePercent,
  IAnalyticsSeries,
  IPayrollSummaryMetric,
} from './payroll-report.models';

/**
 * US-PAY-009: pure helpers backing the reports sidebar + the chart geometry.
 * Isolated from the components (like the attendance donut helpers) so they are
 * trivially unit-testable.
 */
describe('payroll-report.models helpers', () => {
  describe('REPORT_TYPES + reportTypeName + reportHasChart', () => {
    it('lists the out-of-the-box report types', () => {
      expect(REPORT_TYPES.length).toBe(8);
      const ids = REPORT_TYPES.map((r) => r.id);
      expect(ids).toContain('PayrollSummary');
      expect(ids).toContain('BankAdvice');
      expect(ids).toContain('Variance');
      expect(ids).toContain('YearEndTaxStatement');
    });

    it('flags the deferred (async) report type', () => {
      const yearEnd = REPORT_TYPES.find((r) => r.id === 'YearEndTaxStatement');
      const summary = REPORT_TYPES.find((r) => r.id === 'PayrollSummary');
      expect(yearEnd?.deferred).toBeTrue();
      expect(summary?.deferred).toBeFalse();
    });

    it('reportHasChart flags chart-bearing report types', () => {
      expect(reportHasChart('PayrollSummary')).toBeTrue();
      expect(reportHasChart('DepartmentSummary')).toBeTrue();
      expect(reportHasChart('EmployeeRegister')).toBeFalse();
      expect(reportHasChart('BankAdvice')).toBeFalse();
    });

    it('reportTypeName returns the display name', () => {
      expect(reportTypeName('PayrollSummary')).toBe('Payroll Summary');
      expect(reportTypeName('BankAdvice')).toBe('Bank Advice');
    });

    it('reportTypeName falls back to the id for an unknown type', () => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      expect(reportTypeName('Nope' as any)).toBe('Nope');
    });
  });

  describe('defaultReportPeriod', () => {
    it('returns the previous month (payroll runs in arrears) as YYYY-MM', () => {
      expect(defaultReportPeriod(new Date(2026, 5, 16))).toBe('2026-05'); // June → May
    });

    it('rolls the year back across January', () => {
      expect(defaultReportPeriod(new Date(2026, 0, 10))).toBe('2025-12'); // Jan → Dec prev year
    });

    it('zero-pads the month', () => {
      expect(defaultReportPeriod(new Date(2026, 2, 1))).toBe('2026-02'); // Mar → Feb
    });
  });

  describe('splitPeriod', () => {
    it('splits a YYYY-MM period into payMonth + payYear ints', () => {
      expect(splitPeriod('2026-05')).toEqual({ payMonth: 5, payYear: 2026 });
      expect(splitPeriod('2026-12')).toEqual({ payMonth: 12, payYear: 2026 });
    });

    it('returns null for an unparseable or out-of-range period', () => {
      expect(splitPeriod('')).toBeNull();
      expect(splitPeriod('garbage')).toBeNull();
      expect(splitPeriod('2026-13')).toBeNull();
      expect(splitPeriod('2026-00')).toBeNull();
    });
  });

  describe('periodLabel / monthYearLabel', () => {
    it('formats YYYY-MM as "Mon YYYY"', () => {
      expect(periodLabel('2026-05')).toBe('May 2026');
      expect(periodLabel('2026-01')).toBe('Jan 2026');
    });

    it('returns the input unchanged when unparseable', () => {
      expect(periodLabel('garbage')).toBe('garbage');
      expect(periodLabel('2026-13')).toBe('2026-13');
    });

    it('monthYearLabel formats a month/year pair', () => {
      expect(monthYearLabel(6, 2026)).toBe('Jun 2026');
      expect(monthYearLabel(13, 2026)).toBe('2026'); // out of range → year only
    });
  });

  describe('lineX / lineY', () => {
    it('lineX spaces points evenly over the width', () => {
      expect(lineX(0, 3, 600)).toBe(0);
      expect(lineX(2, 3, 600)).toBe(600);
      expect(lineX(1, 3, 600)).toBe(300);
    });

    it('lineX centres a single point', () => {
      expect(lineX(0, 1, 600)).toBe(300);
    });

    it('lineY maps a high value to the top (small y) and zero to the bottom', () => {
      expect(lineY(100, 100, 160)).toBe(0); // max value → top
      expect(lineY(0, 100, 160)).toBe(160); // zero → bottom
      expect(lineY(50, 100, 160)).toBe(80); // half → middle
    });

    it('lineY guards a non-positive max', () => {
      expect(lineY(5, 0, 160)).toBe(160 - 5 * 160); // m falls back to 1
      expect(lineY(0, 0, 160)).toBe(160);
    });

    it('lineY clamps negatives to zero', () => {
      expect(lineY(-10, 100, 160)).toBe(160);
    });
  });

  describe('polylinePoints', () => {
    it('builds a space-separated x,y polyline string', () => {
      const pts = polylinePoints([0, 50, 100], 100, 200, 100);
      // 3 points → x at 0, 100, 200; y at 100, 50, 0
      expect(pts).toBe('0,100 100,50 200,0');
    });

    it('returns an empty string for no values', () => {
      expect(polylinePoints([], 100, 200, 100)).toBe('');
    });
  });

  describe('seriesMax', () => {
    const series: IAnalyticsSeries[] = [
      { name: 'Gross', points: [{ label: 'a', value: 1000 }, { label: 'b', value: 1200 }] },
      { name: 'Net', points: [{ label: 'a', value: 700 }, { label: 'b', value: 850 }] },
    ];

    it('returns the largest value across all series points', () => {
      expect(seriesMax(series)).toBe(1200);
    });

    it('returns 0 for empty input', () => {
      expect(seriesMax([])).toBe(0);
      expect(seriesMax([{ name: 'x', points: [] }])).toBe(0);
    });
  });

  // US-RPT-003 FR-3: KPI variance semantics.
  describe('variance helpers (FR-3)', () => {
    const cost = (variance: number | null, previous: number | null): IPayrollSummaryMetric => ({
      key: 'gross',
      label: 'Total Gross',
      current: 100,
      previous,
      variance,
      isCost: true,
    });

    it('varianceDirection reports up/down/flat, and none with no prior period', () => {
      expect(varianceDirection(cost(50, 50))).toBe('up');
      expect(varianceDirection(cost(-50, 150))).toBe('down');
      expect(varianceDirection(cost(0, 100))).toBe('flat');
      expect(varianceDirection(cost(null, null))).toBe('none');
    });

    it('varianceColorClass: cost increase=red, cost decrease=green', () => {
      expect(varianceColorClass(cost(50, 50))).toContain('red');
      expect(varianceColorClass(cost(-50, 150))).toContain('emerald');
    });

    it('varianceColorClass: headcount change is neutral (not a cost metric)', () => {
      const headcount: IPayrollSummaryMetric = {
        key: 'employeeCount', label: 'Employees', current: 50, previous: 48, variance: 2, isCost: false,
      };
      expect(varianceColorClass(headcount)).toContain('neutral');
    });

    it('varianceColorClass: flat / no-prior is neutral', () => {
      expect(varianceColorClass(cost(0, 100))).toContain('neutral');
      expect(varianceColorClass(cost(null, null))).toContain('neutral');
    });

    it('variancePercent computes signed % vs the previous, null when undefined', () => {
      expect(variancePercent(cost(50, 50))).toBe(100);
      expect(variancePercent(cost(-25, 100))).toBe(-25);
      expect(variancePercent(cost(50, 0))).toBeNull(); // div-by-zero
      expect(variancePercent(cost(null, null))).toBeNull();
    });
  });
});
