import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { TerminateTenantDialogComponent } from './terminate-tenant-dialog.component';
import { environment } from '../../../../../../environments/environment';
import { ITenantLifecycleResult } from '../../models/lifecycle.models';

describe('TerminateTenantDialogComponent', () => {
  let fixture: ComponentFixture<TerminateTenantDialogComponent>;
  let component: TerminateTenantDialogComponent;
  let httpMock: HttpTestingController;

  const terminateUrl = `${environment.apiBaseUrl}/system/tenants/t-1/lifecycle/terminate`;

  const result: ITenantLifecycleResult = {
    tenantId: 't-1',
    subdomain: 'acme',
    status: 'terminating',
    suspendedAt: null,
    suspendedReason: null,
    terminationScheduledAt: '2026-07-17T10:00:00Z',
    eventType: 'termination_initiated',
  };

  let toastrSuccess: jasmine.Spy;

  beforeEach(() => {
    toastrSuccess = jasmine.createSpy('success');

    TestBed.configureTestingModule({
      imports: [TerminateTenantDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideTranslateService(),
        { provide: ToastrService, useValue: { success: toastrSuccess } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TerminateTenantDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('tenantId', 't-1');
    fixture.componentRef.setInput('tenantName', 'Acme');
    fixture.componentRef.setInput('subdomain', 'acme');
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('validates the grace period is within 7–90 (BR-4)', () => {
    component.reason = 'a clearly valid termination reason';
    component.graceDays = 5;
    component.onInput();
    expect(component.graceValid()).toBeFalse();
    expect(component.step1Valid()).toBeFalse();

    component.graceDays = 95;
    component.onInput();
    expect(component.graceValid()).toBeFalse();

    component.graceDays = 30;
    component.onInput();
    expect(component.graceValid()).toBeTrue();
    expect(component.step1Valid()).toBeTrue();
  });

  it('advances through the steps only when step 1 is valid', () => {
    component.reason = 'short';
    component.graceDays = 30;
    component.onInput();
    component.next();
    // Reason too short → cannot leave step 1.
    expect(component.step()).toBe(1);

    component.reason = 'a clearly valid termination reason';
    component.onInput();
    component.next();
    expect(component.step()).toBe(2);
    component.next();
    expect(component.step()).toBe(3);
  });

  it('keeps Terminate disabled until the typed subdomain matches exactly (FR-4)', () => {
    component.reason = 'a clearly valid termination reason';
    component.graceDays = 30;
    component.onInput();
    component.next();
    component.next();
    expect(component.step()).toBe(3);

    component.confirmSubdomain = 'acm';
    component.onInput();
    expect(component.subdomainMatches()).toBeFalse();
    expect(component.canSubmit()).toBeFalse();

    component.confirmSubdomain = 'Acme'; // wrong case
    component.onInput();
    expect(component.subdomainMatches()).toBeFalse();

    component.confirmSubdomain = 'acme';
    component.onInput();
    expect(component.subdomainMatches()).toBeTrue();
    expect(component.canSubmit()).toBeTrue();
  });

  it('blocks paste on the confirmation input (NFR-5)', () => {
    component.reason = 'a clearly valid termination reason';
    component.graceDays = 30;
    component.onInput();
    component.next();
    component.next();
    fixture.detectChanges();

    const input = fixture.nativeElement.querySelector(
      '[data-testid="confirm-subdomain-input"]',
    ) as HTMLInputElement;
    const pasteEvent = new Event('paste', { cancelable: true });
    input.dispatchEvent(pasteEvent);
    expect(pasteEvent.defaultPrevented).toBeTrue();
  });

  it('posts { reason, graceDays } and emits terminated on success (AC-3)', () => {
    component.reason = 'Account closed at customer request';
    component.graceDays = 45;
    component.onInput();
    component.next();
    component.next();
    component.confirmSubdomain = 'acme';
    component.onInput();

    let emitted: ITenantLifecycleResult | undefined;
    component.terminated.subscribe((r) => (emitted = r));

    component.confirm();

    const req = httpMock.expectOne(terminateUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      reason: 'Account closed at customer request',
      graceDays: 45,
    });
    req.flush(result);

    expect(toastrSuccess).toHaveBeenCalled();
    expect(emitted).toEqual(result);
  });

  it('emits cancelled on cancel()', () => {
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    component.cancel();
    expect(cancelled).toBeTrue();
  });
});
