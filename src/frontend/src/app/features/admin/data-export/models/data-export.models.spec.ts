import {
  EXPORT_ENTITY_CATALOGUE,
  IExportRecord,
  allEntityCodes,
  canDownload,
  exportStatusClass,
  formatFileSize,
  isInProgress,
} from './data-export.models';

describe('data-export models', () => {
  function record(over: Partial<IExportRecord> = {}): IExportRecord {
    return {
      id: 'x',
      scope: 'Full export',
      status: 'Completed',
      requestedAt: '2026-06-17T00:00:00Z',
      completedAt: '2026-06-17T00:05:00Z',
      fileSizeBytes: 1024,
      expiresAt: '2026-06-20T00:05:00Z',
      downloadAvailable: true,
      ...over,
    };
  }

  it('catalogue covers all 27 brief entities across modules', () => {
    expect(allEntityCodes().length).toBe(27);
    // Spot-check a few codes are present.
    const codes = allEntityCodes();
    expect(codes).toContain('employees');
    expect(codes).toContain('audit_log');
    expect(codes).toContain('payslips');
    // Codes are unique.
    expect(new Set(codes).size).toBe(codes.length);
    // Grouped by module.
    expect(EXPORT_ENTITY_CATALOGUE.length).toBeGreaterThan(1);
  });

  it('isInProgress is true only for Queued / Processing', () => {
    expect(isInProgress('Queued')).toBeTrue();
    expect(isInProgress('Processing')).toBeTrue();
    expect(isInProgress('Completed')).toBeFalse();
    expect(isInProgress('Failed')).toBeFalse();
    expect(isInProgress('Expired')).toBeFalse();
  });

  it('canDownload requires downloadAvailable AND Completed', () => {
    expect(canDownload(record())).toBeTrue();
    expect(canDownload(record({ downloadAvailable: false }))).toBeFalse();
    expect(canDownload(record({ status: 'Processing' }))).toBeFalse();
    expect(
      canDownload(record({ status: 'Expired', downloadAvailable: false }))
    ).toBeFalse();
  });

  it('formatFileSize renders compact human sizes', () => {
    expect(formatFileSize(null)).toBe('—');
    expect(formatFileSize(0)).toBe('0 B');
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(2048)).toBe('2.0 KB');
    expect(formatFileSize(5 * 1024 * 1024)).toBe('5.0 MB');
  });

  it('exportStatusClass maps each status to a distinct badge style', () => {
    expect(exportStatusClass('Completed')).toContain('green');
    expect(exportStatusClass('Processing')).toContain('blue');
    expect(exportStatusClass('Queued')).toContain('amber');
    expect(exportStatusClass('Failed')).toContain('red');
    expect(exportStatusClass('Expired')).toContain('neutral');
  });
});
