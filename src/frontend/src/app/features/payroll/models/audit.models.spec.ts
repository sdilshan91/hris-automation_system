import {
  auditActionLabel,
  auditDotClass,
  buildDiff,
  emptyAuditFilters,
  hasDiff,
  parseSnapshot,
  IAuditEntry,
} from './audit.models';

describe('audit.models helpers', () => {
  describe('buildDiff', () => {
    it('flags a key only in after as added, only in before as removed', () => {
      const rows = buildDiff({ removedKey: 1 }, { addedKey: 2 });
      const added = rows.find((r) => r.field === 'addedKey');
      const removed = rows.find((r) => r.field === 'removedKey');
      expect(added?.kind).toBe('added');
      expect(added?.before).toBeNull();
      expect(added?.after).toBe('2');
      expect(removed?.kind).toBe('removed');
      expect(removed?.before).toBe('1');
      expect(removed?.after).toBeNull();
    });

    it('flags a changed value as modified and an equal value as unchanged', () => {
      const rows = buildDiff(
        { rate: 10, name: 'HRA' },
        { rate: 12, name: 'HRA' },
      );
      const rate = rows.find((r) => r.field === 'rate');
      const name = rows.find((r) => r.field === 'name');
      expect(rate?.kind).toBe('modified');
      expect(rate?.before).toBe('10');
      expect(rate?.after).toBe('12');
      expect(name?.kind).toBe('unchanged');
    });

    it('returns keys sorted so the two columns line up', () => {
      const rows = buildDiff({ b: 1, a: 1 }, { c: 1 });
      expect(rows.map((r) => r.field)).toEqual(['a', 'b', 'c']);
    });

    it('treats a null before (create) as all-added and null after (delete) as all-removed', () => {
      const created = buildDiff(null, { x: 1 });
      expect(created.every((r) => r.kind === 'added')).toBeTrue();

      const deleted = buildDiff({ x: 1 }, null);
      expect(deleted.every((r) => r.kind === 'removed')).toBeTrue();
    });

    it('returns [] when both snapshots are null', () => {
      expect(buildDiff(null, null)).toEqual([]);
    });

    it('stringifies nested objects/arrays as compact JSON', () => {
      const rows = buildDiff({ slabs: [1, 2] }, { slabs: [1, 2, 3] });
      const slabs = rows[0];
      expect(slabs.kind).toBe('modified');
      expect(slabs.before).toBe('[1,2]');
      expect(slabs.after).toBe('[1,2,3]');
    });

    it('renders a null field value as the em-dash sentinel', () => {
      const rows = buildDiff({ ref: 'x' }, { ref: null });
      expect(rows[0].kind).toBe('modified');
      expect(rows[0].after).toBe('—');
    });
  });

  describe('parseSnapshot', () => {
    it('parses a JSON object string into an object', () => {
      expect(parseSnapshot('{"status":"Approved"}')).toEqual({
        status: 'Approved',
      });
    });

    it('returns null for null/empty input', () => {
      expect(parseSnapshot(null)).toBeNull();
      expect(parseSnapshot(undefined)).toBeNull();
      expect(parseSnapshot('')).toBeNull();
    });

    it('returns null for malformed JSON', () => {
      expect(parseSnapshot('{not json')).toBeNull();
    });

    it('returns null for a non-object JSON value (array/scalar)', () => {
      expect(parseSnapshot('[1,2]')).toBeNull();
      expect(parseSnapshot('42')).toBeNull();
    });
  });

  describe('hasDiff', () => {
    const base: IAuditEntry = {
      id: 'a-1',
      timestamp: '2026-06-01T10:00:00Z',
      action: 'SalaryComponent.Updated',
      resourceType: 'SalaryComponent',
      resourceId: 'sc-1',
      actorUserId: 'u-1',
      actorEmployeeNo: 'EMP001',
      before: null,
      after: null,
      ipAddress: null,
      userAgent: null,
      traceId: null,
    };

    it('is false when both before and after are null', () => {
      expect(hasDiff(base)).toBeFalse();
    });

    it('is true when either before or after is a parseable object string', () => {
      expect(hasDiff({ ...base, after: '{"x":1}' })).toBeTrue();
      expect(hasDiff({ ...base, before: '{"x":1}' })).toBeTrue();
    });

    it('is false when before/after are non-object/malformed strings', () => {
      expect(hasDiff({ ...base, before: 'oops', after: null })).toBeFalse();
    });
  });

  describe('auditActionLabel', () => {
    it('uses the known option label for a standard action', () => {
      expect(auditActionLabel('PayrollRun.Finalized')).toBe('Run finalized');
    });

    it('de-camelCases an unknown action into a readable label', () => {
      expect(auditActionLabel('PayrollRun.SomethingNew')).toBe(
        'Payroll Run · Something New',
      );
    });

    it('handles an action with no verb', () => {
      expect(auditActionLabel('Mystery')).toBe('Mystery');
    });
  });

  describe('auditDotClass', () => {
    it('maps known resource prefixes to distinct colours', () => {
      expect(auditDotClass('PayrollRun.Approved')).toBe('bg-violet-500');
      expect(auditDotClass('EmployeeSalary.Assigned')).toBe('bg-emerald-500');
      expect(auditDotClass('PayslipEmail.Sent')).toBe('bg-teal-500');
    });

    it('falls back to neutral for an unknown resource', () => {
      expect(auditDotClass('Unknown.Thing')).toBe('bg-neutral-400');
    });
  });

  describe('emptyAuditFilters', () => {
    it('returns an all-null filter set', () => {
      expect(emptyAuditFilters()).toEqual({
        fromUtc: null,
        toUtc: null,
        action: null,
        actorUserId: null,
        resourceType: null,
        resourceId: null,
      });
    });

    it('returns a fresh object each call (no shared reference)', () => {
      const a = emptyAuditFilters();
      const b = emptyAuditFilters();
      expect(a).not.toBe(b);
    });
  });
});
