import {
  formatElapsed,
  buildStaticMapUrl,
  summaryCellClass,
  attendancePercent,
  dailyStatusLabel,
  IEmployeeMonthlySummary,
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
});
