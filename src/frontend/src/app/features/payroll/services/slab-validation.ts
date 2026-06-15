import { ISlabValidation, ITaxSlab, SlabIssue } from '../models/statutory.models';

/**
 * US-PAY-006 FR-6: pure, side-effect-free validation of an income-tax slab set.
 * Isolated from the component so it is trivially unit-testable (mirrors the
 * backend's NFR-5 "calculation logic in a dedicated, side-effect-free service").
 *
 * Rules (after sorting by `slabFrom`):
 *  - each slab must have slabFrom < slabTo (unless slabTo is null = unlimited);
 *    a non-positive rate or from > to is `invalid`.
 *  - exactly one slab may be unlimited (slabTo null) and it must be the last.
 *  - consecutive slabs must be contiguous: next.slabFrom === prev.slabTo
 *    (a gap or overlap flags BOTH adjacent rows).
 *
 * Returns a map of ORIGINAL-array indices → issue so the editor can highlight the
 * offending rows in red in real time, plus an overall `valid` flag that gates Save.
 */
export function validateSlabs(slabs: ITaxSlab[]): ISlabValidation {
  const issues = new Map<number, SlabIssue>();
  if (slabs.length === 0) {
    return { issues, valid: false };
  }

  // Pair each slab with its original index, then sort by lower bound so we can
  // walk them in order while still reporting issues against the original row.
  const indexed = slabs.map((slab, index) => ({ slab, index }));
  const ordered = [...indexed].sort((a, b) => a.slab.slabFrom - b.slab.slabFrom);

  const flag = (index: number, issue: SlabIssue): void => {
    // Keep the first/most-severe issue; 'invalid' wins over contiguity issues.
    if (!issues.has(index) || issue === 'invalid') {
      issues.set(index, issue);
    }
  };

  // 1) Per-row sanity: bounds + rate.
  for (const { slab, index } of ordered) {
    const badRate =
      slab.ratePercentage == null ||
      Number.isNaN(slab.ratePercentage) ||
      slab.ratePercentage < 0 ||
      slab.ratePercentage > 100;
    const badFrom = slab.slabFrom == null || Number.isNaN(slab.slabFrom) || slab.slabFrom < 0;
    const badBounds = slab.slabTo != null && slab.slabFrom >= slab.slabTo;
    if (badRate || badFrom || badBounds) {
      flag(index, 'invalid');
    }
  }

  // 2) Only the LAST (highest) slab may be unlimited.
  ordered.forEach((entry, pos) => {
    if (entry.slab.slabTo == null && pos !== ordered.length - 1) {
      flag(entry.index, 'invalid');
    }
  });

  // 3) Contiguity between consecutive slabs (gap / overlap flags both rows).
  for (let pos = 0; pos < ordered.length - 1; pos++) {
    const current = ordered[pos];
    const next = ordered[pos + 1];
    // An unlimited slab that isn't last is already 'invalid'; skip contiguity.
    if (current.slab.slabTo == null) {
      continue;
    }
    if (next.slab.slabFrom > current.slab.slabTo) {
      flag(current.index, 'gap');
      flag(next.index, 'gap');
    } else if (next.slab.slabFrom < current.slab.slabTo) {
      flag(current.index, 'overlap');
      flag(next.index, 'overlap');
    }
  }

  return { issues, valid: issues.size === 0 };
}
