import {
  auditActionLabel,
  auditDotClass,
  buildDiff,
  emptyAuditFilters,
  hasDiff,
  mapAuditEntry,
  mapAuditPage,
  mapPayrollHistoryRun,
  parseSnapshot,
  AuditEntryWire,
  IAuditEntry,
  PayrollHistoryRunWire,
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

  // ─── D1 wire → view-model mappers ────────────────────────────

  describe('mapAuditEntry', () => {
    it('maps every field of a full PayrollAuditEntryDto', () => {
      const wire: AuditEntryWire = {
        id: 'a-1',
        tenantId: 't-1',
        timestamp: '2026-05-31T10:00:00Z',
        actorUserId: 'u-2',
        actorEmployeeNo: 'EMP002',
        action: 'PayrollRun.Finalized',
        resourceType: 'PayrollRun',
        resourceId: 'r-1',
        before: '{"status":"Approved"}',
        after: '{"status":"Finalized"}',
        ipAddress: '10.0.0.1',
        userAgent: 'jasmine',
        traceId: 'tr-1',
      };
      expect(mapAuditEntry(wire)).toEqual({
        id: 'a-1',
        tenantId: 't-1',
        timestamp: '2026-05-31T10:00:00Z',
        actorUserId: 'u-2',
        actorEmployeeNo: 'EMP002',
        action: 'PayrollRun.Finalized',
        resourceType: 'PayrollRun',
        resourceId: 'r-1',
        before: '{"status":"Approved"}',
        after: '{"status":"Finalized"}',
        ipAddress: '10.0.0.1',
        userAgent: 'jasmine',
        traceId: 'tr-1',
      });
    });

    it('keeps every absent evidence field as null, never an empty string', () => {
      // The audit trail is append-only evidence: '' would assert the server sent a blank value,
      // which is a different claim from "the server sent nothing".
      const mapped = mapAuditEntry({ id: 'a-2', action: 'PayrollRun.Initiated' });
      expect(mapped.actorUserId).toBeNull();
      expect(mapped.actorEmployeeNo).toBeNull();
      expect(mapped.before).toBeNull();
      expect(mapped.after).toBeNull();
      expect(mapped.ipAddress).toBeNull();
      expect(mapped.userAgent).toBeNull();
      expect(mapped.traceId).toBeNull();
      expect(mapped.resourceType).toBeNull();
      expect(mapped.resourceId).toBeNull();
      expect(mapped.tenantId).toBeNull();
    });

    it('produces an entry the diff helpers can consume', () => {
      const mapped: IAuditEntry = mapAuditEntry({
        id: 'a-3',
        action: 'SalaryComponent.Updated',
        before: '{"amount":100}',
        after: '{"amount":200}',
      });
      expect(hasDiff(mapped)).toBeTrue();
      expect(buildDiff(parseSnapshot(mapped.before), parseSnapshot(mapped.after))).toEqual([
        { field: 'amount', before: '100', after: '200', kind: 'modified' },
      ]);
    });
  });

  describe('mapPayrollHistoryRun', () => {
    it('maps a full PayrollRunHistoryItemDto', () => {
      const wire: PayrollHistoryRunWire = {
        runId: 'r-1',
        payMonth: 5,
        payYear: 2026,
        period: '2026-05',
        status: 'Finalized',
        employeeCount: 250,
        totalNet: 800000,
        totalGross: 1000000,
        totalDeductions: 200000,
        initiatedBy: 'u-1',
        initiatedAt: '2026-05-25T09:00:00Z',
        approvedBy: 'u-2',
        approvedAt: '2026-05-30T09:00:00Z',
        finalizedAt: '2026-05-31T12:00:00Z',
      };
      const mapped = mapPayrollHistoryRun(wire);
      expect(mapped.status).toBe('Finalized');
      expect(mapped.totalNet).toBe(800000);
      expect(mapped.finalizedAt).toBe('2026-05-31T12:00:00Z');
    });

    it('falls back to the Unknown status sentinel rather than a real lifecycle state', () => {
      // Defaulting to e.g. 'Finalized' would paint a green "done" badge on a run whose status the
      // server never sent (the admin slice shipped exactly this bug with 'terminated').
      const mapped = mapPayrollHistoryRun({ runId: 'r-2' });
      expect(mapped.status).toBe('Unknown' as never);
    });

    it('defaults absent server-computed totals to 0 and absent approval fields to null', () => {
      const mapped = mapPayrollHistoryRun({ runId: 'r-3', payYear: 2026, payMonth: 6 });
      expect(mapped.totalNet).toBe(0);
      expect(mapped.totalGross).toBe(0);
      expect(mapped.totalDeductions).toBe(0);
      expect(mapped.employeeCount).toBe(0);
      // Not-yet-approved / not-yet-finalized stay null, not ''.
      expect(mapped.approvedBy).toBeNull();
      expect(mapped.approvedAt).toBeNull();
      expect(mapped.finalizedAt).toBeNull();
    });
  });

  describe('mapAuditPage', () => {
    it('maps the items and the page meta', () => {
      const page = mapAuditPage({
        items: [{ id: 'a-1', action: 'PayrollRun.Approved' }],
        totalCount: 42,
        page: 2,
        pageSize: 50,
      });
      expect(page.items.length).toBe(1);
      expect(page.items[0].action).toBe('PayrollRun.Approved');
      expect(page.totalCount).toBe(42);
      expect(page.page).toBe(2);
      expect(page.pageSize).toBe(50);
    });

    it('returns an empty page for a null/absent body so the table renders an empty state', () => {
      expect(mapAuditPage(null)).toEqual({ items: [], totalCount: 0, page: 1, pageSize: 0 });
      expect(mapAuditPage({}).items).toEqual([]);
    });
  });
});
