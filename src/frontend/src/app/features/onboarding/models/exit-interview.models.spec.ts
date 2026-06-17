import {
  IExitInterviewTemplate,
  IReasonDatum,
  ITrendDatum,
  ratingLabel,
  todayIso,
  totalRequiredQuestions,
  isAnswered,
  completionPercent,
  chartColor,
  pieSlices,
  ratingBarPercent,
  trendPolyline,
  RATING_MAX,
} from './exit-interview.models';

describe('exit-interview.models (pure helpers)', () => {
  describe('ratingLabel', () => {
    it('maps 1..5 to human labels and out-of-range to empty', () => {
      expect(ratingLabel(1)).toBe('Very poor');
      expect(ratingLabel(3)).toBe('Neutral');
      expect(ratingLabel(5)).toBe('Excellent');
      expect(ratingLabel(0)).toBe('');
      expect(ratingLabel(6)).toBe('');
    });
  });

  describe('todayIso', () => {
    it('formats a fixed date as yyyy-MM-dd in local time', () => {
      expect(todayIso(new Date(2026, 5, 7))).toBe('2026-06-07');
    });
  });

  describe('totalRequiredQuestions', () => {
    const tmpl: IExitInterviewTemplate = {
      templateId: 't',
      categories: [
        {
          category: 'A',
          questions: [
            { questionId: 'q1', text: '', type: 'rating', required: true },
            { questionId: 'q2', text: '', type: 'free_text', required: false },
          ],
        },
        {
          category: 'B',
          questions: [
            { questionId: 'q3', text: '', type: 'yes_no', required: true },
          ],
        },
      ],
    };

    it('counts only required questions across all categories', () => {
      expect(totalRequiredQuestions(tmpl)).toBe(2);
    });

    it('returns 0 for a null template', () => {
      expect(totalRequiredQuestions(null)).toBe(0);
    });
  });

  describe('isAnswered', () => {
    it('treats null/undefined/empty/0 as unanswered', () => {
      expect(isAnswered(null)).toBeFalse();
      expect(isAnswered(undefined)).toBeFalse();
      expect(isAnswered('')).toBeFalse();
      expect(isAnswered('   ')).toBeFalse();
      expect(isAnswered(0)).toBeFalse();
    });

    it('treats a rating, non-empty text and any boolean as answered', () => {
      expect(isAnswered(4)).toBeTrue();
      expect(isAnswered('hello')).toBeTrue();
      expect(isAnswered(true)).toBeTrue();
      expect(isAnswered(false)).toBeTrue();
    });
  });

  describe('completionPercent', () => {
    it('returns 100 when there are no required questions', () => {
      expect(completionPercent(0, 0)).toBe(100);
    });

    it('rounds the answered/total ratio and clamps to 0..100', () => {
      expect(completionPercent(0, 4)).toBe(0);
      expect(completionPercent(1, 4)).toBe(25);
      expect(completionPercent(4, 4)).toBe(100);
      expect(completionPercent(5, 4)).toBe(100);
    });
  });

  describe('chartColor', () => {
    it('cycles deterministically through the palette', () => {
      expect(chartColor(0)).toBe(chartColor(8));
      expect(chartColor(1)).not.toBe(chartColor(0));
    });
  });

  describe('pieSlices', () => {
    const data: IReasonDatum[] = [
      { reason: 'Pay', count: 3 },
      { reason: 'Growth', count: 1 },
    ];

    it('returns one slice per datum with percents summing ~100', () => {
      const slices = pieSlices(data, 50, 50, 48);
      expect(slices.length).toBe(2);
      expect(slices[0].percent).toBe(75);
      expect(slices[1].percent).toBe(25);
      expect(slices[0].path).toContain('A '); // arc command present
      expect(slices[0].color).toBe(chartColor(0));
    });

    it('returns [] when total count is zero or data empty', () => {
      expect(pieSlices([], 50, 50, 48)).toEqual([]);
      expect(pieSlices([{ reason: 'x', count: 0 }], 50, 50, 48)).toEqual([]);
    });

    it('renders a single 100% reason as a full-circle path', () => {
      const slices = pieSlices([{ reason: 'Only', count: 5 }], 50, 50, 48);
      expect(slices.length).toBe(1);
      expect(slices[0].percent).toBe(100);
      // full-circle path uses a large-arc flag of 1
      expect(slices[0].path).toContain('A 48 48 0 1 1');
    });
  });

  describe('ratingBarPercent', () => {
    it('maps the 1..5 rating onto a 0..100 width', () => {
      expect(ratingBarPercent(RATING_MAX)).toBe(100);
      expect(ratingBarPercent(2.5)).toBe(50);
      expect(ratingBarPercent(0)).toBe(0);
      expect(ratingBarPercent(99)).toBe(100); // clamped
    });
  });

  describe('trendPolyline', () => {
    const trend: ITrendDatum[] = [
      { period: 'Q1', count: 1, averageRating: 5 },
      { period: 'Q2', count: 2, averageRating: 0 },
    ];

    it('returns "" for an empty series', () => {
      expect(trendPolyline([], 600, 160)).toBe('');
    });

    it('produces one x,y pair per period within the viewBox', () => {
      const pts = trendPolyline(trend, 600, 160, 8);
      const pairs = pts.split(' ');
      expect(pairs.length).toBe(2);
      // top of chart for the max rating, bottom for the min
      const [, y1] = pairs[0].split(',').map(Number);
      const [, y2] = pairs[1].split(',').map(Number);
      expect(y1).toBeLessThan(y2);
    });

    it('centers a single-point series horizontally', () => {
      const pts = trendPolyline([trend[0]], 600, 160, 8);
      const [x] = pts.split(',').map(Number);
      expect(x).toBe(300);
    });
  });
});
