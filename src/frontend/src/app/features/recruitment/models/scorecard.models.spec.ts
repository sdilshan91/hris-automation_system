import {
  averageOf,
  aggregateOf,
  isScorecardEditable,
  recommendationLabel,
  recommendationBadge,
  scoreLabel,
  scorecardInitials,
  IScorecard,
  ICriterionRating,
  RECOMMENDATION_BADGE,
} from './scorecard.models';

describe('scorecard.models helpers (US-REC-006)', () => {
  const rating = (score: number): ICriterionRating => ({
    criterionKey: 'k',
    score: score as 1 | 2 | 3 | 4 | 5,
    comment: null,
  });

  const card = (over: Partial<IScorecard> = {}): IScorecard => ({
    id: 's1',
    interviewId: 'iv1',
    interviewerEmployeeId: 'e1',
    interviewerName: 'Ada Lovelace',
    overallRecommendation: 'Hire',
    ratings: [rating(4), rating(2)],
    generalNotes: null,
    averageScore: 3,
    submittedAt: '2026-06-15T10:00:00Z',
    ...over,
  });

  // ─── averageOf (FR-3) ─────────────────────────────────────

  it('averageOf computes the mean rounded to one decimal', () => {
    expect(averageOf([rating(4), rating(2)])).toBe(3);
    expect(averageOf([rating(5), rating(4), rating(4)])).toBe(4.3);
  });

  it('averageOf returns 0 for an empty list', () => {
    expect(averageOf([])).toBe(0);
  });

  // ─── aggregateOf (FR-3) ───────────────────────────────────

  it('aggregateOf averages across scorecards rounded to one decimal', () => {
    expect(aggregateOf([card({ averageScore: 4 }), card({ averageScore: 3 })])).toBe(
      3.5,
    );
  });

  it('aggregateOf returns null when there are no scorecards', () => {
    expect(aggregateOf([])).toBeNull();
  });

  // ─── isScorecardEditable (FR-2 / BR-4) ────────────────────

  it('isScorecardEditable is true when not locked', () => {
    expect(isScorecardEditable(card())).toBeTrue();
    expect(isScorecardEditable(card({ isLocked: false }))).toBeTrue();
  });

  it('isScorecardEditable is false when isLocked is true or lockedAt is set', () => {
    expect(isScorecardEditable(card({ isLocked: true }))).toBeFalse();
    expect(
      isScorecardEditable(card({ lockedAt: '2026-06-17T10:00:00Z' })),
    ).toBeFalse();
  });

  // ─── label / badge helpers (enum casing must be PascalCase, US-PLT-003) ──

  it('recommendationLabel maps PascalCase enum members to human labels', () => {
    expect(recommendationLabel('StrongHire')).toBe('Strong Hire');
    expect(recommendationLabel('Hire')).toBe('Hire');
    expect(recommendationLabel('NoHire')).toBe('No Hire');
    expect(recommendationLabel('StrongNoHire')).toBe('Strong No Hire');
  });

  it('recommendationLabel falls back to the raw value for unknown input', () => {
    expect(recommendationLabel('Mystery')).toBe('Mystery');
  });

  it('recommendationBadge returns the matching class or a fallback', () => {
    expect(recommendationBadge('StrongHire')).toBe(
      RECOMMENDATION_BADGE.StrongHire,
    );
    expect(recommendationBadge('Mystery')).toBe(RECOMMENDATION_BADGE.NoHire);
  });

  it('scoreLabel formats a 1-5 score with its label and tolerates out-of-range', () => {
    expect(scoreLabel(1)).toBe('1 · Poor');
    expect(scoreLabel(5)).toBe('5 · Excellent');
    expect(scoreLabel(9)).toBe('9');
  });

  it('scorecardInitials returns up to two uppercase initials', () => {
    expect(scorecardInitials('Ada Lovelace')).toBe('AL');
    expect(scorecardInitials('Plato')).toBe('P');
    expect(scorecardInitials('')).toBe('');
  });
});
