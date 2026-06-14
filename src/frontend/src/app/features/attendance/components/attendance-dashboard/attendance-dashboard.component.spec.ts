import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { AttendanceDashboardComponent } from './attendance-dashboard.component';
import { AttendanceService } from '../../services/attendance.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { IDashboardKpi, ILiveBoardResult } from '../../models/attendance.models';

/**
 * US-ATT-010 (AC-1/AC-2) dashboard spec. AttendanceService + AuthService mocked —
 * no HttpClient. Covers: KPI cards render from dashboard data, donut segments build,
 * live-board rows + status pills render, the scope toggle reloads, and the ~30s
 * polling fires (fakeAsync/tick) and is torn down on destroy.
 */
describe('AttendanceDashboardComponent', () => {
  let fixture: ComponentFixture<AttendanceDashboardComponent>;
  let component: AttendanceDashboardComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let authSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const kpi: IDashboardKpi = {
    date: '2026-06-15',
    expectedHeadcount: 50,
    clockedIn: 40,
    pendingClockIn: 5,
    onLeave: 3,
    absent: 2,
    attendancePercent: 80,
  };

  const board: ILiveBoardResult = {
    date: '2026-06-15',
    rows: [
      {
        employeeId: 'e1',
        employeeName: 'Ada Lovelace',
        employeeNumber: 'EMP-001',
        departmentName: 'Engineering',
        status: 'CLOCKED_IN',
        clockInAt: '2026-06-15T08:05:00Z',
      },
      {
        employeeId: 'e2',
        employeeName: 'Alan Turing',
        departmentName: 'Research',
        status: 'ON_LEAVE',
      },
    ],
  };

  function setup(hasManager = false): void {
    authSpy.hasRole.and.callFake((r: string) => (r === 'Manager' ? hasManager : false));
    attendanceSpy.getDashboardKpi.and.returnValue(of(kpi));
    attendanceSpy.getLiveBoard.and.returnValue(of(board));
    fixture = TestBed.createComponent(AttendanceDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getDashboardKpi',
      'getLiveBoard',
    ]);
    authSpy = jasmine.createSpyObj<AuthService>('AuthService', ['hasRole']);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [AttendanceDashboardComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: AuthService, useValue: authSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  it('renders the KPI cards from dashboard data (AC-1)', () => {
    setup();
    const text = fixture.nativeElement.textContent as string;
    expect(component.kpi()!.clockedIn).toBe(40);
    expect(text).toContain('Expected today');
    expect(text).toContain('Clocked in');
    expect(text).toContain('Attendance %');
    expect(text).toContain('80%'); // attendance percent
  });

  it('builds donut segments for today\'s breakdown (§8)', () => {
    setup();
    // 40 + 3 + 2 + 5 = 50 total -> 4 non-zero segments
    expect(component.donutSegments().length).toBe(4);
    const donut = fixture.nativeElement.querySelector('[data-test="donut-chart"]');
    expect(donut).toBeTruthy();
  });

  it('renders live-board rows with status pills + clock-in time (AC-2)', () => {
    setup();
    expect(component.liveRows().length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('Alan Turing');
    expect(text).toContain('Clocked In');
    expect(text).toContain('On Leave');
    const pills = fixture.nativeElement.querySelectorAll('[data-test="status-pill"]');
    expect(pills.length).toBeGreaterThan(0);
  });

  it('formats the clock-in time only for clocked-in rows', () => {
    setup();
    expect(component.clockInTime(board.rows[0])).not.toBe('—');
    expect(component.clockInTime(board.rows[1])).toBe('—'); // ON_LEAVE
  });

  it('hides the scope toggle for a non-manager HR viewer (BR-4)', () => {
    setup(false);
    expect(component.canScopeTeam()).toBeFalse();
    expect(fixture.nativeElement.querySelector('[data-test="scope-team"]')).toBeNull();
  });

  it('shows the scope toggle for a manager and reloads on switch (BR-4)', () => {
    setup(true);
    expect(fixture.nativeElement.querySelector('[data-test="scope-team"]')).toBeTruthy();
    attendanceSpy.getDashboardKpi.calls.reset();
    component.setScope('team');
    expect(component.scope()).toBe('team');
    expect(attendanceSpy.getDashboardKpi).toHaveBeenCalledWith(component.date(), 'team');
  });

  it('polls the dashboard + live board every ~30s and stops on destroy (§10)', fakeAsync(() => {
    setup();
    // 1 initial call each
    expect(attendanceSpy.getDashboardKpi).toHaveBeenCalledTimes(1);
    expect(attendanceSpy.getLiveBoard).toHaveBeenCalledTimes(1);

    tick(30_000);
    expect(attendanceSpy.getDashboardKpi).toHaveBeenCalledTimes(2);
    expect(attendanceSpy.getLiveBoard).toHaveBeenCalledTimes(2);

    fixture.destroy();
    tick(30_000);
    // no further polls after teardown
    expect(attendanceSpy.getDashboardKpi).toHaveBeenCalledTimes(2);
    expect(attendanceSpy.getLiveBoard).toHaveBeenCalledTimes(2);
  }));

  it('highlights rows whose status changed between polls (§8)', () => {
    setup();
    // simulate a poll where e2 transitions ON_LEAVE -> CLOCKED_IN
    attendanceSpy.getLiveBoard.and.returnValue(
      of({
        date: '2026-06-15',
        rows: [
          board.rows[0],
          { ...board.rows[1], status: 'CLOCKED_IN' as const, clockInAt: '2026-06-15T09:00:00Z' },
        ],
      }),
    );
    component.refresh(false);
    expect(component.isChanged('e2')).toBeTrue();
    expect(component.isChanged('e1')).toBeFalse();
  });
});
