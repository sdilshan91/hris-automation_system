import { TestBed, ComponentFixture, fakeAsync, tick } from '@angular/core/testing';
import {
  ActivatedRoute,
  Router,
  convertToParamMap,
  provideRouter,
} from '@angular/router';
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

  const baseUrl = `${environment.apiBaseUrl}/tenant/audit-logs`;
  const filterOptionsUrl = `${baseUrl}/filter-options`;
  const actorsUrl = `${baseUrl}/actors`;

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

  /**
   * Like `configure`, but seeds the route's query params so `restoreFromUrl`
   * (FR-3) hydrates the filter signals on init. Does NOT call detectChanges.
   */
  function configureWithParams(params: Record<string, string | string[]>): void {
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
          useValue: { hasRole: () => false },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap(params) },
          },
        },
      ],
    });

    fixture = TestBed.createComponent(AuditLogListComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  }

  /**
   * Flush the initial filter-options + list requests that fire on init.
   * US-NTF-005 replaced the eager actor preload (`/users`) with the
   * `/filter-options` call + the on-demand `/actors` type-ahead.
   */
  function flushInit(
    response: IAuditLogPage = page1,
    options: { actions: string[]; resourceTypes: string[] } = {
      actions: [],
      resourceTypes: [],
    }
  ): void {
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === filterOptionsUrl).flush(options);
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

  it('gives the results table a caption and scoped column headers (ISSUE-213 / WCAG 1.3.1)', () => {
    configure();
    flushInit();
    fixture.detectChanges();

    const table: HTMLTableElement = fixture.nativeElement.querySelector('table');
    expect(table).toBeTruthy();
    // Caption present (sr-only) so a screen reader announces the table's purpose.
    expect(table.querySelector('caption')).toBeTruthy();
    // Every header cell carries scope="col".
    const headers = Array.from(table.querySelectorAll('thead th'));
    expect(headers.length).toBe(6);
    expect(headers.every((th) => th.getAttribute('scope') === 'col')).toBeTrue();
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

  it('applies the multi-select action filter to the next list query (FR-2)', () => {
    configure();
    flushInit();

    component.toggleAction('Leave.Approve');
    const req = httpMock.expectOne((r) => r.url === baseUrl && r.method === 'GET');
    expect(req.request.params.getAll('actions')).toEqual(['Leave.Approve']);
    expect(req.request.params.get('page')).toBe('1');
    req.flush(page1);
  });

  it('selects multiple actions and sends them as repeated params (FR-2)', () => {
    configure();
    flushInit();

    component.toggleAction('Leave.Approve');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.toggleAction('Employee.Create');
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.getAll('actions')).toEqual([
      'Leave.Approve',
      'Employee.Create',
    ]);
    req.flush(page1);
  });

  it('"Select All" actions selects every option, then clears on re-toggle (FR-2)', () => {
    configure();
    flushInit();
    // actionOptions derived from rows: ['Employee.Create', 'Leave.Approve'].

    component.toggleAllActions();
    let req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.getAll('actions')).toEqual([
      'Employee.Create',
      'Leave.Approve',
    ]);
    expect(component.allActionsSelected()).toBeTrue();
    req.flush(page1);

    component.toggleAllActions();
    req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.has('actions')).toBeFalse();
    expect(component.selectedActions().length).toBe(0);
    req.flush(page1);
  });

  it('combines multiple filters in one query (AND)', () => {
    configure();
    flushInit();

    component.toggleResource('Employee');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.onStartDateChange('2026-06-01');
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.getAll('resourceTypes')).toEqual(['Employee']);
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

    component.toggleAction('Leave.Approve');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.nextPage();
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(req.request.params.get('page')).toBe('2');
    // Filter still applied on the new page.
    expect(req.request.params.getAll('actions')).toEqual(['Leave.Approve']);
    req.flush(page1);
  });

  it('resets to page 1 when a filter changes after paging', () => {
    configure();
    flushInit();

    component.nextPage();
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);
    expect(component.page()).toBe(2);

    component.selectActor({
      id: 'ut-1',
      displayName: 'Jane Doe',
      email: 'jane@acme.com',
    });
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    expect(component.page()).toBe(1);
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('actorUserId')).toBe('ut-1');
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

    component.toggleAction('Employee.Create');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.openExport();
    component.onExportConfirmed('csv');

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/export`);
    expect(req.request.params.get('format')).toBe('csv');
    expect(req.request.params.getAll('actions')).toEqual(['Employee.Create']);
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

  // ─── US-NTF-005 deltas ─────────────────────────────────

  it('seeds the multi-select options from /filter-options (FR-2)', () => {
    configure();
    flushInit(page1, {
      actions: ['Auth.Login', 'Employee.Delete'],
      resourceTypes: ['Session'],
    });
    // Options merge the endpoint values with the row-derived ones, sorted.
    expect(component.actionOptions()).toEqual([
      'Auth.Login',
      'Employee.Create',
      'Employee.Delete',
      'Leave.Approve',
    ]);
    expect(component.resourceOptions()).toContain('Session');
  });

  it('falls back to row-derived options when /filter-options is empty (FR-2)', () => {
    configure();
    flushInit(); // empty options by default
    expect(component.actionOptions()).toEqual([
      'Employee.Create',
      'Leave.Approve',
    ]);
  });

  it('actor type-ahead queries /actors (debounced) and selects a result (FR-2)', fakeAsync(() => {
    configure();
    flushInit();

    component.onActorInput('jan');
    tick(300);
    const req = httpMock.expectOne((r) => r.url === actorsUrl);
    expect(req.request.params.get('search')).toBe('jan');
    req.flush([{ userId: 'u-7', name: 'Jane Doe', email: 'jane@acme.com' }]);

    expect(component.actorResults().length).toBe(1);
    expect(component.actorMenuOpen()).toBeTrue();

    component.selectActor(component.actorResults()[0]);
    expect(component.actorFilter()).toBe('u-7');
    expect(component.actorMenuOpen()).toBeFalse();

    // Selection triggers a list reload carrying actorUserId.
    const listReq = httpMock.expectOne((r) => r.url === baseUrl);
    expect(listReq.request.params.get('actorUserId')).toBe('u-7');
    listReq.flush(page1);
  }));

  it('does not query /actors for a sub-2-char term (FR-2)', fakeAsync(() => {
    configure();
    flushInit();

    component.onActorInput('j');
    tick(300);
    httpMock.expectNone((r) => r.url === actorsUrl);
    expect(component.actorMenuOpen()).toBeFalse();
  }));

  it('clearing the actor input clears the selection and reloads', fakeAsync(() => {
    configure();
    flushInit();

    component.onActorInput('jane');
    tick(300);
    httpMock
      .expectOne((r) => r.url === actorsUrl)
      .flush([{ userId: 'u-7', name: 'Jane Doe', email: 'jane@acme.com' }]);
    component.selectActor(component.actorResults()[0]);
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    component.onActorInput('');
    expect(component.actorFilter()).toBe('');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);
  }));

  it('syncs applied filters into the URL query params (FR-3)', () => {
    configure();
    flushInit();
    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigate').and.callThrough();

    component.toggleAction('Leave.Approve');
    httpMock.expectOne((r) => r.url === baseUrl).flush(page1);

    expect(navSpy).toHaveBeenCalled();
    const queryParams = (navSpy.calls.mostRecent().args[1] as { queryParams: Record<string, unknown> })
      .queryParams;
    expect(queryParams['actions']).toEqual(['Leave.Approve']);
  });

  it('restores filters from the URL on load (FR-3)', () => {
    configureWithParams({
      actions: ['Employee.Create'],
      resourceTypes: ['Employee'],
      actorUserId: 'u-9',
      actorLabel: 'Jane (jane@acme.com)',
      search: 'john',
      startDate: '2026-06-01',
      page: '3',
    });
    fixture.detectChanges();
    httpMock.expectOne((r) => r.url === filterOptionsUrl).flush({
      actions: [],
      resourceTypes: [],
    });
    const listReq = httpMock.expectOne((r) => r.url === baseUrl);
    expect(listReq.request.params.getAll('actions')).toEqual(['Employee.Create']);
    expect(listReq.request.params.getAll('resourceTypes')).toEqual(['Employee']);
    expect(listReq.request.params.get('actorUserId')).toBe('u-9');
    expect(listReq.request.params.get('search')).toBe('john');
    expect(listReq.request.params.get('startDate')).toBe('2026-06-01');
    expect(listReq.request.params.get('page')).toBe('3');
    listReq.flush(page1);

    expect(component.selectedActions()).toEqual(['Employee.Create']);
    expect(component.actorLabel()).toBe('Jane (jane@acme.com)');
    expect(component.page()).toBe(3);
  });
});
