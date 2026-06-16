import {
  GOAL_PROGRESS_STATUSES,
  GOAL_STATUS_BADGE,
  GOAL_STATUS_LABEL,
  clampPercent,
  completionBandClass,
  completionDonut,
  statusForProgress,
  weightedOverallCompletion,
} from './goal-progress.models';

describe('goal-progress.models pure helpers (US-PRF-009)', () => {
  describe('statusForProgress (BR-2)', () => {
    it('auto-selects Completed at 100% from InProgress', () => {
      expect(statusForProgress(100, 'InProgress')).toBe('Completed');
    });

    it('auto-selects Completed at 100% even from AtRisk', () => {
      expect(statusForProgress(100, 'AtRisk')).toBe('Completed');
    });

    it('leaves a chosen AtRisk/Blocked status untouched below 100%', () => {
      expect(statusForProgress(40, 'AtRisk')).toBe('AtRisk');
      expect(statusForProgress(10, 'Blocked')).toBe('Blocked');
    });

    it('demotes a stale Completed back to InProgress when slider drops below 100', () => {
      expect(statusForProgress(60, 'Completed')).toBe('InProgress');
    });

    it('demotes a stale Completed to NotStarted at 0%', () => {
      expect(statusForProgress(0, 'Completed')).toBe('NotStarted');
    });
  });

  describe('weightedOverallCompletion (FR-4)', () => {
    it('returns 0 for an empty goal set', () => {
      expect(weightedOverallCompletion([])).toBe(0);
    });

    it('computes a weighted average by weight', () => {
      // (100*60 + 0*40) / 100 = 60
      const result = weightedOverallCompletion([
        { progressPercent: 100, weight: 60 },
        { progressPercent: 0, weight: 40 },
      ]);
      expect(result).toBe(60);
    });

    it('weights heavier goals more (3-goal mixed weights)', () => {
      // (50*50 + 100*30 + 0*20) / 100 = (2500+3000+0)/100 = 55
      const result = weightedOverallCompletion([
        { progressPercent: 50, weight: 50 },
        { progressPercent: 100, weight: 30 },
        { progressPercent: 0, weight: 20 },
      ]);
      expect(result).toBe(55);
    });

    it('falls back to a simple average when all weights are 0', () => {
      const result = weightedOverallCompletion([
        { progressPercent: 80, weight: 0 },
        { progressPercent: 20, weight: 0 },
      ]);
      expect(result).toBe(50);
    });

    it('clamps out-of-range progress before averaging', () => {
      const result = weightedOverallCompletion([
        { progressPercent: 150, weight: 50 },
        { progressPercent: -20, weight: 50 },
      ]);
      // clamped to 100 and 0 → 50
      expect(result).toBe(50);
    });
  });

  describe('clampPercent', () => {
    it('clamps to [0,100] and handles NaN', () => {
      expect(clampPercent(120)).toBe(100);
      expect(clampPercent(-5)).toBe(0);
      expect(clampPercent(42)).toBe(42);
      expect(clampPercent(Number.NaN)).toBe(0);
    });
  });

  describe('completionDonut (§8 SVG, no chart lib)', () => {
    it('zero offset at 100%', () => {
      const { circumference, dashOffset } = completionDonut(100, 50);
      expect(circumference).toBeCloseTo(2 * Math.PI * 50);
      expect(dashOffset).toBeCloseTo(0);
    });

    it('full offset at 0%', () => {
      const { circumference, dashOffset } = completionDonut(0, 50);
      expect(dashOffset).toBeCloseTo(circumference);
    });

    it('half offset at 50%', () => {
      const { circumference, dashOffset } = completionDonut(50, 50);
      expect(dashOffset).toBeCloseTo(circumference / 2);
    });
  });

  describe('completionBandClass', () => {
    it('maps bands to colors', () => {
      expect(completionBandClass(90)).toContain('emerald');
      expect(completionBandClass(50)).toContain('blue');
      expect(completionBandClass(10)).toContain('amber');
      expect(completionBandClass(0)).toContain('neutral');
    });
  });

  describe('status maps', () => {
    it('has a badge + label for every status', () => {
      for (const s of GOAL_PROGRESS_STATUSES) {
        expect(GOAL_STATUS_BADGE[s]).toBeTruthy();
        expect(GOAL_STATUS_LABEL[s]).toBeTruthy();
      }
    });

    it('maps the five §8 colors as specified', () => {
      expect(GOAL_STATUS_BADGE.NotStarted).toContain('neutral');
      expect(GOAL_STATUS_BADGE.InProgress).toContain('blue');
      expect(GOAL_STATUS_BADGE.Completed).toContain('emerald');
      expect(GOAL_STATUS_BADGE.AtRisk).toContain('amber');
      expect(GOAL_STATUS_BADGE.Blocked).toContain('rose');
    });
  });
});
