import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import {
  LifecycleConfirmDialogComponent,
  LifecycleConfirmAction,
} from './lifecycle-confirm-dialog.component';
import { environment } from '../../../../../../environments/environment';
import { ITenantLifecycleResult } from '../../models/lifecycle.models';

describe('LifecycleConfirmDialogComponent', () => {
  let fixture: ComponentFixture<LifecycleConfirmDialogComponent>;
  let component: LifecycleConfirmDialogComponent;
  let httpMock: HttpTestingController;

  const base = `${environment.apiBaseUrl}/system/tenants/t-1/lifecycle`;

  const result: ITenantLifecycleResult = {
    tenantId: 't-1',
    subdomain: 'acme',
    status: 'active',
    suspendedAt: null,
    suspendedReason: null,
    terminationScheduledAt: null,
    eventType: 'reactivated',
  };

  let toastrSuccess: jasmine.Spy;

  function build(action: LifecycleConfirmAction): void {
    toastrSuccess = jasmine.createSpy('success');

    TestBed.configureTestingModule({
      imports: [LifecycleConfirmDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideTranslateService(),
        { provide: ToastrService, useValue: { success: toastrSuccess } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(LifecycleConfirmDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('tenantId', 't-1');
    fixture.componentRef.setInput('tenantName', 'Acme');
    fixture.componentRef.setInput('action', action);
    fixture.detectChanges();
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('POSTs reactivate and emits confirmed (AC-5)', () => {
    build('reactivate');

    let emitted: ITenantLifecycleResult | undefined;
    component.confirmed.subscribe((r) => (emitted = r));

    component.confirm();

    const req = httpMock.expectOne(`${base}/reactivate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(result);

    expect(toastrSuccess).toHaveBeenCalled();
    expect(emitted?.eventType).toBe('reactivated');
  });

  it('POSTs restore and emits confirmed (AC-6)', () => {
    build('restore');

    let emitted: ITenantLifecycleResult | undefined;
    component.confirmed.subscribe((r) => (emitted = r));

    component.confirm();

    const req = httpMock.expectOne(`${base}/restore`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush({ ...result, status: 'suspended', eventType: 'restored' });

    expect(emitted?.eventType).toBe('restored');
  });

  it('surfaces an error on failure and keeps the dialog open', () => {
    build('reactivate');
    component.confirm();

    const req = httpMock.expectOne(`${base}/reactivate`);
    req.flush(
      { code: 'invalid_transition', message: 'bad state' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.submitError()).toContain('not allowed');
    expect(component.submitting()).toBeFalse();
  });

  it('emits cancelled on cancel()', () => {
    build('reactivate');
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    component.cancel();
    expect(cancelled).toBeTrue();
  });
});
