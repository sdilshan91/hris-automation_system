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
  mapFeedback360Results,
  IFeedback360ResultsRaw,
} from './feedback-360.models';

function makeRaw(
  overrides: Partial<IFeedback360ResultsRaw> = {},
): IFeedback360ResultsRaw {
  return {
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    revieweeEmployeeId: 'e-1',
    revieweeName: 'Alex Doe',
    jobTitle: 'Engineer',
    isAnonymousFeedback: true,
    ratingScaleMax: 5,
    compositeScore: 82,
    competencyAverages: [
      {
        goalId: null,
        competencyKey: 'communication',
        label: 'Communication',
        averageRating: 4.2,
        responseCount: 5,
      },
    ],
    categoryAverages: [
      {
        category: 'Peer',
        categoryName: 'Peers',
        averageRating: 4.1,
        responseCount: 3,
        weight: 40,
      },
    ],
    entries: [
      {
        feedbackId: 'f-1',
        category: 'Peer',
        categoryName: 'Peers',
        isAnonymous: true,
        reviewerEmployeeId: null,
        reviewerName: null,
        overallComment: 'Strong communicator.',
        submittedAt: '2026-06-10T10:00:00Z',
        items: [
          {
            goalId: null,
            competencyKey: 'communication',
            label: 'Communication',
            rating: 4,
            comment: 'Clear in standups.',
          },
          {
            goalId: null,
            competencyKey: 'delivery',
            label: 'Delivery',
            rating: 5,
            comment: null,
          },
        ],
      },
    ],
    minPeerReviewers: 3,
    peerResponseCount: 3,
    minPeerThresholdMet: true,
    releaseWarning: null,
    released: true,
    releasedAt: '2026-06-11T09:00:00Z',
    exportAvailable: true,
    ...overrides,
  };
}

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

  describe('mapFeedback360Results (ISSUE-373 / GAP-012 S-1)', () => {
    it('renames every drifted identity/aggregate field into the FE vocabulary', () => {
      const r = mapFeedback360Results(makeRaw());
      expect(r.employeeId).toBe('e-1'); // ← revieweeEmployeeId
      expect(r.employeeName).toBe('Alex Doe'); // ← revieweeName
      expect(r.anonymous).toBeTrue(); // ← isAnonymousFeedback
      expect(r.jobTitle).toBe('Engineer');
      expect(r.cycleName).toBe('2026 Annual');
      expect(r.ratingScaleMax).toBe(5);
      expect(r.compositeScore).toBe(82);
    });

    it('maps competencyAverages → competencies (flat, byCategory always [])', () => {
      const r = mapFeedback360Results(makeRaw());
      expect(r.competencies.length).toBe(1);
      expect(r.competencies[0].title).toBe('Communication'); // ← label
      expect(r.competencies[0].overallAverage).toBe(4.2); // ← averageRating
      expect(r.competencies[0].kind).toBe('Competency');
      expect(r.competencies[0].byCategory).toEqual([]);
    });

    it('marks a competency backed by a goalId as kind Goal', () => {
      const r = mapFeedback360Results(
        makeRaw({
          competencyAverages: [
            {
              goalId: 'g-1',
              competencyKey: null,
              label: 'Ship the roadmap',
              averageRating: 3.5,
              responseCount: 2,
            },
          ],
        }),
      );
      expect(r.competencies[0].kind).toBe('Goal');
      expect(r.competencies[0].questionId).toBe('g-1');
    });

    it('maps categoryAverages[].averageRating → .average', () => {
      const r = mapFeedback360Results(makeRaw());
      expect(r.categoryAverages[0].category).toBe('Peer');
      expect(r.categoryAverages[0].average).toBe(4.1); // ← averageRating
      expect(r.categoryAverages[0].responseCount).toBe(3);
    });

    it('flattens entries into overall + per-item comment rows, dropping null comments', () => {
      const r = mapFeedback360Results(makeRaw());
      // overall comment + one item with a comment; the null-comment item drops
      expect(r.comments.length).toBe(2);
      expect(r.comments.find((c) => c.questionTitle === '')?.comment).toBe(
        'Strong communicator.',
      );
      expect(
        r.comments.find((c) => c.questionTitle === 'Communication')?.comment,
      ).toBe('Clear in standups.');
    });

    it('carries a NULL reviewer name through as undefined — never reconstructs identity (FR-5/NFR-3)', () => {
      const r = mapFeedback360Results(makeRaw());
      expect(r.comments.every((c) => c.reviewerName == null)).toBeTrue();
    });

    it('passes an attributed reviewer name through when anonymity is off', () => {
      const r = mapFeedback360Results(
        makeRaw({
          isAnonymousFeedback: false,
          entries: [
            {
              feedbackId: 'f-2',
              category: 'Manager',
              categoryName: 'Manager',
              isAnonymous: false,
              reviewerEmployeeId: 'm-1',
              reviewerName: 'Morgan Manager',
              overallComment: 'Great clarity.',
              submittedAt: '2026-06-10T10:00:00Z',
              items: [],
            },
          ],
        }),
      );
      expect(r.comments[0].reviewerName).toBe('Morgan Manager');
    });

    it('carries the BR-4 release gate + threshold counts through', () => {
      const r = mapFeedback360Results(
        makeRaw({
          released: false,
          releasedAt: null,
          minPeerReviewers: 3,
          peerResponseCount: 1,
          minPeerThresholdMet: false,
          releaseWarning: 'Only 1 of 3 peers have submitted.',
        }),
      );
      expect(r.released).toBeFalse();
      expect(r.releasedAt).toBeNull();
      expect(r.minPeerReviewers).toBe(3);
      expect(r.peerResponseCount).toBe(1);
      expect(r.minPeerThresholdMet).toBeFalse();
      expect(r.releaseWarning).toBe('Only 1 of 3 peers have submitted.');
    });
  });
});
