import { TestBed, ComponentFixture } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { HttpResponse, HttpHeaders } from '@angular/common/http';
import { signal } from '@angular/core';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { ReportViewerComponent } from './report-viewer.component';
import { ReportsService } from '../../services/reports.service';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  IExportHistoryItem,
  IExportResponse,
  IReportResult,
  emptyReportFilters,
} from '../../models/reports.models';

describe('ReportViewerComponent', () => {
  let fixture: ComponentFixture<ReportViewerComponent>;
  let component: ReportViewerComponent;
  let serviceSpy: jasmine.SpyObj<ReportsService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let authStub: { permissions: ReturnType<typeof signal<string[]>> };

  const result: IReportResult = {
    metadata: {
      type: 'headcount',
      title: 'Headcount Summary',
      generatedAt: '2026-06-17T00:00:00Z',
      appliedFilters: emptyReportFilters(),
      summary: [{ label: 'Total', value: 42 }],
    },
    charts: [
      {
        kind: 'bar',
        title: 'Headcount by sub-department',
        series: [{ name: 'Headcount', points: [{ label: 'Eng', value: 20 }] }],
      },
    ],
    table: {
      columns: ['Sub-department', 'Headcount'],
      rows: [['Eng', 20]],
    },
  };

  function setup(): void {
    fixture = TestBed.createComponent(ReportViewerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    serviceSpy = jasmine.createSpyObj<ReportsService>('ReportsService', [
      'getCatalog',
      'generateReport',
      'exportReport',
      'listExports',
      'downloadExport',
    ]);
    serviceSpy.generateReport.and.returnValue(of(result));
    serviceSpy.listExports.and.returnValue(of([]));
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'info',
      'error',
      'success',
    ]);
    // Default: user CAN export (most tests exercise the export UI).
    authStub = { permissions: signal<string[]>(['Reports.Export']) };

    TestBed.configureTestingModule({
      imports: [ReportViewerComponent],
      providers: [
        provideTranslateService(),
        { provide: ReportsService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        { provide: AuthService, useValue: authStub },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ type: 'headcount' }) },
          },
        },
      ],
    });
  });

  it('generates the report for the route type on init (AC-2)', () => {
    setup();
    expect(component.reportType()).toBe('headcount');
    expect(serviceSpy.generateReport).toHaveBeenCalledWith(
      'headcount',
      jasmine.any(Object),
      false
    );
    expect(component.result()?.metadata.title).toBe('Headcount Summary');
  });

  it('toggles between chart and table view (FR-4)', () => {
    setup();
    expect(component.view()).toBe('chart');
    // Chart view shows chart cards.
    expect(
      fixture.nativeElement.querySelectorAll('.rv-chart-card').length
    ).toBe(1);

    component.setView('table');
    fixture.detectChanges();
    // Table view (the screen-reader alternative) is reachable (NFR-5).
    const table = fixture.nativeElement.querySelector('.rv-table');
    expect(table).toBeTruthy();
    expect(table.querySelectorAll('thead th').length).toBe(2);
    expect(table.querySelectorAll('tbody tr').length).toBe(1);
  });

  it('Refresh regenerates with the cache-bypass flag (FR-8)', () => {
    setup();
    serviceSpy.generateReport.calls.reset();
    component.refresh();
    expect(serviceSpy.generateReport).toHaveBeenCalledWith(
      'headcount',
      jasmine.any(Object),
      true
    );
  });

  it('builds filters from the bound form fields', () => {
    setup();
    component.dateFrom = '2026-06-01';
    component.departmentsRaw = 'dep-1, dep-2';
    component.employmentTypes = ['full-time'];
    const filters = component.buildFilters();
    expect(filters.dateFrom).toBe('2026-06-01');
    expect(filters.departmentIds).toEqual(['dep-1', 'dep-2']);
    expect(filters.employmentTypes).toEqual(['full-time']);
  });

  it('renders summary KPI stats from metadata', () => {
    setup();
    const stats = fixture.nativeElement.querySelectorAll('.rv-stat');
    expect(stats.length).toBe(1);
  });

  it('surfaces a generate error', () => {
    serviceSpy.generateReport.and.returnValue(
      throwError(() => ({ error: { message: 'failed' } }))
    );
    setup();
    expect(component.loadError()).toBe('failed');
    expect(component.loading()).toBeFalse();
  });

  // ── US-RPT-002 FR-2: leave & attendance filters ──

  it('builds the US-RPT-002 leave & attendance filters from the form (FR-2)', () => {
    setup();
    component.employeeId = ' emp-1 ';
    component.leaveTypesRaw = 'annual, sick';
    component.shiftsRaw = 'morning';
    const filters = component.buildFilters();
    expect(filters.employeeId).toBe('emp-1');
    expect(filters.leaveTypeIds).toEqual(['annual', 'sick']);
    expect(filters.shiftIds).toEqual(['morning']);
  });

  it('sends an empty employeeId as null', () => {
    setup();
    component.employeeId = '   ';
    expect(component.buildFilters().employeeId).toBeNull();
  });

  it('generates the new leave-balance report for its route type', () => {
    TestBed.resetTestingModule();
    serviceSpy = jasmine.createSpyObj<ReportsService>('ReportsService', [
      'getCatalog',
      'generateReport',
      'exportReport',
      'listExports',
      'downloadExport',
    ]);
    serviceSpy.generateReport.and.returnValue(of(balanceResult));
    serviceSpy.listExports.and.returnValue(of([]));
    TestBed.configureTestingModule({
      imports: [ReportViewerComponent],
      providers: [
        provideTranslateService(),
        { provide: ReportsService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        { provide: AuthService, useValue: authStub },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ type: 'leave-balance' }),
            },
          },
        },
      ],
    });
    setup();
    expect(component.reportType()).toBe('leave-balance');
    expect(serviceSpy.generateReport).toHaveBeenCalledWith(
      'leave-balance',
      jasmine.any(Object),
      false
    );
  });

  // ── US-RPT-002 AC-2: leave-balance color bands ──

  const balanceResult: IReportResult = {
    metadata: {
      type: 'leave-balance',
      title: 'Leave Balance',
      generatedAt: '2026-06-17T00:00:00Z',
      appliedFilters: emptyReportFilters(),
      summary: [],
    },
    charts: [],
    table: {
      columns: ['Employee', 'Annual', 'Sick'],
      rows: [
        ['Alice', 18, 2],
        ['Bob', 6, 5],
      ],
      // bands aligned to rows: name cells have no band; Annual ok/low, Sick low/warn.
      cellBands: [
        [null, 'ok', 'low'],
        [null, 'low', 'warn'],
      ],
    },
  };

  function setupBalance(): void {
    serviceSpy.generateReport.and.returnValue(of(balanceResult));
    setup();
    component.setView('table');
    fixture.detectChanges();
  }

  it('maps server cell bands via bandAt() (AC-2)', () => {
    setup();
    expect(component.bandAt(balanceResult.table, 0, 1)).toBe('ok');
    expect(component.bandAt(balanceResult.table, 0, 2)).toBe('low');
    expect(component.bandAt(balanceResult.table, 1, 2)).toBe('warn');
    // The employee-name cell carries no band.
    expect(component.bandAt(balanceResult.table, 0, 0)).toBeNull();
  });

  it('degrades to null bands for reports without band data (AC-2 graceful)', () => {
    setup();
    // The default headcount result has no cellBands at all.
    expect(component.bandAt(result.table, 0, 0)).toBeNull();
    expect(component.bandAt(result.table, 0, 1)).toBeNull();
  });

  it('renders balance-band CSS classes on the table cells (AC-2)', () => {
    setupBalance();
    const okCells = fixture.nativeElement.querySelectorAll('td.rv-band-ok');
    const warnCells = fixture.nativeElement.querySelectorAll('td.rv-band-warn');
    const lowCells = fixture.nativeElement.querySelectorAll('td.rv-band-low');
    expect(okCells.length).toBe(1);
    expect(warnCells.length).toBe(1);
    expect(lowCells.length).toBe(2);
  });

  it('does not color any cell for a report without band data (AC-2 graceful)', () => {
    setup();
    component.setView('table');
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelectorAll(
        'td.rv-band-ok, td.rv-band-warn, td.rv-band-low'
      ).length
    ).toBe(0);
  });

  // ── US-RPT-004 export ──

  const completedResponse: IExportResponse = {
    exportId: 'exp-1',
    status: 'Completed',
    rowCount: 42,
    format: 'csv',
  };
  const queuedResponse: IExportResponse = {
    exportId: 'exp-2',
    status: 'Queued',
    rowCount: 5000,
    format: 'pdf',
  };

  function blobResponse(): HttpResponse<Blob> {
    return new HttpResponse<Blob>({
      body: new Blob(['a,b,c'], { type: 'text/csv' }),
      headers: new HttpHeaders({
        'Content-Disposition': 'attachment; filename="headcount.csv"',
      }),
    });
  }

  it('renders the three export format options when the menu is open (AC-1)', () => {
    setup();
    component.toggleExportMenu();
    fixture.detectChanges();
    const items = fixture.nativeElement.querySelectorAll(
      '.rv-export-desktop .rv-export-item'
    );
    expect(items.length).toBe(3);
  });

  it('hides the export button without Reports.Export permission (precondition)', () => {
    authStub = { permissions: signal<string[]>([]) };
    TestBed.overrideProvider(AuthService, { useValue: authStub });
    setup();
    expect(component.canExport()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('.rv-export-trigger')
    ).toBeNull();
  });

  it('CSV export with status=Completed downloads + saves the file (sync path)', () => {
    serviceSpy.exportReport.and.returnValue(of(completedResponse));
    serviceSpy.downloadExport.and.returnValue(of(blobResponse()));
    setup();

    component.onExport('csv');

    expect(serviceSpy.exportReport).toHaveBeenCalledWith(
      'headcount',
      jasmine.any(Object),
      'csv',
      undefined
    );
    expect(serviceSpy.downloadExport).toHaveBeenCalledWith('exp-1');
    expect(toastrSpy.info).not.toHaveBeenCalled();
    expect(component.exporting()).toBeFalse();
  });

  it('PDF export sends includeCharts=true', () => {
    serviceSpy.exportReport.and.returnValue(of(completedResponse));
    serviceSpy.downloadExport.and.returnValue(of(blobResponse()));
    setup();
    component.onExport('pdf');
    expect(serviceSpy.exportReport).toHaveBeenCalledWith(
      'headcount',
      jasmine.any(Object),
      'pdf',
      true
    );
  });

  it('status=Queued toasts and refreshes the history (async path)', () => {
    serviceSpy.exportReport.and.returnValue(of(queuedResponse));
    serviceSpy.listExports.and.returnValue(of([]));
    setup();
    serviceSpy.listExports.calls.reset();

    component.onExport('pdf');

    expect(toastrSpy.info).toHaveBeenCalled();
    expect(serviceSpy.downloadExport).not.toHaveBeenCalled();
    expect(serviceSpy.listExports).toHaveBeenCalled();
    expect(component.historyOpen()).toBeTrue();
    expect(component.exporting()).toBeFalse();
  });

  it('surfaces an export error via toastr', () => {
    serviceSpy.exportReport.and.returnValue(
      throwError(() => ({ error: { message: 'boom' } }))
    );
    setup();
    component.onExport('csv');
    expect(toastrSpy.error).toHaveBeenCalledWith('boom');
    expect(component.exporting()).toBeFalse();
  });

  const historyItem: IExportHistoryItem = {
    id: 'exp-7',
    reportType: 'headcount',
    format: 'xlsx',
    status: 'Completed',
    requestedAt: '2026-06-17T10:00:00Z',
    completedAt: '2026-06-17T10:00:05Z',
    rowCount: 120,
    fileSizeBytes: 4096,
    expiresAt: '2026-06-24T10:00:05Z',
    downloadReady: true,
  };

  it('renders the history table rows and a Download button (§8)', () => {
    serviceSpy.listExports.and.returnValue(of([historyItem]));
    setup();
    component.toggleHistory();
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll(
      '.rv-history-table tbody tr'
    );
    expect(rows.length).toBe(1);
    const btn = fixture.nativeElement.querySelector('.rv-history-download');
    expect(btn).toBeTruthy();
    expect(btn.disabled).toBeFalse();
  });

  it('disables the Download button when downloadReady is false (gating)', () => {
    serviceSpy.listExports.and.returnValue(
      of([{ ...historyItem, downloadReady: false, status: 'Processing' }])
    );
    setup();
    component.toggleHistory();
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('.rv-history-download');
    expect(btn.disabled).toBeTrue();
  });

  it('onDownload pulls + saves a ready export and ignores a not-ready one', () => {
    serviceSpy.downloadExport.and.returnValue(of(blobResponse()));
    setup();

    component.onDownload(historyItem);
    expect(serviceSpy.downloadExport).toHaveBeenCalledWith('exp-7');

    serviceSpy.downloadExport.calls.reset();
    component.onDownload({ ...historyItem, downloadReady: false });
    expect(serviceSpy.downloadExport).not.toHaveBeenCalled();
  });

  it('formatBytes renders human-readable sizes', () => {
    setup();
    expect(component.formatBytes(512)).toBe('512 B');
    expect(component.formatBytes(2048)).toBe('2.0 KB');
    expect(component.formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
  });

  it('toggleHistory lazy-loads the export list on open', () => {
    setup();
    serviceSpy.listExports.calls.reset();
    component.toggleHistory();
    expect(serviceSpy.listExports).toHaveBeenCalledTimes(1);
  });
});
