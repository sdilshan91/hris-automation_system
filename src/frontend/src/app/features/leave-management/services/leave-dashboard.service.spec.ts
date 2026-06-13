import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { LeaveDashboardService } from './leave-dashboard.service';
import {
  ILeaveBalanceSummary,
  ILeaveLedgerEntry,
} from '../models/leave-dashboard.models';
import { ILeaveRequest } from '../models/leave-request.models';
import { environment } from '../../../../environments/environment';

describe('LeaveDashboardService (US-LV-006)', () => {
  let service: LeaveDashboardService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/leaves`;

  const mockBalance: ILeaveBalanceSummary = {
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    color: '#2563eb',
    entitlement: 14,
    used: 4,
    pending: 2,
    balance: 10,
    carryForward: 0,
    expired: 0,
  };

  const mockLedger: ILeaveLedgerEntry = {
    ledgerId: 'led-1',
    leaveTypeId: 'lt-1',
    leaveYear: 2026,
    entryType: 'Accrual',
    amount: 14,
    balanceAfter: 14,
    description: 'Annual upfront allocation',
    occurredAt: '2026-01-01T00:00:00Z',
  };

  const mockUpcoming: ILeaveRequest = {
    leaveRequestId: 'lr-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    leaveTypeColor: '#2563eb',
    startDate: '2026-08-01',
    endDate: '2026-08-03',
    isHalfDay: false,
    halfDaySession: null,
    totalDays: 3,
    reason: 'Trip',
    status: 'Approved',
    requestedAt: '2026-06-13T10:00:00Z',
    attachmentUrls: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        LeaveDashboardService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(LeaveDashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getMyBalance (FR-1/FR-2)', () => {
    it('GETs my-balance with the year query param', () => {
      service.getMyBalance(2026).subscribe((balances) => {
        expect(balances.length).toBe(1);
        expect(balances[0].leaveTypeName).toBe('Annual Leave');
      });

      const req = httpMock.expectOne(`${baseUrl}/my-balance?year=2026`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockBalance]);
    });

    it('supports an empty balance array (AC-5 empty state)', () => {
      service.getMyBalance(2026).subscribe((balances) => {
        expect(balances).toEqual([]);
      });
      httpMock.expectOne(`${baseUrl}/my-balance?year=2026`).flush([]);
    });
  });

  describe('getMyLedger (FR-3)', () => {
    it('GETs my-ledger with leaveTypeId + year params', () => {
      service.getMyLedger('lt-1', 2026).subscribe((entries) => {
        expect(entries.length).toBe(1);
        expect(entries[0].entryType).toBe('Accrual');
      });

      const req = httpMock.expectOne(`${baseUrl}/my-ledger?leaveTypeId=lt-1&year=2026`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockLedger]);
    });
  });

  describe('getMyUpcoming (FR-4)', () => {
    it('GETs my-upcoming', () => {
      service.getMyUpcoming().subscribe((items) => {
        expect(items.length).toBe(1);
        expect(items[0].status).toBe('Approved');
      });

      const req = httpMock.expectOne(`${baseUrl}/my-upcoming`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockUpcoming]);
    });
  });
});
