import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { ImpersonateDialogComponent } from './impersonate-dialog.component';
import { AuthService } from '../../../../../core/auth/auth.service';
import { environment } from '../../../../../../environments/environment';
import {
  IImpersonationTarget,
  IStartImpersonationResponse,
} from '../../models/impersonation.models';

describe('ImpersonateDialogComponent', () => {
  let fixture: ComponentFixture<ImpersonateDialogComponent>;
  let component: ImpersonateDialogComponent;
  let httpMock: HttpTestingController;

  const base = `${environment.apiBaseUrl}/system/impersonation`;

  const targets: IImpersonationTarget[] = [
    { userId: 'u-1', email: 'ada@acme.test', displayName: 'Ada', roles: ['HR Officer'] },
    { userId: 'u-2', email: 'bob@acme.test', displayName: 'Bob', roles: [] },
  ];

  const startRes: IStartImpersonationResponse = {
    sessionId: 'sess-1',
    token: 'imp.jwt.token',
    redirectUrl: 'https://acme.yourhrm.com',
    expiresAt: '2026-06-16T13:00:00Z',
    isReadOnly: false,
  };

  let activateSpy: jasmine.Spy;
  let toastrSuccess: jasmine.Spy;

  function flushTargets(list: IImpersonationTarget[] = targets): void {
    const req = httpMock.expectOne((r) => r.url === `${base}/targets`);
    req.flush(list);
  }

  beforeEach(() => {
    activateSpy = jasmine.createSpy('activateImpersonation');
    toastrSuccess = jasmine.createSpy('success');

    TestBed.configureTestingModule({
      imports: [ImpersonateDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        { provide: AuthService, useValue: { activateImpersonation: activateSpy } },
        { provide: ToastrService, useValue: { success: toastrSuccess } },
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ImpersonateDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('tenantId', 't-1');
    fixture.componentRef.setInput('tenantName', 'Acme');
  });

  afterEach(() => httpMock.verify());

  it('loads the target list from GET targets on init', () => {
    fixture.detectChanges();
    flushTargets();

    expect(component.targets().length).toBe(2);
    expect(component.targetsLoading()).toBeFalse();
  });

  it('keeps submit disabled until a reason of >= 10 chars is provided (AC-1/BR-4)', () => {
    fixture.detectChanges();
    flushTargets();

    component.selectedUserId = 'u-1';
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

  it('requires a selected target even with a valid reason', () => {
    fixture.detectChanges();
    flushTargets();

    component.reason = 'a clearly valid reason';
    component.onInput();
    expect(component.canSubmit()).toBeFalse();
  });

  it('posts the correct body, activates the token, toasts, and emits started', () => {
    fixture.detectChanges();
    flushTargets();

    component.selectedUserId = 'u-1';
    component.reason = 'reproducing the reported issue';
    component.onInput();

    let emitted: IStartImpersonationResponse | undefined;
    component.started.subscribe((r) => (emitted = r));

    component.confirm();

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      targetUserId: 'u-1',
      targetTenantId: 't-1',
      reason: 'reproducing the reported issue',
    });
    req.flush(startRes);

    expect(activateSpy).toHaveBeenCalledWith('imp.jwt.token');
    expect(toastrSuccess).toHaveBeenCalled();
    expect(emitted).toEqual(startRes);
    expect(component.submitting()).toBeFalse();
  });

  it('surfaces a clear error on 409 session_active', () => {
    fixture.detectChanges();
    flushTargets();

    component.selectedUserId = 'u-1';
    component.reason = 'reproducing the reported issue';
    component.onInput();
    component.confirm();

    const req = httpMock.expectOne(base);
    req.flush(
      { code: 'session_active', message: 'one active session per admin' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.submitError()).toContain('active impersonation session');
    expect(activateSpy).not.toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });

  it('emits cancelled on cancel()', () => {
    fixture.detectChanges();
    flushTargets();

    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    component.cancel();
    expect(cancelled).toBeTrue();
  });
});
