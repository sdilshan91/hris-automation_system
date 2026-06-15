import {
  OfferStatus,
  IOffer,
  offerStatusBadge,
  offerStatusLabel,
  salaryFrequencySuffix,
  canSendOffer,
  canRespondToOffer,
  canWithdrawOffer,
  defaultExpiryDate,
  toIsoDate,
  formatCompensation,
  OFFER_STATUS_BADGE,
} from './offer.models';

describe('offer.models (US-REC-007)', () => {
  const offer = (over: Partial<IOffer> = {}): IOffer => ({
    id: 'o1',
    offerReferenceNumber: 'OFF-001',
    applicantId: 'a1',
    vacancyId: 'v1',
    status: 'Draft',
    offeredPosition: 'Senior Engineer',
    salaryAmount: 120000,
    currency: 'USD',
    salaryFrequency: 'Annual',
    startDate: '2026-07-01',
    expiryDate: '2026-06-22',
    version: 1,
    createdAt: '2026-06-15T10:00:00Z',
    ...over,
  });

  // ─── Status badge mapping (FR-6 / §8) ─────────────────────

  it('maps every status to a badge class', () => {
    const statuses: OfferStatus[] = [
      'Draft',
      'Sent',
      'Accepted',
      'Declined',
      'Expired',
      'Withdrawn',
    ];
    for (const s of statuses) {
      expect(offerStatusBadge(s)).toBe(OFFER_STATUS_BADGE[s]);
    }
  });

  it('Withdrawn badge carries a strikethrough (§8)', () => {
    expect(offerStatusBadge('Withdrawn')).toContain('line-through');
  });

  it('Sent/Accepted/Declined/Expired badges use the §8 colors', () => {
    expect(offerStatusBadge('Sent')).toContain('blue');
    expect(offerStatusBadge('Accepted')).toContain('green');
    expect(offerStatusBadge('Declined')).toContain('red');
    expect(offerStatusBadge('Expired')).toContain('orange');
  });

  it('falls back to the Draft badge for an unknown status', () => {
    expect(offerStatusBadge('Mystery')).toBe(OFFER_STATUS_BADGE.Draft);
  });

  it('labels a status and tolerates unknowns', () => {
    expect(offerStatusLabel('Sent')).toBe('Sent');
    expect(offerStatusLabel('Mystery')).toBe('Mystery');
  });

  // ─── Frequency suffix ─────────────────────────────────────

  it('returns the pay-frequency suffix', () => {
    expect(salaryFrequencySuffix('Annual')).toBe('/ yr');
    expect(salaryFrequencySuffix('Monthly')).toBe('/ mo');
    expect(salaryFrequencySuffix('Hourly')).toBe('/ hr');
    expect(salaryFrequencySuffix('Nonsense')).toBe('');
  });

  // ─── Lifecycle gates (FR-5 / AC-3 / FR-8 / BR-7) ──────────

  it('canSendOffer only for Draft', () => {
    expect(canSendOffer(offer({ status: 'Draft' }))).toBeTrue();
    expect(canSendOffer(offer({ status: 'Sent' }))).toBeFalse();
    expect(canSendOffer(null)).toBeFalse();
  });

  it('canRespondToOffer only for Sent (AC-3)', () => {
    expect(canRespondToOffer(offer({ status: 'Sent' }))).toBeTrue();
    expect(canRespondToOffer(offer({ status: 'Draft' }))).toBeFalse();
    expect(canRespondToOffer(offer({ status: 'Accepted' }))).toBeFalse();
  });

  it('canWithdrawOffer only before acceptance — Draft or Sent (FR-8)', () => {
    expect(canWithdrawOffer(offer({ status: 'Draft' }))).toBeTrue();
    expect(canWithdrawOffer(offer({ status: 'Sent' }))).toBeTrue();
    expect(canWithdrawOffer(offer({ status: 'Accepted' }))).toBeFalse();
    expect(canWithdrawOffer(offer({ status: 'Declined' }))).toBeFalse();
    expect(canWithdrawOffer(offer({ status: 'Withdrawn' }))).toBeFalse();
  });

  // ─── Default expiry (BR-6) ────────────────────────────────

  it('defaultExpiryDate is +7 days by default (BR-6)', () => {
    const from = new Date(2026, 5, 15); // 2026-06-15 local
    expect(defaultExpiryDate(from)).toBe('2026-06-22');
  });

  it('defaultExpiryDate honors a custom day offset', () => {
    const from = new Date(2026, 0, 1);
    expect(defaultExpiryDate(from, 10)).toBe('2026-01-11');
  });

  it('toIsoDate zero-pads month and day', () => {
    expect(toIsoDate(new Date(2026, 2, 5))).toBe('2026-03-05');
  });

  // ─── Compensation formatting ──────────────────────────────

  it('formats compensation with currency, grouped amount, and suffix', () => {
    expect(
      formatCompensation({
        salaryAmount: 120000,
        currency: 'USD',
        salaryFrequency: 'Annual',
      }),
    ).toBe('USD 120,000 / yr');
  });

  it('formats monthly compensation', () => {
    expect(
      formatCompensation({
        salaryAmount: 8000,
        currency: 'EUR',
        salaryFrequency: 'Monthly',
      }),
    ).toBe('EUR 8,000 / mo');
  });
});
