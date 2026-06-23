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
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/sign-off`;

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

  it('getSignoff() GETs the record with credentials', () => {
    let result: IReviewSignoff | undefined;
    service.getSignoff('rv-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/reviews/rv-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockRecord);

    expect(result).toEqual(mockRecord);
  });

  it('saveNotes() PUTs the notes body to the notes route', () => {
    const body = { meetingNotesHtml: '<p>Updated</p>' };
    service.saveNotes('rv-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/reviews/rv-1/notes`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(mockRecord);
  });

  it('requestSignoff() POSTs the notes and returns the pending record', () => {
    const body = { meetingNotesHtml: '<p>Final notes</p>' };
    let result: IReviewSignoff | undefined;
    service.requestSignoff('rv-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/reviews/rv-1/request`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ ...mockRecord, status: 'PendingEmployeeSignOff' });

    expect(result?.status).toBe('PendingEmployeeSignOff');
  });

  it('acknowledge() POSTs an empty body to the acknowledge route', () => {
    let result: IReviewSignoff | undefined;
    service.acknowledge('rv-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/reviews/rv-1/acknowledge`);
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
    service.dispute('rv-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/reviews/rv-1/dispute`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({
      ...mockRecord,
      status: 'Disputed',
      disputeComments: body.comments,
    });

    expect(result?.status).toBe('Disputed');
  });

  it('resolveDispute() POSTs the resolution to the resolve route', () => {
    service.resolveDispute('rv-1', { resolution: 'Amend' }).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/reviews/rv-1/resolve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ resolution: 'Amend' });
    req.flush({ ...mockRecord, status: 'NotesDraft' });
  });

  it('exportPdf() GETs a blob with credentials and the full response', () => {
    let status: number | undefined;
    service.exportPdf('rv-1').subscribe((resp) => (status = resp.status));

    const req = httpMock.expectOne(`${baseUrl}/reviews/rv-1/export`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(new Blob(['pdf']), { status: 200, statusText: 'OK' });

    expect(status).toBe(200);
  });
});
