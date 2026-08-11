// ============================================================================
// GAP-023 — the FTE validator, and the fact that it agrees with the backend.
//
// FTE and work arrangement existed end-to-end on the backend (entity, create command, update command with an
// employment-history audit trail, a FluentValidation rule) and had NO frontend at all: STATUS.md:61 claimed
// "SHIPPED ... + FE employee-form", while zero hand-written Angular files referenced either field. HR could
// only set them by bulk import or SQL.
//
// These arms exist for the drift risk specifically. `fteValidator` is a hand-written mirror of the backend's
// `EmployeeFteRules.ValidFte()`, and a client-side rule that is LOOSER than the server's is worse than none:
// the user gets a clean-looking form and then an opaque validation error on submit, beside no field. The
// SCALE rule is the one that would realistically drift, because it is the non-obvious half.
// ============================================================================

import { FormControl } from '@angular/forms';
import {
  fteValidator,
  WORK_ARRANGEMENT_OPTIONS,
  workArrangementLabel,
} from './employee.models';

describe('GAP-023 fteValidator', () => {
  const run = (value: unknown) => fteValidator(new FormControl(value));

  // ── the backend rule: > 0, <= 1.00, at most 2 decimal places ────────────

  it('accepts a full-time 1 and a typical part-time 0.5', () => {
    expect(run(1)).toBeNull();
    expect(run(0.5)).toBeNull();
  });

  it('accepts exactly 2 decimal places', () => {
    expect(run(0.75)).toBeNull();
    expect(run(0.01)).toBeNull();
  });

  it('rejects 0 — proration DIVIDES by FTE, so zero is a divide-by-zero downstream', () => {
    expect(run(0)).toEqual({ fteRange: true });
  });

  it('rejects a negative value', () => {
    expect(run(-0.5)).toEqual({ fteRange: true });
  });

  it('rejects more than 1.00 — nobody is more than full-time, and it would over-accrue leave', () => {
    expect(run(1.5)).toEqual({ fteRange: true });
  });

  it('rejects more than 2 decimal places, matching the backend scale rule', () => {
    // The arm most likely to catch drift. Without it 0.333 passes every visible check here and is then
    // refused by the server, which surfaces as an error with no field attached to it.
    expect(run(0.333)).toEqual({ fteScale: true });
  });

  it('rejects a non-numeric entry rather than coercing it', () => {
    expect(run('abc')).toEqual({ fteInvalid: true });
  });

  // ── absence is not this validator's job ─────────────────────────────────

  it('treats blank as valid, because blank means LEAVE UNCHANGED on the profile', () => {
    // The profile sends `undefined` for a blank control so the backend keeps the stored value
    // (EmploymentInfoUpdate reads null as "no change"). If this validator rejected blank, the profile's
    // employment section could not be saved without also restating the FTE.
    expect(run('')).toBeNull();
    expect(run(null)).toBeNull();
    expect(run(undefined)).toBeNull();
  });

  it('does not silently accept a value the backend would reject at 3dp even when it looks round', () => {
    // 0.100 is numerically 0.1 but arrives from the input as the string '0.100'. Guards the string-based
    // scale check against a future "just use toFixed" simplification that would let 3dp through.
    expect(run('0.100')).toEqual({ fteScale: true });
  });
});

describe('GAP-023 work arrangement options', () => {
  it('offers exactly the three values the backend enum defines', () => {
    // Guards against a fourth option being added client-side, which would be an unassignable value the API
    // refuses — the same class of defect as the invented permission strings in ISSUE-363.
    expect(WORK_ARRANGEMENT_OPTIONS.map((o) => o.value)).toEqual([
      'OnSite',
      'Hybrid',
      'Remote',
    ]);
  });

  it('labels every value, and falls back to the raw value rather than blank', () => {
    for (const option of WORK_ARRANGEMENT_OPTIONS) {
      expect(workArrangementLabel(option.value)).toBe(option.label);
    }
    // An unknown value from an older/newer API should still render something readable.
    expect(workArrangementLabel('Something')).toBe('Something');
  });
});
