import {
  RUN_STATUS_BADGE,
  RUN_STATUS_LABELS,
  runActionErrorMessage,
} from './payroll-run.models';

describe('payroll-run.models', () => {
  // ISSUE-317 / DF-12: the backend tolerates a corrupt enum row by returning the
  // `Unknown` sentinel. Both the badge and the label maps must carry an `Unknown`
  // entry so a corrupt row is visibly flagged rather than rendering blank.
  describe('Unknown status entries (DF-12)', () => {
    it('has a non-empty Unknown badge distinct from a valid status', () => {
      expect(RUN_STATUS_BADGE.Unknown).toBeTruthy();
      expect(RUN_STATUS_BADGE.Unknown.length).toBeGreaterThan(0);
      expect(RUN_STATUS_BADGE.Unknown).not.toBe(RUN_STATUS_BADGE.Finalized);
    });

    it('labels the Unknown status as "Unknown"', () => {
      expect(RUN_STATUS_LABELS.Unknown).toBe('Unknown');
    });
  });

  describe('runActionErrorMessage', () => {
    it('maps a known code to its friendly message', () => {
      expect(runActionErrorMessage('run_finalized')).toContain('finalized');
    });

    it('falls back to a generic message for an unknown/undefined code', () => {
      expect(runActionErrorMessage(undefined)).toContain('could not be completed');
      expect(runActionErrorMessage('nope')).toContain('could not be completed');
    });
  });
});
