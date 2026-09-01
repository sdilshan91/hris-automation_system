import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AuditLogService } from './audit-log.service';
import {
  IAuditLogDetail,
  IAuditLogPage,
  AuditLogPageWire,
  AuditLogDetailWire,
} from '../models/audit-log.models';
import { environment } from '../../../../../environments/environment';

describe('AuditLogService', () => {
  let service: AuditLogService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/audit-logs`;
  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  /** D1 — the WIRE shape (`AuditLogAuditLogPageDto`), not the view model. */
  const mockPageWire: AuditLogPageWire = {
    items: [
      {
        id: 'a-1',
        timestamp: '2026-06-16T10:00:00Z',
        actorUserId: 'u-7',
        actorName: 'Jane Doe',
        actorEmail: 'jane@acme.com',
        action: 'Employee.Create',
        resourceType: 'Employee',
        resourceId: 'e-1',
        ipAddress: '10.0.0.1',
        summary: 'Created employee John',
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50,
    retentionDays: 365,
  };

  const expectedPage: IAuditLogPage = {
    items: [
      {
        id: 'a-1',
        timestamp: '2026-06-16T10:00:00Z',
        actorUserId: 'u-7',
        actorName: 'Jane Doe',
        actorEmail: 'jane@acme.com',
        action: 'Employee.Create',
        resourceType: 'Employee',
        resourceId: 'e-1',
        ipAddress: '10.0.0.1',
        summary: 'Created employee John',
      },
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50,
    retentionDays: 365,
  };

  /**
   * `AuditLogAuditLogDetailDto`. Note it has NO `summary` — only the list DTO
   * carries one. The old fixture spread the list row in, which made the detail
   * endpoint look like it returned a summary it has never sent.
   */
  const mockDetailWire: AuditLogDetailWire = {
    id: 'a-1',
    timestamp: '2026-06-16T10:00:00Z',
    actorUserId: 'u-7',
    actorName: 'Jane Doe',
    actorEmail: 'jane@acme.com',
    actorEmployeeNo: 'EMP-007',
    action: 'Employee.Create',
    resourceType: 'Employee',
    resourceId: 'e-1',
    ipAddress: '10.0.0.1',
    userAgent: 'Mozilla/5.0',
    traceId: 'trace-123',
    before: null,
    after: '{"name":"John"}',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuditLogService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists with pagination params', () => {
    service
      .getAuditLog({ page: 2, pageSize: 50 })
      .subscribe((res) => expect(res).toEqual(expectedPage));

    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.method === 'GET'
    );
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('50');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockPageWire);
  });

  it('passes every filter as a query param', () => {
    service
      .getAuditLog({
        page: 1,
        pageSize: 50,
        startDate: '2026-06-01',
        endDate: '2026-06-30',
        actorUserId: 'u-1',
        action: 'Leave.Approve',
        resourceType: 'LeaveRequest',
        search: 'john',
      })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    const p = req.request.params;
    expect(p.get('startDate')).toBe('2026-06-01');
    expect(p.get('endDate')).toBe('2026-06-30');
    expect(p.get('actorUserId')).toBe('u-1');
    expect(p.get('action')).toBe('Leave.Approve');
    expect(p.get('resourceType')).toBe('LeaveRequest');
    expect(p.get('search')).toBe('john');
    req.flush(mockPageWire);
  });

  it('omits empty filters from the query', () => {
    service
      .getAuditLog({ page: 1, pageSize: 50, action: '', search: undefined })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.has('action')).toBeFalse();
    expect(req.request.params.has('search')).toBeFalse();
    req.flush(mockPageWire);
  });

  it('fetches a single record detail', () => {
    let detail: IAuditLogDetail | undefined;
    service.getAuditDetail('a-1').subscribe((res) => (detail = res));

    const req = httpMock.expectOne(`${baseUrl}/a-1`);
    expect(req.request.method).toBe('GET');
    req.flush(mockDetailWire);

    expect(detail?.traceId).toBe('trace-123');
    expect(detail?.actorEmployeeNo).toBe('EMP-007');
    // NO WIRE SOURCE: the detail DTO carries no summary, so the mapper must not
    // manufacture one. A caller that needs it merges in the list row.
    expect(detail?.summary).toBeUndefined();
  });

  it('substitutes empty strings for the nullable actor fields', () => {
    let detail: IAuditLogDetail | undefined;
    service.getAuditDetail('a-2').subscribe((res) => (detail = res));

    // A system-generated event has no actor at all. `actorName` was declared a
    // non-null `string`, and the list template calls `initials(actorName)`.
    httpMock
      .expectOne(`${baseUrl}/a-2`)
      .flush({ id: 'a-2', actorName: null, actorEmail: null, resourceType: null });

    expect(detail?.actorName).toBe('');
    expect(detail?.actorEmail).toBe('');
    expect(detail?.resourceType).toBe('');
    expect(detail?.actorUserId).toBeNull();
  });

  it('exports with the chosen format and current filters as a blob', () => {
    const blob = new Blob(['data'], { type: 'text/csv' });
    service
      .exportAuditLog({ action: 'Employee.Create', search: 'john' }, 'csv')
      .subscribe((resp) => expect(resp.body).toBe(blob));

    const req = httpMock.expectOne(
      (r) => r.url === `${baseUrl}/export` && r.method === 'GET'
    );
    expect(req.request.params.get('format')).toBe('csv');
    expect(req.request.params.get('action')).toBe('Employee.Create');
    expect(req.request.params.get('search')).toBe('john');
    expect(req.request.responseType).toBe('blob');
    req.flush(blob);
  });

  it('exports JSON format', () => {
    service.exportAuditLog({}, 'json').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/export`);
    expect(req.request.params.get('format')).toBe('json');
    req.flush(new Blob(['[]']));
  });

  // ─── US-NTF-005 deltas ─────────────────────────────────

  it('serializes multi-select actions/resourceTypes as repeated params (FR-2)', () => {
    service
      .getAuditLog({
        page: 1,
        pageSize: 50,
        actions: ['Employee.Create', 'Leave.Approve'],
        resourceTypes: ['Employee', 'LeaveRequest'],
      })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.getAll('actions')).toEqual([
      'Employee.Create',
      'Leave.Approve',
    ]);
    expect(req.request.params.getAll('resourceTypes')).toEqual([
      'Employee',
      'LeaveRequest',
    ]);
    req.flush(mockPageWire);
  });

  it('omits empty multi-select arrays from the query (FR-2)', () => {
    service
      .getAuditLog({ page: 1, pageSize: 50, actions: [], resourceTypes: [] })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.has('actions')).toBeFalse();
    expect(req.request.params.has('resourceTypes')).toBeFalse();
    req.flush(mockPageWire);
  });

  it('searches actors via the dedicated endpoint and maps the shape (FR-2)', () => {
    service.searchActors('jan').subscribe((opts) => {
      expect(opts).toEqual([
        { id: 'u-7', displayName: 'Jane Doe', email: 'jane@acme.com' },
      ]);
    });

    const req = httpMock.expectOne(
      (r) => r.url === `${baseUrl}/actors` && r.method === 'GET'
    );
    expect(req.request.params.get('search')).toBe('jan');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([{ userId: 'u-7', name: 'Jane Doe', email: 'jane@acme.com' }]);
  });

  it('defaults a nameless actor rather than rendering undefined (FR-2)', () => {
    let opts: { id: string; displayName: string; email: string }[] | undefined;
    service.searchActors('x').subscribe((o) => (opts = o));

    httpMock.expectOne((r) => r.url === `${baseUrl}/actors`).flush([{ userId: 'u-8' }]);

    // RENAMES asserted: id <- userId, displayName <- name.
    expect(opts).toEqual([{ id: 'u-8', displayName: '', email: '' }]);
  });

  it('omits a blank actor search term', () => {
    service.searchActors('   ').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/actors`);
    expect(req.request.params.has('search')).toBeFalse();
    req.flush([]);
  });

  it('fetches multi-select filter options (FR-2)', () => {
    const opts = {
      actions: ['Employee.Create', 'Leave.Approve'],
      resourceTypes: ['Employee', 'LeaveRequest'],
    };
    service.getFilterOptions().subscribe((res) => expect(res).toEqual(opts));

    const req = httpMock.expectOne(
      (r) => r.url === `${baseUrl}/filter-options` && r.method === 'GET'
    );
    expect(req.request.withCredentials).toBeTrue();
    req.flush(opts);
  });

  it('derives actor options from the reused US-ADM-005 user list', () => {
    service.getActorOptions().subscribe((opts) => {
      expect(opts).toEqual([
        { id: 'ut-1', displayName: 'Jane Doe', email: 'jane@acme.com' },
      ]);
    });

    const req = httpMock.expectOne((r) => r.url === usersUrl);
    expect(req.request.params.get('pageSize')).toBe('200');
    expect(req.request.params.get('status')).toBe('active');
    req.flush({
      items: [
        { userTenantId: 'ut-1', displayName: 'Jane Doe', email: 'jane@acme.com' },
      ],
    });
  });
});
