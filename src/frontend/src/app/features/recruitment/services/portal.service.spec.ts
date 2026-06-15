import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
  HttpHeaders,
  HttpResponse,
} from '@angular/common/http';

import { PortalService } from './portal.service';
import { environment } from '../../../../environments/environment';
import { IPortalDashboard } from '../models/portal.models';

describe('PortalService (US-REC-008)', () => {
  let service: PortalService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/careers/portal`;
  const TOKEN = 'magic-token-123';

  const dashboardDto = (over: Partial<IPortalDashboard> = {}): IPortalDashboard => ({
    applicantName: 'Jane Doe',
    applicantEmail: 'jane@example.com',
    applications: [
      {
        id: 'app1',
        applicationReferenceNumber: 'APP-001',
        vacancyId: 'v1',
        vacancyTitle: 'Backend Engineer',
        departmentName: 'Engineering',
        appliedAt: '2026-06-01',
        stage: 'Interview',
        interview: {
          id: 'iv1',
          roundNumber: 1,
          interviewType: 'Video',
          scheduledDate: '2026-06-20',
          startTime: '10:00',
          durationMinutes: 60,
          location: null,
          videoLink: 'https://meet.example.com/abc',
          interviewers: [{ name: 'Sam Lead' }],
        },
        offer: null,
        timeline: [{ at: '2026-06-01T09:00:00Z', label: 'Application received' }],
      },
    ],
    ...over,
  });

  const HEADER = 'X-Portal-Token';

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PortalService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PortalService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── getDashboard (AC-1 / FR-2) ───────────────────────────

  it('getDashboard GETs the dashboard with the token in the X-Portal-Token header (anonymous)', () => {
    let result: IPortalDashboard | undefined;
    service.getDashboard(TOKEN).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/dashboard`);
    expect(req.request.method).toBe('GET');
    expect(req.request.headers.get(HEADER)).toBe(TOKEN);
    // Anonymous — must NOT send credentials.
    expect(req.request.withCredentials).toBeFalse();
    req.flush(dashboardDto());
    expect(result!.applicantName).toBe('Jane Doe');
    expect(result!.applications.length).toBe(1);
    expect(result!.applications[0].interview!.videoLink).toBe(
      'https://meet.example.com/abc',
    );
  });

  it('getDashboard maps a sparse DTO with safe defaults', () => {
    let result: IPortalDashboard | undefined;
    service.getDashboard(TOKEN).subscribe((r) => (result = r));
    httpMock
      .expectOne((r) => r.url === `${base}/dashboard`)
      .flush({ applications: [{ id: 'a', vacancyTitle: 'Role' }] });
    const app = result!.applications[0];
    expect(app.stage).toBe('Applied');
    expect(app.interview).toBeNull();
    expect(app.offer).toBeNull();
    expect(app.timeline).toEqual([]);
    expect(result!.applicantName).toBe('');
  });

  it('getDashboard tolerates a missing applications array', () => {
    let result: IPortalDashboard | undefined;
    service.getDashboard(TOKEN).subscribe((r) => (result = r));
    httpMock.expectOne((r) => r.url === `${base}/dashboard`).flush({});
    expect(result!.applications).toEqual([]);
  });

  // ─── respondToOffer (AC-3 / BR-2) ─────────────────────────

  it('respondToOffer POSTs { accept:true } for Accept with the token header', () => {
    service.respondToOffer('off1', TOKEN, 'Accepted').subscribe();
    const req = httpMock.expectOne(`${base}/offers/off1/respond`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get(HEADER)).toBe(TOKEN);
    expect(req.request.body).toEqual({ accept: true });
    expect(req.request.withCredentials).toBeFalse();
    req.flush({ id: 'off1', status: 'Accepted' });
  });

  it('respondToOffer sends { accept:false } for a decline', () => {
    service.respondToOffer('off1', TOKEN, 'Declined').subscribe();
    const req = httpMock.expectOne(`${base}/offers/off1/respond`);
    expect(req.request.body).toEqual({ accept: false });
    req.flush({ id: 'off1', status: 'Declined' });
  });

  // ─── downloadOfferDocument (FR-4) ─────────────────────────

  it('downloadOfferDocument requests a blob with the token and observe response', () => {
    let res: HttpResponse<Blob> | undefined;
    service
      .downloadOfferDocument('off1', TOKEN)
      .subscribe((r) => (res = r));
    const req = httpMock.expectOne(`${base}/offers/off1/document`);
    expect(req.request.headers.get(HEADER)).toBe(TOKEN);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.withCredentials).toBeFalse();
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    req.flush(blob, {
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="offer-OFF-1.pdf"',
      }),
    });
    expect(res!.body).toBe(blob);
  });

  // ─── requestLink (FR-8 / BR-5) ────────────────────────────

  it('requestLink POSTs the email (no token needed)', () => {
    let done = false;
    service.requestLink('jane@example.com').subscribe(() => (done = true));
    const req = httpMock.expectOne(`${base}/request-link`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'jane@example.com' });
    req.flush({ sent: true });
    expect(done).toBeTrue();
  });

  // ─── isTokenError (FR-8 / BR-5) ───────────────────────────

  it('isTokenError is true for 401 and 410, false otherwise', () => {
    expect(
      PortalService.isTokenError(new HttpErrorResponse({ status: 401 })),
    ).toBeTrue();
    expect(
      PortalService.isTokenError(new HttpErrorResponse({ status: 410 })),
    ).toBeTrue();
    expect(
      PortalService.isTokenError(new HttpErrorResponse({ status: 500 })),
    ).toBeFalse();
    expect(
      PortalService.isTokenError(new HttpErrorResponse({ status: 404 })),
    ).toBeFalse();
  });

  // ─── static helpers ───────────────────────────────────────

  it('filenameFromResponse reads the Content-Disposition filename', () => {
    const res = new HttpResponse<Blob>({
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="offer-1.pdf"',
      }),
    });
    expect(PortalService.filenameFromResponse(res, 'fallback.pdf')).toBe(
      'offer-1.pdf',
    );
  });

  it('filenameFromResponse falls back when no header', () => {
    const res = new HttpResponse<Blob>({ headers: new HttpHeaders() });
    expect(PortalService.filenameFromResponse(res, 'fallback.pdf')).toBe(
      'fallback.pdf',
    );
  });

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({
      error: { message: 'Token expired' },
      status: 410,
    });
    expect(PortalService.parseErrorMessage(withMsg)).toBe('Token expired');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(PortalService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
