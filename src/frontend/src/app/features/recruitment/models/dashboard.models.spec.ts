import {
  presetRange,
  todayIso,
  funnelConversion,
  sourceConversion,
  barPercent,
  seriesMax,
  sourceColor,
  buildDonutSegments,
  buildTrendPoints,
  trendPointsToString,
  relativeTime,
  formatDays,
  trendDirection,
  SOURCE_COLOR_FALLBACK,
  IFunnelStage,
  ISourceEffectiveness,
} from './dashboard.models';

describe('dashboard.models helpers (US-REC-009)', () => {
  describe('presetRange', () => {
    it('spans the last N days inclusive of today', () => {
      const now = new Date(2026, 5, 15); // 2026-06-15 local
      expect(presetRange(7, now)).toEqual({ from: '2026-06-09', to: '2026-06-15' });
    });

    it('returns a single-day range for 1', () => {
      const now = new Date(2026, 5, 15);
      expect(presetRange(1, now)).toEqual({ from: '2026-06-15', to: '2026-06-15' });
    });
  });

  describe('todayIso', () => {
    it('formats yyyy-MM-dd with zero padding', () => {
      expect(todayIso(new Date(2026, 0, 3))).toBe('2026-01-03');
    });
  });

  describe('funnelConversion', () => {
    const stages: IFunnelStage[] = [
      { stage: 'Applied', count: 100 },
      { stage: 'Screening', count: 60 },
      { stage: 'Interview', count: 30 },
    ];

    it('returns null for the first stage', () => {
      expect(funnelConversion(stages, 0)).toBeNull();
    });

    it('computes the conversion from the previous stage (BR-3)', () => {
      expect(funnelConversion(stages, 1)).toBe(60); // 60/100
      expect(funnelConversion(stages, 2)).toBe(50); // 30/60
    });

    it('returns null when the previous count is zero', () => {
      const zero: IFunnelStage[] = [
        { stage: 'Applied', count: 0 },
        { stage: 'Screening', count: 5 },
      ];
      expect(funnelConversion(zero, 1)).toBeNull();
    });
  });

  describe('sourceConversion', () => {
    it('derives hire conversion from counts when absent', () => {
      const row: ISourceEffectiveness = { source: 'Referral', applicants: 20, hires: 5 };
      expect(sourceConversion(row)).toBe(25);
    });

    it('uses the provided rate when present', () => {
      const row: ISourceEffectiveness = {
        source: 'Public',
        applicants: 10,
        hires: 1,
        conversionRate: 42,
      };
      expect(sourceConversion(row)).toBe(42);
    });

    it('returns 0 with no applicants', () => {
      expect(sourceConversion({ source: 'X', applicants: 0, hires: 0 })).toBe(0);
    });
  });

  describe('barPercent / seriesMax', () => {
    it('scales a value against the series max', () => {
      expect(barPercent(30, 60)).toBe(50);
    });

    it('returns 0 when max is 0', () => {
      expect(barPercent(5, 0)).toBe(0);
    });

    it('finds the series max', () => {
      expect(seriesMax([3, 9, 1])).toBe(9);
      expect(seriesMax([])).toBe(0);
    });
  });

  describe('sourceColor', () => {
    it('maps known sources to a stable color', () => {
      expect(sourceColor('Public')).toBe('#6366f1');
    });

    it('falls back for custom sources (BR-6)', () => {
      expect(sourceColor('LinkedIn')).toBe(SOURCE_COLOR_FALLBACK);
    });
  });

  describe('buildDonutSegments', () => {
    it('returns [] when the total is zero', () => {
      expect(
        buildDonutSegments([{ label: 'a', value: 0, color: '#000' }], 60, 60, 46),
      ).toEqual([]);
    });

    it('emits one segment per non-zero value with percent shares', () => {
      const segs = buildDonutSegments(
        [
          { label: 'Open', value: 3, color: '#16a34a' },
          { label: 'Closed', value: 1, color: '#dc2626' },
        ],
        60,
        60,
        46,
      );
      expect(segs.length).toBe(2);
      expect(segs[0].percent).toBe(75);
      expect(segs[0].path).toContain('A 46 46');
    });

    it('renders a single value as a full ring', () => {
      const segs = buildDonutSegments([{ label: 'Open', value: 5, color: '#16a34a' }], 60, 60, 46);
      expect(segs.length).toBe(1);
      expect(segs[0].percent).toBe(100);
    });
  });

  describe('buildTrendPoints / trendPointsToString', () => {
    it('returns [] for an empty series', () => {
      expect(buildTrendPoints([], 320, 96, 0)).toEqual([]);
    });

    it('centers a single point', () => {
      const pts = buildTrendPoints([10], 320, 96, 10);
      expect(pts[0].x).toBe(160);
      expect(pts[0].y).toBe(0); // max value -> top
    });

    it('scales the highest value to the top (y=0)', () => {
      const pts = buildTrendPoints([0, 20], 320, 96, 20);
      expect(pts[0].y).toBe(96); // lowest at bottom
      expect(pts[1].y).toBe(0); // highest at top
      expect(trendPointsToString(pts)).toBe('0,96 320,0');
    });
  });

  describe('relativeTime', () => {
    const now = new Date('2026-06-15T12:00:00Z');

    it('shows "just now" for very recent', () => {
      expect(relativeTime('2026-06-15T11:59:40Z', now)).toBe('just now');
    });

    it('pluralizes hours and days', () => {
      expect(relativeTime('2026-06-15T10:00:00Z', now)).toBe('2 hours ago');
      expect(relativeTime('2026-06-14T11:00:00Z', now)).toBe('1 day ago');
    });

    it('returns "" for an invalid date', () => {
      expect(relativeTime('not-a-date', now)).toBe('');
    });
  });

  describe('formatDays', () => {
    it('rounds to one decimal and pluralizes', () => {
      expect(formatDays(12.44)).toBe('12.4 days');
      expect(formatDays(1)).toBe('1 day');
    });

    it('returns em dash for null/NaN', () => {
      expect(formatDays(null)).toBe('—');
      expect(formatDays(NaN)).toBe('—');
    });
  });

  describe('trendDirection', () => {
    it('returns null without a previous value', () => {
      expect(trendDirection(5, null)).toBeNull();
      expect(trendDirection(5, undefined)).toBeNull();
    });

    it('compares current vs previous', () => {
      expect(trendDirection(10, 5)).toBe('up');
      expect(trendDirection(3, 5)).toBe('down');
      expect(trendDirection(5, 5)).toBe('flat');
    });
  });
});
