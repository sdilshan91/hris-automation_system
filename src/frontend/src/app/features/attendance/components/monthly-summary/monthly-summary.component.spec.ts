import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpResponse, HttpHeaders } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { MonthlySummaryComponent } from './monthly-summary.component';
import { AttendanceService } from '../../services/attendance.service';
import { DepartmentService } from '../../../core-hr/departments/services/department.service';
import { LocationService } from '../../../core-hr/locations/services/location.service';
import {
  IEmployeeMonthlySummary,
  IMonthlySummaryResult,
  IEmployeeDailyBreakdownResult,
  ISummaryGenerationStatus,
} from '../../models/attendance.models';
import { IDepartment } from '../../../core-hr/departments/models/department.models';
import { ILocation } from '../../../core-hr/locations/models/location.models';

/**
 * US-ATT-007 monthly-summary spec. All services are mocked (no HttpClient). Covers:
 * summary table render + banner (AC-1), department filter triggers a reload (AC-5),
 * drill-down loads the daily breakdown (AC-2), on-demand generation POSTs + polls to
 * COMPLETED (AC-3), and export calls the endpoint with the format + downloads the blob (AC-4).
 */
describe('MonthlySummaryComponent', () => {
  let fixture: ComponentFixture<MonthlySummaryComponent>;
  let component: MonthlySummaryComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let departmentSpy: jasmine.SpyObj<DepartmentService>;
  let locationSpy: jasmine.SpyObj<LocationService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const rows: IEmployeeMonthlySummary[] = [
    {
      employeeId: 'e1', employeeName: 'Ada Lovelace', departmentName: 'Engineering',
      presentDays: 20, absentDays: 0, lateCount: 1, earlyDepartureCount: 0,
      workMinutes: 9600, overtimeMinutes: 120, leaveDays: 1, holidays: 2,
      weeklyOffs: 8, lopDays: 0, generatedAt: '2026-06-01T01:00:00Z',
    },
    {
      employeeId: 'e2', employeeName: 'Alan Turing', departmentName: 'Engineering',
      presentDays: 15, absentDays: 4, lateCount: 5, earlyDepartureCount: 2,
      workMinutes: 7200, overtimeMinutes: 0, leaveDays: 0, holidays: 2,
      weeklyOffs: 8, lopDays: 4, generatedAt: '2026-06-01T01:00:00Z',
    },
  ];

  const result: IMonthlySummaryResult = {
    yearMonth: '2026-06',
    rows,
    banner: { totalEmployees: 2, averageAttendancePercent: 88, totalLopDays: 4 },
    generatedAt: '2026-06-01T01:00:00Z',
  };

  const departments: IDepartment[] = [
    {
      departmentId: 'd1', tenantId: 't1', name: 'Engineering', description: null,
      parentDepartmentId: null, parentDepartmentName: null, managerEmployeeId: null,
      managerName: null, isActive: true, employeeCount: 2,
    } as IDepartment,
  ];

  const locations: ILocation[] = [
    { locationId: 'l1', tenantId: 't1', name: 'HQ', isActive: true } as ILocation,
  ];

  function setup(res: IMonthlySummaryResult = result): void {
    attendanceSpy.getMonthlySummary.and.returnValue(of(res));
    fixture = TestBed.createComponent(MonthlySummaryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getMonthlySummary',
      'getEmployeeDailyBreakdown',
      'generateMonthlySummary',
      'exportMonthlySummary',
      'getShifts',
    ]);
    departmentSpy = jasmine.createSpyObj<DepartmentService>('DepartmentService', [
      'getDepartments',
    ]);
    locationSpy = jasmine.createSpyObj<LocationService>('LocationService', [
      'getLocations',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success', 'error', 'warning', 'info',
    ]);

    // Default filter-list responses (loaded in ngOnInit).
    departmentSpy.getDepartments.and.returnValue(of(departments));
    locationSpy.getLocations.and.returnValue(of(locations));
    attendanceSpy.getShifts.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [MonthlySummaryComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: DepartmentService, useValue: departmentSpy },
        { provide: LocationService, useValue: locationSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  it('renders one row per employee with the banner (AC-1)', () => {
    setup();
    expect(component.rows().length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('Alan Turing');
    // Banner aggregates.
    expect(text).toContain('Total employees');
    expect(text).toContain('88%');
    expect(component.banner()?.totalLopDays).toBe(4);
  });

  it('loads the department/location/shift filter lists on init (AC-5)', () => {
    setup();
    expect(departmentSpy.getDepartments).toHaveBeenCalled();
    expect(locationSpy.getLocations).toHaveBeenCalledWith(true);
    expect(attendanceSpy.getShifts).toHaveBeenCalled();
    expect(component.departments().length).toBe(1);
  });

  it('sorts by absent days descending by default', () => {
    setup();
    // Default sortKey absentDays desc -> Alan (4) before Ada (0).
    expect(component.rows()[0].employeeName).toBe('Alan Turing');
  });

  it('color-codes a high-absent cell red and a clean cell neutral (§8)', () => {
    setup();
    const ada = rows[0];
    const alan = rows[1];
    expect(component.absentClass(ada)).toContain('text-neutral-400'); // 0 absent
    expect(component.absentClass(alan)).toContain('text-red-700'); // 4 absent
    expect(component.lateClass(alan)).toContain('text-amber-700'); // 5 late
  });

  it('reloads the summary with the selected department filter (AC-5)', () => {
    setup();
    attendanceSpy.getMonthlySummary.calls.reset();
    component.onFilterChange('department', 'd1');
    expect(component.departmentId()).toBe('d1');
    expect(attendanceSpy.getMonthlySummary).toHaveBeenCalledWith(
      jasmine.objectContaining({ month: '2026-06', departmentId: 'd1' }),
    );
  });

  it('reloads when the month changes via the picker', () => {
    setup();
    attendanceSpy.getMonthlySummary.calls.reset();
    component.onMonthChange('2026-05');
    expect(component.month()).toBe('2026-05');
    expect(attendanceSpy.getMonthlySummary).toHaveBeenCalledWith(
      jasmine.objectContaining({ month: '2026-05' }),
    );
  });

  it('steps the month with the arrow buttons', () => {
    setup();
    component.month.set('2026-06');
    attendanceSpy.getMonthlySummary.calls.reset();
    component.stepMonth(-1);
    expect(component.month()).toBe('2026-05');
    component.stepMonth(1);
    expect(component.month()).toBe('2026-06');
  });

  it('loads the daily breakdown when an employee row is opened (AC-2)', () => {
    setup();
    const breakdown: IEmployeeDailyBreakdownResult = {
      employeeId: 'e1', employeeName: 'Ada Lovelace', yearMonth: '2026-06',
      days: [
        { date: '2026-06-01', status: 'PRESENT', clockIn: '2026-06-01T09:00:00Z', clockOut: '2026-06-01T17:00:00Z', workMinutes: 480, isRegularized: false, isLate: false, isEarlyDeparture: false },
        { date: '2026-06-02', status: 'ABSENT', isRegularized: false, isLate: false, isEarlyDeparture: false },
      ],
    };
    attendanceSpy.getEmployeeDailyBreakdown.and.returnValue(of(breakdown));

    component.openDrillDown(rows[0]);
    fixture.detectChanges();

    expect(attendanceSpy.getEmployeeDailyBreakdown).toHaveBeenCalledWith('e1', '2026-06');
    expect(component.drillDays().length).toBe(2);
    expect(component.drillName()).toBe('Ada Lovelace');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Present');
    expect(text).toContain('Absent');
  });

  it('shows the generate action when the summary is not generated (AC-3)', () => {
    setup({ ...result, generatedAt: null, rows: [], banner: { totalEmployees: 0, averageAttendancePercent: 0, totalLopDays: 0 } });
    expect(component.notGenerated()).toBeTrue();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Summary not generated yet');
  });

  it('triggers generation, polls until COMPLETED, then reloads (AC-3)', fakeAsync(() => {
    setup({ ...result, generatedAt: null, rows: [], banner: { totalEmployees: 0, averageAttendancePercent: 0, totalLopDays: 0 } });

    const running: ISummaryGenerationStatus = { yearMonth: '2026-06', status: 'RUNNING', generatedAt: null };
    const completed: ISummaryGenerationStatus = { yearMonth: '2026-06', status: 'COMPLETED', generatedAt: '2026-06-15T01:00:00Z' };
    attendanceSpy.generateMonthlySummary.and.returnValues(of(running), of(completed));
    attendanceSpy.getMonthlySummary.calls.reset();
    attendanceSpy.getMonthlySummary.and.returnValue(of(result));

    component.generate();
    expect(attendanceSpy.generateMonthlySummary).toHaveBeenCalledWith('2026-06');
    expect(component.isGenerating()).toBeTrue();

    tick(2000); // first poll -> COMPLETED
    expect(component.generationStatus()).toBe('COMPLETED');
    expect(component.isGenerating()).toBeFalse();
    expect(attendanceSpy.getMonthlySummary).toHaveBeenCalled();
    expect(toastrSpy.success).toHaveBeenCalled();
  }));

  it('short-circuits the poll when generation is already COMPLETED (AC-3)', fakeAsync(() => {
    setup({ ...result, generatedAt: null, rows: [], banner: { totalEmployees: 0, averageAttendancePercent: 0, totalLopDays: 0 } });
    const completed: ISummaryGenerationStatus = { yearMonth: '2026-06', status: 'COMPLETED', generatedAt: '2026-06-15T01:00:00Z' };
    attendanceSpy.generateMonthlySummary.and.returnValue(of(completed));
    attendanceSpy.getMonthlySummary.and.returnValue(of(result));

    component.generate();
    tick(0);
    expect(component.isGenerating()).toBeFalse();
    expect(attendanceSpy.generateMonthlySummary).toHaveBeenCalledTimes(1);
  }));

  it('exports with the chosen format and downloads the returned blob (AC-4)', () => {
    setup();
    const blob = new Blob(['x'], { type: 'text/csv' });
    const resp = new HttpResponse<Blob>({
      body: blob,
      headers: new HttpHeaders({ 'Content-Disposition': 'attachment; filename="att-summary.csv"' }),
    });
    attendanceSpy.exportMonthlySummary.and.returnValue(of(resp));

    const createSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    const revokeSpy = spyOn(URL, 'revokeObjectURL');
    const clickSpy = jasmine.createSpy('click');
    spyOn(document, 'createElement').and.returnValue({
      href: '', download: '', click: clickSpy,
    } as unknown as HTMLAnchorElement);

    component.exportAs('csv');

    expect(attendanceSpy.exportMonthlySummary).toHaveBeenCalledWith(
      jasmine.objectContaining({ month: '2026-06' }),
      'csv',
    );
    expect(createSpy).toHaveBeenCalled();
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeSpy).toHaveBeenCalled();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('shows an error toast when the summary load fails', () => {
    attendanceSpy.getMonthlySummary.and.returnValue(throwError(() => new Error('boom')));
    fixture = TestBed.createComponent(MonthlySummaryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isLoading()).toBeFalse();
  });
});
