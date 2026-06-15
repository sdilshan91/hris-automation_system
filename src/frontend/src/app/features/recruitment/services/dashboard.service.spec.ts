import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
  HttpHeaders,
  HttpResponse,
} from '@angular/common/http';

import { RecruitmentDashboardService } from './dashboard.service';
import { environment } from '../../../../environments/environment';
import { IRecruitmentDashboard, IDashboardQuery } from '../models/dashboard.models';

describe('RecruitmentDashboardService (US-REC-009)', () => {
  let service: RecruitmentDashboardService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/recruitment/dashboard`;

  const query: IDashboardQuery = { from: '2026-05-16', to: '2026-06-15' };

  const dto = (over: Partial<IRecruitmentDashboard> = {}): IRecruitmentDashboard => ({
    range: { from: '2026-05-16', to: '2026-06-15' },
    kpis: {
      openVacancies: 4,
      totalApplicants: 100,
      hires: 5,
      avgTimeToHireDays: 18,
      offerAcceptanceRate: 70,
      offersPending: 2,
    },
    funnel: [
      { stage: 'Applied', count: 100 },
      { stage: 'Screening', count: 60 },
      { stage: 'Interview', count: 30 },
    ],
    sources: [{ source: 'Referral', applicants: 20, hires: 5 }],
    timeToHireTrend: [{ label: 'May', avgDays: 20 }],
    vacancyStatus: [{ status: 'Open', count: 4 }],
    recentActivity: [
      { id: 'e1', type: 'Hired', description: 'Ada was hired', occurredAt: '2026-06-15T10:00:00Z' },
    ],
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        RecruitmentDashboardService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(RecruitmentDashboardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getDashboard', () => {
    it('GETs the dashboard with the range as query params', () => {
      service.getDashboard(query).subscribe();
      const req = httpMock.expectOne(
        (r) =>
          r.url === base &&
          r.params.get('from') === '2026-05-16' &&
          r.params.get('to') === '2026-06-15',
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(dto());
    });

    it('appends department/vacancy drill-down params when set (FR-7)', () => {
      service
        .getDashboard({ ...query, departmentId: 'd1', vacancyId: 'v9' })
        .subscribe();
      const req = httpMock.expectOne(
        (r) =>
          r.url === base &&
          r.params.get('departmentId') === 'd1' &&
          r.params.get('vacancyId') === 'v9',
      );
      expect(req.request.method).toBe('GET');
      req.flush(dto());
    });

    it('derives funnel conversion when the backend omits it (BR-3)', () => {
      let result: IRecruitmentDashboard | undefined;
      service.getDashboard(query).subscribe((d) => (result = d));
      httpMock.expectOne((r) => r.url === base).flush(dto());
      expect(result!.funnel[0].conversionRate).toBeNull();
      expect(result!.funnel[1].conversionRate).toBe(60);
      expect(result!.funnel[2].conversionRate).toBe(50);
    });

    it('derives source hire-conversion when absent', () => {
      let result: IRecruitmentDashboard | undefined;
      service.getDashboard(query).subscribe((d) => (result = d));
      httpMock.expectOne((r) => r.url === base).flush(dto());
      expect(result!.sources[0].conversionRate).toBe(25); // 5/20
    });

    it('tolerates a { data } envelope', () => {
      let result: IRecruitmentDashboard | undefined;
      service.getDashboard(query).subscribe((d) => (result = d));
      httpMock.expectOne((r) => r.url === base).flush({ data: dto() });
      expect(result!.kpis.totalApplicants).toBe(100);
    });

    it('defaults arrays when the DTO is sparse', () => {
      let result: IRecruitmentDashboard | undefined;
      service.getDashboard(query).subscribe((d) => (result = d));
      httpMock.expectOne((r) => r.url === base).flush({
        kpis: dto().kpis,
      });
      expect(result!.funnel).toEqual([]);
      expect(result!.sources).toEqual([]);
      expect(result!.recentActivity).toEqual([]);
      expect(result!.range).toEqual({ from: '2026-05-16', to: '2026-06-15' });
    });
  });

  describe('getFilterOptions', () => {
    it('returns departments + vacancies, defaulting to []', () => {
      let result: { departments: unknown[]; vacancies: unknown[] } | undefined;
      service.getFilterOptions().subscribe((r) => (result = r));
      httpMock.expectOne(`${base}/filters`).flush({
        departments: [{ id: 'd1', label: 'Engineering' }],
      });
      expect(result!.departments.length).toBe(1);
      expect(result!.vacancies).toEqual([]);
    });
  });

  describe('exportDashboard', () => {
    it('requests a blob with format + range and observes the response (FR-8)', () => {
      let resp: HttpResponse<Blob> | undefined;
      service.exportDashboard(query, 'csv').subscribe((r) => (resp = r));
      const req = httpMock.expectOne(
        (r) => r.url === `${base}/export` && r.params.get('format') === 'csv',
      );
      expect(req.request.responseType).toBe('blob');
      expect(req.request.withCredentials).toBeTrue();
      const blob = new Blob(['a,b,c'], { type: 'text/csv' });
      req.flush(blob, {
        headers: new HttpHeaders({
          'content-disposition': 'attachment; filename="recruitment.csv"',
        }),
      });
      expect(resp!.body).toBe(blob);
    });
  });

  describe('static helpers', () => {
    it('filenameFromResponse parses Content-Disposition', () => {
      const res = new HttpResponse<Blob>({
        headers: new HttpHeaders({
          'content-disposition': 'attachment; filename="report.xlsx"',
        }),
      });
      expect(RecruitmentDashboardService.filenameFromResponse(res, 'fallback')).toBe('report.xlsx');
    });

    it('filenameFromResponse falls back when absent', () => {
      const res = new HttpResponse<Blob>({ headers: new HttpHeaders() });
      expect(RecruitmentDashboardService.filenameFromResponse(res, 'fb.csv')).toBe('fb.csv');
    });

    it('parseErrorMessage reads message verbatim', () => {
      const err = new HttpErrorResponse({ error: { message: 'Nope' }, status: 400 });
      expect(RecruitmentDashboardService.parseErrorMessage(err)).toBe('Nope');
    });

    it('parseErrorMessage falls back on a non-object body', () => {
      const err = new HttpErrorResponse({ error: 'boom', status: 500 });
      expect(RecruitmentDashboardService.parseErrorMessage(err)).toBe('An unexpected error occurred.');
    });
  });
});
