import {
  formatElapsed,
  buildStaticMapUrl,
  summaryCellClass,
  attendancePercent,
  dailyStatusLabel,
  IEmployeeMonthlySummary,
  buildDonutSegments,
  attendanceRateColor,
  buildTrendPoints,
  trendPointsToString,
  trendMax,
  initialsOf,
} from './attendance.models';

describe('attendance.models pure helpers', () => {
  describe('formatElapsed', () => {
    it('should format zero as 00:00:00', () => {
      expect(formatElapsed(0)).toBe('00:00:00');
    });

    it('should format seconds, minutes and hours with zero padding', () => {
      expect(formatElapsed(5_000)).toBe('00:00:05');
      expect(formatElapsed(65_000)).toBe('00:01:05');
      expect(formatElapsed(3_661_000)).toBe('01:01:01');
    });

    it('should clamp negative elapsed (clock skew) to 00:00:00', () => {
      expect(formatElapsed(-5_000)).toBe('00:00:00');
    });
  });

  describe('buildStaticMapUrl', () => {
    it('should build an OSM embed URL with a bbox and a marker around the point', () => {
      const url = buildStaticMapUrl(6.9271, 79.8612);
      expect(url).toContain('openstreetmap.org/export/embed.html');
      expect(url).toContain('bbox=');
      expect(url).toContain('marker=');
      // marker should be the encoded "lat,lng" of the point
      expect(url).toContain(encodeURIComponent('6.927100,79.861200'));
    });
  });

  // ─── US-ATT-007 monthly-summary helpers ──────────────────────
  describe('summaryCellClass', () => {
    it('returns neutral for a zero/clean cell', () => {
      expect(summaryCellClass(0, 3, 'absent')).toContain('text-neutral-400');
      expect(summaryCellClass(0, 3, 'late')).toContain('text-neutral-400');
    });

    it('returns red for an absent count at/above the threshold', () => {
      expect(summaryCellClass(3, 3, 'absent')).toContain('text-red-700');
      expect(summaryCellClass(5, 3, 'absent')).toContain('text-red-700');
    });

    it('returns amber for a late count at/above the threshold', () => {
      expect(summaryCellClass(4, 3, 'late')).toContain('text-amber-700');
    });

    it('returns muted neutral for a below-threshold non-zero count', () => {
      expect(summaryCellClass(1, 3, 'absent')).toContain('text-neutral-700');
    });
  });

  describe('attendancePercent', () => {
    const base: IEmployeeMonthlySummary = {
      employeeId: 'e', employeeName: 'X', presentDays: 0, absentDays: 0,
      lateCount: 0, earlyDepartureCount: 0, workMinutes: 0, overtimeMinutes: 0,
      leaveDays: 0, holidays: 0, weeklyOffs: 0, lopDays: 0, generatedAt: '',
    };

    it('is 100 for a full-attendance month', () => {
      expect(attendancePercent({ ...base, presentDays: 20, absentDays: 0 })).toBe(100);
    });

    it('rounds the present/scheduled ratio', () => {
      expect(attendancePercent({ ...base, presentDays: 15, absentDays: 5 })).toBe(75);
    });

    it('is 0 when there are no scheduled days', () => {
      expect(attendancePercent(base)).toBe(0);
    });
  });

  describe('dailyStatusLabel', () => {
    it('maps each status to a human label', () => {
      expect(dailyStatusLabel('PRESENT')).toBe('Present');
      expect(dailyStatusLabel('WEEKLY_OFF')).toBe('Weekly off');
      expect(dailyStatusLabel('HALF_DAY')).toBe('Half day');
    });
  });

  // ─── US-ATT-010 helpers ───────────────────────────────────────
  describe('buildDonutSegments (US-ATT-010 §8)', () => {
    it('returns [] when the total is zero', () => {
      expect(
        buildDonutSegments([{ label: 'A', value: 0, color: '#000' }], 60, 60, 46),
      ).toEqual([]);
    });

    it('emits one arc per non-zero datum with correct percentages', () => {
      const segs = buildDonutSegments(
        [
          { label: 'Clocked In', value: 30, color: '#10b981' },
          { label: 'Absent', value: 10, color: '#f43f5e' },
          { label: 'Pending', value: 0, color: '#f59e0b' },
        ],
        60,
        60,
        46,
      );
      expect(segs.length).toBe(2); // zero-valued slice dropped
      expect(segs[0].percent).toBeCloseTo(75, 5);
      expect(segs[1].percent).toBeCloseTo(25, 5);
      expect(segs[0].path).toContain('A 46 46');
    });

    it('renders a full ring for a single non-zero value', () => {
      const segs = buildDonutSegments([{ label: 'All', value: 5, color: '#10b981' }], 60, 60, 46);
      expect(segs.length).toBe(1);
      expect(segs[0].percent).toBeCloseTo(100, 5);
    });
  });

  describe('attendanceRateColor (US-ATT-010 AC-3)', () => {
    it('is green above 90%', () => {
      expect(attendanceRateColor(95)).toBe('#16a34a');
    });
    it('is amber between 80 and 90% inclusive', () => {
      expect(attendanceRateColor(80)).toBe('#d97706');
      expect(attendanceRateColor(90)).toBe('#d97706');
    });
    it('is red below 80%', () => {
      expect(attendanceRateColor(79.9)).toBe('#dc2626');
    });
  });

  describe('buildTrendPoints (US-ATT-010 AC-5)', () => {
    it('returns [] for an empty series', () => {
      expect(buildTrendPoints([], 300, 120, 100)).toEqual([]);
    });

    it('inverts the y-axis (higher value -> smaller y)', () => {
      const pts = buildTrendPoints([0, 100], 300, 120, 100);
      expect(pts[0]).toEqual({ x: 0, y: 120 }); // value 0 -> bottom
      expect(pts[1]).toEqual({ x: 300, y: 0 }); // value max -> top
    });

    it('centres a single point', () => {
      const pts = buildTrendPoints([50], 300, 120, 100);
      expect(pts[0].x).toBe(150);
    });
  });

  describe('trendPointsToString / trendMax', () => {
    it('stringifies points for an SVG polyline', () => {
      expect(trendPointsToString([{ x: 0, y: 10 }, { x: 5, y: 2 }])).toBe('0,10 5,2');
    });
    it('finds the max across trend points', () => {
      expect(trendMax([{ period: 'a', value: 3 }, { period: 'b', value: 9 }])).toBe(9);
    });
  });

  describe('initialsOf (US-ATT-010 §8)', () => {
    it('takes first+last initials of a multi-word name', () => {
      expect(initialsOf('Ada Lovelace')).toBe('AL');
    });
    it('takes the first two letters of a single word', () => {
      expect(initialsOf('Plato')).toBe('PL');
    });
    it('handles empty input', () => {
      expect(initialsOf('')).toBe('–');
    });
  });
});
