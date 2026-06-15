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

import { OfferService } from './offer.service';
import { environment } from '../../../../environments/environment';
import { IOffer, IOfferRequest } from '../models/offer.models';

describe('OfferService (US-REC-007)', () => {
  let service: OfferService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/recruitment`;

  const dto = (over: Partial<IOffer> = {}): IOffer => ({
    id: 'o1',
    offerReferenceNumber: 'OFF-001',
    applicantId: 'a1',
    vacancyId: 'v1',
    status: 'Draft',
    offeredPosition: 'Senior Engineer',
    salaryAmount: 120000,
    currency: 'USD',
    salaryFrequency: 'Annual',
    startDate: '2026-07-01',
    expiryDate: '2026-06-22',
    version: 1,
    createdAt: '2026-06-15T10:00:00Z',
    ...over,
  });

  const request: IOfferRequest = {
    offeredPosition: 'Senior Engineer',
    departmentId: 'd1',
    reportingManagerId: 'm1',
    salaryAmount: 120000,
    currency: 'USD',
    salaryFrequency: 'Annual',
    benefitsSummary: 'Health + bonus',
    startDate: '2026-07-01',
    expiryDate: '2026-06-22',
    probationMonths: 3,
    customClauses: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        OfferService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(OfferService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── generate (AC-1 / FR-1) ───────────────────────────────

  it('generate POSTs to the applicant offers endpoint with credentials', () => {
    let result: IOffer | undefined;
    service.generate('a1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/offers`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual({ ...request, applicantId: 'a1' });
    req.flush(dto());
    expect(result!.id).toBe('o1');
    expect(result!.status).toBe('Draft');
    expect(result!.offerReferenceNumber).toBe('OFF-001');
  });

  it('generate maps a sparse DTO with safe defaults', () => {
    let result: IOffer | undefined;
    service.generate('a1', request).subscribe((r) => (result = r));
    httpMock
      .expectOne(`${base}/offers`)
      .flush({ id: 'o2', applicantId: 'a1' });
    expect(result!.status).toBe('Draft');
    expect(result!.salaryFrequency).toBe('Annual');
    expect(result!.version).toBe(1);
    expect(result!.currency).toBe('');
  });

  // ─── listForApplicant (FR-9 versions) ─────────────────────

  it('listForApplicant GETs the offers list and maps each', () => {
    let rows: IOffer[] | undefined;
    service.listForApplicant('a1').subscribe((r) => (rows = r));
    const req = httpMock.expectOne(`${base}/applicants/a1/offers`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([dto({ id: 'a' }), dto({ id: 'b', version: 2 })]);
    expect(rows!.length).toBe(2);
    expect(rows![1].version).toBe(2);
  });

  it('listForApplicant tolerates a { data } envelope', () => {
    let rows: IOffer[] | undefined;
    service.listForApplicant('a1').subscribe((r) => (rows = r));
    httpMock
      .expectOne(`${base}/applicants/a1/offers`)
      .flush({ data: [dto({ id: 'x' })] });
    expect(rows!.length).toBe(1);
    expect(rows![0].id).toBe('x');
  });

  // ─── getOffer ─────────────────────────────────────────────

  it('getOffer GETs a single offer by id', () => {
    let result: IOffer | undefined;
    service.getOffer('o1').subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/offers/o1`);
    expect(req.request.method).toBe('GET');
    req.flush(dto());
    expect(result!.id).toBe('o1');
  });

  // ─── send (AC-2 / FR-5) ───────────────────────────────────

  it('send POSTs to the send endpoint and returns the Sent offer', () => {
    let result: IOffer | undefined;
    service.send('o1').subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/offers/o1/send`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(dto({ status: 'Sent', sentAt: '2026-06-16T09:00:00Z' }));
    expect(result!.status).toBe('Sent');
    expect(result!.sentAt).toBe('2026-06-16T09:00:00Z');
  });

  // ─── respond (AC-3 / BR-3) ────────────────────────────────

  it('respond POSTs the response body and maps an Accepted offer', () => {
    let result: IOffer | undefined;
    service.respond('o1', 'Accepted').subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/offers/o1/respond`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ accepted: true });
    req.flush(dto({ status: 'Accepted', response: 'Accepted' }));
    expect(result!.status).toBe('Accepted');
    expect(result!.response).toBe('Accepted');
  });

  it('respond sends accepted=false for a decline', () => {
    service.respond('o1', 'Declined', 'Took another offer').subscribe();
    const req = httpMock.expectOne(`${base}/offers/o1/respond`);
    expect(req.request.body).toEqual({ accepted: false });
    req.flush(dto({ status: 'Declined', response: 'Declined' }));
  });

  // ─── withdraw (FR-8) ──────────────────────────────────────

  it('withdraw POSTs to the withdraw endpoint with an optional reason', () => {
    let result: IOffer | undefined;
    service.withdraw('o1', 'Role frozen').subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/offers/o1/withdraw`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'Role frozen' });
    req.flush(dto({ status: 'Withdrawn' }));
    expect(result!.status).toBe('Withdrawn');
  });

  it('withdraw sends a null reason when omitted', () => {
    service.withdraw('o1').subscribe();
    const req = httpMock.expectOne(`${base}/offers/o1/withdraw`);
    expect(req.request.body).toEqual({ reason: null });
    req.flush(dto({ status: 'Withdrawn' }));
  });

  // ─── downloadPdf (FR-4) ───────────────────────────────────

  it('downloadPdf requests a blob with observe response', () => {
    let res: HttpResponse<Blob> | undefined;
    service.downloadPdf('o1').subscribe((r) => (res = r));
    const req = httpMock.expectOne(`${base}/offers/o1/document`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.withCredentials).toBeTrue();
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    req.flush(blob, {
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="offer-OFF-001.pdf"',
      }),
    });
    expect(res!.body).toBe(blob);
  });

  // ─── static helpers ───────────────────────────────────────

  it('filenameFromResponse reads the Content-Disposition filename', () => {
    const res = new HttpResponse<Blob>({
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="offer-1.pdf"',
      }),
    });
    expect(OfferService.filenameFromResponse(res, 'fallback.pdf')).toBe(
      'offer-1.pdf',
    );
  });

  it('filenameFromResponse falls back when no header', () => {
    const res = new HttpResponse<Blob>({ headers: new HttpHeaders() });
    expect(OfferService.filenameFromResponse(res, 'fallback.pdf')).toBe(
      'fallback.pdf',
    );
  });

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({
      error: { message: 'Offer already sent' },
      status: 409,
    });
    expect(OfferService.parseErrorMessage(withMsg)).toBe('Offer already sent');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(OfferService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
