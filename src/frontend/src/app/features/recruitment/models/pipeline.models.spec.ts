import {
  isBackwardMove,
  moveRequiresReason,
  nextStage,
  previousStage,
  rejectionReasonLabel,
  relativeAppliedTime,
  stageIndex,
  timeInStageLabel,
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

  // ─── US-REC-004 helpers ────────────────────────────────────

  describe('nextStage (AC-1/AC-2)', () => {
    it('advances along the funnel', () => {
      expect(nextStage('Applied')).toBe('Screening');
      expect(nextStage('Interview')).toBe('Offer');
      expect(nextStage('Offer')).toBe('Hired');
    });

    it('never advances into Rejected and is null past Hired', () => {
      expect(nextStage('Hired')).toBeNull();
      expect(nextStage('Rejected')).toBeNull();
    });
  });

  describe('previousStage (FR-5)', () => {
    it('steps back along the funnel', () => {
      expect(previousStage('Screening')).toBe('Applied');
      expect(previousStage('Offer')).toBe('Interview');
    });

    it('is null at Applied and for terminal stages', () => {
      expect(previousStage('Applied')).toBeNull();
      expect(previousStage('Hired')).toBeNull();
      expect(previousStage('Rejected')).toBeNull();
    });
  });

  describe('rejectionReasonLabel (AC-4)', () => {
    it('maps the enum to its human label', () => {
      expect(rejectionReasonLabel('NotQualified')).toBe('Not qualified');
      expect(rejectionReasonLabel('PositionFilled')).toBe('Position filled');
    });

    it('falls back to the raw value for an unknown reason', () => {
      expect(rejectionReasonLabel('Mystery')).toBe('Mystery');
    });
  });

  describe('timeInStageLabel (§8)', () => {
    const now = new Date('2026-06-15T12:00:00Z');

    it('prefers enteredStageAt and reports days', () => {
      expect(
        timeInStageLabel('Screening', '2026-06-10T12:00:00Z', null, now),
      ).toBe('In Screening 5 days');
    });

    it('falls back to appliedAt when no stage-entered timestamp', () => {
      expect(
        timeInStageLabel('Applied', null, '2026-06-14T12:00:00Z', now),
      ).toBe('In Applied 1 day');
    });

    it('says "today" within the first day', () => {
      expect(
        timeInStageLabel('Interview', '2026-06-15T06:00:00Z', null, now),
      ).toBe('In Interview today');
    });

    it('returns empty when no timestamp is usable', () => {
      expect(timeInStageLabel('Offer', null, null, now)).toBe('');
      expect(timeInStageLabel('Offer', 'bad', null, now)).toBe('');
    });
  });
});
