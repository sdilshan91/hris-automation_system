import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { LeaveReportsService } from './leave-reports.service';
import { IReportQuery } from '../models/leave-reports.models';
import { environment } from '../../../../environments/environment';

describe('LeaveReportsService (US-LV-012)', () => {
  let service: LeaveReportsService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/leaves`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [LeaveReportsService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LeaveReportsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getReport (FR-3/FR-6)', () => {
    it('GETs the report endpoint with pagination + sort + filter params', () => {
      const query: IReportQuery = {
        from: '2026-01-01',
        to: '2026-12-31',
        departmentId: 'dept-1',
        leaveTypeId: 'lt-1',
        search: 'jane',
        page: 2,
        pageSize: 25,
        sortBy: 'employeeName',
        sortDir: 'desc',
      };
      service.getReport('balance-summary', query).subscribe((res) => {
        expect(res.totalCount).toBe(2);
        expect(res.items.length).toBe(2);
      });
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/reports/balance-summary`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.get('page')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('25');
      expect(req.request.params.get('sortBy')).toBe('employeeName');
      expect(req.request.params.get('sortDir')).toBe('desc');
      expect(req.request.params.get('departmentId')).toBe('dept-1');
      expect(req.request.params.get('search')).toBe('jane');
      req.flush({ items: [{ employeeName: 'A' }, { employeeName: 'B' }], totalCount: 2 });
    });

    it('omits empty filter params', () => {
      service
        .getReport('lop-summary', { page: 1, pageSize: 10 })
        .subscribe();
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/reports/lop-summary`);
      expect(req.request.params.get('departmentId')).toBeNull();
      expect(req.request.params.get('sortBy')).toBeNull();
      req.flush({ items: [], totalCount: 0 });
    });
  });

  describe('getAnalytics (FR-7)', () => {
    it('GETs the analytics endpoint for a chart type with filters', () => {
      service
        .getAnalytics('utilization-by-department', { departmentId: 'd1' })
        .subscribe((a) => expect(a.data?.length).toBe(1));
      const req = httpMock.expectOne(
        (r) => r.url === `${baseUrl}/analytics/utilization-by-department`,
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.get('departmentId')).toBe('d1');
      req.flush({ data: [{ label: 'Eng', value: 40 }] });
    });
  });

  describe('getSummaryMetrics (AC cards)', () => {
    it('GETs the summary endpoint', () => {
      service.getSummaryMetrics().subscribe((m) => expect(m.totalUtilizationPct).toBe(42));
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/reports/summary`);
      expect(req.request.method).toBe('GET');
      req.flush({ totalUtilizationPct: 42, topLeaveType: 'Annual Leave', absenteeismRatePct: 3 });
    });
  });

  describe('export (FR-4/AC-5)', () => {
    it('requests the export endpoint with the chosen format and returns a blob', (done) => {
      service.export('balance-summary', 'csv', { departmentId: 'd1' }).subscribe((res) => {
        expect(res.blob).toBeTruthy();
        expect(res.filename).toContain('.csv');
        done();
      });
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/reports/balance-summary/export`);
      expect(req.request.method).toBe('GET');
      expect(req.request.params.get('format')).toBe('csv');
      expect(req.request.params.get('departmentId')).toBe('d1');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['a,b\n1,2'], { type: 'text/csv' }), {
        headers: { 'Content-Type': 'text/csv', 'Content-Disposition': 'attachment; filename="balance.csv"' },
      });
    });

    it('uses the filename from Content-Disposition when present', (done) => {
      service.export('utilization', 'xlsx', {}).subscribe((res) => {
        expect(res.filename).toBe('util-report.xlsx');
        done();
      });
      const req = httpMock.expectOne((r) => r.url === `${baseUrl}/reports/utilization/export`);
      req.flush(new Blob(['x']), {
        headers: { 'Content-Type': 'application/octet-stream', 'Content-Disposition': 'attachment; filename="util-report.xlsx"' },
      });
    });
  });

  describe('readJobEnvelope (AC-5 large export)', () => {
    it('parses a JSON background-job envelope', async () => {
      const blob = new Blob([JSON.stringify({ status: 'processing', jobId: 'job-9' })], {
        type: 'application/json',
      });
      const job = await LeaveReportsService.readJobEnvelope(blob, 'application/json');
      expect(job?.status).toBe('processing');
      expect(job?.jobId).toBe('job-9');
    });

    it('returns null when the blob is a real file (non-JSON content-type)', async () => {
      const blob = new Blob(['a,b'], { type: 'text/csv' });
      const job = await LeaveReportsService.readJobEnvelope(blob, 'text/csv');
      expect(job).toBeNull();
    });
  });

  describe('parseError helpers', () => {
    it('extracts a typed error body', () => {
      const err = new HttpErrorResponse({ error: { message: 'boom', code: 'x' } });
      expect(LeaveReportsService.parseError(err)?.message).toBe('boom');
      expect(LeaveReportsService.parseErrorMessage(err)).toBe('boom');
    });

    it('falls back to a default message', () => {
      const err = new HttpErrorResponse({ error: 'not-json' });
      expect(LeaveReportsService.parseError(err)).toBeNull();
      expect(LeaveReportsService.parseErrorMessage(err)).toBe('Failed to load the report.');
    });
  });

  describe('filenameFromDisposition', () => {
    it('returns the fallback when no header', () => {
      expect(LeaveReportsService.filenameFromDisposition(null, 'f.csv')).toBe('f.csv');
    });
    it('parses a quoted filename', () => {
      expect(
        LeaveReportsService.filenameFromDisposition('attachment; filename="x.xlsx"', 'f.csv'),
      ).toBe('x.xlsx');
    });
  });
});
