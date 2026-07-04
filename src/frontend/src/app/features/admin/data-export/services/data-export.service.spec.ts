import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { DataExportService } from './data-export.service';
import {
  IExportRecord,
  IInitiateExportResponse,
} from '../models/data-export.models';
import { environment } from '../../../../../environments/environment';

describe('DataExportService', () => {
  let service: DataExportService;
  let httpMock: HttpTestingController;

  // BUG-104 (US-ADM-010): the backend routes are `/tenant/data-exports` and
  // `/system/tenants/{id}/data-exports`. The service previously omitted the
  // `data-` segment (`/tenant/exports`), so history GETs 404'd ("No exports
  // yet.") and Start-Export POSTed to a dead route. These bases assert the
  // CORRECT backend path — the wrong path is guarded with `expectNone` below.
  const tenantBase = `${environment.apiBaseUrl}/tenant/data-exports`;
  const systemBase = `${environment.apiBaseUrl}/system/tenants/t-9/data-exports`;

  // The exact buggy URLs that must NEVER be hit post-fix. Kept separate so a
  // naive contains-check can't sneak past: `/tenant/exports` is NOT a substring
  // of `/tenant/data-exports`, so these are genuinely distinct exact URLs.
  const wrongTenantBase = `${environment.apiBaseUrl}/tenant/exports`;
  const wrongSystemBase = `${environment.apiBaseUrl}/system/tenants/t-9/exports`;

  const initiateRes: IInitiateExportResponse = {
    exportId: 'exp-1',
    status: 'Queued',
  };

  const record: IExportRecord = {
    id: 'exp-1',
    scope: 'Full export',
    status: 'Completed',
    requestedAt: '2026-06-17T10:00:00Z',
    completedAt: '2026-06-17T10:05:00Z',
    fileSizeBytes: 2048,
    expiresAt: '2026-06-20T10:05:00Z',
    downloadAvailable: true,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DataExportService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  // ─── Tenant-Admin base (no tenantId) ────────────────────────

  it('initiates a full export against the tenant base with the right body', () => {
    service
      .initiate({ scope: 'full', formatOptions: { delimiter: ',', dateFormat: 'ISO' } })
      .subscribe((res) => expect(res).toEqual(initiateRes));

    const req = httpMock.expectOne(tenantBase);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.scope).toBe('full');
    expect(req.request.body.formatOptions.delimiter).toBe(',');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(initiateRes);
  });

  it('initiates a partial export with the selected entity codes', () => {
    service.initiate({ scope: ['employees', 'leave_requests'] }).subscribe();

    const req = httpMock.expectOne(tenantBase);
    expect(req.request.body.scope).toEqual(['employees', 'leave_requests']);
    req.flush(initiateRes);
  });

  it('gets the tenant export history', () => {
    service.getHistory().subscribe((res) => expect(res).toEqual([record]));

    const req = httpMock.expectOne(tenantBase);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([record]);
  });

  it('gets a single export status', () => {
    service.getStatus('exp-1').subscribe((res) => expect(res).toEqual(record));

    const req = httpMock.expectOne(`${tenantBase}/exp-1`);
    expect(req.request.method).toBe('GET');
    req.flush(record);
  });

  it('downloads an export as a blob with the response observed', () => {
    const blob = new Blob(['zip'], { type: 'application/zip' });
    service.download('exp-1').subscribe((resp) => expect(resp.body).toBe(blob));

    const req = httpMock.expectOne(`${tenantBase}/exp-1/download`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(blob);
  });

  // ─── System-Admin base (tenantId present) ───────────────────

  it('routes initiate to the system base when a tenantId is given (AC-6)', () => {
    service.initiate({ scope: 'full' }, 't-9').subscribe();

    const req = httpMock.expectOne(systemBase);
    expect(req.request.method).toBe('POST');
    req.flush(initiateRes);
  });

  it('routes history to the system base for a tenantId', () => {
    service.getHistory('t-9').subscribe();
    const req = httpMock.expectOne(systemBase);
    expect(req.request.method).toBe('GET');
    req.flush([record]);
  });

  it('routes status to the system base for a tenantId', () => {
    service.getStatus('exp-1', 't-9').subscribe();
    const req = httpMock.expectOne(`${systemBase}/exp-1`);
    expect(req.request.method).toBe('GET');
    req.flush(record);
  });

  it('routes download to the system base for a tenantId', () => {
    service.download('exp-1', 't-9').subscribe();
    const req = httpMock.expectOne(`${systemBase}/exp-1/download`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['zip']));
  });

  // ─── BUG-104 regression guard (TC-ADM-010-21) ───────────────
  // The base path MUST include the `data-` segment to match the backend
  // routes. Pre-fix the service requests `/tenant/exports` (and
  // `/system/tenants/{id}/exports`), so each `expectOne('.../data-exports')`
  // below finds no match and the test goes RED; the paired `expectNone` on the
  // bare `/tenant/exports` proves the dead route is never hit post-fix.

  describe('BUG-104 – data-export URL must include the "data-" segment', () => {
    it('getExportHistory should GET /tenant/data-exports, never /tenant/exports (BUG-104)', () => {
      service.getHistory().subscribe();

      const req = httpMock.expectOne(tenantBase);
      expect(req.request.method).toBe('GET');
      expect(req.request.url).toBe(tenantBase);
      // Pre-fix the request went to the bare (dead) route — assert it is gone.
      httpMock.expectNone(wrongTenantBase);
      req.flush([record]);
    });

    it('startExport (initiate) should POST /tenant/data-exports, never /tenant/exports (BUG-104)', () => {
      service.initiate({ scope: 'full' }).subscribe();

      const req = httpMock.expectOne(tenantBase);
      expect(req.request.method).toBe('POST');
      expect(req.request.url).toBe(tenantBase);
      httpMock.expectNone(wrongTenantBase);
      req.flush(initiateRes);
    });

    it('getHistory(tenantId) should GET /system/tenants/{id}/data-exports, never .../exports (BUG-104, AC-6)', () => {
      service.getHistory('t-9').subscribe();

      const req = httpMock.expectOne(systemBase);
      expect(req.request.method).toBe('GET');
      expect(req.request.url).toBe(systemBase);
      httpMock.expectNone(wrongSystemBase);
      req.flush([record]);
    });

    it('initiate(tenantId) should POST /system/tenants/{id}/data-exports, never .../exports (BUG-104, AC-6)', () => {
      service.initiate({ scope: 'full' }, 't-9').subscribe();

      const req = httpMock.expectOne(systemBase);
      expect(req.request.method).toBe('POST');
      expect(req.request.url).toBe(systemBase);
      httpMock.expectNone(wrongSystemBase);
      req.flush(initiateRes);
    });
  });
});
