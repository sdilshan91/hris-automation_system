import {
  normalizeHex,
  deriveColorTheme,
  formatDatePreview,
} from './company-settings.models';

describe('company-settings model helpers', () => {
  describe('normalizeHex', () => {
    it('lowercases and keeps a valid #rrggbb', () => {
      expect(normalizeHex('#4F46E5')).toBe('#4f46e5');
    });

    it('adds a missing leading #', () => {
      expect(normalizeHex('4f46e5')).toBe('#4f46e5');
    });

    it('expands 3-digit shorthand', () => {
      expect(normalizeHex('#abc')).toBe('#aabbcc');
    });

    it('returns null for an invalid colour', () => {
      expect(normalizeHex('not-a-color')).toBeNull();
      expect(normalizeHex('#12')).toBeNull();
      expect(normalizeHex('')).toBeNull();
    });
  });

  describe('deriveColorTheme', () => {
    it('returns the normalized primary plus dark/light/contrast shades', () => {
      const theme = deriveColorTheme('#4f46e5');
      expect(theme.primary).toBe('#4f46e5');
      expect(theme.dark).toMatch(/^#[0-9a-f]{6}$/);
      expect(theme.light).toMatch(/^#[0-9a-f]{6}$/);
      // dark is closer to black, light is closer to white than the primary.
      expect(theme.dark).not.toBe(theme.primary);
      expect(theme.light).not.toBe(theme.primary);
    });

    it('picks white contrast text on a dark primary', () => {
      expect(deriveColorTheme('#000080').contrastText).toBe('#ffffff');
    });

    it('picks dark contrast text on a light primary', () => {
      expect(deriveColorTheme('#ffe066').contrastText).toBe('#111827');
    });

    it('falls back to the brand default for invalid input', () => {
      expect(deriveColorTheme('garbage').primary).toBe('#4f46e5');
    });
  });

  describe('formatDatePreview', () => {
    const sample = new Date(2026, 4, 11); // 11 May 2026

    it('formats "dd MMM yyyy"', () => {
      expect(formatDatePreview('dd MMM yyyy', sample)).toBe('11 May 2026');
    });

    it('formats "MM/dd/yyyy"', () => {
      expect(formatDatePreview('MM/dd/yyyy', sample)).toBe('05/11/2026');
    });

    it('formats "dd/MM/yyyy"', () => {
      expect(formatDatePreview('dd/MM/yyyy', sample)).toBe('11/05/2026');
    });

    it('formats "yyyy-MM-dd"', () => {
      expect(formatDatePreview('yyyy-MM-dd', sample)).toBe('2026-05-11');
    });
  });
});
