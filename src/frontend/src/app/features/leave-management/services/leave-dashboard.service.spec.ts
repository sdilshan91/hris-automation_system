import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { LeaveDashboardService } from './leave-dashboard.service';
import { environment } from '../../../../environments/environment';

describe('LeaveDashboardService (US-LV-006)', () => {
  let service: LeaveDashboardService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/leaves`;

  // Real wire shape (LeaveBalanceDto): names align 1:1 with the card VM, plus wire-only adjustments/leaveYear.
  const wireBalance = {
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    color: '#2563eb',
    entitlement: 14,
    used: 4,
    pending: 2,
    balance: 10,
    carryForward: 0,
    expired: 0,
    isArchived: false,
    adjustments: 1,
    leaveYear: 2026,
  };

  // Real wire shape (LeaveLedgerEntryDto): note `id` (not ledgerId) and wire-only leaveRequestId.
  const wireLedger = {
    id: 'led-1',
    leaveTypeId: 'lt-1',
    leaveRequestId: 'lr-9',
    leaveYear: 2026,
    entryType: 'Accrual',
    amount: 14,
    balanceAfter: 14,
    description: 'Annual upfront allocation',
    occurredAt: '2026-01-01T00:00:00Z',
  };

  // Real wire shape (UpcomingLeaveDto): `requestId` (not leaveRequestId), carries leaveTypeColor, and has NO
  // reason/employeeId/requestedAt.
  const wireUpcoming = {
    requestId: 'lr-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    leaveTypeColor: '#2563eb',
    startDate: '2026-08-01',
    endDate: '2026-08-03',
    isHalfDay: false,
    halfDaySession: null,
    totalDays: 3,
    status: 'Approved',
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
    it('GETs my-balance with the year query param and maps each row', () => {
      service.getMyBalance(2026).subscribe((balances) => {
        expect(balances.length).toBe(1);
        expect(balances[0].leaveTypeName).toBe('Annual Leave');
        expect(balances[0].balance).toBe(10);
        // mapLeaveBalanceSummary reconstructs the card, dropping the wire-only `adjustments`. A raw
        // pass-through cast would leak it — this fails against the un-migrated code.
        expect('adjustments' in balances[0]).toBeFalse();
      });

      const req = httpMock.expectOne(`${baseUrl}/my-balance?year=2026`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([wireBalance]);
    });

    it('supports an empty balance array (AC-5 empty state)', () => {
      service.getMyBalance(2026).subscribe((balances) => {
        expect(balances).toEqual([]);
      });
      httpMock.expectOne(`${baseUrl}/my-balance?year=2026`).flush([]);
    });

    it('tolerates a null body (AC-5 empty state)', () => {
      service.getMyBalance(2026).subscribe((balances) => {
        expect(balances).toEqual([]);
      });
      httpMock.expectOne(`${baseUrl}/my-balance?year=2026`).flush(null);
    });
  });

  describe('getMyLedger (FR-3)', () => {
    it('GETs my-ledger with leaveTypeId + year params and maps id→ledgerId', () => {
      service.getMyLedger('lt-1', 2026).subscribe((entries) => {
        expect(entries.length).toBe(1);
        expect(entries[0].entryType).toBe('Accrual');
        // Fails against the un-migrated pass-through (wire row has `id`, not `ledgerId`).
        expect(entries[0].ledgerId).toBe('led-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/my-ledger?leaveTypeId=lt-1&year=2026`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([wireLedger]);
    });
  });

  describe('getMyUpcoming (FR-4)', () => {
    it('GETs my-upcoming and maps the UpcomingLeaveDto onto ILeaveRequest', () => {
      service.getMyUpcoming().subscribe((items) => {
        expect(items.length).toBe(1);
        expect(items[0].status).toBe('Approved');
        // Fails against the un-migrated pass-through (wire has `requestId`, not `leaveRequestId`).
        expect(items[0].leaveRequestId).toBe('lr-1');
        // Upcoming DTO carries a colour (unlike LeaveRequestDto) — it is mapped through.
        expect(items[0].leaveTypeColor).toBe('#2563eb');
        // No wire source on the upcoming DTO — defaulted, not fabricated.
        expect(items[0].reason).toBe('');
        expect(items[0].requestedAt).toBe('');
      });

      const req = httpMock.expectOne(`${baseUrl}/my-upcoming`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([wireUpcoming]);
    });
  });
});
