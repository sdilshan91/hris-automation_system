import {
  diffAuditJson,
  diffAuditRaw,
  parseAuditJson,
} from './audit-log.models';

/**
 * US-ADM-008 AC-3 / FR-3: unit tests for the pure JSON-diff helper that backs the
 * visual diff in the detail panel. Covers added / removed / modified detection,
 * unchanged fields, and that pre-masked sensitive values ("***REDACTED***") are
 * rendered as-is (never unmasked).
 */
describe('audit-log diff helper', () => {
  describe('parseAuditJson', () => {
    it('returns an empty object for null/blank/invalid input', () => {
      expect(parseAuditJson(null)).toEqual({});
      expect(parseAuditJson(undefined)).toEqual({});
      expect(parseAuditJson('')).toEqual({});
      expect(parseAuditJson('   ')).toEqual({});
      expect(parseAuditJson('{ not json')).toEqual({});
    });

    it('parses a JSON object string into a record', () => {
      expect(parseAuditJson('{"name":"John","age":30}')).toEqual({
        name: 'John',
        age: 30,
      });
    });

    it('wraps a bare array/scalar under a synthetic "value" key', () => {
      expect(parseAuditJson('[1,2,3]')).toEqual({ value: [1, 2, 3] });
    });
  });

  describe('diffAuditJson', () => {
    it('detects a modified field', () => {
      const diff = diffAuditJson({ name: 'John' }, { name: 'Jane' });
      const row = diff.find((d) => d.key === 'name')!;
      expect(row.changeType).toBe('modified');
      expect(row.oldVal).toBe('John');
      expect(row.newVal).toBe('Jane');
    });

    it('detects an added field (present only in after)', () => {
      const diff = diffAuditJson({}, { email: 'a@b.com' });
      const row = diff.find((d) => d.key === 'email')!;
      expect(row.changeType).toBe('added');
      expect(row.oldVal).toBeNull();
      expect(row.newVal).toBe('a@b.com');
    });

    it('detects a removed field (present only in before)', () => {
      const diff = diffAuditJson({ phone: '555' }, {});
      const row = diff.find((d) => d.key === 'phone')!;
      expect(row.changeType).toBe('removed');
      expect(row.oldVal).toBe('555');
      expect(row.newVal).toBeNull();
    });

    it('marks equal fields as unchanged', () => {
      const diff = diffAuditJson({ id: 'X' }, { id: 'X' });
      expect(diff.find((d) => d.key === 'id')!.changeType).toBe('unchanged');
    });

    it('returns keys in stable sorted order', () => {
      const diff = diffAuditJson(
        { zebra: 1, alpha: 2 },
        { zebra: 1, alpha: 9 }
      );
      expect(diff.map((d) => d.key)).toEqual(['alpha', 'zebra']);
    });

    it('stringifies nested objects for comparison', () => {
      const diff = diffAuditJson(
        { addr: { city: 'NY' } },
        { addr: { city: 'LA' } }
      );
      const row = diff.find((d) => d.key === 'addr')!;
      expect(row.changeType).toBe('modified');
      expect(row.oldVal).toBe('{"city":"NY"}');
      expect(row.newVal).toBe('{"city":"LA"}');
    });

    it('renders pre-masked sensitive values as-is without unmasking', () => {
      const diff = diffAuditJson(
        { bankAccount: '***REDACTED***' },
        { bankAccount: '***REDACTED***' }
      );
      const row = diff.find((d) => d.key === 'bankAccount')!;
      // Both sides masked & equal → unchanged, but value stays masked.
      expect(row.changeType).toBe('unchanged');
      expect(row.oldVal).toBe('***REDACTED***');
      expect(row.newVal).toBe('***REDACTED***');
    });

    it('keeps a modified sensitive field masked on both sides', () => {
      const diff = diffAuditJson(
        { passwordHash: '***REDACTED***' },
        { passwordHash: '***REDACTED***', name: 'New' }
      );
      const pwd = diff.find((d) => d.key === 'passwordHash')!;
      expect(pwd.oldVal).toBe('***REDACTED***');
      expect(pwd.newVal).toBe('***REDACTED***');
      const added = diff.find((d) => d.key === 'name')!;
      expect(added.changeType).toBe('added');
    });
  });

  describe('diffAuditRaw', () => {
    it('parses two raw JSON strings and diffs them end-to-end', () => {
      const diff = diffAuditRaw('{"name":"John"}', '{"name":"Jane"}');
      const row = diff.find((d) => d.key === 'name')!;
      expect(row.changeType).toBe('modified');
      expect(row.oldVal).toBe('John');
      expect(row.newVal).toBe('Jane');
    });

    it('treats a null before as all-added (create event)', () => {
      const diff = diffAuditRaw(null, '{"name":"John"}');
      expect(diff.find((d) => d.key === 'name')!.changeType).toBe('added');
    });

    it('treats a null after as all-removed (delete event)', () => {
      const diff = diffAuditRaw('{"name":"John"}', null);
      expect(diff.find((d) => d.key === 'name')!.changeType).toBe('removed');
    });
  });
});
