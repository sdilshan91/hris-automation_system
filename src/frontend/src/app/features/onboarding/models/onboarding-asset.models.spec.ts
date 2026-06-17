import {
  IAsset,
  assetOptionLabel,
  conditionChipClass,
  formatAssetFileSize,
  isFutureDate,
  issuedToast,
  todayIso,
  validateAcknowledgmentFile,
} from './onboarding-asset.models';

describe('onboarding-asset.models', () => {
  const fakeFile = (type: string, size: number): File => {
    const f = new File(['x'], 'f', { type });
    Object.defineProperty(f, 'size', { value: size });
    return f;
  };

  describe('validateAcknowledgmentFile (FR-6 / NFR-4)', () => {
    it('accepts a valid PDF within 10 MB', () => {
      expect(validateAcknowledgmentFile(fakeFile('application/pdf', 1024))).toBeNull();
    });

    it('accepts JPEG and PNG', () => {
      expect(validateAcknowledgmentFile(fakeFile('image/jpeg', 1024))).toBeNull();
      expect(validateAcknowledgmentFile(fakeFile('image/png', 1024))).toBeNull();
    });

    it('rejects an unsupported MIME type (e.g. Word doc)', () => {
      const msg = validateAcknowledgmentFile(fakeFile('application/msword', 1024));
      expect(msg).toContain('Unsupported file type');
    });

    it('rejects files over 10 MB', () => {
      const msg = validateAcknowledgmentFile(
        fakeFile('application/pdf', 10 * 1024 * 1024 + 1),
      );
      expect(msg).toContain('too large');
    });

    it('rejects an empty file', () => {
      expect(validateAcknowledgmentFile(fakeFile('application/pdf', 0))).toContain('empty');
    });
  });

  describe('formatAssetFileSize', () => {
    it('formats bytes, KB, and MB', () => {
      expect(formatAssetFileSize(512)).toBe('512 B');
      expect(formatAssetFileSize(2048)).toBe('2.0 KB');
      expect(formatAssetFileSize(3 * 1024 * 1024)).toBe('3.0 MB');
    });
  });

  describe('conditionChipClass (UI/UX §8 colours)', () => {
    it('maps each condition to its colour class', () => {
      expect(conditionChipClass('New')).toBe('chip-condition-new');
      expect(conditionChipClass('Good')).toBe('chip-condition-good');
      expect(conditionChipClass('Fair')).toBe('chip-condition-fair');
      expect(conditionChipClass('Poor')).toBe('chip-condition-poor');
    });
  });

  describe('todayIso + isFutureDate (Data Req §7 — no future date)', () => {
    const fixed = new Date(2026, 5, 17); // 2026-06-17 local

    it('todayIso renders zero-padded yyyy-MM-dd', () => {
      expect(todayIso(new Date(2026, 0, 5))).toBe('2026-01-05');
      expect(todayIso(fixed)).toBe('2026-06-17');
    });

    it('isFutureDate is true only strictly after today', () => {
      expect(isFutureDate('2026-06-18', fixed)).toBeTrue();
      expect(isFutureDate('2026-06-17', fixed)).toBeFalse();
      expect(isFutureDate('2026-06-16', fixed)).toBeFalse();
    });

    it('isFutureDate treats empty as not-future', () => {
      expect(isFutureDate('', fixed)).toBeFalse();
    });
  });

  describe('issuedToast (AC-2)', () => {
    it('pluralizes the noun and names the employee', () => {
      expect(issuedToast(1, 'Grace Hopper')).toBe('1 asset issued to Grace Hopper.');
      expect(issuedToast(3, 'Grace Hopper')).toBe('3 assets issued to Grace Hopper.');
    });
  });

  describe('assetOptionLabel', () => {
    const asset = (over: Partial<IAsset> = {}): IAsset => ({
      assetId: 'a-1',
      assetType: 'Laptop',
      assetTag: 'LAP-1',
      serialNumber: 'SN-9',
      brand: 'Dell',
      model: 'XPS',
      condition: 'New',
      status: 'available',
      ...over,
    });

    it('joins tag, serial and make', () => {
      expect(assetOptionLabel(asset())).toBe('LAP-1 · S/N SN-9 · Dell XPS');
    });

    it('omits missing optional parts', () => {
      expect(assetOptionLabel(asset({ serialNumber: null, brand: null, model: null }))).toBe('LAP-1');
    });
  });
});
