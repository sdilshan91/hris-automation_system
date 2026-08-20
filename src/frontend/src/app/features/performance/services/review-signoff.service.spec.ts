import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { ReviewSignoffService } from './review-signoff.service';
import { environment } from '../../../../environments/environment';
import {
  IReviewSignoff,
  ReviewMeetingNotesWire,
} from '../models/review-signoff.models';

describe('ReviewSignoffService', () => {
  let service: ReviewSignoffService;
  let httpMock: HttpTestingController;
  const perfBase = `${environment.apiBaseUrl}/tenant/performance`;
  const activeCycleUrl = `${perfBase}/cycles/active`;
  // reviews/cycles/{cycleId}/employees/{employeeId}
  const notesBase = `${perfBase}/reviews/cycles/cyc-1/employees/e-1`;

  // Real wire (PerformanceReviewMeetingNotesDto): the record id is `managerReviewId`,
  // the notes HTML is `body`, the status enum is `signoffStatusName` (wire `NotesAdded`),
  // and the signatures live in the `signoffs[]` audit array — there is NO goal snapshot,
  // rating scale, manager name, cycle name, final score, or export flag on this DTO.
  const mockNotesWire: ReviewMeetingNotesWire = {
    managerReviewId: 'rv-1',
    cycleId: 'cyc-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    employeeNo: 'E-001',
    body: '<p>Notes</p>',
    signoffStatus: 'NotesAdded',
    signoffStatusName: 'NotesAdded',
    isLocked: false,
    signoffs: [],
  };

  // Expected mapped view-model. The eight backend-gap fields are defaulted here.
  const mockRecord: IReviewSignoff = {
    reviewId: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: null,
    managerName: '',
    status: 'NotesDraft',
    ratingScaleMax: 0,
    finalScore: null,
    meetingNotesHtml: '<p>Notes</p>',
    managerSignature: null,
    employeeSignature: null,
    disputeComments: null,
    disputedOn: null,
    employeeViewed: false,
    goals: [],
    exportAvailable: false,
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

  /** Every manager/HR method first resolves the active cycle. */
  function flushActiveCycle(): void {
    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
  }

  it('getSignoff() resolves the cycle then maps the notes record (fails on the un-migrated pass-through)', () => {
    // The un-migrated code returned the raw payload cast to IReviewSignoff, so
    // `reviewId` (wire `managerReviewId`) and `meetingNotesHtml` (wire `body`) were
    // undefined, and `status` was the wire `NotesAdded` — not a valid FE SignoffStatus.
    let result: IReviewSignoff | undefined;
    service.getSignoff('e-1').subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/notes`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockNotesWire);

    expect(result).toEqual(mockRecord);
    expect(result?.reviewId).toBe('rv-1');
    expect(result?.meetingNotesHtml).toBe('<p>Notes</p>');
    expect(result?.status).toBe('NotesDraft');
  });

  it('getSignoff() reads the manager + employee signatures and dispute out of signoffs[]', () => {
    let result: IReviewSignoff | undefined;
    service.getSignoff('e-1').subscribe((r) => (result = r));

    flushActiveCycle();
    httpMock.expectOne(`${notesBase}/notes`).flush({
      ...mockNotesWire,
      signoffStatus: 'Disputed',
      signoffStatusName: 'Disputed',
      signoffs: [
        {
          action: 'RequestedSignOff',
          actionName: 'RequestedSignOff',
          party: 'Manager',
          partyName: 'Manager',
          signerName: 'Sam Lead',
          signedAt: '2026-06-10T09:00:00Z',
          clientIpAddress: '10.0.0.1',
        },
        {
          action: 'Disputed',
          actionName: 'Disputed',
          party: 'Employee',
          partyName: 'Employee',
          signerName: 'Alex Doe',
          signedAt: '2026-06-12T10:00:00Z',
          comments: 'I disagree with goal 1.',
        },
      ],
    });

    expect(result?.status).toBe('Disputed');
    expect(result?.managerSignature).toEqual({
      name: 'Sam Lead',
      signedOn: '2026-06-10T09:00:00Z',
      ipAddress: '10.0.0.1',
    });
    expect(result?.disputeComments).toBe('I disagree with goal 1.');
    expect(result?.disputedOn).toBe('2026-06-12T10:00:00Z');
    expect(result?.employeeSignature).toBeNull();
  });

  it('saveNotes() PUTs the notes mapped to the backend Body field', () => {
    service.saveNotes('e-1', { meetingNotesHtml: '<p>Updated</p>' }).subscribe();

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/notes`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ body: '<p>Updated</p>' });
    req.flush(mockNotesWire);
  });

  it('requestSignoff() POSTs the notes to request-signoff and maps the pending record', () => {
    let result: IReviewSignoff | undefined;
    service
      .requestSignoff('e-1', { meetingNotesHtml: '<p>Final notes</p>' })
      .subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/request-signoff`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: '<p>Final notes</p>' });
    req.flush({
      ...mockNotesWire,
      signoffStatus: 'PendingEmployeeSignOff',
      signoffStatusName: 'PendingEmployeeSignOff',
    });

    expect(result?.status).toBe('PendingEmployeeSignOff');
  });

  it('acknowledge() POSTs an empty body and maps the signed-off record', () => {
    let result: IReviewSignoff | undefined;
    service.acknowledge('e-1').subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/acknowledge`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({
      ...mockNotesWire,
      signoffStatus: 'SignedOff',
      signoffStatusName: 'SignedOff',
      signoffs: [
        {
          action: 'Acknowledged',
          actionName: 'Acknowledged',
          party: 'Employee',
          partyName: 'Employee',
          signerName: 'Alex Doe',
          signedAt: '2026-06-12T10:00:00Z',
        },
      ],
    });

    expect(result?.status).toBe('SignedOff');
    expect(result?.employeeSignature?.name).toBe('Alex Doe');
  });

  it('dispute() POSTs the comments and maps the disputed record', () => {
    const body = { comments: 'I disagree with the rating on goal 1.' };
    let result: IReviewSignoff | undefined;
    service.dispute('e-1', body).subscribe((r) => (result = r));

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/dispute`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({
      ...mockNotesWire,
      signoffStatus: 'Disputed',
      signoffStatusName: 'Disputed',
    });

    expect(result?.status).toBe('Disputed');
  });

  it('resolveDispute() POSTs Amend + comments to the resolve-dispute route', () => {
    service.resolveDispute('e-1', { resolution: 'Amend' }).subscribe();

    flushActiveCycle();
    const req = httpMock.expectOne(`${notesBase}/resolve-dispute`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ amend: true, comments: null });
    req.flush(mockNotesWire);
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
    req.flush(mockNotesWire);

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
      ...mockNotesWire,
      signoffStatus: 'SignedOff',
      signoffStatusName: 'SignedOff',
      signoffs: [
        {
          action: 'Acknowledged',
          actionName: 'Acknowledged',
          party: 'Employee',
          partyName: 'Employee',
          signerName: 'Alex Doe',
          signedAt: '2026-06-12T10:00:00Z',
        },
      ],
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
    req.flush({
      ...mockNotesWire,
      signoffStatus: 'Disputed',
      signoffStatusName: 'Disputed',
    });

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
