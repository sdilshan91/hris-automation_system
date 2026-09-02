import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PerformanceDashboardService } from './performance-dashboard.service';
import { environment } from '../../../../environments/environment';
import {
  DASHBOARD_SCALE_FALLBACK,
  DashboardOverviewWire,
  DepartmentDrilldownWire,
  IDashboardFilters,
  IDashboardOverview,
  IDepartmentDrilldown,
  ITrendResponse,
  TrendWire,
  emptyFilters,
} from '../models/dashboard.models';

describe('PerformanceDashboardService (US-PRF-007)', () => {
  let service: PerformanceDashboardService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/dashboard`;

  // ─── WIRE fixture (the real PerformancePerformanceDashboardDto the API sends) ──
  // Note the renamed / nested fields: ratingScaleMax, scoredEmployeeCount,
  // scoreDistribution, progress{...completionRate}, and the *Completed counts.
  const overviewWire: DashboardOverviewWire = {
    scope: 'Organization',
    ratingScaleMax: 100,
    scoredEmployeeCount: 40,
    averageScore: 81,
    scoreDistribution: [
      { rangeStart: 0, rangeEnd: 50, label: '0–50', count: 4 },
      { rangeStart: 50, rangeEnd: 100, label: '50–100', count: 36 },
    ],
    departmentAverages: [
      {
        departmentId: 'd1',
        departmentName: 'Engineering',
        averageScore: 83,
        headcount: 12,
      },
    ],
    topPerformers: [
      {
        employeeId: 'e1',
        employeeName: 'Alex Doe',
        employeeNo: 'E-001',
        departmentId: 'd1',
        departmentName: 'Engineering',
        score: 96,
      },
    ],
    bottomPerformers: [],
    progress: {
      totalParticipants: 50,
      completionRate: 72,
      goalSettingCompleted: 50,
      selfAssessmentCompleted: 45,
      managerReviewCompleted: 40,
      calibrationCompleted: 38,
      signedOff: 36,
    },
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
    req.flush(overviewWire);

    expect(result?.scope).toBe('Organization');
    expect(result?.averageScore).toBe(81);
  });

  it('getOverview() maps every renamed / nested wire field onto the view-model', () => {
    // MUTATION ARM: fails against the un-migrated code, which cast the raw body to
    // IDashboardOverview so scoreScaleMax/ratedCount/histogram/cycleProgress and the
    // nested completionRate were all undefined (blank donut, histogram and metrics).
    let result: IDashboardOverview | undefined;
    service.getOverview(emptyFilters()).subscribe((o) => (result = o));

    httpMock.expectOne(`${baseUrl}/overview`).flush(overviewWire);

    expect(result?.scoreScaleMax).toBe(100); // ← ratingScaleMax
    expect(result?.ratedCount).toBe(40); // ← scoredEmployeeCount
    expect(result?.completionRate).toBe(72); // ← progress.completionRate (nested)
    expect(result?.histogram.length).toBe(2); // ← scoreDistribution
    expect(result?.histogram[0].label).toBe('0–50');
    expect(result?.departmentAverages[0].departmentName).toBe('Engineering');
    expect(result?.topPerformers[0].employeeName).toBe('Alex Doe');
    // ← progress, with *Completed → *Complete
    expect(result?.cycleProgress.totalParticipants).toBe(50);
    expect(result?.cycleProgress.goalSettingComplete).toBe(50);
    expect(result?.cycleProgress.selfAssessmentComplete).toBe(45);
    expect(result?.cycleProgress.managerReviewComplete).toBe(40);
    expect(result?.cycleProgress.signedOff).toBe(36);
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
    req.flush(overviewWire);
  });

  it('getTrend() GETs the trend endpoint and flattens the aggregate + overlays', () => {
    const filters: IDashboardFilters = {
      ...emptyFilters(),
      cycleIds: ['c1', 'c2', 'c3'],
    };
    const trendWire: TrendWire = {
      scope: 'Organization',
      points: [
        { cycleId: 'c1', cycleName: '2024', averageScore: 70 },
        { cycleId: 'c2', cycleName: '2025', averageScore: 75 },
        { cycleId: 'c3', cycleName: '2026', averageScore: 81 },
      ],
      departmentSeries: [
        {
          departmentId: 'd1',
          departmentName: 'Engineering',
          points: [{ cycleId: 'c3', cycleName: '2026', averageScore: 83 }],
        },
      ],
    };
    let result: ITrendResponse | undefined;
    service.getTrend(filters).subscribe((t) => (result = t));

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/trend`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.getAll('cycleId')).toEqual(['c1', 'c2', 'c3']);
    req.flush(trendWire);

    // aggregate first (keyless), then the department overlay
    expect(result?.series.length).toBe(2);
    expect(result?.series[0].key).toBeNull();
    expect(result?.series[0].points.length).toBe(3);
    expect(result?.series[0].points[0].cycleLabel).toBe('2024'); // ← cycleName
    expect(result?.series[1].key).toBe('d1');
    expect(result?.series[1].label).toBe('Engineering');
    // This fixture omits `ratingScaleMax`, so the absent-field fallback applies. (The wire
    // DOES carry the field — see the G8 arm below; the older claim that it never did was
    // what pinned the mapper to the fallback unconditionally.)
    expect(result?.scoreScaleMax).toBe(DASHBOARD_SCALE_FALLBACK);
  });

  it('getDepartmentDrilldown() targets the department path and maps the employees', () => {
    const drillWire: DepartmentDrilldownWire = {
      departmentId: 'd1',
      departmentName: 'Engineering',
      cycleId: 'c1',
      averageScore: 83,
      headcount: 3,
      employees: [
        {
          employeeId: 'e1',
          employeeName: 'Alex Doe',
          employeeNo: 'E-001',
          jobTitle: 'Engineer',
          score: 88,
          status: 'SignedOff',
        },
      ],
    };
    let result: IDepartmentDrilldown | undefined;
    service.getDepartmentDrilldown('d1', 'c1').subscribe((d) => (result = d));

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/department/d1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('cycleId')).toBe('c1');
    req.flush(drillWire);

    expect(result?.departmentName).toBe('Engineering');
    expect(result?.employees[0].employeeName).toBe('Alex Doe');
    expect(result?.employees[0].jobTitle).toBe('Engineer');
    // NOTE: grade + trend have no wire source — defaulted + reported.
    expect(result?.employees[0].grade).toBeNull();
    expect(result?.employees[0].trend).toBe('Flat');
    // No `ratingScaleMax` on this fixture → the absent-field fallback. The drill-down wire
    // does carry the field; see the G8 arm below.
    expect(result?.scoreScaleMax).toBe(DASHBOARD_SCALE_FALLBACK);
  });

  it('getDepartmentDrilldown() omits cycleId when not provided', () => {
    service.getDepartmentDrilldown('d2').subscribe();
    const req = httpMock.expectOne(`${baseUrl}/department/d2`);
    expect(req.request.params.has('cycleId')).toBeFalse();
    req.flush({
      departmentId: 'd2',
      departmentName: 'Sales',
      averageScore: null,
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

  // ─── G8: fields the wire DOES send that the mapper was hardcoding away ───────
  // Each arm below flushes a payload CARRYING the field. Against the pre-G8 mapper
  // (`availableExportFormats: []`, `scoreScaleMax: DASHBOARD_SCALE_FALLBACK`,
  // `cycleLabel: ''`) every one of them fails: the mapper ignored its input.

  it('getOverview() maps availableExportFormats from the wire (ISSUE-379 was mis-filed as a backend gap)', () => {
    let result: IDashboardOverview | undefined;
    service.getOverview(emptyFilters()).subscribe((o) => (result = o));

    // The server sources this from ExportFormatNormalizer.Supported → lowercase tokens.
    httpMock
      .expectOne(`${baseUrl}/overview`)
      .flush({ ...overviewWire, availableExportFormats: ['csv', 'xlsx', 'pdf'] });

    expect(result?.availableExportFormats).toEqual(['Csv', 'Excel', 'Pdf']);
  });

  it('getOverview() drops an export format the FE does not understand instead of casting it', () => {
    let result: IDashboardOverview | undefined;
    service.getOverview(emptyFilters()).subscribe((o) => (result = o));

    httpMock
      .expectOne(`${baseUrl}/overview`)
      .flush({ ...overviewWire, availableExportFormats: ['csv', 'parquet'] });

    // BUG-311's lesson: narrow, never cast — an unknown token must not render a button
    // whose handler the FE cannot honour.
    expect(result?.availableExportFormats).toEqual(['Csv']);
  });

  it('getOverview() falls back to an empty format list when the wire omits it', () => {
    let result: IDashboardOverview | undefined;
    service.getOverview(emptyFilters()).subscribe((o) => (result = o));

    httpMock.expectOne(`${baseUrl}/overview`).flush(overviewWire);

    expect(result?.availableExportFormats).toEqual([]);
  });

  it('getTrend() maps scoreScaleMax from the wire ratingScaleMax rather than the fallback', () => {
    const trendWire: TrendWire = {
      scope: 'Organization',
      ratingScaleMax: 10,
      points: [{ cycleId: 'c1', cycleName: '2026', averageScore: 8 }],
    };
    let result: ITrendResponse | undefined;
    service.getTrend(emptyFilters()).subscribe((t) => (result = t));

    httpMock.expectOne((r) => r.url === `${baseUrl}/trend`).flush(trendWire);

    expect(result?.scoreScaleMax).toBe(10);
    expect(result?.scoreScaleMax).not.toBe(DASHBOARD_SCALE_FALLBACK);
  });

  it('getDepartmentDrilldown() maps cycleLabel + scoreScaleMax from the wire', () => {
    const drillWire: DepartmentDrilldownWire = {
      departmentId: 'd1',
      departmentName: 'Engineering',
      cycleId: 'c1',
      cycleName: 'FY2026 Annual',
      ratingScaleMax: 10,
      averageScore: 8.3,
      headcount: 3,
      employees: [],
    };
    let result: IDepartmentDrilldown | undefined;
    service.getDepartmentDrilldown('d1', 'c1').subscribe((d) => (result = d));

    httpMock.expectOne((r) => r.url === `${baseUrl}/department/d1`).flush(drillWire);

    expect(result?.cycleLabel).toBe('FY2026 Annual'); // ← cycleName
    expect(result?.scoreScaleMax).toBe(10); // ← ratingScaleMax
  });
});
