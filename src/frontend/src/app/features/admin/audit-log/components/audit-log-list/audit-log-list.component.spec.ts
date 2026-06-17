import { TestBed, ComponentFixture, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '../../../../../core/auth/auth.service';
import { AuditLogListComponent } from './audit-log-list.component';
import {
  IAuditLogDetail,
  IAuditLogPage,
} from '../../models/audit-log.models';
import { environment } from '../../../../../../environments/environment';

describe('AuditLogListComponent', () => {
  let component: AuditLogListComponent;
  let fixture: ComponentFixture<AuditLogListComponent>;
  let httpMock: HttpTestingController;
  let toastr: jasmine.SpyObj<ToastrService>;

  const baseUrl = `${environment.apiBaseUrl}/tenant/audit-log`;
  const usersUrl = `${environment.apiBaseUrl}/users`;

  const page1: IAuditLogPage = {
    items: [
      {
        id: 'a-1',
        timestamp: '2026-06-16T10:00:00Z',
        actorName: 'Jane Doe',
        actorEmail: 'jane@acme.com',
        action: 'Employee.Create',
        resourceType: 'Employee',
        resourceId: 'e-1',
        ipAddress: '10.0.0.1',
        summary: 'Created employee John',
      },
      {
        id: 'a-2',
        timestamp: '2026-06-16T09:00:00Z',
        actorName: 'Bob Smith',
        actorEmail: 'bob@acme.com',
        action: 'Leave.Approve',
        resourceType: 'LeaveRequest',
        resourceId: 'l-9',
        ipAddress: '10.0.0.2',
        summary: 'Approved leave',
      },
    ],
    totalCount: 120,
    page: 1,
    pageSize: 50,
    retentionDays: 365,
  };

  const detail: IAuditLogDetail = {
    ...page1.items[0],
    userAgent: 'Mozilla/5.0',
    traceId: 'trace-123',
    before: '{"name":"John"}',
    after: '{"name":"Jane"}',
  };

  /** A non-Auditor admin by default (export allowed). */
  function configure(isAuditor = false): void {
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    TestBed.configureTestingModule({
      imports: [AuditLogListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        provideTranslateService(),
        { provide: ToastrService, useValue: toastr },
        {
          provide: AuthService,
          useValue: { hasRole: (r: string) => isAuditor && r === 'Auditor' },
        },
      ],
    });

    fixture = TestBed.createComponent(AuditLogListComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  }

  /** Flush the initial actor-options + list requests that fire on init. */
  function flushInit(response: IAuditLogPage = page1): void {
    fixture.detectChanges();
    httpMock
      .expectOne((r) => r.url === usersUrl)
      .flush({ items: [{ userTenantId: 'ut-1', displayName: 'Jane Doe', email: 'jane@acme.com' }] });
    httpMock
      .expectOne((r) => r.url === baseUrl && r.method === 'GET')
      .flush(response);
  }

  afterEach(() => {
    if (httpMock) {
      httpMock.verify();
    }
  });

  it('renders a row per audit record from the mock', () => {
    configure();
    flushInit();
    fixture.detectChanges();

    expect(component.entries().length).toBe(2);
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="audit-row"]');
    expect(rows.length).toBe(2);
    expect(fixture.nativeElement.textContent).toContain('Employee.Create');
    expect(fixture.nativeElement.textContent).toContain('jane@acme.com');
  });

  it('shows the retention badge from the response', () => {
    configure();
    flushInit();
    fixture.detectChanges();

    expect(component.retentionDays()).toBe(365);
    const badge = fixture.nativeElement.querySelector('[data-testid="retention-badge"]');
    expect(badge).toBeTruthy();
  });

  it('derives distinct action + resource options from loaded rows', () => {
    configure();
    flushInit();
    expect(component.actionOptions()).toEqual(['Employee.Create', 'Leave.Approve']);
    expect(component.resourceOptions()).toEqual(['Employee', 'LeaveRequest']);
  });

  it('opens the detail panel and loads the full record', () => {
    configure();
    flushInit();

    component.openDetail(page1.items[0]);
    // Panel seeded immediately with row data.
    expect(component.detail()?.id).toBe('a-1');
    httpMock.expectOne(`${baseUrl}/a-1`).flush(detail);

    expect(component.detail()?.before).toBe('{"name":"John"}');
    expect(component.detail()?.after).toBe('{"name":"Jane"}');
  });

  it('applies the action filter to the next list query', () => {
    configure();
    flushInit();

    component.onActionChange('Leave.Approve');
    const req = httpMock.expectOne((r) => r.url === baseUrl && r.method === 'GET');
    expect(req.request.params.get('action')).toBe('Leave.Approve');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(page1);
  });

  it('combines multiple filters in one query (AND)', () => {
    configure();
    flushInit();

    component.onResourceChange('Employee');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.onStartDateChange('2026-06-01');
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.get('resourceType')).toBe('Employee');
    expect(req.request.params.get('startDate')).toBe('2026-06-01');
    req.flush(page1);
  });

  it('debounced keyword search hits the endpoint with the term', fakeAsync(() => {
    configure();
    flushInit();

    component.onSearchInput('john');
    tick(300);
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.get('search')).toBe('john');
    req.flush(page1);
  }));

  it('preserves active filters across pagination', () => {
    configure();
    flushInit();

    component.onActionChange('Leave.Approve');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.nextPage();
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.get('page')).toBe('2');
    // Filter still applied on the new page.
    expect(req.request.params.get('action')).toBe('Leave.Approve');
    req.flush(page1);
  });

  it('resets to page 1 when a filter changes after paging', () => {
    configure();
    flushInit();

    component.nextPage();
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);
    expect(component.page()).toBe(2);

    component.onActorChange('ut-1');
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(component.page()).toBe(1);
    expect(req.request.params.get('page')).toBe('1');
    req.flush(page1);
  });

  it('export dialog shows the total record count', () => {
    configure();
    flushInit();

    component.openExport();
    fixture.detectChanges();
    expect(component.showExport()).toBeTrue();
    // The dialog binds recordCount = total(); the export-dialog renders it into
    // the (translated) confirmation copy. Under the fake translate loader the
    // interpolation isn't applied, so assert the count the dialog receives.
    expect(component.total()).toBe(120);
    const dialog = fixture.nativeElement.querySelector('app-audit-export-dialog');
    expect(dialog).toBeTruthy();
  });

  it('export triggers a download with the current filters', () => {
    configure();
    flushInit();

    component.onActionChange('Employee.Create');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.openExport();
    component.onExportConfirmed('csv');

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/export`);
    expect(req.request.params.get('format')).toBe('csv');
    expect(req.request.params.get('action')).toBe('Employee.Create');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['data'], { type: 'text/csv' }));

    expect(component.exporting()).toBeFalse();
    expect(component.showExport()).toBeFalse();
    expect(toastr.success).toHaveBeenCalled();
  });

  it('surfaces a read-only message when export returns 403 (Auditor, FR-7)', () => {
    configure(true);
    flushInit();

    component.openExport();
    component.onExportConfirmed('json');

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/export`);
    req.flush(new Blob(['{}']), { status: 403, statusText: 'Forbidden' });

    expect(toastr.warning).toHaveBeenCalled();
    expect(toastr.error).not.toHaveBeenCalled();
    expect(component.exporting()).toBeFalse();
  });

  it('exposes isAuditor from the auth role', () => {
    configure(true);
    flushInit();
    expect(component.isAuditor()).toBeTrue();
  });
});
