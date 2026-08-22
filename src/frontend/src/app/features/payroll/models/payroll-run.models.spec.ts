import {
  RUN_STATUS_BADGE,
  RUN_STATUS_LABELS,
  runActionErrorMessage,
  canGeneratePayslipsFor,
  payslipRegenerationNeedsConfirmation,
  PAYSLIP_GENERATION_STATUSES,
} from './payroll-run.models';

describe('payroll-run.models', () => {
  // ── B5: the payslip generation gate mirrors the server (US-PAY-004 BR-1) ────────────────────────
  //
  // The UI used to carry its own version of this rule — `status !== 'Finalized'` — which was wrong in BOTH
  // directions against `PayslipGenerationService.GenerateAsync`: it enabled the button on a Queued or
  // Processing run, where the call always comes back 400 `run_not_ready_for_payslips`, and disabled it on a
  // Finalized run, where the backend explicitly allows regeneration after a template change. The
  // component's own comments contradicted each other about which behaviour was intended.
  describe('payslip generation gate (B5 / BR-1)', () => {
    it('allows exactly the three states the server accepts', () => {
      expect(canGeneratePayslipsFor('ReviewPending')).toBeTrue();
      expect(canGeneratePayslipsFor('Approved')).toBeTrue();
      expect(canGeneratePayslipsFor('Finalized'))
        .withContext('the backend allows regeneration on a finalized run; hiding it loses a real capability')
        .toBeTrue();
    });

    it('refuses the states the server rejects, so the button is never a guaranteed 400', () => {
      for (const status of ['Queued', 'Processing', 'AwaitingApproval', 'Rejected', 'Cancelled'] as const) {
        expect(canGeneratePayslipsFor(status))
          .withContext(`${status} is not a BR-1 state — generating from it always 400s`)
          .toBeFalse();
      }
      expect(canGeneratePayslipsFor(null)).toBeFalse();
    });

    it('lists exactly three permitted states', () => {
      // A guard on the list itself: silently widening it is how the UI drifts from the server again.
      expect([...PAYSLIP_GENERATION_STATUSES].sort()).toEqual(
        ['Approved', 'Finalized', 'ReviewPending'],
      );
    });

    it('requires confirmation only where regenerating would overwrite distributed PDFs', () => {
      expect(payslipRegenerationNeedsConfirmation('Finalized'))
        .withContext('a finalized run\'s payslips may already be in employees\' inboxes')
        .toBeTrue();
      expect(payslipRegenerationNeedsConfirmation('ReviewPending')).toBeFalse();
      expect(payslipRegenerationNeedsConfirmation('Approved')).toBeFalse();
      expect(payslipRegenerationNeedsConfirmation(null)).toBeFalse();
    });
  });

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
