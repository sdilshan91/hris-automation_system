import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { ReviewSignoffService } from './review-signoff.service';
import { environment } from '../../../../environments/environment';
import { IReviewSignoff } from '../models/review-signoff.models';

describe('ReviewSignoffService', () => {
  let service: ReviewSignoffService;
  let httpMock: HttpTestingController;
  const perfBase = `${environment.apiBaseUrl}/tenant/performance`;
  const activeCycleUrl = `${perfBase}/cycles/active`;
  // reviews/cycles/{cycleId}/employees/{employeeId}
  const notesBase = `${perfBase}/reviews/cycles/cyc-1/employees/e-1`;

  const mockRecord: IReviewSignoff = {
    reviewId: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'NotesDraft',
    ratingScaleMax: 5,
    finalScore: 87,
    meetingNotesHtml: '<p>Notes</p>',
    managerSignature: null,
    employeeSignature: null,
    disputeComments: null,
    disputedOn: null,
    employeeViewed: false,
    goals: [{ goalId: 'g-1', title: 'NPS', weight: 100, managerRating: 4 }],
    exportAvailable: true,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ReviewSignoffService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ReviewSignoffService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  /** Every method first resolves the active cycle. */
  function flushActiveCycle(): void {
    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
  }

  it('getSignoff() resolves the cycle then GETs the notes record', () => {
    let result: IReviewSignoff | undefined;
    service.getSignoff('e-1').subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/notes`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockRecord);

    expect(result).toEqual(mockRecord);
  });

  it('saveNotes() PUTs the notes mapped to the backend Body field', () => {
    service.saveNotes('e-1', { meetingNotesHtml: '<p>Updated</p>' }).subscribe();

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/notes`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ body: '<p>Updated</p>' });
    req.flush(mockRecord);
  });

  it('requestSignoff() POSTs the notes to request-signoff and returns the pending record', () => {
    let result: IReviewSignoff | undefined;
    service
      .requestSignoff('e-1', { meetingNotesHtml: '<p>Final notes</p>' })
      .subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/request-signoff`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: '<p>Final notes</p>' });
    req.flush({ ...mockRecord, status: 'PendingEmployeeSignOff' });

    expect(result?.status).toBe('PendingEmployeeSignOff');
  });

  it('acknowledge() POSTs an empty body to the acknowledge route', () => {
    let result: IReviewSignoff | undefined;
    service.acknowledge('e-1').subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/acknowledge`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({
      ...mockRecord,
      status: 'SignedOff',
      employeeSignature: { name: 'Alex Doe', signedOn: '2026-06-12T10:00:00Z' },
    });

    expect(result?.status).toBe('SignedOff');
    expect(result?.employeeSignature?.name).toBe('Alex Doe');
  });

  it('dispute() POSTs the comments and returns the disputed record', () => {
    const body = { comments: 'I disagree with the rating on goal 1.' };
    let result: IReviewSignoff | undefined;
    service.dispute('e-1', body).subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/dispute`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({
      ...mockRecord,
      status: 'Disputed',
      disputeComments: body.comments,
    });

    expect(result?.status).toBe('Disputed');
  });

  it('resolveDispute() POSTs Amend + comments to the resolve-dispute route', () => {
    service.resolveDispute('e-1', { resolution: 'Amend' }).subscribe();

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/resolve-dispute`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ amend: true, comments: null });
    req.flush({ ...mockRecord, status: 'NotesDraft' });
  });

  // ── Employee self-service (ISSUE-288) ──────────────────────────────────────
  // Caller-scoped: NO active-cycle prefetch, NO cycleId/employeeId in the URL.
  const selfBase = `${perfBase}/reviews/cycles/active/me`;

  it('getMySignoff() GETs the caller-scoped notes with NO active-cycle prefetch', () => {
    let result: IReviewSignoff | undefined;
    service.getMySignoff().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${selfBase}/notes`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockRecord);

    // No secondary call to cycles/active — the server resolves the cycle itself.
    httpMock.expectNone(activeCycleUrl);
    expect(result).toEqual(mockRecord);
  });

  it('acknowledgeMy() POSTs an empty body to the caller-scoped acknowledge route', () => {
    let result: IReviewSignoff | undefined;
    service.acknowledgeMy().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${selfBase}/acknowledge`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    expect(req.request.withCredentials).toBeTrue();
    req.flush({
      ...mockRecord,
      status: 'SignedOff',
      employeeSignature: { name: 'Alex Doe', signedOn: '2026-06-12T10:00:00Z' },
    });

    httpMock.expectNone(activeCycleUrl);
    expect(result?.status).toBe('SignedOff');
    expect(result?.employeeSignature?.name).toBe('Alex Doe');
  });

  it('disputeMy() POSTs the comments to the caller-scoped dispute route', () => {
    const comments = 'I disagree with the rating on goal 1.';
    let result: IReviewSignoff | undefined;
    service.disputeMy(comments).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${selfBase}/dispute`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ comments });
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ ...mockRecord, status: 'Disputed', disputeComments: comments });

    httpMock.expectNone(activeCycleUrl);
    expect(result?.status).toBe('Disputed');
  });

  it('exportPdf() GETs a blob with credentials and the full response', () => {
    let status: number | undefined;
    service.exportPdf('e-1').subscribe((resp) => (status = resp.status));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/export`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(new Blob(['pdf']), { status: 200, statusText: 'OK' });

    expect(status).toBe(200);
  });
});
