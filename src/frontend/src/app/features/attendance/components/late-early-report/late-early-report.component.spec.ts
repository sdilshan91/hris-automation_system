import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { signal } from '@angular/core';
import { ToastrService } from 'ngx-toastr';
import { LateEarlyReportComponent } from './late-early-report.component';
import { AttendanceService } from '../../services/attendance.service';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  ILateEarlyReportResult,
  ILateEarlyRow,
} from '../../models/attendance.models';

/**
 * US-ATT-008 (AC-5, FR-6) late/early report spec. AttendanceService + AuthService are
 * mocked — no HttpClient. Covers: rows render, chronic-row highlight, sorting, the
 * scope tab visibility by role + scope switch reload, a filter-change reload, and the
 * empty state.
 */
describe('LateEarlyReportComponent', () => {
  let fixture: ComponentFixture<LateEarlyReportComponent>;
  let component: LateEarlyReportComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let rolesSignal: ReturnType<typeof signal<string[]>>;

  const rows: ILateEarlyRow[] = [
    {
      employeeId: 'e1',
      employeeName: 'Ada Lovelace',
      departmentName: 'Engineering',
      lateCount: 2,
      totalLateMinutes: 35,
      earlyDepartureCount: 1,
      totalEarlyMinutes: 20,
      isChronic: false,
    },
    {
      employeeId: 'e2',
      employeeName: 'Alan Turing',
      departmentName: 'Research',
      lateCount: 6,
      totalLateMinutes: 130,
      earlyDepartureCount: 0,
      totalEarlyMinutes: 0,
      isChronic: true,
    },
  ];

  const result: ILateEarlyReportResult = {
    from: '2026-06-01',
    to: '2026-06-14',
    rows,
  };

  function setup(roles: string[] = ['Manager']): void {
    rolesSignal.set(roles);
    attendanceSpy.getLateEarlyReport.and.returnValue(of(result));
    fixture = TestBed.createComponent(LateEarlyReportComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    rolesSignal = signal<string[]>(['Manager']);
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getLateEarlyReport',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [LateEarlyReportComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        { provide: AuthService, useValue: { roles: rolesSignal } },
      ],
    }).compileComponents();
  });

  it('renders the per-employee late/early rows (AC-5)', () => {
    setup();
    expect(component.rows().length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('Alan Turing');
    expect(text).toContain('Engineering');
  });

  it('highlights chronic rows with the chronic badge (FR-7)', () => {
    setup();
    const badge = fixture.nativeElement.querySelector('[data-test="chronic-badge"]');
    expect(badge).toBeTruthy();
    const chronicRow = fixture.nativeElement.querySelector('.row-chronic');
    expect(chronicRow).toBeTruthy();
    expect((chronicRow.textContent as string)).toContain('Alan Turing');
  });

  it('sorts by late count descending by default', () => {
    setup();
    expect(component.rows()[0].employeeName).toBe('Alan Turing'); // 6 > 2
  });

  it('toggles sort direction on repeated header clicks', () => {
    setup();
    component.sortBy('lateCount'); // already key -> flips asc
    expect(component.rows()[0].employeeName).toBe('Ada Lovelace');
    expect(component.sortIndicator('lateCount')).toBe('↑');
  });

  it('hides the scope tabs for a non-HR manager', () => {
    setup(['Manager']);
    expect(component.canSeeAll()).toBeFalse();
    expect(fixture.nativeElement.querySelector('[data-test="scope-all"]')).toBeNull();
  });

  it('shows the All-scope tab for HR and reloads on switch (FR-6)', () => {
    setup(['HR Officer']);
    expect(component.canSeeAll()).toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-test="scope-all"]')).toBeTruthy();
    attendanceSpy.getLateEarlyReport.calls.reset();
    component.setScope('all');
    expect(component.scope()).toBe('all');
    expect(attendanceSpy.getLateEarlyReport).toHaveBeenCalledWith(
      jasmine.objectContaining({ scope: 'all' }),
    );
  });

  it('does not switch to All scope for a non-HR user', () => {
    setup(['Manager']);
    component.setScope('all');
    expect(component.scope()).toBe('team');
  });

  it('reloads the report when a filter changes', () => {
    setup();
    attendanceSpy.getLateEarlyReport.calls.reset();
    component.onDeptChange('dept-7');
    expect(component.departmentId()).toBe('dept-7');
    expect(attendanceSpy.getLateEarlyReport).toHaveBeenCalledWith(
      jasmine.objectContaining({ departmentId: 'dept-7' }),
    );
  });

  it('shows the empty state for a range with no records', () => {
    rolesSignal.set(['Manager']);
    attendanceSpy.getLateEarlyReport.and.returnValue(
      of({ from: '2026-06-01', to: '2026-06-14', rows: [] }),
    );
    fixture = TestBed.createComponent(LateEarlyReportComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    expect((fixture.nativeElement.textContent as string)).toContain('No late or early records');
  });
});
