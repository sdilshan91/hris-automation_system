import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { ExportPanelComponent } from './export-panel.component';
import { IExportRecord } from '../../models/data-export.models';
import { environment } from '../../../../../../environments/environment';

describe('ExportPanelComponent', () => {
  let fixture: ComponentFixture<ExportPanelComponent>;
  let component: ExportPanelComponent;
  let httpMock: HttpTestingController;
  let toastr: jasmine.SpyObj<ToastrService>;

  const tenantBase = `${environment.apiBaseUrl}/tenant/exports`;

  const completed: IExportRecord = {
    id: 'exp-done',
    scope: 'Full export',
    status: 'Completed',
    requestedAt: '2026-06-17T10:00:00Z',
    completedAt: '2026-06-17T10:05:00Z',
    fileSizeBytes: 4096,
    expiresAt: '2026-06-20T10:05:00Z',
    downloadAvailable: true,
  };

  const expired: IExportRecord = {
    ...completed,
    id: 'exp-expired',
    status: 'Expired',
    downloadAvailable: false,
  };

  const processing: IExportRecord = {
    ...completed,
    id: 'exp-proc',
    status: 'Processing',
    completedAt: null,
    fileSizeBytes: null,
    expiresAt: null,
    downloadAvailable: false,
  };

  function build(): void {
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    TestBed.configureTestingModule({
      imports: [ExportPanelComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideTranslateService(),
        provideToastr(),
        { provide: ToastrService, useValue: toastr },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ExportPanelComponent);
    component = fixture.componentInstance;
  }

  /** Trigger ngOnInit and flush the initial history GET with the given rows. */
  function init(rows: IExportRecord[] = []): void {
    fixture.detectChanges();
    httpMock.expectOne(tenantBase).flush(rows);
    fixture.detectChanges();
  }

  afterEach(() => {
    // Cancel any pending poll timer so verify() isn't tripped, then verify.
    component.ngOnDestroy();
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  beforeEach(() => build());

  it('defaults to a full export and can start immediately', () => {
    init();
    expect(component.fullExport()).toBeTrue();
    expect(component.canStart()).toBeTrue();
  });

  it('posts scope "full" when full export is selected (AC-1)', () => {
    init();
    component.startExport();

    const req = httpMock.expectOne(tenantBase);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.scope).toBe('full');
    req.flush({ exportId: 'e1', status: 'Queued' });

    // After success it reloads history.
    httpMock.expectOne(tenantBase).flush([]);
    expect(toastr.success).toHaveBeenCalled();
  });

  it('posts the selected entity codes for a partial export', () => {
    init();
    component.setFullExport(false);
    component.toggleEntity('employees');
    component.toggleEntity('leave_requests');
    expect(component.canStart()).toBeTrue();

    component.startExport();
    const req = httpMock.expectOne(tenantBase);
    expect(req.request.body.scope).toEqual(['employees', 'leave_requests']);
    req.flush({ exportId: 'e2', status: 'Queued' });
    httpMock.expectOne(tenantBase).flush([]);
  });

  it('blocks Start for a partial export with nothing selected', () => {
    init();
    component.setFullExport(false);
    component.clearSelection();
    expect(component.canStart()).toBeFalse();

    component.startExport();
    // No POST should be issued.
    httpMock.expectNone(tenantBase);
  });

  it('surfaces an in-progress (409) error from initiate (FR-9)', () => {
    init();
    component.startExport();
    const req = httpMock.expectOne(tenantBase);
    req.flush('An export is already in progress.', {
      status: 409,
      statusText: 'Conflict',
    });

    expect(toastr.error).toHaveBeenCalledWith('An export is already in progress.');
    expect(component.submitting()).toBeFalse();
  });

  it('surfaces a rate-limit message from the server (BR-5)', () => {
    init();
    component.startExport();
    httpMock
      .expectOne(tenantBase)
      .flush('Monthly export limit reached.', {
        status: 400,
        statusText: 'Bad Request',
      });

    expect(toastr.error).toHaveBeenCalledWith('Monthly export limit reached.');
  });

  it('lists history rows with status badges and file size', () => {
    init([completed, expired]);
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="history-list"]')).not.toBeNull();
    expect(
      el.querySelector('[data-testid="status-exp-done"]')
    ).not.toBeNull();
  });

  it('enables download only for a completed, available export', () => {
    init([completed]);
    const el: HTMLElement = fixture.nativeElement;
    const btn = el.querySelector(
      '[data-testid="download-exp-done"]'
    ) as HTMLButtonElement;
    expect(btn.disabled).toBeFalse();
  });

  it('disables download for an expired export', () => {
    init([expired]);
    const el: HTMLElement = fixture.nativeElement;
    const btn = el.querySelector(
      '[data-testid="download-exp-expired"]'
    ) as HTMLButtonElement;
    expect(btn.disabled).toBeTrue();
  });

  it('disables download for a processing export', () => {
    init([processing]);
    const el: HTMLElement = fixture.nativeElement;
    const btn = el.querySelector(
      '[data-testid="download-exp-proc"]'
    ) as HTMLButtonElement;
    expect(btn.disabled).toBeTrue();
  });

  it('shows the progress indicator while an export is in progress (§8)', () => {
    init([processing]);
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="progress-indicator"]')).not.toBeNull();
    expect(component.hasInProgress()).toBeTrue();
  });

  it('downloads a completed export and triggers a blob save (AC-3)', () => {
    init([completed]);
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    spyOn(URL, 'revokeObjectURL');

    component.download(completed);
    const req = httpMock.expectOne(`${tenantBase}/exp-done/download`);
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['zip'], { type: 'application/zip' }), {
      headers: { 'Content-Disposition': 'attachment; filename="acme-export.zip"' },
    });

    expect(clickSpy).toHaveBeenCalled();
  });

  it('warns and refreshes when a download is expired (410)', () => {
    init([completed]);
    component.download(completed);
    httpMock
      .expectOne(`${tenantBase}/exp-done/download`)
      .flush(null, { status: 410, statusText: 'Gone' });

    expect(toastr.warning).toHaveBeenCalled();
    // It reloads history after an expiry.
    httpMock.expectOne(tenantBase).flush([expired]);
  });

  it('routes requests to the system base when a tenantId is provided (AC-6)', () => {
    fixture.componentRef.setInput('tenantId', 't-42');
    const systemBase = `${environment.apiBaseUrl}/system/tenants/t-42/exports`;

    fixture.detectChanges();
    httpMock.expectOne(systemBase).flush([]);

    component.startExport();
    const req = httpMock.expectOne(systemBase);
    expect(req.request.method).toBe('POST');
    req.flush({ exportId: 'e3', status: 'Queued' });
    httpMock.expectOne(systemBase).flush([]);
  });
});
