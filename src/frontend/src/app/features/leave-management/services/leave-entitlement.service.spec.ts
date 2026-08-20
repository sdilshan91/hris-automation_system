import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { LeaveEntitlementService } from './leave-entitlement.service';
import {
  IBulkEntitlementResponse,
  EntitlementRuleWire,
  EntitlementOverrideWire,
  EffectiveEntitlementWire,
} from '../models/leave-entitlement.models';
import { environment } from '../../../../environments/environment';
import { HttpErrorResponse } from '@angular/common/http';

describe('LeaveEntitlementService', () => {
  let service: LeaveEntitlementService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/leave-entitlements`;

  // REAL wire shapes (unwrapped from the ApiResponse envelope). The rule/override DTOs carry `id` (NOT
  // `ruleId`/`overrideId`), no `tenantId`, and the rule DTO carries an unused `jobLevelId` — the drift the
  // mappers close.
  const wireRule: EntitlementRuleWire = {
    id: 'rule-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: null,
    jobTitleName: null,
    jobLevelId: null,
    employmentType: 'FullTime',
    tenureMinMonths: null,
    tenureMaxMonths: null,
    entitlementDays: 25,
    priority: 5,
    effectiveFrom: '2026-01-01T00:00:00Z',
    effectiveTo: null,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const wireOverride: EntitlementOverrideWire = {
    id: 'ov-1',
    employeeId: 'emp-1',
    employeeName: null,
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    leaveYear: 2026,
    entitlementDays: 30,
    reason: 'Senior adjustment',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(LeaveEntitlementService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // ─── Rules CRUD ─────────────────────────────────────────

  describe('getRules', () => {
    it('should GET all rules and map wire `id` to `ruleId` (fails against un-migrated code)', () => {
      service.getRules().subscribe(rules => {
        expect(rules.length).toBe(1);
        // The un-migrated service returned the wire verbatim, so `ruleId` was undefined here.
        expect(rules[0].ruleId).toBe('rule-1');
        expect(rules[0].leaveTypeName).toBe('Annual Leave');
        expect(rules[0].employmentType).toBe('FullTime');
        // Wire-only field must not leak into the view-model.
        expect((rules[0] as unknown as Record<string, unknown>)['jobLevelId']).toBeUndefined();
        expect((rules[0] as unknown as Record<string, unknown>)['id']).toBeUndefined();
      });

      const req = httpMock.expectOne(`${baseUrl}/rules`);
      expect(req.request.method).toBe('GET');
      req.flush([wireRule]);
    });

    it('should pass filter params when provided', () => {
      service.getRules({
        leaveTypeId: 'lt-1',
        departmentId: 'dept-1',
        employmentType: 'FullTime',
        activeOnly: true,
      }).subscribe();

      const req = httpMock.expectOne(r =>
        r.url === `${baseUrl}/rules` &&
        r.params.get('leaveTypeId') === 'lt-1' &&
        r.params.get('departmentId') === 'dept-1' &&
        r.params.get('employmentType') === 'FullTime' &&
        r.params.get('activeOnly') === 'true'
      );
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  describe('createRule', () => {
    it('should POST a new rule and map the wire response', () => {
      const request = {
        leaveTypeId: 'lt-1',
        departmentId: 'dept-1',
        entitlementDays: 25,
        priority: 5,
        effectiveFrom: '2026-01-01',
      };

      service.createRule(request).subscribe(rule => {
        expect(rule.ruleId).toBe('rule-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/rules`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.leaveTypeId).toBe('lt-1');
      req.flush(wireRule);
    });
  });

  describe('updateRule', () => {
    it('should PUT to update a rule and map the wire response', () => {
      const request = {
        leaveTypeId: 'lt-1',
        entitlementDays: 30,
        priority: 5,
        effectiveFrom: '2026-01-01',
      };

      service.updateRule('rule-1', request).subscribe(rule => {
        expect(rule.ruleId).toBe('rule-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/rules/rule-1`);
      expect(req.request.method).toBe('PUT');
      req.flush(wireRule);
    });
  });

  describe('updateRuleDays', () => {
    it('should PATCH to update only the days and map the wire response', () => {
      service.updateRuleDays('rule-1', { entitlementDays: 30 }).subscribe(rule => {
        expect(rule.ruleId).toBe('rule-1');
        expect(rule.entitlementDays).toBe(30);
      });

      const req = httpMock.expectOne(`${baseUrl}/rules/rule-1/days`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body.entitlementDays).toBe(30);
      req.flush({ ...wireRule, entitlementDays: 30 });
    });
  });

  describe('deleteRule', () => {
    it('should DELETE a rule', () => {
      service.deleteRule('rule-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/rules/rule-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ─── Overrides ──────────────────────────────────────────

  describe('getOverrides', () => {
    it('should GET overrides and map wire `id` to `overrideId` (fails against un-migrated code)', () => {
      service.getOverrides('emp-1').subscribe(overrides => {
        expect(overrides.length).toBe(1);
        expect(overrides[0].overrideId).toBe('ov-1');
        expect(overrides[0].entitlementDays).toBe(30);
        expect((overrides[0] as unknown as Record<string, unknown>)['id']).toBeUndefined();
      });

      const req = httpMock.expectOne(r =>
        r.url === `${baseUrl}/overrides` &&
        r.params.get('employeeId') === 'emp-1'
      );
      expect(req.request.method).toBe('GET');
      req.flush([wireOverride]);
    });

    it('should pass leaveYear param when provided', () => {
      service.getOverrides('emp-1', 2026).subscribe();

      const req = httpMock.expectOne(r =>
        r.url === `${baseUrl}/overrides` &&
        r.params.get('employeeId') === 'emp-1' &&
        r.params.get('leaveYear') === '2026'
      );
      req.flush([]);
    });
  });

  describe('upsertOverride', () => {
    it('should POST an override with employeeId in body and map the wire response', () => {
      service.upsertOverride('emp-1', {
        leaveTypeId: 'lt-1',
        leaveYear: 2026,
        entitlementDays: 30,
        reason: 'test',
      }).subscribe(ov => {
        expect(ov.overrideId).toBe('ov-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/overrides`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.employeeId).toBe('emp-1');
      expect(req.request.body.leaveTypeId).toBe('lt-1');
      req.flush(wireOverride);
    });
  });

  describe('deleteOverride', () => {
    it('should DELETE an override', () => {
      service.deleteOverride('ov-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/overrides/ov-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  // ─── Computed effective ─────────────────────────────────

  describe('getEffectiveEntitlements', () => {
    it('derives entitlementDays from the wire prorated value and normalizes source (fails against un-migrated code)', () => {
      // The wire (`EffectiveEntitlementDto`) has NO flat `entitlementDays` — it splits into
      // base/prorated — and sends `source` as `"rule:{id}"`. The un-migrated service returned this verbatim,
      // so `entitlementDays` was undefined and `source` was the raw `"rule:rule-1"` string.
      const wireEffective: EffectiveEntitlementWire[] = [
        {
          employeeId: 'emp-1',
          leaveTypeId: 'lt-1',
          leaveTypeName: 'Annual Leave',
          leaveYear: 2026,
          baseEntitlementDays: 25,
          proratedEntitlementDays: 20,
          currentBalance: 12,
          source: 'rule:rule-1',
        },
      ];

      service.getEffectiveEntitlements('emp-1').subscribe(eff => {
        expect(eff.length).toBe(1);
        expect(eff[0].entitlementDays).toBe(20);
        expect(eff[0].source).toBe('rule');
        // Wire-only fields must not leak into the view-model.
        expect((eff[0] as unknown as Record<string, unknown>)['baseEntitlementDays']).toBeUndefined();
        expect((eff[0] as unknown as Record<string, unknown>)['currentBalance']).toBeUndefined();
      });

      const req = httpMock.expectOne(r =>
        r.url === `${baseUrl}/compute-effective` &&
        r.params.get('employeeId') === 'emp-1'
      );
      expect(req.request.method).toBe('GET');
      req.flush(wireEffective);
    });

    it('normalizes the override and leave_type_default source values to the UI union', () => {
      const wireEffective: EffectiveEntitlementWire[] = [
        {
          employeeId: 'emp-1',
          leaveTypeId: 'lt-1',
          leaveTypeName: 'Annual Leave',
          proratedEntitlementDays: 30,
          source: 'override',
        },
        {
          employeeId: 'emp-1',
          leaveTypeId: 'lt-2',
          leaveTypeName: 'Sick Leave',
          proratedEntitlementDays: 10,
          source: 'leave_type_default',
        },
      ];

      service.getEffectiveEntitlements('emp-1').subscribe(eff => {
        expect(eff[0].source).toBe('override');
        expect(eff[1].source).toBe('default');
      });

      const req = httpMock.expectOne(r => r.url === `${baseUrl}/compute-effective`);
      req.flush(wireEffective);
    });
  });

  // ─── Bulk ───────────────────────────────────────────────

  describe('bulkAssign', () => {
    it('should POST a bulk assignment request', () => {
      const response: IBulkEntitlementResponse = {
        totalProcessed: 3,
        totalSuccess: 2,
        totalFailed: 1,
      };

      service.bulkAssign({
        leaveTypeId: 'lt-1',
        entitlementDays: 25,
        employeeIds: ['emp-1', 'emp-2', 'emp-3'],
        leaveYear: 2026,
        reason: 'Bulk update',
      }).subscribe(res => {
        expect(res.totalSuccess).toBe(2);
        expect(res.totalFailed).toBe(1);
      });

      const req = httpMock.expectOne(`${baseUrl}/bulk`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.employeeIds.length).toBe(3);
      req.flush(response);
    });
  });
});

// ─── parseError (pure function -- separate describe, no httpMock.verify) ────

describe('LeaveEntitlementService.parseError (pure function)', () => {
  it('should extract message from error body', () => {
    const err = { error: { message: 'Duplicate rule' } } as HttpErrorResponse;
    expect(LeaveEntitlementService.parseError(err)).toBe('Duplicate rule');
  });

  it('should return fallback for unknown error shape', () => {
    const err = { error: 'plain string' } as HttpErrorResponse;
    expect(LeaveEntitlementService.parseError(err)).toBe('An unexpected error occurred.');
  });

  it('should return fallback for null error body', () => {
    const err = { error: null } as HttpErrorResponse;
    expect(LeaveEntitlementService.parseError(err)).toBe('An unexpected error occurred.');
  });
});
