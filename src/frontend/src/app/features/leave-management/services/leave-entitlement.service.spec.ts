import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { LeaveEntitlementService } from './leave-entitlement.service';
import {
  IEntitlementRule,
  IEntitlementOverride,
  IEffectiveEntitlement,
  IBulkEntitlementResponse,
} from '../models/leave-entitlement.models';
import { environment } from '../../../../environments/environment';
import { HttpErrorResponse } from '@angular/common/http';

describe('LeaveEntitlementService', () => {
  let service: LeaveEntitlementService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/leave-entitlements`;

  const mockRule: IEntitlementRule = {
    ruleId: 'rule-1',
    tenantId: 'tenant-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: null,
    jobTitleName: null,
    employmentType: 'Full-Time',
    tenureMinMonths: null,
    tenureMaxMonths: null,
    entitlementDays: 25,
    priority: 5,
    effectiveFrom: '2026-01-01',
    effectiveTo: null,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const mockOverride: IEntitlementOverride = {
    overrideId: 'ov-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
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
    it('should GET all rules', () => {
      service.getRules().subscribe(rules => {
        expect(rules.length).toBe(1);
        expect(rules[0].ruleId).toBe('rule-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/rules`);
      expect(req.request.method).toBe('GET');
      req.flush([mockRule]);
    });

    it('should pass filter params when provided', () => {
      service.getRules({
        leaveTypeId: 'lt-1',
        departmentId: 'dept-1',
        employmentType: 'Full-Time',
        activeOnly: true,
      }).subscribe();

      const req = httpMock.expectOne(r =>
        r.url === `${baseUrl}/rules` &&
        r.params.get('leaveTypeId') === 'lt-1' &&
        r.params.get('departmentId') === 'dept-1' &&
        r.params.get('employmentType') === 'Full-Time' &&
        r.params.get('activeOnly') === 'true'
      );
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });
  });

  describe('createRule', () => {
    it('should POST a new rule', () => {
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
      req.flush(mockRule);
    });
  });

  describe('updateRule', () => {
    it('should PUT to update a rule', () => {
      const request = {
        leaveTypeId: 'lt-1',
        entitlementDays: 30,
        priority: 5,
        effectiveFrom: '2026-01-01',
      };

      service.updateRule('rule-1', request).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/rules/rule-1`);
      expect(req.request.method).toBe('PUT');
      req.flush(mockRule);
    });
  });

  describe('updateRuleDays', () => {
    it('should PATCH to update only the days', () => {
      service.updateRuleDays('rule-1', { entitlementDays: 30 }).subscribe(rule => {
        expect(rule.entitlementDays).toBe(30);
      });

      const req = httpMock.expectOne(`${baseUrl}/rules/rule-1/days`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body.entitlementDays).toBe(30);
      req.flush({ ...mockRule, entitlementDays: 30 });
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
    it('should GET overrides for an employee', () => {
      service.getOverrides('emp-1').subscribe(overrides => {
        expect(overrides.length).toBe(1);
        expect(overrides[0].overrideId).toBe('ov-1');
      });

      const req = httpMock.expectOne(r =>
        r.url === `${baseUrl}/overrides` &&
        r.params.get('employeeId') === 'emp-1'
      );
      expect(req.request.method).toBe('GET');
      req.flush([mockOverride]);
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
    it('should POST an override with employeeId in body', () => {
      service.upsertOverride('emp-1', {
        leaveTypeId: 'lt-1',
        leaveYear: 2026,
        entitlementDays: 30,
        reason: 'test',
      }).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/overrides`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.employeeId).toBe('emp-1');
      expect(req.request.body.leaveTypeId).toBe('lt-1');
      req.flush(mockOverride);
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
    it('should GET computed effective entitlements for an employee', () => {
      const mockEffective: IEffectiveEntitlement[] = [
        {
          employeeId: 'emp-1',
          leaveTypeId: 'lt-1',
          leaveTypeName: 'Annual Leave',
          entitlementDays: 25,
          source: 'rule',
          ruleId: 'rule-1',
          overrideId: null,
        },
      ];

      service.getEffectiveEntitlements('emp-1').subscribe(eff => {
        expect(eff.length).toBe(1);
        expect(eff[0].source).toBe('rule');
      });

      const req = httpMock.expectOne(r =>
        r.url === `${baseUrl}/compute-effective` &&
        r.params.get('employeeId') === 'emp-1'
      );
      expect(req.request.method).toBe('GET');
      req.flush(mockEffective);
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
