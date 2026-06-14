import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { HttpResponse, HttpHeaders } from '@angular/common/http';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { AttendanceReportsComponent } from './attendance-reports.component';
import { AttendanceService } from '../../services/attendance.service';
import {
  IDeptComparisonResult,
  ICustomReportResult,
  ITrendsResult,
  IScheduledReportConfig,
} from '../../models/attendance.models';

/**
 * US-ATT-010 (AC-3/AC-4/AC-5, FR-8) reports-hub spec. AttendanceService mocked —
 * no HttpClient. Covers: department bars render + color-code, custom report
 * generate + export (blob download), trends render, scheduled config create.
 */
describe('AttendanceReportsComponent', () => {
  let fixture: ComponentFixture<AttendanceReportsComponent>;
  let component: AttendanceReportsComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const deptResult: IDeptComparisonResult = {
    month: '2026-06',
    rows: [
      { departmentId: 'd1', departmentName: 'Engineering', attendanceRatePct: 95, employeeCount: 12 },
      { departmentId: 'd2', departmentName: 'Sales', attendanceRatePct: 75, employeeCount: 8 },
    ],
  };

  const customResult: ICustomReportResult = {
    from: '2026-06-01',
    to: '2026-06-15',
    rows: [
      {
        employeeId: 'e1',
        employeeName: 'Ada Lovelace',
        presentDays: 10,
        absentDays: 1,
        lateCount: 2,
        overtimeMinutes: 120,
        workMinutes: 4800,
      },
    ],
  };

  const trendsResult: ITrendsResult = {
    attendanceRate: [
      { period: '2026-05', value: 90 },
      { period: '2026-06', value: 92 },
    ],
    lateArrivals: [
      { period: '2026-05', value: 4 },
      { period: '2026-06', value: 3 },
    ],
    overtimeHours: [
      { period: '2026-05', value: 30 },
      { period: '2026-06', value: 40 },
    ],
    absenteeismRate: [
      { period: '2026-05', value: 5 },
      { period: '2026-06', value: 4 },
    ],
  };

  function setup(): void {
    attendanceSpy.getDepartmentComparison.and.returnValue(of(deptResult));
    fixture = TestBed.createComponent(AttendanceReportsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getDepartmentComparison',
      'getCustomReport',
      'exportCustomReport',
      'getTrends',
      'getScheduledReports',
      'createScheduledReport',
      'deleteScheduledReport',
    ]);
    attendanceSpy.getScheduledReports.and.returnValue(of([]));
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [AttendanceReportsComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  // ─── Departments (AC-3) ─────────────────────────────────────
  it('renders color-coded department bars (AC-3)', () => {
    setup();
    expect(component.deptRows().length).toBe(2);
    const bars = fixture.nativeElement.querySelectorAll('[data-test="dept-bar"]');
    expect(bars.length).toBe(2);
    // green > 90, red < 80
    expect(component.rateColor(95)).toBe('#16a34a');
    expect(component.rateColor(75)).toBe('#dc2626');
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Engineering');
    expect(text).toContain('Sales');
  });

  it('clamps the bar width to 0–100', () => {
    setup();
    expect(component.clamp(120)).toBe(100);
    expect(component.clamp(-5)).toBe(0);
    expect(component.clamp(83)).toBe(83);
  });

  // ─── Custom report (AC-4) ───────────────────────────────────
  it('generates the custom report and renders rows (AC-4)', () => {
    setup();
    attendanceSpy.getCustomReport.and.returnValue(of(customResult));
    component.selectTab('custom');
    fixture.detectChanges();
    component.generate();
    fixture.detectChanges();

    expect(attendanceSpy.getCustomReport).toHaveBeenCalled();
    expect(component.customRows().length).toBe(1);
    expect((fixture.nativeElement.textContent as string)).toContain('Ada Lovelace');
  });

  it('blocks generate without a date range (AC-4)', () => {
    setup();
    component.selectTab('custom');
    component.patchFilter('from', '');
    component.patchFilter('to', '');
    component.generate();
    expect(attendanceSpy.getCustomReport).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('exports the custom report by downloading the blob (AC-4, FR-5)', () => {
    setup();
    const blob = new Blob(['employee,present\nAda,10'], { type: 'text/csv' });
    const resp = new HttpResponse<Blob>({
      body: blob,
      headers: new HttpHeaders({ 'Content-Disposition': 'attachment; filename="report.csv"' }),
    });
    attendanceSpy.exportCustomReport.and.returnValue(of(resp));

    const createUrlSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    const revokeSpy = spyOn(URL, 'revokeObjectURL');
    const clickSpy = jasmine.createSpy('click');
    const anchor = { href: '', download: '', click: clickSpy } as unknown as HTMLAnchorElement;
    spyOn(document, 'createElement').and.returnValue(anchor);

    component.selectTab('custom');
    component.exportAs('csv');

    expect(attendanceSpy.exportCustomReport).toHaveBeenCalledWith(jasmine.any(Object), 'csv');
    expect(createUrlSpy).toHaveBeenCalledWith(blob);
    expect(anchor.download).toBe('report.csv'); // from Content-Disposition
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeSpy).toHaveBeenCalled();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  // ─── Trends (AC-5) ──────────────────────────────────────────
  it('loads and renders the four trend charts (AC-5)', () => {
    setup();
    attendanceSpy.getTrends.and.returnValue(of(trendsResult));
    component.selectTab('trends');
    fixture.detectChanges();

    expect(attendanceSpy.getTrends).toHaveBeenCalledWith(12);
    expect(component.trendCharts().length).toBe(4);
    const svgs = fixture.nativeElement.querySelectorAll('[data-test="trend-svg"]');
    expect(svgs.length).toBe(4);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Attendance rate');
    expect(text).toContain('Absenteeism rate');
  });

  it('computes trend point coordinates (AC-5)', () => {
    setup();
    expect(component.pointX(0, 2)).toBe(0);
    expect(component.pointX(1, 2)).toBe(component.chartW);
    expect(component.pointX(0, 1)).toBe(component.chartW / 2);
    expect(component.pointY(0, 100)).toBe(component.chartH); // 0 -> bottom
    expect(component.pointY(100, 100)).toBe(0); // max -> top
  });

  // ─── Scheduled (FR-8) ───────────────────────────────────────
  it('creates a scheduled report config and prepends it to the list (FR-8)', () => {
    setup();
    const created: IScheduledReportConfig = {
      id: 's-new',
      reportType: 'daily-attendance',
      frequency: 'DAILY',
      filters: {},
      recipients: ['hr@acme.com'],
      deliveryTime: '08:00',
      format: 'CSV',
      isActive: true,
    };
    attendanceSpy.createScheduledReport.and.returnValue(of(created));

    component.selectTab('scheduled');
    component.onRecipientsChange('hr@acme.com, ops@acme.com');
    expect(component.scheduleForm().recipients).toEqual(['hr@acme.com', 'ops@acme.com']);

    component.saveSchedule();
    expect(attendanceSpy.createScheduledReport).toHaveBeenCalled();
    expect(component.schedules().length).toBe(1);
    expect(component.schedules()[0].id).toBe('s-new');
    // form reset
    expect(component.scheduleForm().recipients.length).toBe(0);
  });

  it('blocks saving a schedule with no recipients (FR-8)', () => {
    setup();
    component.selectTab('scheduled');
    component.saveSchedule();
    expect(attendanceSpy.createScheduledReport).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('deletes a scheduled report from the list (FR-8)', () => {
    setup();
    component.schedules.set([
      {
        id: 's1',
        reportType: 'daily-attendance',
        frequency: 'DAILY',
        filters: {},
        recipients: ['a@b.com'],
        deliveryTime: '08:00',
        format: 'CSV',
        isActive: true,
      },
    ]);
    attendanceSpy.deleteScheduledReport.and.returnValue(of(void 0));

    component.deleteSchedule(component.schedules()[0]);
    expect(attendanceSpy.deleteScheduledReport).toHaveBeenCalledWith('s1');
    expect(component.schedules().length).toBe(0);
  });
});
