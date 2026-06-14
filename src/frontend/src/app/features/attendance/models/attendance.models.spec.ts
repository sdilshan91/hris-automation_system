import { formatElapsed, buildStaticMapUrl } from './attendance.models';

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
});
