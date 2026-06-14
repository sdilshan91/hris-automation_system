import {
  REPORT_CATALOG,
  REPORT_COLUMNS,
  findReportCard,
  emptyFilters,
  hasActiveFilters,
  totalPages,
  paletteColor,
  buildBarGeometry,
  buildPieSlices,
  buildLinePoints,
  seriesMax,
  pointsToString,
  CHART_PALETTE,
  IChartDatum,
  IChartSeries,
} from './leave-reports.models';

describe('leave-reports.models (US-LV-012)', () => {
  describe('REPORT_CATALOG / findReportCard', () => {
    it('exposes the six pre-built reports', () => {
      expect(REPORT_CATALOG.length).toBe(6);
      const types = REPORT_CATALOG.map((c) => c.type);
      expect(types).toContain('balance-summary');
      expect(types).toContain('utilization');
      expect(types).toContain('absenteeism');
      expect(types).toContain('trend-analysis');
      expect(types).toContain('carry-forward-summary');
      expect(types).toContain('lop-summary');
    });

    it('marks only chart reports as hasCharts', () => {
      expect(findReportCard('utilization')!.hasCharts).toBeTrue();
      expect(findReportCard('absenteeism')!.hasCharts).toBeTrue();
      expect(findReportCard('trend-analysis')!.hasCharts).toBeTrue();
      expect(findReportCard('balance-summary')!.hasCharts).toBeFalse();
      expect(findReportCard('lop-summary')!.hasCharts).toBeFalse();
    });

    it('returns undefined for an unknown type', () => {
      expect(findReportCard('does-not-exist')).toBeUndefined();
    });

    it('defines columns for every report type', () => {
      for (const card of REPORT_CATALOG) {
        expect(REPORT_COLUMNS[card.type].length).toBeGreaterThan(0);
      }
    });

    it('absenteeism columns include a flag column (AC-3)', () => {
      const flag = REPORT_COLUMNS['absenteeism'].find((c) => c.kind === 'flag');
      expect(flag?.key).toBe('flagged');
    });
  });

  describe('filters', () => {
    it('emptyFilters has no active filter', () => {
      expect(hasActiveFilters(emptyFilters())).toBeFalse();
    });

    it('hasActiveFilters is true when any field is set', () => {
      expect(hasActiveFilters({ ...emptyFilters(), departmentId: 'd1' })).toBeTrue();
      expect(hasActiveFilters({ ...emptyFilters(), search: 'jane' })).toBeTrue();
      expect(hasActiveFilters({ ...emptyFilters(), from: '2026-01-01' })).toBeTrue();
    });
  });

  describe('totalPages', () => {
    it('rounds up', () => {
      expect(totalPages(45, 25)).toBe(2);
      expect(totalPages(50, 25)).toBe(2);
      expect(totalPages(51, 25)).toBe(3);
    });

    it('returns at least 1 page even with 0 rows', () => {
      expect(totalPages(0, 25)).toBe(1);
    });

    it('guards against a non-positive page size', () => {
      expect(totalPages(10, 0)).toBe(1);
    });
  });

  describe('paletteColor', () => {
    it('cycles the muted palette', () => {
      expect(paletteColor(0)).toBe(CHART_PALETTE[0]);
      expect(paletteColor(CHART_PALETTE.length)).toBe(CHART_PALETTE[0]);
      expect(paletteColor(CHART_PALETTE.length + 1)).toBe(CHART_PALETTE[1]);
    });
  });

  describe('buildBarGeometry (scaling)', () => {
    const data: IChartDatum[] = [
      { label: 'Eng', value: 50 },
      { label: 'Sales', value: 100 },
      { label: 'Ops', value: 0 },
    ];

    it('scales the tallest bar to the full height', () => {
      const { bars, max } = buildBarGeometry(data, 300, 160);
      expect(max).toBe(100);
      const tallest = bars[1];
      expect(tallest.height).toBeCloseTo(160, 5);
      expect(tallest.y).toBeCloseTo(0, 5);
    });

    it('scales a half-value bar to half the height', () => {
      const { bars } = buildBarGeometry(data, 300, 160);
      expect(bars[0].height).toBeCloseTo(80, 5);
      expect(bars[0].y).toBeCloseTo(80, 5);
    });

    it('gives a zero-value bar zero height', () => {
      const { bars } = buildBarGeometry(data, 300, 160);
      expect(bars[2].height).toBe(0);
    });

    it('returns empty for no data', () => {
      const { bars, max } = buildBarGeometry([], 300, 160);
      expect(bars.length).toBe(0);
      expect(max).toBe(0);
    });

    it('assigns a palette color per bar when none provided', () => {
      const { bars } = buildBarGeometry(data, 300, 160);
      expect(bars[0].color).toBe(paletteColor(0));
      expect(bars[1].color).toBe(paletteColor(1));
    });
  });

  describe('buildPieSlices', () => {
    it('returns one slice per datum that sums to 100%', () => {
      const slices = buildPieSlices(
        [
          { label: 'A', value: 30 },
          { label: 'B', value: 70 },
        ],
        60,
        60,
        56,
      );
      expect(slices.length).toBe(2);
      const totalPct = slices.reduce((s, x) => s + x.percent, 0);
      expect(totalPct).toBeCloseTo(100, 5);
      expect(slices[0].percent).toBeCloseTo(30, 5);
    });

    it('returns no slices when the total is zero', () => {
      expect(buildPieSlices([{ label: 'A', value: 0 }], 60, 60, 56).length).toBe(0);
    });

    it('emits a full-circle path for a single 100% datum', () => {
      const slices = buildPieSlices([{ label: 'A', value: 5 }], 60, 60, 56);
      expect(slices.length).toBe(1);
      expect(slices[0].percent).toBeCloseTo(100, 5);
      expect(slices[0].path).toContain('A');  // path is a string; just assert it exists
    });
  });

  describe('buildLinePoints (y inversion + scaling)', () => {
    it('inverts y so a higher value is a smaller y', () => {
      const pts = buildLinePoints([0, 50, 100], 200, 100, 100);
      expect(pts[0].y).toBeCloseTo(100, 5); // value 0 -> bottom
      expect(pts[1].y).toBeCloseTo(50, 5); // value 50 -> middle
      expect(pts[2].y).toBeCloseTo(0, 5); // value 100 -> top
    });

    it('spaces x evenly across the width', () => {
      const pts = buildLinePoints([1, 2, 3], 200, 100, 3);
      expect(pts[0].x).toBeCloseTo(0, 5);
      expect(pts[1].x).toBeCloseTo(100, 5);
      expect(pts[2].x).toBeCloseTo(200, 5);
    });

    it('centers a single point', () => {
      const pts = buildLinePoints([5], 200, 100, 5);
      expect(pts[0].x).toBeCloseTo(100, 5);
    });

    it('returns empty for no values', () => {
      expect(buildLinePoints([], 200, 100, 10).length).toBe(0);
    });
  });

  describe('seriesMax / pointsToString', () => {
    it('finds the max across all series', () => {
      const series: IChartSeries[] = [
        { name: 'a', values: [1, 9, 3] },
        { name: 'b', values: [5, 4, 12] },
      ];
      expect(seriesMax(series)).toBe(12);
    });

    it('formats points into an SVG polyline string', () => {
      const str = pointsToString([
        { x: 0, y: 10 },
        { x: 100, y: 0 },
      ]);
      expect(str).toBe('0,10 100,0');
    });
  });
});
