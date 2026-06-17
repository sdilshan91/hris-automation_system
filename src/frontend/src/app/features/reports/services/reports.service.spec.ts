import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  ReportsService,
  downloadBlob,
  filenameFromDisposition,
} from './reports.service';
import {
  IExportHistoryItem,
  IExportResponse,
  IReportCatalogServerItem,
  IReportFilters,
  IReportResult,
  emptyReportFilters,
} from '../models/reports.models';
import { environment } from '../../../../environments/environment';

describe('ReportsService', () => {
  let service: ReportsService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/reports`;

  // Server sends only { type, icon }; the FE derives i18n keys.
  const mockCatalog: IReportCatalogServerItem[] = [
    { type: 'headcount', icon: 'groups' },
  ];

  const mockResult: IReportResult = {
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

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ReportsService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ReportsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('GETs the report catalog with credentials and derives i18n keys from type', () => {
    service.getCatalog().subscribe((items) => {
      expect(items.length).toBe(1);
      expect(items[0].type).toBe('headcount');
      expect(items[0].icon).toBe('groups');
      expect(items[0].titleKey).toBe('reports.catalog.headcount.title');
      expect(items[0].descriptionKey).toBe(
        'reports.catalog.headcount.description'
      );
    });
    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockCatalog);
  });

  it('POSTs to /{type}/generate with filters in the body and refresh=false by default', () => {
    const filters: IReportFilters = {
      ...emptyReportFilters(),
      departmentIds: ['dep-1'],
      dateFrom: '2026-06-01',
    };
    service.generateReport('headcount', filters).subscribe((res) => {
      expect(res.metadata.title).toBe('Headcount Summary');
    });
    const req = httpMock.expectOne(
      (r) =>
        r.url === `${baseUrl}/headcount/generate` &&
        r.params.get('refresh') === 'false'
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(filters);
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockResult);
  });

  it('adds ?refresh=true when bypassing the cache (FR-8)', () => {
    const filters = emptyReportFilters();
    service.generateReport('turnover', filters, true).subscribe();
    const req = httpMock.expectOne(
      (r) =>
        r.url === `${baseUrl}/turnover/generate` &&
        r.params.get('refresh') === 'true'
    );
    expect(req.request.method).toBe('POST');
    req.flush(mockResult);
  });

  it('targets the correct URL per report type', () => {
    service.generateReport('demographics', emptyReportFilters()).subscribe();
    const req = httpMock.expectOne(
      (r) => r.url === `${baseUrl}/demographics/generate`
    );
    expect(req.request.method).toBe('POST');
    req.flush(mockResult);
  });

  // US-RPT-002: the new leave & attendance report types route through the same
  // generic generate endpoint.
  it('targets the correct URL for the US-RPT-002 report types', () => {
    service.generateReport('leave-balance', emptyReportFilters()).subscribe();
    const req = httpMock.expectOne(
      (r) => r.url === `${baseUrl}/leave-balance/generate`
    );
    expect(req.request.method).toBe('POST');
    req.flush(mockResult);
  });

  // US-RPT-002 §8: the server-driven `category` flows through to the catalog
  // item so the catalog can render grouped sections.
  it('passes the server category through to the catalog item', () => {
    const serverCatalog: IReportCatalogServerItem[] = [
      { type: 'leave-utilization', icon: 'beach_access', category: 'leave-attendance' },
    ];
    service.getCatalog().subscribe((items) => {
      expect(items[0].category).toBe('leave-attendance');
      expect(items[0].titleKey).toBe('reports.catalog.leave-utilization.title');
    });
    const req = httpMock.expectOne(baseUrl);
    req.flush(serverCatalog);
  });

  // ── US-RPT-004 export ──

  it('POSTs an export with the format + filters in the body (AC-1)', () => {
    const filters: IReportFilters = {
      ...emptyReportFilters(),
      departmentIds: ['dep-1'],
    };
    const response: IExportResponse = {
      exportId: 'exp-1',
      status: 'Completed',
      rowCount: 42,
      format: 'csv',
    };
    service.exportReport('headcount', filters, 'csv').subscribe((res) => {
      expect(res.exportId).toBe('exp-1');
      expect(res.status).toBe('Completed');
    });
    const req = httpMock.expectOne(`${baseUrl}/headcount/export`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ format: 'csv', filters });
    expect(req.request.withCredentials).toBeTrue();
    req.flush(response);
  });

  it('includes includeCharts in the body only when provided (PDF/Excel)', () => {
    service
      .exportReport('headcount', emptyReportFilters(), 'pdf', true)
      .subscribe();
    const req = httpMock.expectOne(`${baseUrl}/headcount/export`);
    expect(req.request.body.includeCharts).toBeTrue();
    expect(req.request.body.format).toBe('pdf');
    req.flush({ exportId: 'e', status: 'Queued', rowCount: 0, format: 'pdf' });
  });

  it('omits includeCharts when not provided (CSV)', () => {
    service.exportReport('headcount', emptyReportFilters(), 'csv').subscribe();
    const req = httpMock.expectOne(`${baseUrl}/headcount/export`);
    expect('includeCharts' in req.request.body).toBeFalse();
    req.flush({ exportId: 'e', status: 'Completed', rowCount: 0, format: 'csv' });
  });

  it('GETs the export history list (§8)', () => {
    const history: IExportHistoryItem[] = [
      {
        id: 'exp-1',
        reportType: 'headcount',
        format: 'csv',
        status: 'Completed',
        requestedAt: '2026-06-17T10:00:00Z',
        completedAt: '2026-06-17T10:00:01Z',
        rowCount: 42,
        fileSizeBytes: 2048,
        expiresAt: '2026-06-24T10:00:01Z',
        downloadReady: true,
      },
    ];
    service.listExports().subscribe((items) => {
      expect(items.length).toBe(1);
      expect(items[0].downloadReady).toBeTrue();
    });
    const req = httpMock.expectOne(`${baseUrl}/exports`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(history);
  });

  it('downloads an export as a blob, observing the full response (AC-5)', () => {
    const blob = new Blob(['a,b,c'], { type: 'text/csv' });
    service.downloadExport('exp-9').subscribe((resp) => {
      expect(resp.body).toBe(blob);
    });
    const req = httpMock.expectOne(`${baseUrl}/exports/exp-9/download`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(blob);
  });

  it('filenameFromDisposition prefers the UTF-8 form then the quoted form', () => {
    expect(
      filenameFromDisposition("attachment; filename*=UTF-8''headcount%20(1).csv")
    ).toBe('headcount (1).csv');
    expect(
      filenameFromDisposition('attachment; filename="report.xlsx"')
    ).toBe('report.xlsx');
    expect(filenameFromDisposition(null)).toBeNull();
  });

  it('downloadBlob clicks an anchor with the filename and revokes the URL', () => {
    const createSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:url');
    const revokeSpy = spyOn(URL, 'revokeObjectURL');
    const anchor = document.createElement('a');
    const clickSpy = spyOn(anchor, 'click');
    spyOn(document, 'createElement').and.returnValue(anchor);

    downloadBlob(new Blob(['x']), 'my-report.csv');

    expect(createSpy).toHaveBeenCalled();
    expect(anchor.download).toBe('my-report.csv');
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeSpy).toHaveBeenCalledWith('blob:url');
  });
});
