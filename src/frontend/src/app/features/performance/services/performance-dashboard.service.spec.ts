import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PerformanceDashboardService } from './performance-dashboard.service';
import { environment } from '../../../../environments/environment';
import {
  IDashboardFilters,
  IDashboardOverview,
  IDepartmentDrilldown,
  ITrendResponse,
  emptyFilters,
} from '../models/dashboard.models';

describe('PerformanceDashboardService (US-PRF-007)', () => {
  let service: PerformanceDashboardService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/dashboard`;

  const mockOverview: IDashboardOverview = {
    scope: 'Organization',
    scopeLabel: 'Acme Corp',
    filterOptions: {
      cycles: [{ id: 'c1', label: '2026 Annual' }],
      departments: [{ id: 'd1', label: 'Engineering' }],
      grades: [],
      employmentTypes: [],
      locations: [],
    },
    completionRate: 72,
    averageScore: 81,
    scoreScaleMax: 100,
    ratedCount: 40,
    histogram: [],
    departmentAverages: [],
    topPerformers: [],
    bottomPerformers: [],
    teamRanking: [],
    cycleProgress: {
      totalParticipants: 50,
      goalSettingComplete: 50,
      selfAssessmentComplete: 45,
      managerReviewComplete: 40,
      signedOff: 36,
    },
    availableExportFormats: ['Csv', 'Excel'],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PerformanceDashboardService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PerformanceDashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getOverview() GETs the overview with withCredentials and no params when filters empty', () => {
    let result: IDashboardOverview | undefined;
    service.getOverview(emptyFilters()).subscribe((o) => (result = o));

    const req = httpMock.expectOne(`${baseUrl}/overview`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.params.keys().length).toBe(0);
    req.flush(mockOverview);
    expect(result).toEqual(mockOverview);
  });

  it('getOverview() maps multi-select filters to repeated query params', () => {
    const filters: IDashboardFilters = {
      cycleIds: ['c1', 'c2'],
      departmentIds: ['d1'],
      grades: ['B3'],
      employmentTypes: ['FullTime'],
      locations: ['NYC'],
    };
    service.getOverview(filters).subscribe();

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/overview`);
    expect(req.request.params.getAll('cycleId')).toEqual(['c1', 'c2']);
    expect(req.request.params.get('departmentId')).toBe('d1');
    expect(req.request.params.get('grade')).toBe('B3');
    expect(req.request.params.get('employmentType')).toBe('FullTime');
    expect(req.request.params.get('location')).toBe('NYC');
    req.flush(mockOverview);
  });

  it('getTrend() GETs the trend endpoint with the same filter params', () => {
    const filters: IDashboardFilters = {
      ...emptyFilters(),
      cycleIds: ['c1', 'c2', 'c3'],
    };
    const trend: ITrendResponse = {
      scoreScaleMax: 100,
      series: [
        {
          key: null,
          label: 'Organization',
          points: [
            { cycleId: 'c1', cycleLabel: '2024', averageScore: 70 },
            { cycleId: 'c2', cycleLabel: '2025', averageScore: 75 },
            { cycleId: 'c3', cycleLabel: '2026', averageScore: 81 },
          ],
        },
      ],
    };
    let result: ITrendResponse | undefined;
    service.getTrend(filters).subscribe((t) => (result = t));

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/trend`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.getAll('cycleId')).toEqual(['c1', 'c2', 'c3']);
    req.flush(trend);
    expect(result?.series[0].points.length).toBe(3);
  });

  it('getDepartmentDrilldown() targets the department path and passes cycleId when given', () => {
    const drill: IDepartmentDrilldown = {
      departmentId: 'd1',
      departmentName: 'Engineering',
      cycleLabel: '2026 Annual',
      averageScore: 83,
      scoreScaleMax: 100,
      employees: [],
    };
    let result: IDepartmentDrilldown | undefined;
    service
      .getDepartmentDrilldown('d1', 'c1')
      .subscribe((d) => (result = d));

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/department/d1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('cycleId')).toBe('c1');
    req.flush(drill);
    expect(result?.departmentName).toBe('Engineering');
  });

  it('getDepartmentDrilldown() omits cycleId when not provided', () => {
    service.getDepartmentDrilldown('d2').subscribe();
    const req = httpMock.expectOne(`${baseUrl}/department/d2`);
    expect(req.request.params.has('cycleId')).toBeFalse();
    req.flush({
      departmentId: 'd2',
      departmentName: 'Sales',
      cycleLabel: '2026',
      averageScore: null,
      scoreScaleMax: 100,
      employees: [],
    });
  });

  it('export() requests a blob with the format param and observes the response', () => {
    const filters: IDashboardFilters = { ...emptyFilters(), cycleIds: ['c1'] };
    let response: { body: Blob | null } | undefined;
    service.export('Csv', filters).subscribe((r) => (response = r));

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/export`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.get('format')).toBe('Csv');
    expect(req.request.params.get('cycleId')).toBe('c1');
    req.flush(new Blob(['a,b,c']), {
      headers: { 'Content-Disposition': 'attachment; filename="dashboard.csv"' },
    });
    expect(response?.body).toBeInstanceOf(Blob);
  });
});
