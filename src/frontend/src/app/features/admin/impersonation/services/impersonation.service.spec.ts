import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ImpersonationService } from './impersonation.service';
import { environment } from '../../../../../environments/environment';
import {
  IStartImpersonationResponse,
  IEndImpersonationResponse,
  IImpersonationTarget,
} from '../models/impersonation.models';

describe('ImpersonationService', () => {
  let service: ImpersonationService;
  let httpMock: HttpTestingController;

  // Impersonation is rooted at /api/v1/system/impersonation (NOT /api/admin).
  const base = `${environment.apiBaseUrl}/system/impersonation`;

  const startRes: IStartImpersonationResponse = {
    sessionId: 'sess-1',
    token: 'imp.jwt.token',
    redirectUrl: 'https://acme.yourhrm.com',
    expiresAt: '2026-06-16T13:00:00Z',
    isReadOnly: false,
  };

  const endRes: IEndImpersonationResponse = {
    sessionId: 'sess-1',
    status: 'ended',
    actionsCount: 3,
    endedAt: '2026-06-16T12:30:00Z',
  };

  const targets: IImpersonationTarget[] = [
    {
      userId: 'u-1',
      email: 'ada@acme.test',
      displayName: 'Ada Lovelace',
      roles: ['HR Officer'],
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ImpersonationService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ImpersonationService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('POST /system/impersonation sends the start body and maps the response (AC-1)', () => {
    let result: IStartImpersonationResponse | undefined;
    service
      .start({
        targetUserId: 'u-1',
        targetTenantId: 't-1',
        reason: 'investigating a reported bug',
      })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual({
      targetUserId: 'u-1',
      targetTenantId: 't-1',
      reason: 'investigating a reported bug',
    });
    req.flush(startRes);

    expect(result).toEqual(startRes);
    expect(result?.token).toBe('imp.jwt.token');
  });

  it('POST /system/impersonation/{id}/end ends the session (AC-3)', () => {
    let result: IEndImpersonationResponse | undefined;
    service.end('sess-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/sess-1/end`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeNull();
    req.flush(endRes);

    expect(result?.status).toBe('ended');
    expect(result?.actionsCount).toBe(3);
  });

  it('GET /system/impersonation/targets forwards the tenantId query param', () => {
    let result: IImpersonationTarget[] | undefined;
    service.getTargets('t-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === `${base}/targets`,
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('tenantId')).toBe('t-1');
    req.flush(targets);

    expect(result?.length).toBe(1);
    expect(result?.[0].email).toBe('ada@acme.test');
  });
});
