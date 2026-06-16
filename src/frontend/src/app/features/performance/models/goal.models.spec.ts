import { weightsTotalCorrect } from './goal.models';

describe('weightsTotalCorrect', () => {
  it('is true when weights sum to exactly 100', () => {
    expect(weightsTotalCorrect([100])).toBeTrue();
    expect(weightsTotalCorrect([60, 40])).toBeTrue();
    expect(weightsTotalCorrect([50, 25, 25])).toBeTrue();
  });

  it('is false when weights sum below 100 (e.g. 95%)', () => {
    expect(weightsTotalCorrect([60, 35])).toBeFalse();
    expect(weightsTotalCorrect([95])).toBeFalse();
  });

  it('is false when weights sum above 100 (e.g. 105%)', () => {
    expect(weightsTotalCorrect([60, 45])).toBeFalse();
    expect(weightsTotalCorrect([105])).toBeFalse();
  });

  it('is false for an empty list (min 1 goal required)', () => {
    expect(weightsTotalCorrect([])).toBeFalse();
  });

  it('coerces non-numeric values to 0', () => {
    expect(weightsTotalCorrect([100, NaN as unknown as number])).toBeTrue();
  });
});
