import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { ClockInComponent } from './clock-in.component';
import { AttendanceService } from '../../services/attendance.service';
import { IAttendanceLog, IClockStatus, IClockOutResult } from '../../models/attendance.models';

describe('ClockInComponent', () => {
  let component: ClockInComponent;
  let fixture: ComponentFixture<ClockInComponent>;
  let serviceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const baseStatus: IClockStatus = {
    isClockedIn: false,
    clockedInAt: null,
    requireGeolocation: false,
    shiftName: 'Day Shift',
    shiftStart: '09:00',
  };

  const createdLog: IAttendanceLog = {
    attendanceLogId: 'att-99',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    clockIn: '2026-06-14T08:00:00Z',
    clockOut: null,
    clockInLatitude: 6.9271,
    clockInLongitude: 79.8612,
    source: 'WEB',
  };

  // ─── Geolocation mock helpers ─────────────────────────────
  // navigator.geolocation is a getter-only property, so we redefine it.
  let originalGeolocationDescriptor: PropertyDescriptor | undefined;

  function setGeolocation(value: unknown): void {
    Object.defineProperty(navigator, 'geolocation', {
      value,
      configurable: true,
      writable: true,
    });
  }

  function mockGeolocationGranted(lat = 6.9271, lng = 79.8612): void {
    setGeolocation({
      getCurrentPosition: (success: PositionCallback) => {
        success({ coords: { latitude: lat, longitude: lng } } as GeolocationPosition);
      },
    });
  }

  function mockGeolocationDenied(): void {
    setGeolocation({
      getCurrentPosition: (_success: PositionCallback, error: PositionErrorCallback) => {
        error({ code: 1, message: 'User denied Geolocation' } as GeolocationPositionError);
      },
    });
  }

  /**
   * Build the component with a given initial status. `getStatus` returns a
   * synchronous `of(...)`, so `detectChanges()` (-> ngOnInit -> loadStatus)
   * resolves it inline; no awaiting needed (keeps fakeAsync tests clean).
   */
  function setup(status: IClockStatus): void {
    serviceSpy.getStatus.and.returnValue(of(status));
    fixture = TestBed.createComponent(ClockInComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    originalGeolocationDescriptor = Object.getOwnPropertyDescriptor(navigator, 'geolocation');

    serviceSpy = jasmine.createSpyObj('AttendanceService', ['getStatus', 'clockIn', 'clockOut']);
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    TestBed.configureTestingModule({
      imports: [ClockInComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AttendanceService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  afterEach(() => {
    if (originalGeolocationDescriptor) {
      Object.defineProperty(navigator, 'geolocation', originalGeolocationDescriptor);
    } else {
      delete (navigator as any).geolocation;
    }
  });

  it('should create and load status', () => {
    setup(baseStatus);
    expect(component).toBeTruthy();
    expect(component.isLoading()).toBeFalse();
    expect(component.isClockedIn()).toBeFalse();
    expect(component.shiftLabel()).toContain('Day Shift');
  });

  // ─── AC-1 / AC-3 (granted) success path with coordinates ──
  it('clocks in successfully with coordinates and shows the live timer + map (AC-1)', fakeAsync(() => {
    setup({ ...baseStatus, requireGeolocation: true });
    mockGeolocationGranted();
    serviceSpy.clockIn.and.returnValue(of(createdLog));

    component.onClockIn();
    tick(); // flush the geolocation promise + clockIn observable

    expect(serviceSpy.clockIn).toHaveBeenCalledWith(
      jasmine.objectContaining({ latitude: 6.9271, longitude: 79.8612, source: 'WEB' }),
    );
    expect(component.isClockedIn()).toBeTrue();
    expect(component.errorMessage()).toBeNull();
    expect(component.mapUrl()).not.toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();

    component.ngOnDestroy(); // clear the live timer interval
  }));

  // ─── AC-4 geo optional + denied -> success without coordinates ──
  it('clocks in without coordinates when geo is optional and permission denied (AC-4)', fakeAsync(() => {
    setup({ ...baseStatus, requireGeolocation: false });
    mockGeolocationDenied();
    serviceSpy.clockIn.and.returnValue(of({ ...createdLog, clockInLatitude: null, clockInLongitude: null }));

    component.onClockIn();
    tick();

    expect(serviceSpy.clockIn).toHaveBeenCalledWith(
      jasmine.objectContaining({ latitude: null, longitude: null, source: 'WEB' }),
    );
    expect(component.isClockedIn()).toBeTrue();
    expect(component.errorMessage()).toBeNull();
    expect(component.mapUrl()).toBeNull(); // no coords -> no map preview

    component.ngOnDestroy();
  }));

  // ─── AC-3 geo required + denied -> blocked, no POST ──
  it('blocks clock-in when geolocation is required and permission denied (AC-3)', fakeAsync(() => {
    setup({ ...baseStatus, requireGeolocation: true });
    mockGeolocationDenied();

    component.onClockIn();
    tick();

    expect(serviceSpy.clockIn).not.toHaveBeenCalled();
    expect(component.isClockedIn()).toBeFalse();
    expect(component.isSubmitting()).toBeFalse();
    expect(component.errorMessage()).toContain('Location access is required');
  }));

  // ─── AC-2 already-clocked-in (409) -> inline error + reflect state ──
  it('shows the duplicate error and reflects clocked-in state on a 409 (AC-2)', fakeAsync(() => {
    setup({ ...baseStatus, requireGeolocation: false });
    mockGeolocationDenied(); // optional -> proceeds, backend rejects as duplicate
    const errorResponse = new HttpErrorResponse({
      status: 409,
      error: { message: 'You have already clocked in. Please clock out first.', code: 'already_clocked_in' },
    });
    serviceSpy.clockIn.and.returnValue(throwError(() => errorResponse));
    // The 409 handler re-fetches status (returns the open record's timestamp).
    serviceSpy.getStatus.and.returnValue(
      of({ ...baseStatus, isClockedIn: true, clockedInAt: '2026-06-14T08:00:00Z' }),
    );

    component.onClockIn();
    tick();

    expect(component.errorMessage()).toContain('already clocked in');
    expect(component.isClockedIn()).toBeTrue();

    component.ngOnDestroy();
  }));

  // ─── AC-5 IP-allowlist rejection (403) -> inline error + help link ──
  it('shows the IP-restriction error with a help link on a 403 (AC-5)', fakeAsync(() => {
    setup({ ...baseStatus, requireGeolocation: false });
    mockGeolocationDenied();
    const errorResponse = new HttpErrorResponse({
      status: 403,
      error: { message: 'Clock-in is only allowed from authorized network locations.', code: 'ip_not_allowed' },
    });
    serviceSpy.clockIn.and.returnValue(throwError(() => errorResponse));

    component.onClockIn();
    tick();

    expect(component.errorMessage()).toContain('authorized network locations');
    expect(component.showIpHelp()).toBeTrue();
    expect(component.isClockedIn()).toBeFalse();
  }));

  // ─── Initial state: already clocked in from status (AC-2 reflect on load) ──
  it('starts in the clocked-in state with a running timer when status says so', fakeAsync(() => {
    setup({ ...baseStatus, isClockedIn: true, clockedInAt: '2026-06-14T08:00:00Z' });

    expect(component.isClockedIn()).toBeTrue();
    expect(component.clockedInAtLocal()).not.toBeNull();
    tick(1000);
    expect(component.elapsed()).toMatch(/^\d{2}:\d{2}:\d{2}$/);

    component.ngOnDestroy();
  }));

  // ════════════════ US-ATT-002: Clock-out ════════════════

  const clockedInStatus: IClockStatus = {
    ...baseStatus,
    isClockedIn: true,
    clockedInAt: '2026-06-14T03:00:00Z',
  };

  const completeResult: IClockOutResult = {
    attendanceLogId: 'att-99',
    clockIn: '2026-06-14T03:00:00Z',
    clockOut: '2026-06-14T11:45:00Z',
    totalWorkMinutes: 465, // 7h 45m
    overtimeMinutes: null,
    status: 'COMPLETE',
  };

  // ─── AC-1: clock-out success renders the summary card ──
  it('clocks out successfully and renders the work summary (AC-1)', fakeAsync(() => {
    setup(clockedInStatus);
    serviceSpy.clockOut.and.returnValue(of(completeResult));

    component.onClockOut();
    tick(); // flush the clockOut observable (no geo required)

    expect(serviceSpy.clockOut).toHaveBeenCalledWith({ latitude: null, longitude: null });
    expect(component.isClockedIn()).toBeFalse();
    expect(component.errorMessage()).toBeNull();
    expect(component.summary()).toEqual(completeResult);
    expect(component.totalHoursLabel()).toBe('7h 45m');
    expect(component.statusLabel()).toBe('Complete');
    expect(component.pillClass()).toBe('pill-complete');
    expect(component.overtimeLabel()).toBe('');
    expect(toastrSpy.success).toHaveBeenCalled();

    // Summary content rendered in the DOM (flush change detection after the async result).
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('7h 45m');
    expect(text).toContain('Complete');
  }));

  // ─── AC-3: overtime status shows the blue Overtime pill + overtime label ──
  it('renders the Overtime pill and overtime hours (AC-3)', fakeAsync(() => {
    setup(clockedInStatus);
    serviceSpy.clockOut.and.returnValue(
      of({
        ...completeResult,
        clockOut: '2026-06-14T13:00:00Z',
        totalWorkMinutes: 600, // 10h
        overtimeMinutes: 120, // 2h
        status: 'OVERTIME',
      }),
    );

    component.onClockOut();
    tick();

    expect(component.statusLabel()).toBe('Overtime');
    expect(component.pillClass()).toBe('pill-overtime');
    expect(component.totalHoursLabel()).toBe('10h 0m');
    expect(component.overtimeLabel()).toBe('2h 0m');

    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Overtime');
    expect(text).toContain('2h 0m overtime');
  }));

  // ─── AC-2: no open record -> inline error + reset to clock-in state ──
  it('shows the no-open-record error and resets to clock-in state (AC-2)', fakeAsync(() => {
    setup(clockedInStatus);
    const errorResponse = new HttpErrorResponse({
      status: 404,
      error: {
        message: 'No active clock-in found. Please clock in first or submit a regularization request.',
        code: 'no_active_clock_in',
      },
    });
    serviceSpy.clockOut.and.returnValue(throwError(() => errorResponse));

    component.onClockOut();
    tick();

    expect(component.errorMessage()).toContain('No active clock-in found');
    expect(component.summary()).toBeNull();
    expect(component.isClockedIn()).toBeFalse(); // reset to clock-in state

    // The green Clock In button is shown again (clocked-out state).
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Clock In');
  }));

  // ─── AC-5: tenant requires geolocation on clock-out -> coords captured + sent ──
  it('captures and sends geolocation when the tenant requires it on clock-out (AC-5)', fakeAsync(() => {
    setup({ ...clockedInStatus, requireGeolocation: true });
    mockGeolocationGranted(6.9271, 79.8612);
    serviceSpy.clockOut.and.returnValue(of(completeResult));

    component.onClockOut();
    tick(); // flush the geolocation promise + clockOut observable

    expect(serviceSpy.clockOut).toHaveBeenCalledWith({ latitude: 6.9271, longitude: 79.8612 });
    expect(component.summary()).not.toBeNull();
    expect(component.errorMessage()).toBeNull();
  }));

  // ─── AC-5: tenant requires geolocation but permission denied -> blocked, no POST ──
  it('blocks clock-out when geolocation is required and permission denied (AC-5)', fakeAsync(() => {
    setup({ ...clockedInStatus, requireGeolocation: true });
    mockGeolocationDenied();

    component.onClockOut();
    tick();

    expect(serviceSpy.clockOut).not.toHaveBeenCalled();
    expect(component.errorMessage()).toContain('Location access is required to clock out');
    expect(component.isClockedIn()).toBeTrue(); // remains clocked in
    expect(component.isSubmitting()).toBeFalse();

    component.ngOnDestroy();
  }));
});
