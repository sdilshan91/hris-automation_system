import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { By } from '@angular/platform-browser';
import { provideTranslateService } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { SuspendTenantDialogComponent } from './suspend-tenant-dialog.component';
import { TrappedDialogDirective } from '../../../../../shared/directives';
import { environment } from '../../../../../../environments/environment';
import { ITenantLifecycleResult } from '../../models/lifecycle.models';

describe('SuspendTenantDialogComponent', () => {
  let fixture: ComponentFixture<SuspendTenantDialogComponent>;
  let component: SuspendTenantDialogComponent;
  let httpMock: HttpTestingController;

  const suspendUrl = `${environment.apiBaseUrl}/system/tenants/t-1/lifecycle/suspend`;

  const result: ITenantLifecycleResult = {
    tenantId: 't-1',
    subdomain: 'acme',
    status: 'suspended',
    suspendedAt: '2026-06-17T10:00:00Z',
    suspendedReason: 'Repeated ToS violations',
    terminationScheduledAt: null,
    eventType: 'suspended',
  };

  let toastrSuccess: jasmine.Spy;

  beforeEach(() => {
    toastrSuccess = jasmine.createSpy('success');

    TestBed.configureTestingModule({
      imports: [SuspendTenantDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideTranslateService(),
        { provide: ToastrService, useValue: { success: toastrSuccess } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SuspendTenantDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('tenantId', 't-1');
    fixture.componentRef.setInput('tenantName', 'Acme');
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('traps focus in the dialog (ISSUE-296)', () => {
    const dialog = fixture.debugElement.query(By.directive(TrappedDialogDirective));
    expect(dialog).toBeTruthy();
  });

  it('keeps submit disabled until reason >= 10 chars (AC-1)', () => {
    component.acknowledged = true;
    component.reason = 'too short';
    component.onInput();
    expect(component.reasonLength()).toBe(9);
    expect(component.reasonValid()).toBeFalse();
    expect(component.canSubmit()).toBeFalse();

    component.reason = 'a clearly valid reason';
    component.onInput();
    expect(component.reasonValid()).toBeTrue();
    expect(component.canSubmit()).toBeTrue();
  });

  it('requires the two-step acknowledgement before submit (AC-1)', () => {
    component.reason = 'a clearly valid reason';
    component.acknowledged = false;
    component.onInput();
    // Valid reason but not acknowledged → still disabled.
    expect(component.reasonValid()).toBeTrue();
    expect(component.canSubmit()).toBeFalse();

    component.acknowledged = true;
    component.onInput();
    expect(component.canSubmit()).toBeTrue();
  });

  it('posts the reason body and emits suspended on success', () => {
    component.reason = 'Repeated ToS violations on the platform';
    component.acknowledged = true;
    component.onInput();

    let emitted: ITenantLifecycleResult | undefined;
    component.suspended.subscribe((r) => (emitted = r));

    component.confirm();

    const req = httpMock.expectOne(suspendUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      reason: 'Repeated ToS violations on the platform',
    });
    req.flush(result);

    expect(toastrSuccess).toHaveBeenCalled();
    expect(emitted).toEqual(result);
    expect(component.submitting()).toBeFalse();
  });

  it('surfaces a clear error on 409 invalid_transition', () => {
    component.reason = 'Repeated ToS violations on the platform';
    component.acknowledged = true;
    component.onInput();
    component.confirm();

    const req = httpMock.expectOne(suspendUrl);
    req.flush(
      { code: 'invalid_transition', message: 'bad state' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.submitError()).toContain('not allowed');
    expect(component.submitting()).toBeFalse();
  });

  it('emits cancelled on cancel()', () => {
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    component.cancel();
    expect(cancelled).toBeTrue();
  });
});
