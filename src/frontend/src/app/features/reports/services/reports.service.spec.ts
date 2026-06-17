import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ReportsService } from './reports.service';
import {
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
});
