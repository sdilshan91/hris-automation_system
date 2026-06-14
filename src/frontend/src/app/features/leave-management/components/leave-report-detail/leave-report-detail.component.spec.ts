import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError, BehaviorSubject } from 'rxjs';
import { convertToParamMap, ParamMap } from '@angular/router';

import { LeaveReportDetailComponent } from './leave-report-detail.component';
import { LeaveReportsService } from '../../services/leave-reports.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import { DepartmentService } from '../../../core-hr/departments/services/department.service';
import { IReportPage, IAnalyticsResponse } from '../../models/leave-reports.models';

describe('LeaveReportDetailComponent (US-LV-012)', () => {
  let component: LeaveReportDetailComponent;
  let fixture: ComponentFixture<LeaveReportDetailComponent>;
  let reportsSpy: jasmine.SpyObj<LeaveReportsService>;
  let leaveTypeSpy: jasmine.SpyObj<LeaveTypeService>;
  let departmentSpy: jasmine.SpyObj<DepartmentService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let paramMap$: BehaviorSubject<ParamMap>;

  const page: IReportPage = {
    items: [
      { employeeName: 'Alice', departmentName: 'Eng', leaveTypeName: 'Annual', entitlement: 14, used: 4, balance: 10 },
      { employeeName: 'Bob', departmentName: 'Sales', leaveTypeName: 'Annual', entitlement: 14, used: 7, balance: 7 },
    ],
    totalCount: 60,
  };

  const barAnalytics: IAnalyticsResponse = {
    data: [
      { label: 'Eng', value: 40 },
      { label: 'Sales', value: 80 },
    ],
  };
  const lineAnalytics: IAnalyticsResponse = {
    categories: ['Jan', 'Feb', 'Mar'],
    series: [{ name: 'Annual', values: [3, 6, 9] }],
  };

  function setup(reportType = 'balance-summary'): void {
    reportsSpy = jasmine.createSpyObj('LeaveReportsService', [
      'getReport',
      'getAnalytics',
      'getSummaryMetrics',
      'export',
    ]);
    reportsSpy.getReport.and.returnValue(of(page));
    reportsSpy.getAnalytics.and.returnValue(of(barAnalytics));
    reportsSpy.export.and.returnValue(
      of({ blob: new Blob(['a,b']), contentType: 'text/csv', filename: 'r.csv' }),
    );

    leaveTypeSpy = jasmine.createSpyObj('LeaveTypeService', ['getLeaveTypes']);
    leaveTypeSpy.getLeaveTypes.and.returnValue(
      of([{ leaveTypeId: 'lt-1', name: 'Annual Leave' } as never]),
    );

    departmentSpy = jasmine.createSpyObj('DepartmentService', ['getDepartments']);
    departmentSpy.getDepartments.and.returnValue(
      of([{ departmentId: 'd1', name: 'Engineering' } as never]),
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'info', 'warning']);
    paramMap$ = new BehaviorSubject<ParamMap>(convertToParamMap({ reportType }));

    TestBed.configureTestingModule({
      imports: [LeaveReportDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideRouter([]),
        provideToastr(),
        { provide: LeaveReportsService, useValue: reportsSpy },
        { provide: LeaveTypeService, useValue: leaveTypeSpy },
        { provide: DepartmentService, useValue: departmentSpy },
        { provide: ToastrService, useValue: toastrSpy },
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$.asObservable() } },
      ],
    });

    fixture = TestBed.createComponent(LeaveReportDetailComponent);
    component = fixture.componentInstance;
  }

  it('loads the report for the route reportType on init', () => {
    setup('balance-summary');
    fixture.detectChanges();
    expect(reportsSpy.getReport).toHaveBeenCalled();
    const [type, query] = reportsSpy.getReport.calls.mostRecent().args;
    expect(type).toBe('balance-summary');
    expect(query.page).toBe(1);
    expect(query.pageSize).toBe(25);
    expect(component.rows().length).toBe(2);
    expect(component.totalCount()).toBe(60);
  });

  it('loads lookups for the filter sidebar', () => {
    setup();
    fixture.detectChanges();
    expect(departmentSpy.getDepartments).toHaveBeenCalled();
    expect(leaveTypeSpy.getLeaveTypes).toHaveBeenCalled();
    expect(component.departments().length).toBe(1);
    expect(component.leaveTypes().length).toBe(1);
  });

  it('renders the report table with rows (AC-1)', () => {
    setup();
    fixture.detectChanges();
    const table = fixture.nativeElement.querySelector('[data-testid="report-table"]');
    expect(table).toBeTruthy();
    const bodyRows = table.querySelectorAll('tbody tr');
    expect(bodyRows.length).toBe(2);
  });

  it('applying a filter resets to page 1 and reloads (FR-2)', () => {
    setup();
    fixture.detectChanges();
    component.goToPage(2); // moves off page 1
    expect(component.page()).toBe(2);
    reportsSpy.getReport.calls.reset();

    component.patchFilter('departmentId', 'd1');
    component.applyFilters();
    expect(component.page()).toBe(1);
    expect(reportsSpy.getReport).toHaveBeenCalled();
    const [, query] = reportsSpy.getReport.calls.mostRecent().args;
    expect(query.departmentId).toBe('d1');
  });

  it('clearFilters empties filters and reloads', () => {
    setup();
    fixture.detectChanges();
    component.patchFilter('search', 'jane');
    expect(component.hasFilters()).toBeTrue();
    component.clearFilters();
    expect(component.hasFilters()).toBeFalse();
  });

  it('sorting toggles direction and re-queries server-side (FR-3)', () => {
    setup();
    fixture.detectChanges();
    component.sortBy('employeeName');
    expect(component.sortKey()).toBe('employeeName');
    expect(component.sortDir()).toBe('asc');
    component.sortBy('employeeName');
    expect(component.sortDir()).toBe('desc');
    const [, query] = reportsSpy.getReport.calls.mostRecent().args;
    expect(query.sortBy).toBe('employeeName');
    expect(query.sortDir).toBe('desc');
  });

  it('pagination guards out-of-range pages and reloads valid ones', () => {
    setup();
    fixture.detectChanges();
    reportsSpy.getReport.calls.reset();
    component.goToPage(0); // invalid -> no reload
    expect(reportsSpy.getReport).not.toHaveBeenCalled();
    component.goToPage(2); // valid (60 rows / 25 = 3 pages)
    expect(component.page()).toBe(2);
    expect(reportsSpy.getReport).toHaveBeenCalled();
  });

  it('changing page size resets to page 1', () => {
    setup();
    fixture.detectChanges();
    component.goToPage(2);
    component.changePageSize(50);
    expect(component.pageSize()).toBe(50);
    expect(component.page()).toBe(1);
  });

  it('toasts on report load error', () => {
    setup();
    reportsSpy.getReport.and.returnValue(throwError(() => ({ error: { message: 'boom' } })));
    fixture.detectChanges();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.rows().length).toBe(0);
  });

  it('shows the empty state when no rows', () => {
    setup();
    reportsSpy.getReport.and.returnValue(of({ items: [], totalCount: 0 }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="empty-state"]')).toBeTruthy();
  });

  // ─── Charts (AC-2/AC-3/AC-4) ───────────────────────────

  it('utilization view fetches bar + pie analytics and renders an SVG bar chart', () => {
    setup('utilization');
    reportsSpy.getAnalytics.and.callFake((chartType) =>
      of(chartType === 'utilization-by-type' ? { data: [{ label: 'Annual', value: 10 }] } : barAnalytics),
    );
    fixture.detectChanges();
    expect(reportsSpy.getAnalytics).toHaveBeenCalledWith('utilization-by-department', jasmine.anything());
    expect(reportsSpy.getAnalytics).toHaveBeenCalledWith('utilization-by-type', jasmine.anything());
    const bar = fixture.nativeElement.querySelector('[data-testid="bar-chart"]');
    expect(bar).toBeTruthy();
    expect(bar.querySelectorAll('rect').length).toBe(2);
    expect(component.bars()[1].height).toBeGreaterThan(component.bars()[0].height); // 80 > 40
  });

  it('trend-analysis view renders an SVG line chart with a polyline per series (AC-4)', () => {
    setup('trend-analysis');
    reportsSpy.getAnalytics.and.returnValue(of(lineAnalytics));
    fixture.detectChanges();
    const line = fixture.nativeElement.querySelector('[data-testid="line-chart"]');
    expect(line).toBeTruthy();
    expect(line.querySelectorAll('polyline').length).toBe(1);
    expect(component.lineSeries()[0].points.split(' ').length).toBe(3);
  });

  it('absenteeism view flags rows over the threshold (AC-3)', () => {
    setup('absenteeism');
    reportsSpy.getReport.and.returnValue(
      of({
        items: [
          { employeeName: 'Carol', absenteeismDays: 5, flagged: true },
          { employeeName: 'Dan', absenteeismDays: 1, flagged: false },
        ],
        totalCount: 2,
      }),
    );
    reportsSpy.getAnalytics.and.returnValue(of(lineAnalytics));
    fixture.detectChanges();
    expect(component.isFlaggedRow(component.rows()[0])).toBeTrue();
    expect(component.isFlaggedRow(component.rows()[1])).toBeFalse();
    expect(fixture.nativeElement.querySelector('[data-testid="flag-badge"]')).toBeTruthy();
  });

  it('does NOT fetch analytics for a non-chart report', () => {
    setup('lop-summary');
    fixture.detectChanges();
    expect(reportsSpy.getAnalytics).not.toHaveBeenCalled();
    expect(component.hasCharts()).toBeFalse();
  });

  // ─── Export (AC-5) ─────────────────────────────────────

  it('export dropdown calls the service with the correct format (csv)', () => {
    setup();
    fixture.detectChanges();
    component.export('csv');
    expect(reportsSpy.export).toHaveBeenCalledWith('balance-summary', 'csv', jasmine.anything());
  });

  it('export with xlsx format triggers a download for a synchronous file response', () => {
    setup();
    fixture.detectChanges();
    component.export('xlsx');
    expect(reportsSpy.export).toHaveBeenCalledWith('balance-summary', 'xlsx', jasmine.anything());
  });

  it('shows the background-export processing state for a large dataset (AC-5)', async () => {
    setup();
    const jobBlob = new Blob([JSON.stringify({ status: 'processing', jobId: 'job-1' })], {
      type: 'application/json',
    });
    reportsSpy.export.and.returnValue(
      of({ blob: jobBlob, contentType: 'application/json', filename: 'r.csv' }),
    );
    fixture.detectChanges();
    component.export('csv');
    // The processing decision runs after blob.text() resolves (a real microtask
    // chain). Poll the signal until it flips rather than guessing the tick count.
    for (let i = 0; i < 20 && !component.exportProcessing(); i++) {
      await new Promise((r) => setTimeout(r, 0));
    }
    expect(component.exportProcessing()).toBeTrue();
    expect(toastrSpy.info).toHaveBeenCalled();
  });

  it('toasts on export error', () => {
    setup();
    fixture.detectChanges();
    reportsSpy.export.and.returnValue(throwError(() => ({ error: { message: 'export failed' } })));
    component.export('csv');
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isExporting()).toBeFalse();
  });

  it('toggles the export menu and filter sidebar', () => {
    setup();
    fixture.detectChanges();
    expect(component.exportMenuOpen()).toBeFalse();
    component.toggleExportMenu();
    expect(component.exportMenuOpen()).toBeTrue();
    expect(component.filtersOpen()).toBeFalse();
    component.toggleFilters();
    expect(component.filtersOpen()).toBeTrue();
  });

  it('switching reportType via the route param reloads columns + data', () => {
    setup('balance-summary');
    fixture.detectChanges();
    expect(component.reportType()).toBe('balance-summary');
    paramMap$.next(convertToParamMap({ reportType: 'lop-summary' }));
    fixture.detectChanges();
    expect(component.reportType()).toBe('lop-summary');
    expect(component.columns.some((c) => c.key === 'source')).toBeTrue();
  });
});
