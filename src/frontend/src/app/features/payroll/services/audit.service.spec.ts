import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { AuditService } from './audit.service';
import { environment } from '../../../../environments/environment';
import {
  IAuditEntry,
  IAuditTrailFilters,
  IPage,
  IPayrollHistoryRun,
} from '../models/audit.models';

describe('AuditService', () => {
  let service: AuditService;
  let httpMock: HttpTestingController;
  const payrollUrl = `${environment.apiBaseUrl}/payroll`;
  const historyUrl = `${payrollUrl}/runs/history`;
  const auditUrl = `${payrollUrl}/audit-trail`;

  const historyRow: IPayrollHistoryRun = {
    runId: 'r-1',
    payMonth: 5,
    payYear: 2026,
    period: '2026-05',
    status: 'Finalized',
    employeeCount: 250,
    totalNet: 800000,
    totalGross: 1000000,
    totalDeductions: 200000,
    initiatedBy: 'u-1',
    initiatedAt: '2026-05-25T09:00:00Z',
    approvedBy: 'u-2',
    approvedAt: '2026-05-30T09:00:00Z',
    finalizedAt: '2026-05-31T12:00:00Z',
  };

  const historyPage: IPage<IPayrollHistoryRun> = {
    items: [historyRow],
    totalCount: 1,
    page: 1,
    pageSize: 25,
  };

  const entry: IAuditEntry = {
    id: 'a-1',
    tenantId: 't-1',
    timestamp: '2026-05-31T10:00:00Z',
    actorUserId: 'u-2',
    actorEmployeeNo: 'EMP002',
    action: 'PayrollRun.Finalized',
    resourceType: 'PayrollRun',
    resourceId: 'r-1',
    before: '{"status":"Approved"}',
    after: '{"status":"Finalized"}',
    ipAddress: '10.0.0.1',
    userAgent: 'jasmine',
    traceId: 't-1',
  };

  const auditPage: IPage<IAuditEntry> = {
    items: [entry],
    totalCount: 1,
    page: 1,
    pageSize: 50,
  };

  const allFilters: IAuditTrailFilters = {
    fromUtc: '2026-05-01',
    toUtc: '2026-05-31',
    action: 'PayrollRun.Finalized',
    actorUserId: 'u-2',
    resourceType: 'PayrollRun',
    resourceId: 'r-1',
  };

  const noFilters: IAuditTrailFilters = {
    fromUtc: null,
    toUtc: null,
    action: null,
    actorUserId: null,
    resourceType: null,
    resourceId: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuditService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(AuditService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  describe('getHistory', () => {
    it('GETs /payroll/runs/history with no params by default and returns the page', () => {
      let result: IPage<IPayrollHistoryRun> | undefined;
      service.getHistory().subscribe((r) => (result = r));

      const req = httpMock.expectOne((r) => r.url === historyUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.has('year')).toBeFalse();
      expect(req.request.params.has('status')).toBeFalse();
      req.flush(historyPage);

      expect(result).toEqual(historyPage);
      expect(result?.items).toEqual([historyRow]);
    });

    it('passes year and status params when provided', () => {
      service.getHistory(2026, 'Finalized').subscribe();
      const req = httpMock.expectOne((r) => r.url === historyUrl);
      expect(req.request.params.get('year')).toBe('2026');
      expect(req.request.params.get('status')).toBe('Finalized');
      req.flush(historyPage);
    });

    it('tolerates a { data }-wrapped page and an empty/null body', () => {
      let result: IPage<IPayrollHistoryRun> | undefined;
      service.getHistory().subscribe((r) => (result = r));
      httpMock.expectOne(historyUrl).flush({ data: historyPage });
      expect(result?.items).toEqual([historyRow]);

      let result2: IPage<IPayrollHistoryRun> | undefined;
      service.getHistory().subscribe((r) => (result2 = r));
      httpMock.expectOne(historyUrl).flush(null);
      expect(result2?.items).toEqual([]);
    });
  });

  describe('getAuditTrail', () => {
    it('GETs /payroll/audit-trail with all non-empty filter params', () => {
      let result: IPage<IAuditEntry> | undefined;
      service.getAuditTrail(allFilters).subscribe((r) => (result = r));

      const req = httpMock.expectOne((r) => r.url === auditUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.params.get('fromUtc')).toBe('2026-05-01');
      expect(req.request.params.get('toUtc')).toBe('2026-05-31');
      expect(req.request.params.get('action')).toBe('PayrollRun.Finalized');
      expect(req.request.params.get('actorUserId')).toBe('u-2');
      expect(req.request.params.get('resourceType')).toBe('PayrollRun');
      expect(req.request.params.get('resourceId')).toBe('r-1');
      req.flush(auditPage);

      expect(result?.items).toEqual([entry]);
    });

    it('omits every empty filter param', () => {
      service.getAuditTrail(noFilters).subscribe();
      const req = httpMock.expectOne((r) => r.url === auditUrl);
      expect(req.request.params.has('fromUtc')).toBeFalse();
      expect(req.request.params.has('toUtc')).toBeFalse();
      expect(req.request.params.has('action')).toBeFalse();
      expect(req.request.params.has('actorUserId')).toBeFalse();
      expect(req.request.params.has('resourceType')).toBeFalse();
      expect(req.request.params.has('resourceId')).toBeFalse();
      req.flush(auditPage);
    });

    it('tolerates a { data }-wrapped page', () => {
      let result: IPage<IAuditEntry> | undefined;
      service.getAuditTrail(noFilters).subscribe((r) => (result = r));
      httpMock.expectOne(auditUrl).flush({ data: auditPage });
      expect(result?.items).toEqual([entry]);
    });
  });

  describe('getRunAuditTrail', () => {
    it('GETs /payroll/runs/:id/audit-timeline and returns the bare array', () => {
      let result: IAuditEntry[] | undefined;
      service.getRunAuditTrail('r-1').subscribe((r) => (result = r));

      const req = httpMock.expectOne(`${payrollUrl}/runs/r-1/audit-timeline`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([entry]);

      expect(result).toEqual([entry]);
    });
  });

  describe('exportAuditTrail', () => {
    it('requests a blob with format + filter params and reads the response', () => {
      let resp: { body: Blob | null } | undefined;
      service
        .exportAuditTrail(allFilters, 'xlsx')
        .subscribe((r) => (resp = r));

      const req = httpMock.expectOne((r) => r.url === `${auditUrl}/export`);
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      expect(req.request.params.get('format')).toBe('xlsx');
      expect(req.request.params.get('action')).toBe('PayrollRun.Finalized');
      expect(req.request.params.get('fromUtc')).toBe('2026-05-01');

      const blob = new Blob(['x'], { type: 'application/octet-stream' });
      req.flush(blob, {
        headers: { 'Content-Disposition': 'attachment; filename="audit.xlsx"' },
      });

      expect(resp?.body).toBe(blob);
    });

    it('passes the csv format value when csv is requested', () => {
      service.exportAuditTrail(noFilters, 'csv').subscribe();
      const req = httpMock.expectOne((r) => r.url === `${auditUrl}/export`);
      expect(req.request.params.get('format')).toBe('csv');
      req.flush(new Blob(['a,b']));
    });
  });
});
