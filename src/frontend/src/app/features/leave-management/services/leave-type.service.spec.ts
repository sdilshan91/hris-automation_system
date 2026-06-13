import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { LeaveTypeService } from './leave-type.service';
import {
  ILeaveType,
  ICreateLeaveTypeRequest,
  IUpdateLeaveTypeRequest,
  IReorderLeaveTypesRequest,
} from '../models/leave-type.models';
import { environment } from '../../../../environments/environment';

describe('LeaveTypeService', () => {
  let service: LeaveTypeService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/leave-types`;

  const mockLeaveType: ILeaveType = {
    leaveTypeId: 'lt-1',
    tenantId: 'tenant-1',
    name: 'Annual Leave',
    code: 'AL',
    color: '#2563eb',
    description: 'Paid annual leave',
    annualEntitlement: 20,
    accrualFrequency: 'monthly',
    carryForwardLimit: 5,
    carryForwardExpiryMonths: 3,
    probationEligible: false,
    documentsRequired: false,
    documentDayThreshold: null,
    encashable: true,
    maxEncashDays: 10,
    halfDayAllowed: true,
    hourlyAllowed: false,
    gender: 'all',
    maxConsecutiveDays: 15,
    negativeBalanceAllowed: false,
    negativeBalanceLimit: null,
    displayOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const mockLeaveType2: ILeaveType = {
    ...mockLeaveType,
    leaveTypeId: 'lt-2',
    name: 'Sick Leave',
    code: 'SL',
    color: '#dc2626',
    description: 'Sick leave with medical certificate',
    annualEntitlement: 10,
    documentsRequired: true,
    documentDayThreshold: 2,
    encashable: false,
    maxEncashDays: null,
    displayOrder: 1,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LeaveTypeService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(LeaveTypeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getLeaveTypes', () => {
    it('should return all leave types for the tenant', () => {
      service.getLeaveTypes().subscribe((types) => {
        expect(types.length).toBe(2);
        expect(types[0].name).toBe('Annual Leave');
        expect(types[1].name).toBe('Sick Leave');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockLeaveType, mockLeaveType2]);
    });

    it('should return an empty array when no leave types exist', () => {
      service.getLeaveTypes().subscribe((types) => {
        expect(types.length).toBe(0);
      });

      const req = httpMock.expectOne(baseUrl);
      req.flush([]);
    });
  });

  describe('getLeaveType', () => {
    it('should return a single leave type by ID', () => {
      service.getLeaveType('lt-1').subscribe((type) => {
        expect(type.leaveTypeId).toBe('lt-1');
        expect(type.name).toBe('Annual Leave');
      });

      const req = httpMock.expectOne(`${baseUrl}/lt-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockLeaveType);
    });
  });

  describe('createLeaveType', () => {
    it('should create a new leave type', () => {
      const request: ICreateLeaveTypeRequest = {
        name: 'Casual Leave',
        code: 'CL',
        color: '#16a34a',
        annualEntitlement: 5,
        accrualFrequency: 'upfront',
        carryForwardLimit: 0,
        carryForwardExpiryMonths: 0,
        probationEligible: false,
        documentsRequired: false,
        encashable: false,
        halfDayAllowed: true,
        hourlyAllowed: false,
        gender: 'all',
        negativeBalanceAllowed: false,
      };

      service.createLeaveType(request).subscribe((type) => {
        expect(type.name).toBe('Casual Leave');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({
        ...mockLeaveType,
        leaveTypeId: 'lt-3',
        name: 'Casual Leave',
        code: 'CL',
        color: '#16a34a',
      });
    });
  });

  describe('updateLeaveType', () => {
    it('should update an existing leave type', () => {
      const request: IUpdateLeaveTypeRequest = {
        name: 'Updated Annual Leave',
        code: 'AL',
        color: '#2563eb',
        annualEntitlement: 25,
        accrualFrequency: 'monthly',
        carryForwardLimit: 10,
        carryForwardExpiryMonths: 6,
        probationEligible: false,
        documentsRequired: false,
        encashable: true,
        halfDayAllowed: true,
        hourlyAllowed: false,
        gender: 'all',
        negativeBalanceAllowed: false,
      };

      service.updateLeaveType('lt-1', request).subscribe((type) => {
        expect(type.name).toBe('Updated Annual Leave');
      });

      const req = httpMock.expectOne(`${baseUrl}/lt-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockLeaveType, name: 'Updated Annual Leave', annualEntitlement: 25 });
    });
  });

  describe('deactivateLeaveType', () => {
    it('should deactivate a leave type (AC-4)', () => {
      service.deactivateLeaveType('lt-1').subscribe((type) => {
        expect(type.isActive).toBeFalse();
      });

      const req = httpMock.expectOne(`${baseUrl}/lt-1/deactivate`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockLeaveType, isActive: false });
    });
  });

  describe('activateLeaveType', () => {
    it('should reactivate a leave type', () => {
      service.activateLeaveType('lt-1').subscribe((type) => {
        expect(type.isActive).toBeTrue();
      });

      const req = httpMock.expectOne(`${baseUrl}/lt-1/reactivate`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockLeaveType, isActive: true });
    });
  });

  describe('reorderLeaveTypes', () => {
    it('should reorder leave types (FR-3)', () => {
      const request: IReorderLeaveTypesRequest = {
        orderedIds: ['lt-2', 'lt-1'],
      };

      service.reorderLeaveTypes(request).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/reorder`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(null);
    });
  });
});

/**
 * Pure function tests (no TestBed, no httpMock.verify() conflicts).
 */
describe('LeaveTypeService.parseError', () => {
  it('should parse a typed error response', () => {
    const err = {
      error: { message: 'A leave type with this name already exists', code: 'duplicate_name' },
    } as HttpErrorResponse;

    const result = LeaveTypeService.parseError(err);
    expect(result).toBeTruthy();
    expect(result!.message).toBe('A leave type with this name already exists');
    expect(result!.code).toBe('duplicate_name');
  });

  it('should return null for non-object error', () => {
    const err = { error: 'string error' } as HttpErrorResponse;
    expect(LeaveTypeService.parseError(err)).toBeNull();
  });

  it('should return null for null error body', () => {
    const err = { error: null } as HttpErrorResponse;
    expect(LeaveTypeService.parseError(err)).toBeNull();
  });
});
