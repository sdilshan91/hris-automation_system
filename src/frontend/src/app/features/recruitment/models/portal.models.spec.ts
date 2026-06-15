import {
  buildSteps,
  canRespondToPortalOffer,
  formatPortalCompensation,
  isRejected,
  PORTAL_STEPS,
  portalInterviewTypeLabel,
  stepCircleClass,
  stepConnectorClass,
  IPortalOffer,
} from './portal.models';

describe('portal.models (US-REC-008)', () => {
  // ─── buildSteps (AC-1 step indicator) ─────────────────────

  it('buildSteps marks earlier stages completed, current blue, later future', () => {
    const steps = buildSteps('Interview');
    expect(steps.map((s) => s.stage)).toEqual([...PORTAL_STEPS]);
    expect(steps[0].state).toBe('completed'); // Applied
    expect(steps[1].state).toBe('completed'); // Screening
    expect(steps[2].state).toBe('current'); // Interview
    expect(steps[3].state).toBe('future'); // Offer
    expect(steps[4].state).toBe('future'); // Hired
  });

  it('buildSteps marks every step completed/current at Hired', () => {
    const steps = buildSteps('Hired');
    expect(steps.slice(0, 4).every((s) => s.state === 'completed')).toBeTrue();
    expect(steps[4].state).toBe('current');
  });

  it('buildSteps leaves all steps future for an off-funnel (Rejected) stage', () => {
    const steps = buildSteps('Rejected');
    expect(steps.every((s) => s.state === 'future')).toBeTrue();
  });

  it('isRejected detects the terminal Rejected stage', () => {
    expect(isRejected('Rejected')).toBeTrue();
    expect(isRejected('Offer')).toBeFalse();
  });

  // ─── step styling (§8 colors) ─────────────────────────────

  it('stepCircleClass uses green for completed, blue for current, gray for future', () => {
    expect(stepCircleClass('completed')).toContain('bg-green-500');
    expect(stepCircleClass('current')).toContain('bg-blue-600');
    expect(stepCircleClass('future')).toContain('bg-neutral-200');
  });

  it('stepConnectorClass is gray before a future step, green otherwise', () => {
    expect(stepConnectorClass('future')).toContain('bg-neutral-200');
    expect(stepConnectorClass('completed')).toContain('bg-green-500');
    expect(stepConnectorClass('current')).toContain('bg-green-500');
  });

  // ─── interview labels (FR-3) ──────────────────────────────

  it('portalInterviewTypeLabel maps the PascalCase enum to display text', () => {
    expect(portalInterviewTypeLabel('InPerson')).toBe('In-person');
    expect(portalInterviewTypeLabel('Video')).toBe('Video');
    expect(portalInterviewTypeLabel('Phone')).toBe('Phone');
  });

  // ─── offer respond gate (AC-3 / BR-2) ─────────────────────

  const offer = (over: Partial<IPortalOffer> = {}): IPortalOffer => ({
    id: 'o1',
    offerReferenceNumber: 'OFF-1',
    status: 'Sent',
    offeredPosition: 'Engineer',
    departmentName: null,
    salaryAmount: 100000,
    currency: 'USD',
    salaryFrequency: 'Annual',
    benefitsSummary: null,
    startDate: '2026-07-01',
    expiryDate: '2026-07-15',
    canRespond: true,
    hasDocument: true,
    ...over,
  });

  it('canRespondToPortalOffer reflects the backend canRespond flag (BR-2 one-time)', () => {
    expect(canRespondToPortalOffer(offer({ canRespond: true }))).toBeTrue();
    expect(canRespondToPortalOffer(offer({ canRespond: false }))).toBeFalse();
    expect(canRespondToPortalOffer(null)).toBeFalse();
  });

  // ─── compensation formatting ──────────────────────────────

  it('formatPortalCompensation renders currency + amount + suffix', () => {
    expect(
      formatPortalCompensation({
        salaryAmount: 120000,
        currency: 'USD',
        salaryFrequency: 'Annual',
      }),
    ).toBe('USD 120,000 / yr');
    expect(
      formatPortalCompensation({
        salaryAmount: 5000,
        currency: 'EUR',
        salaryFrequency: 'Monthly',
      }),
    ).toBe('EUR 5,000 / mo');
  });
});
