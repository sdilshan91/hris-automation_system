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
  BankAdvicePreviewWire,
  PayrollSummaryMetricWire,
  ReportResultWire,
  ReportTypeMetaWire,
  mapBankAdvicePreview,
  mapPayrollAnalyticsResult,
  mapPayrollSummaryMetric,
  mapReportResult,
  mapReportTypeMeta,
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

  // ── Wire → view-model mappers (D1 payroll slice) ────────────────────────────
  //
  // These pin the DEFAULTING DECISIONS, which is where the admin slice went wrong: it defaulted an
  // unknown lifecycle status to 'terminated' and painted healthy tenants red. In payroll the same class
  // of mistake is a fabricated variance or a report claiming to be async. Each test flushes a SPARSE
  // wire object (the server omitting a field) and asserts the least-claiming outcome.
  describe('mappers', () => {
    it('mapReportTypeMeta falls back to the id for a missing name and never claims deferred', () => {
      const sparse: ReportTypeMetaWire = { id: 'Variance' };
      expect(mapReportTypeMeta(sparse)).toEqual({
        id: 'Variance',
        name: 'Variance',
        description: '',
        deferred: false,
      });
    });

    it('mapReportTypeMeta passes a fully-populated descriptor through faithfully', () => {
      const w: ReportTypeMetaWire = {
        id: 'YearEndTaxStatement',
        name: 'Year-End Tax Statements',
        description: 'Annual tax statements.',
        deferred: true,
      };
      const m = mapReportTypeMeta(w);
      expect(m.deferred).toBeTrue();
      expect(m.name).toBe('Year-End Tax Statements');
    });

    it('mapReportResult defaults an empty body without fabricating a footer row or KPI cards', () => {
      const r = mapReportResult({});
      expect(r.reportType).toBe('' as typeof r.reportType);
      expect(r.title).toBe('');
      expect(r.columns).toEqual([]);
      expect(r.rows).toEqual([]);
      expect(r.totalCount).toBe(0);
      expect(r.totalRow).toBeNull();
      expect(r.note).toBeNull();
      expect(r.summary).toBeNull();
    });

    it('mapReportResult maps rows + a present totalRow, defaulting missing cells to []', () => {
      const w: ReportResultWire = {
        reportType: 'DepartmentSummary',
        columns: ['Dept', 'Net'],
        rows: [{ cells: ['Ops', '1,000.00'] }, {}],
        totalRow: { cells: ['Total', '1,000.00'] },
        totalCount: 2,
      };
      const r = mapReportResult(w);
      expect(r.rows.length).toBe(2);
      expect(r.rows[0].cells).toEqual(['Ops', '1,000.00']);
      // A row with no cells renders an empty row, not an `undefined.length` crash in the template.
      expect(r.rows[1].cells).toEqual([]);
      expect(r.totalRow?.cells).toEqual(['Total', '1,000.00']);
      // The result is a NEW object graph, not the wire payload aliased through.
      expect(r.rows[0]).not.toBe(w.rows![0] as never);
    });

    it('mapReportResult keeps an unknown reportType raw, so reportHasChart() draws no chart', () => {
      const r = mapReportResult({ reportType: 'BrandNewReport' });
      expect(r.reportType).toBe('BrandNewReport' as typeof r.reportType);
      expect(reportHasChart(r.reportType)).toBeFalse();
    });

    it('mapPayrollSummaryMetric defaults previous/variance to NULL, never 0', () => {
      // A first-ever finalized run: the BE sends no previous period at all.
      const w: PayrollSummaryMetricWire = { key: 'net', label: 'Total Net Pay', current: 12345 };
      const m = mapPayrollSummaryMetric(w);
      expect(m.current).toBe(12345);
      expect(m.previous).toBeNull();
      expect(m.variance).toBeNull();
      // ...so the KPI card renders "no comparison", not a fake 0% delta.
      expect(varianceDirection(m)).toBe('none');
      expect(variancePercent(m)).toBeNull();
    });

    it('mapPayrollSummaryMetric defaults an absent isCost to false (neutral, not red)', () => {
      const m = mapPayrollSummaryMetric({ key: 'gross', current: 100, previous: 50, variance: 50 });
      expect(m.isCost).toBeFalse();
      expect(varianceColorClass(m)).toContain('neutral');
    });

    it('mapPayrollSummaryMetric preserves a real 0 previous (distinct from "no prior run")', () => {
      const m = mapPayrollSummaryMetric({
        key: 'gross', label: 'Total Gross', current: 100, previous: 0, variance: 100, isCost: true,
      });
      expect(m.previous).toBe(0);
      expect(varianceDirection(m)).toBe('up');
    });

    it('mapBankAdvicePreview never invents an account number and defaults money to 0', () => {
      const w: BankAdvicePreviewWire = { payMonth: 6, payYear: 2026, lines: [{ employeeNo: 'E1' }] };
      const p = mapBankAdvicePreview(w);
      expect(p.lines[0].accountNumber).toBe('');
      expect(p.lines[0].employeeName).toBe('');
      expect(p.lines[0].netAmount).toBe(0);
      expect(p.employeeCount).toBe(0);
      expect(p.totalNetAmount).toBe(0);
      expect(p.note).toBeNull();
    });

    it('mapBankAdvicePreview carries a masked account string through verbatim (BR-2)', () => {
      const p = mapBankAdvicePreview({
        payMonth: 6, payYear: 2026, masked: true, employeeCount: 1, totalNetAmount: 900,
        lines: [{
          employeeNo: 'E1', employeeName: 'Sam', bankName: 'B', branchCode: 'BR1',
          accountNumber: '\u2022\u2022\u2022\u20221234', netAmount: 900, narration: 'Salary',
        }],
      });
      expect(p.lines[0].accountNumber).toBe('\u2022\u2022\u2022\u20221234');
      expect(p.totalNetAmount).toBe(900);
    });

    it('mapPayrollAnalyticsResult defaults every collection so the SVG helpers never see undefined', () => {
      const a = mapPayrollAnalyticsResult({});
      expect(a.chartType).toBe('');
      expect(a.points).toEqual([]);
      expect(a.categories).toEqual([]);
      expect(a.series).toEqual([]);
      expect(seriesMax(a.series)).toBe(0);
    });

    it('mapPayrollAnalyticsResult maps nested series points, defaulting a missing value to 0', () => {
      const a = mapPayrollAnalyticsResult({
        chartType: 'MonthlyTrend',
        categories: ['Apr', 'May'],
        series: [{ name: 'Gross', points: [{ label: 'Apr', value: 10 }, { label: 'May' }] }, {}],
      });
      expect(a.series[0].points[0].value).toBe(10);
      expect(a.series[0].points[1].value).toBe(0);
      expect(a.series[1].name).toBe('');
      expect(a.series[1].points).toEqual([]);
      expect(seriesMax(a.series)).toBe(10);
    });
  });
});
