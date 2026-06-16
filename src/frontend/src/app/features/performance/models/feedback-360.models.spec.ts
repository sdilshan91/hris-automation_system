import {
  isValidPeerNomination,
  isRemovableReviewer,
  categoryCompletionPercent,
  categoryReviewerTotal,
  isBelowMinimum,
  canSubmitFeedback,
  unratedQuestionTitles,
  ratingBarPercent,
  ratingBand360Classes,
  initials,
} from './feedback-360.models';

describe('feedback-360 model helpers', () => {
  describe('isValidPeerNomination (BR-2)', () => {
    it('blocks an employee from reviewing themselves as a peer', () => {
      expect(isValidPeerNomination('e-1', 'e-1')).toBeFalse();
    });

    it('allows a different employee as a peer', () => {
      expect(isValidPeerNomination('e-1', 'e-2')).toBeTrue();
    });
  });

  describe('isRemovableReviewer (AC-1)', () => {
    it('cannot remove a locked reviewer (auto-assigned Manager)', () => {
      expect(
        isRemovableReviewer({ locked: true, category: 'Manager' }),
      ).toBeFalse();
    });

    it('cannot remove the Self category even if unlocked', () => {
      expect(
        isRemovableReviewer({ locked: false, category: 'Self' }),
      ).toBeFalse();
    });

    it('can remove a manual peer', () => {
      expect(
        isRemovableReviewer({ locked: false, category: 'Peer' }),
      ).toBeTrue();
    });

    it('can remove a manual direct report', () => {
      expect(
        isRemovableReviewer({ locked: false, category: 'DirectReport' }),
      ).toBeTrue();
    });
  });

  describe('completion-tracker math (AC-3 / §8)', () => {
    it('computes the submitted percentage', () => {
      expect(
        categoryCompletionPercent({ submitted: 3, pending: 1, overdue: 0 }),
      ).toBe(75);
    });

    it('returns 0% when there are no reviewers', () => {
      expect(
        categoryCompletionPercent({ submitted: 0, pending: 0, overdue: 0 }),
      ).toBe(0);
    });

    it('counts overdue toward the total denominator', () => {
      expect(
        categoryCompletionPercent({ submitted: 1, pending: 0, overdue: 1 }),
      ).toBe(50);
    });

    it('totals the three buckets', () => {
      expect(
        categoryReviewerTotal({ submitted: 2, pending: 1, overdue: 1 }),
      ).toBe(4);
    });
  });

  describe('isBelowMinimum (BR-4)', () => {
    it('flags a category with fewer reviewers than required', () => {
      expect(
        isBelowMinimum({
          submitted: 1,
          pending: 0,
          overdue: 0,
          minimum: 2,
        }),
      ).toBeTrue();
    });

    it('passes when the minimum is met', () => {
      expect(
        isBelowMinimum({
          submitted: 1,
          pending: 1,
          overdue: 0,
          minimum: 2,
        }),
      ).toBeFalse();
    });
  });

  describe('canSubmitFeedback (AC-3)', () => {
    it('is false when no questions', () => {
      expect(canSubmitFeedback([])).toBeFalse();
    });

    it('is false when any question is unrated', () => {
      expect(
        canSubmitFeedback([{ rating: 4 }, { rating: null }]),
      ).toBeFalse();
    });

    it('is false when a rating is zero', () => {
      expect(canSubmitFeedback([{ rating: 0 }])).toBeFalse();
    });

    it('is true when every question is rated', () => {
      expect(canSubmitFeedback([{ rating: 4 }, { rating: 3 }])).toBeTrue();
    });
  });

  describe('unratedQuestionTitles (AC-3)', () => {
    it('lists only the unrated titles in order', () => {
      expect(
        unratedQuestionTitles([
          { title: 'Communication', rating: 4 },
          { title: 'Teamwork', rating: null },
          { title: 'Delivery', rating: 0 },
        ]),
      ).toEqual(['Teamwork', 'Delivery']);
    });
  });

  describe('ratingBarPercent (AC-4)', () => {
    it('maps a rating to a percentage of the scale', () => {
      expect(ratingBarPercent(4, 5)).toBe(80);
    });

    it('returns 0 for null', () => {
      expect(ratingBarPercent(null, 5)).toBe(0);
    });

    it('caps at 100', () => {
      expect(ratingBarPercent(6, 5)).toBe(100);
    });
  });

  describe('ratingBand360Classes (§8)', () => {
    it('returns green for the top third', () => {
      expect(ratingBand360Classes(5, 5)).toContain('emerald');
    });

    it('returns amber for the middle', () => {
      expect(ratingBand360Classes(3, 5)).toContain('amber');
    });

    it('returns red for the bottom', () => {
      expect(ratingBand360Classes(1, 5)).toContain('rose');
    });

    it('returns neutral for no rating', () => {
      expect(ratingBand360Classes(null, 5)).toContain('neutral');
    });
  });

  describe('initials (§8)', () => {
    it('uses first + last initial', () => {
      expect(initials('Alex Doe')).toBe('AD');
    });

    it('uses a single initial for one name', () => {
      expect(initials('Cher')).toBe('C');
    });

    it('returns ? for empty', () => {
      expect(initials('')).toBe('?');
    });
  });
});
