import {
  canSubmitManagerReview,
  managerGoalIsComplete,
  ratedManagerGoalCount,
  ratingBandClasses,
  unratedManagerGoalTitles,
  weightedManagerScore,
  MANAGER_COMMENT_MIN,
} from './manager-review.models';

const LONG = 'a'.repeat(MANAGER_COMMENT_MIN);
const SHORT = 'too short';

describe('manager-review.models helpers', () => {
  describe('managerGoalIsComplete', () => {
    it('is true when rated and comment ≥ min', () => {
      expect(
        managerGoalIsComplete({ managerRating: 4, managerComment: LONG }),
      ).toBeTrue();
    });

    it('is false when not rated', () => {
      expect(
        managerGoalIsComplete({ managerRating: null, managerComment: LONG }),
      ).toBeFalse();
    });

    it('is false when rating is 0', () => {
      expect(
        managerGoalIsComplete({ managerRating: 0, managerComment: LONG }),
      ).toBeFalse();
    });

    it('is false when comment too short', () => {
      expect(
        managerGoalIsComplete({ managerRating: 5, managerComment: SHORT }),
      ).toBeFalse();
    });

    it('trims whitespace before measuring the comment', () => {
      expect(
        managerGoalIsComplete({
          managerRating: 3,
          managerComment: `   ${SHORT}   `,
        }),
      ).toBeFalse();
    });
  });

  describe('ratedManagerGoalCount', () => {
    it('counts only fully complete goals', () => {
      const goals = [
        { managerRating: 4, managerComment: LONG },
        { managerRating: null, managerComment: LONG },
        { managerRating: 5, managerComment: SHORT },
      ];
      expect(ratedManagerGoalCount(goals)).toBe(1);
    });
  });

  describe('canSubmitManagerReview', () => {
    it('is false for an empty list', () => {
      expect(canSubmitManagerReview([])).toBeFalse();
    });

    it('is true when every goal is complete', () => {
      expect(
        canSubmitManagerReview([
          { managerRating: 4, managerComment: LONG },
          { managerRating: 2, managerComment: LONG },
        ]),
      ).toBeTrue();
    });

    it('is false when any goal is incomplete', () => {
      expect(
        canSubmitManagerReview([
          { managerRating: 4, managerComment: LONG },
          { managerRating: null, managerComment: LONG },
        ]),
      ).toBeFalse();
    });
  });

  describe('unratedManagerGoalTitles (AC-3)', () => {
    it('lists titles of incomplete goals in order', () => {
      const goals = [
        { title: 'Rated', managerRating: 5, managerComment: LONG },
        { title: 'No rating', managerRating: null, managerComment: LONG },
        { title: 'Short comment', managerRating: 3, managerComment: SHORT },
      ];
      expect(unratedManagerGoalTitles(goals)).toEqual([
        'No rating',
        'Short comment',
      ]);
    });

    it('is empty when all goals are complete', () => {
      const goals = [
        { title: 'A', managerRating: 4, managerComment: LONG },
        { title: 'B', managerRating: 5, managerComment: LONG },
      ];
      expect(unratedManagerGoalTitles(goals)).toEqual([]);
    });
  });

  describe('weightedManagerScore (FR-4 preview)', () => {
    it('returns 0 when total weight is zero', () => {
      expect(
        weightedManagerScore([{ managerRating: 5, weight: 0 }], 5),
      ).toBe(0);
    });

    it('computes a weighted percentage on the tenant scale', () => {
      // (5/5)*60 + (3/5)*40 = 60 + 24 = 84; /100 *100 = 84
      const score = weightedManagerScore(
        [
          { managerRating: 5, weight: 60 },
          { managerRating: 3, weight: 40 },
        ],
        5,
      );
      expect(score).toBe(84);
    });

    it('treats an unrated goal as 0 contribution', () => {
      // (4/5)*50 + 0 = 40; /100 *100 = 40
      const score = weightedManagerScore(
        [
          { managerRating: 4, weight: 50 },
          { managerRating: null, weight: 50 },
        ],
        5,
      );
      expect(score).toBe(40);
    });
  });

  describe('ratingBandClasses (§8 color bands)', () => {
    it('is neutral when unrated', () => {
      expect(ratingBandClasses(null, 5)).toContain('neutral');
    });

    it('is green for the top band', () => {
      expect(ratingBandClasses(5, 5)).toContain('emerald');
    });

    it('is yellow for the middle band', () => {
      expect(ratingBandClasses(2, 5)).toContain('amber');
    });

    it('is red for the bottom band', () => {
      expect(ratingBandClasses(1, 5)).toContain('rose');
    });
  });
});
