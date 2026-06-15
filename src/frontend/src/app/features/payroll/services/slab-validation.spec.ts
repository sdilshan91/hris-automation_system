import { validateSlabs } from './slab-validation';
import { ITaxSlab } from '../models/statutory.models';

/**
 * US-PAY-006 FR-6: contiguity validation for income-tax slabs. Pure function, so
 * these tests assert the issue map + overall validity directly (test hints §11).
 */
describe('validateSlabs', () => {
  it('marks an empty slab set as invalid', () => {
    const result = validateSlabs([]);
    expect(result.valid).toBeFalse();
    expect(result.issues.size).toBe(0);
  });

  it('accepts a contiguous progressive set ending in an unlimited slab', () => {
    const slabs: ITaxSlab[] = [
      { slabFrom: 0, slabTo: 250000, ratePercentage: 0 },
      { slabFrom: 250000, slabTo: 500000, ratePercentage: 5 },
      { slabFrom: 500000, slabTo: 1000000, ratePercentage: 20 },
      { slabFrom: 1000000, slabTo: null, ratePercentage: 30 },
    ];
    const result = validateSlabs(slabs);
    expect(result.valid).toBeTrue();
    expect(result.issues.size).toBe(0);
  });

  it('accepts a single unlimited (flat-rate) slab', () => {
    const result = validateSlabs([{ slabFrom: 0, slabTo: null, ratePercentage: 10 }]);
    expect(result.valid).toBeTrue();
  });

  it('flags overlapping ranges on both adjacent rows (test hint §11)', () => {
    const slabs: ITaxSlab[] = [
      { slabFrom: 0, slabTo: 300000, ratePercentage: 0 },
      { slabFrom: 250000, slabTo: 500000, ratePercentage: 5 },
    ];
    const result = validateSlabs(slabs);
    expect(result.valid).toBeFalse();
    expect(result.issues.get(0)).toBe('overlap');
    expect(result.issues.get(1)).toBe('overlap');
  });

  it('flags gaps in ranges on both adjacent rows (test hint §11)', () => {
    const slabs: ITaxSlab[] = [
      { slabFrom: 0, slabTo: 250000, ratePercentage: 0 },
      { slabFrom: 300000, slabTo: 500000, ratePercentage: 5 },
    ];
    const result = validateSlabs(slabs);
    expect(result.valid).toBeFalse();
    expect(result.issues.get(0)).toBe('gap');
    expect(result.issues.get(1)).toBe('gap');
  });

  it('reports issues against the ORIGINAL index after sorting by lower bound', () => {
    // Rows entered out of order: index 0 is the higher slab, index 1 the lower.
    const slabs: ITaxSlab[] = [
      { slabFrom: 500000, slabTo: null, ratePercentage: 20 },
      { slabFrom: 0, slabTo: 250000, ratePercentage: 0 }, // gap to 500000
    ];
    const result = validateSlabs(slabs);
    expect(result.valid).toBeFalse();
    // The gap is between the (sorted) lower row (orig index 1) and higher (orig index 0).
    expect(result.issues.get(0)).toBe('gap');
    expect(result.issues.get(1)).toBe('gap');
  });

  it('flags a row whose from >= to as invalid', () => {
    const slabs: ITaxSlab[] = [
      { slabFrom: 250000, slabTo: 250000, ratePercentage: 5 },
    ];
    const result = validateSlabs(slabs);
    expect(result.valid).toBeFalse();
    expect(result.issues.get(0)).toBe('invalid');
  });

  it('flags a negative or out-of-range rate as invalid', () => {
    expect(
      validateSlabs([{ slabFrom: 0, slabTo: null, ratePercentage: -5 }]).issues.get(
        0,
      ),
    ).toBe('invalid');
    expect(
      validateSlabs([{ slabFrom: 0, slabTo: null, ratePercentage: 150 }]).issues.get(
        0,
      ),
    ).toBe('invalid');
  });

  it('flags an unlimited slab that is not the last as invalid', () => {
    const slabs: ITaxSlab[] = [
      { slabFrom: 0, slabTo: null, ratePercentage: 0 }, // unlimited but not last
      { slabFrom: 500000, slabTo: 1000000, ratePercentage: 20 },
    ];
    const result = validateSlabs(slabs);
    expect(result.valid).toBeFalse();
    // The (sorted) first row is the unlimited one → flagged invalid.
    expect([...result.issues.values()]).toContain('invalid');
  });

  it('prefers the invalid flag over a contiguity flag on the same row', () => {
    const slabs: ITaxSlab[] = [
      { slabFrom: 0, slabTo: 250000, ratePercentage: 0 },
      // from >= to → invalid, AND it gaps/overlaps with the neighbour
      { slabFrom: 400000, slabTo: 300000, ratePercentage: 5 },
    ];
    const result = validateSlabs(slabs);
    expect(result.issues.get(1)).toBe('invalid');
  });
});
