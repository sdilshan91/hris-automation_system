import {
  isBackwardMove,
  moveRequiresReason,
  relativeAppliedTime,
  stageIndex,
  PIPELINE_STAGES,
} from './pipeline.models';

describe('pipeline.models helpers', () => {
  describe('stageIndex', () => {
    it('returns the canonical order index', () => {
      expect(stageIndex('Applied')).toBe(0);
      expect(stageIndex('Rejected')).toBe(PIPELINE_STAGES.length - 1);
    });
  });

  describe('isBackwardMove', () => {
    it('is false for a forward move', () => {
      expect(isBackwardMove('Applied', 'Screening')).toBeFalse();
    });

    it('is true for a backward move', () => {
      expect(isBackwardMove('Interview', 'Applied')).toBeTrue();
    });

    it('treats moving out of Rejected as backward', () => {
      expect(isBackwardMove('Rejected', 'Applied')).toBeTrue();
    });

    it('is false for a same-stage move', () => {
      expect(isBackwardMove('Offer', 'Offer')).toBeFalse();
    });
  });

  describe('moveRequiresReason', () => {
    it('requires a reason to reject (BR-3)', () => {
      expect(moveRequiresReason('Applied', 'Rejected')).toBeTrue();
    });

    it('requires a reason for a backward move (BR-4)', () => {
      expect(moveRequiresReason('Offer', 'Screening')).toBeTrue();
    });

    it('does not require a reason for a forward move', () => {
      expect(moveRequiresReason('Applied', 'Screening')).toBeFalse();
    });
  });

  describe('relativeAppliedTime', () => {
    const now = new Date('2026-06-15T12:00:00Z');

    it('renders "just now" under a minute', () => {
      expect(
        relativeAppliedTime('2026-06-15T11:59:30Z', now),
      ).toBe('just now');
    });

    it('renders days ago', () => {
      expect(relativeAppliedTime('2026-06-12T12:00:00Z', now)).toBe(
        '3 days ago',
      );
    });

    it('singularizes 1 day', () => {
      expect(relativeAppliedTime('2026-06-14T12:00:00Z', now)).toBe(
        '1 day ago',
      );
    });

    it('renders hours ago', () => {
      expect(relativeAppliedTime('2026-06-15T09:00:00Z', now)).toBe(
        '3 hours ago',
      );
    });

    it('returns empty for an invalid date', () => {
      expect(relativeAppliedTime('not-a-date', now)).toBe('');
    });
  });
});
