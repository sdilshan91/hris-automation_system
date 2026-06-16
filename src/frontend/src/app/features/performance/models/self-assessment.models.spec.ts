import {
  ATTACHMENT_MAX_BYTES,
  ATTACHMENT_MAX_FILES,
  canSubmitSelfAssessment,
  formatFileSize,
  goalIsComplete,
  ratedGoalCount,
  validateAttachment,
  weightedSelfScore,
} from './self-assessment.models';

describe('self-assessment.models helpers', () => {
  const long = 'This is a sufficiently long comment.'; // > 20 chars
  const short = 'too short'; // < 20 chars

  describe('goalIsComplete', () => {
    it('is true when rated and comment ≥ 20 chars', () => {
      expect(goalIsComplete({ selfRating: 4, comment: long })).toBeTrue();
    });
    it('is false when unrated', () => {
      expect(goalIsComplete({ selfRating: null, comment: long })).toBeFalse();
    });
    it('is false when rating is 0', () => {
      expect(goalIsComplete({ selfRating: 0, comment: long })).toBeFalse();
    });
    it('is false when comment is too short', () => {
      expect(goalIsComplete({ selfRating: 4, comment: short })).toBeFalse();
    });
    it('trims whitespace before measuring the comment', () => {
      expect(
        goalIsComplete({ selfRating: 4, comment: '   ' + short + '   ' }),
      ).toBeFalse();
    });
  });

  describe('ratedGoalCount', () => {
    it('counts only fully-completed goals', () => {
      const goals = [
        { selfRating: 4, comment: long }, // complete
        { selfRating: null, comment: long }, // unrated
        { selfRating: 3, comment: short }, // short comment
      ];
      expect(ratedGoalCount(goals)).toBe(1);
    });
  });

  describe('canSubmitSelfAssessment', () => {
    it('is false for an empty goal list', () => {
      expect(canSubmitSelfAssessment([])).toBeFalse();
    });
    it('is false when one goal is unrated', () => {
      const goals = [
        { selfRating: 4, comment: long },
        { selfRating: null, comment: long },
      ];
      expect(canSubmitSelfAssessment(goals)).toBeFalse();
    });
    it('is false when one comment is too short', () => {
      const goals = [
        { selfRating: 4, comment: long },
        { selfRating: 5, comment: short },
      ];
      expect(canSubmitSelfAssessment(goals)).toBeFalse();
    });
    it('is true when every goal is rated with a long enough comment', () => {
      const goals = [
        { selfRating: 4, comment: long },
        { selfRating: 5, comment: long },
      ];
      expect(canSubmitSelfAssessment(goals)).toBeTrue();
    });
  });

  describe('weightedSelfScore', () => {
    it('returns 0 when there is no weight', () => {
      expect(
        weightedSelfScore([{ selfRating: 5, weight: 0 }], 5),
      ).toBe(0);
    });
    it('returns 0 when scale max is 0', () => {
      expect(
        weightedSelfScore([{ selfRating: 5, weight: 100 }], 0),
      ).toBe(0);
    });
    it('scores a single fully-rated goal at 100%', () => {
      expect(
        weightedSelfScore([{ selfRating: 5, weight: 100 }], 5),
      ).toBe(100);
    });
    it('weights two goals by their weight', () => {
      // (5/5)*60 + (0/5)*40 = 60 earned over 100 weight = 60%
      const goals = [
        { selfRating: 5, weight: 60 },
        { selfRating: null, weight: 40 },
      ];
      expect(weightedSelfScore(goals, 5)).toBe(60);
    });
    it('produces a half score for a mid rating', () => {
      // (5/10)*100 = 50
      expect(
        weightedSelfScore([{ selfRating: 5, weight: 100 }], 10),
      ).toBe(50);
    });
  });

  describe('validateAttachment', () => {
    const smallFile = { size: 1024 } as File;
    const bigFile = { size: ATTACHMENT_MAX_BYTES + 1 } as File;

    it('rejects when the file cap is reached', () => {
      expect(validateAttachment(smallFile, ATTACHMENT_MAX_FILES)).toBe(
        'too_many',
      );
    });
    it('rejects an oversized file', () => {
      expect(validateAttachment(bigFile, 0)).toBe('too_large');
    });
    it('accepts a small file under the cap', () => {
      expect(validateAttachment(smallFile, 0)).toBeNull();
    });
  });

  describe('formatFileSize', () => {
    it('formats bytes', () => {
      expect(formatFileSize(512)).toBe('512 B');
    });
    it('formats kilobytes', () => {
      expect(formatFileSize(2048)).toBe('2 KB');
    });
    it('formats megabytes', () => {
      expect(formatFileSize(5 * 1024 * 1024)).toBe('5.0 MB');
    });
  });
});
