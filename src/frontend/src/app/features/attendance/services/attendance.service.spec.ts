import { TestBed } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { AttendanceService } from './attendance.service';
import {
  IAttendanceLog,
  IClockInRequest,
  IClockStatus,
  IClockOutRequest,
  IClockOutResult,
  ICreateRegularizationRequest,
  IRegularization,
  IShift,
  IShiftRequest,
} from '../models/attendance.models';
import { environment } from '../../../../environments/environment';

describe('AttendanceService', () => {
  let service: AttendanceService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/attendance`;

  const mockLog: IAttendanceLog = {
    attendanceLogId: 'att-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    clockIn: '2026-06-14T08:00:00Z',
    clockOut: null,
    clockInLatitude: null,
    clockInLongitude: null,
    source: 'WEB',
  };

  const mockStatus: IClockStatus = {
    isClockedIn: false,
    clockedInAt: null,
    requireGeolocation: false,
    shiftName: 'Day Shift',
    shiftStart: '09:00',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AttendanceService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(AttendanceService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getStatus', () => {
    it('should GET the current employee clock-in status', () => {
      service.getStatus().subscribe((status) => {
        expect(status.shiftName).toBe('Day Shift');
        expect(status.isClockedIn).toBeFalse();
      });

      const req = httpMock.expectOne(`${baseUrl}/status`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockStatus);
    });
  });

  describe('clockIn', () => {
    it('should POST a clock-in with coordinates (AC-1, AC-3 granted)', () => {
      const body: IClockInRequest = { latitude: 6.9271, longitude: 79.8612, source: 'WEB' };

      service.clockIn(body).subscribe((log) => {
        expect(log.attendanceLogId).toBe('att-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/clock-in`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockLog);
    });

    it('should POST a clock-in without coordinates (AC-4 geo optional)', () => {
      const body: IClockInRequest = { latitude: null, longitude: null, source: 'WEB' };

      service.clockIn(body).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/clock-in`);
      expect(req.request.body.latitude).toBeNull();
      expect(req.request.body.longitude).toBeNull();
      req.flush(mockLog);
    });
  });

  describe('clockOut (US-ATT-002)', () => {
    const mockResult: IClockOutResult = {
      attendanceLogId: 'att-1',
      clockIn: '2026-06-14T03:00:00Z',
      clockOut: '2026-06-14T11:45:00Z',
      totalWorkMinutes: 465,
      overtimeMinutes: null,
      status: 'COMPLETE',
    };

    it('should POST a clock-out and return the work summary (AC-1)', () => {
      const body: IClockOutRequest = { latitude: null, longitude: null };

      service.clockOut(body).subscribe((result) => {
        expect(result.totalWorkMinutes).toBe(465);
        expect(result.status).toBe('COMPLETE');
      });

      const req = httpMock.expectOne(`${baseUrl}/clock-out`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockResult);
    });

    it('should POST a clock-out with coordinates when geo is required (AC-5)', () => {
      const body: IClockOutRequest = { latitude: 6.9271, longitude: 79.8612 };

      service.clockOut(body).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/clock-out`);
      expect(req.request.body.latitude).toBe(6.9271);
      expect(req.request.body.longitude).toBe(79.8612);
      req.flush(mockResult);
    });
  });

  describe('regularizations (US-ATT-003)', () => {
    const mockReg: IRegularization = {
      regularizationId: 'reg-1',
      tenantId: 'tenant-1',
      employeeId: 'emp-1',
      attendanceLogId: null,
      date: '2026-06-10',
      regularizationType: 'MISSED_BOTH',
      requestedClockIn: '2026-06-10T03:30:00Z',
      requestedClockOut: '2026-06-10T11:30:00Z',
      reason: 'Forgot to clock in and out due to an offsite client meeting.',
      status: 'PENDING',
      createdAt: '2026-06-11T02:00:00Z',
    };

    it('should POST a regularization and return the created PENDING record (AC-1)', () => {
      const body: ICreateRegularizationRequest = {
        date: '2026-06-10',
        regularizationType: 'MISSED_BOTH',
        requestedClockIn: '09:00',
        requestedClockOut: '17:30',
        reason: 'Forgot to clock in and out due to an offsite client meeting.',
      };

      service.submitRegularization(body).subscribe((reg) => {
        expect(reg.regularizationId).toBe('reg-1');
        expect(reg.status).toBe('PENDING');
      });

      const req = httpMock.expectOne(`${baseUrl}/regularizations`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockReg);
    });

    it('should GET the current employee regularizations (§8)', () => {
      service.listRegularizations().subscribe((list) => {
        expect(list.length).toBe(1);
        expect(list[0].status).toBe('PENDING');
      });

      const req = httpMock.expectOne(`${baseUrl}/regularizations`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockReg]);
    });
  });

  // ─── US-ATT-005: Shift management & assignment ──────────────────────────────
  describe('US-ATT-005 shift endpoints', () => {
    const mockShift: IShift = {
      id: 'shift-1',
      name: 'Morning Shift',
      type: 'SINGLE',
      startTime: '09:00',
      endTime: '17:00',
      breakDurationMinutes: 60,
      gracePeriodMinutes: 10,
      minimumHours: null,
      workingDays: [1, 2, 3, 4, 5],
      isDefault: true,
      isActive: true,
      assignedEmployeeCount: 3,
    };

    it('getShifts unwraps the ApiResponse envelope to ShiftDto[]', () => {
      let result: IShift[] | undefined;
      service.getShifts().subscribe((s) => (result = s));

      const req = httpMock.expectOne(`${baseUrl}/shifts`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ success: true, data: [mockShift], message: null });

      expect(result!.length).toBe(1);
      expect(result![0].name).toBe('Morning Shift');
    });

    it('createShift POSTs the request and unwraps data', () => {
      const body: IShiftRequest = {
        name: 'Night Shift',
        type: 'SINGLE',
        startTime: '22:00',
        endTime: '06:00',
        breakDurationMinutes: 30,
        gracePeriodMinutes: 15,
        workingDays: [1, 2, 3, 4, 5],
      };
      let result: IShift | undefined;
      service.createShift(body).subscribe((s) => (result = s));

      const req = httpMock.expectOne(`${baseUrl}/shifts`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);
      req.flush({ success: true, data: { ...mockShift, name: 'Night Shift' } });
      expect(result!.name).toBe('Night Shift');
    });

    it('updateShift PUTs to the id path', () => {
      const body: IShiftRequest = {
        name: 'Updated',
        type: 'FLEXIBLE',
        breakDurationMinutes: 0,
        gracePeriodMinutes: 0,
        minimumHours: 8,
        workingDays: [],
      };
      service.updateShift('shift-1', body).subscribe();
      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
      req.flush({ success: true, data: mockShift });
    });

    it('deleteShift DELETEs the id path (204)', () => {
      service.deleteShift('shift-1').subscribe();
      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('cloneShift POSTs to the clone path', () => {
      service.cloneShift('shift-1').subscribe();
      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1/clone`);
      expect(req.request.method).toBe('POST');
      req.flush({ success: true, data: { ...mockShift, id: 'shift-2', name: 'Morning Shift (copy)' } });
    });

    it('assignShift POSTs employeeIds + effectiveFrom and unwraps the result', () => {
      let result: { assignedCount: number } | undefined;
      service
        .assignShift('shift-1', { employeeIds: ['e1', 'e2'], effectiveFrom: '2026-07-01' })
        .subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${baseUrl}/shifts/shift-1/assign`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ employeeIds: ['e1', 'e2'], effectiveFrom: '2026-07-01' });
      req.flush({ success: true, data: { assignedCount: 2, employeeShiftIds: ['es1', 'es2'] } });
      expect(result!.assignedCount).toBe(2);
    });

    it('getResolvedShift GETs with the date query param', () => {
      service.getResolvedShift('emp-9', '2026-07-01').subscribe();
      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/employees/emp-9/shift` && r.params.get('date') === '2026-07-01',
      );
      expect(req.request.method).toBe('GET');
      req.flush({ success: true, data: { ...mockShift, effectiveFrom: '2026-06-01', effectiveTo: null, resolvedForDate: '2026-07-01' } });
    });
  });
});

// ─── Pure error helpers (no TestBed / httpMock.verify conflicts) ──────────

describe('AttendanceService.parseError (pure function)', () => {
  it('should parse an already-clocked-in 409 body (AC-2)', () => {
    const err = {
      error: { message: 'You have already clocked in. Please clock out first.', code: 'already_clocked_in' },
    } as HttpErrorResponse;
    const parsed = AttendanceService.parseError(err);
    expect(parsed!.message).toContain('already clocked in');
    expect(parsed!.code).toBe('already_clocked_in');
  });

  it('should parse an IP-not-allowed 403 body (AC-5)', () => {
    const err = {
      error: { message: 'Clock-in is only allowed from authorized network locations.', code: 'ip_not_allowed' },
    } as HttpErrorResponse;
    expect(AttendanceService.parseError(err)!.code).toBe('ip_not_allowed');
  });

  it('should return null for a non-object error body', () => {
    const err = { error: 'boom' } as HttpErrorResponse;
    expect(AttendanceService.parseError(err)).toBeNull();
  });

  it('parseErrorMessage should extract the message', () => {
    const err = { error: { message: 'Outside geo-fence', code: 'geo_fence_violation' } } as HttpErrorResponse;
    expect(AttendanceService.parseErrorMessage(err)).toBe('Outside geo-fence');
  });

  it('parseErrorMessage should fall back for an unknown shape', () => {
    const err = { error: null } as HttpErrorResponse;
    expect(AttendanceService.parseErrorMessage(err)).toBe('An unexpected error occurred.');
  });

  it('parseRegularizationError should parse a lookback rejection (AC-3)', () => {
    const err = {
      error: { message: 'Regularization requests can only be submitted for the last 7 days.', code: 'lookback_exceeded' },
    } as HttpErrorResponse;
    const parsed = AttendanceService.parseRegularizationError(err);
    expect(parsed!.message).toContain('last 7 days');
    expect(parsed!.code).toBe('lookback_exceeded');
  });

  it('parseRegularizationError should return null for a non-object body', () => {
    const err = { error: 'boom' } as HttpErrorResponse;
    expect(AttendanceService.parseRegularizationError(err)).toBeNull();
  });

  it('parseShiftInUseError parses the 409 in-use body (AC-4)', () => {
    const err = {
      error: { message: 'This shift is assigned to 3 employees. Please reassign them before deleting.', code: 'shift_in_use' },
    } as HttpErrorResponse;
    const parsed = AttendanceService.parseShiftInUseError(err);
    expect(parsed!.message).toContain('assigned to 3 employees');
    expect(parsed!.code).toBe('shift_in_use');
  });

  it('parseShiftInUseError returns null for a non-object body', () => {
    const err = { error: null } as HttpErrorResponse;
    expect(AttendanceService.parseShiftInUseError(err)).toBeNull();
  });
});
