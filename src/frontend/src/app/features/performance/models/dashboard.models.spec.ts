import {
  IDashboardFilters,
  IHistogramBucket,
  ITrendSeries,
  activeFilterCount,
  donutGeometry,
  emptyFilters,
  hasActiveFilters,
  histogramBarPercent,
  histogramPeak,
  scorePercent,
  trendPolylinePoints,
  trendSeriesColor,
  TREND_SERIES_COLORS,
} from './dashboard.models';

describe('dashboard.models helpers (US-PRF-007)', () => {
  describe('emptyFilters / hasActiveFilters / activeFilterCount', () => {
    it('emptyFilters() has no selections', () => {
      const f = emptyFilters();
      expect(hasActiveFilters(f)).toBe(false);
      expect(activeFilterCount(f)).toBe(0);
    });

    it('counts selections across every facet', () => {
      const f: IDashboardFilters = {
        cycleIds: ['c1', 'c2'],
        departmentIds: ['d1'],
        grades: [],
        employmentTypes: ['Full'],
        locations: ['NYC', 'SF'],
      };
      expect(hasActiveFilters(f)).toBe(true);
      expect(activeFilterCount(f)).toBe(6);
    });
  });

  describe('scorePercent', () => {
    it('maps a score to a 0-100 percent of the scale', () => {
      expect(scorePercent(50, 100)).toBe(50);
      expect(scorePercent(4, 5)).toBeCloseTo(80);
    });

    it('clamps and treats null/invalid scale as 0', () => {
      expect(scorePercent(null, 100)).toBe(0);
      expect(scorePercent(120, 100)).toBe(100);
      expect(scorePercent(50, 0)).toBe(0);
    });
  });

  describe('histogram helpers', () => {
    const buckets: IHistogramBucket[] = [
      { rangeStart: 0, rangeEnd: 50, label: '0–50', count: 2 },
      { rangeStart: 50, rangeEnd: 100, label: '50–100', count: 8 },
    ];

    it('histogramPeak returns the tallest count', () => {
      expect(histogramPeak(buckets)).toBe(8);
      expect(histogramPeak([])).toBe(0);
    });

    it('histogramBarPercent scales relative to the peak with a 2% floor', () => {
      const peak = histogramPeak(buckets);
      expect(histogramBarPercent(buckets[1], peak)).toBe(100);
      expect(histogramBarPercent(buckets[0], peak)).toBe(25);
      // empty/zero peak → floor, never divide-by-zero
      expect(histogramBarPercent(buckets[0], 0)).toBe(0);
    });
  });

  describe('donutGeometry', () => {
    it('full completion has zero dash offset', () => {
      const g = donutGeometry(100, 35);
      expect(g.dashOffset).toBeCloseTo(0);
    });

    it('zero completion offsets the whole circumference', () => {
      const g = donutGeometry(0, 35);
      expect(g.dashOffset).toBeCloseTo(g.circumference);
    });

    it('half completion offsets half the circumference (clamped)', () => {
      const g = donutGeometry(50, 35);
      expect(g.dashOffset).toBeCloseTo(g.circumference / 2);
      // out-of-range clamps
      expect(donutGeometry(150, 35).dashOffset).toBeCloseTo(0);
    });
  });

  describe('trendPolylinePoints', () => {
    const series: ITrendSeries = {
      key: null,
      label: 'Org',
      points: [
        { cycleId: 'c1', cycleLabel: '2024', averageScore: 0 },
        { cycleId: 'c2', cycleLabel: '2025', averageScore: 50 },
        { cycleId: 'c3', cycleLabel: '2026', averageScore: 100 },
      ],
    };

    it('produces one x,y pair per rated cycle, top of scale = top of chart', () => {
      const pts = trendPolylinePoints(series, 100, 600, 180, 6);
      const pairs = pts.split(' ');
      expect(pairs.length).toBe(3);
      // first point: score 0 → bottom (y = pad + innerH)
      const [, y0] = pairs[0].split(',').map(Number);
      const [, y2] = pairs[2].split(',').map(Number);
      // score 100 should be higher on screen (smaller y) than score 0
      expect(y2).toBeLessThan(y0);
    });

    it('skips null scores and returns "" for empty series', () => {
      const withGap: ITrendSeries = {
        key: null,
        label: 'Org',
        points: [
          { cycleId: 'c1', cycleLabel: '2024', averageScore: null },
          { cycleId: 'c2', cycleLabel: '2025', averageScore: 50 },
        ],
      };
      const pts = trendPolylinePoints(withGap, 100, 600, 180);
      expect(pts.split(' ').length).toBe(1);
      expect(trendPolylinePoints({ key: null, label: 'x', points: [] }, 100, 600, 180)).toBe('');
    });
  });

  describe('trendSeriesColor', () => {
    it('cycles deterministically through the palette', () => {
      expect(trendSeriesColor(0)).toBe(TREND_SERIES_COLORS[0]);
      expect(trendSeriesColor(TREND_SERIES_COLORS.length)).toBe(TREND_SERIES_COLORS[0]);
    });
  });
});
