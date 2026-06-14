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
});
