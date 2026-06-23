import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { Feedback360Service } from './feedback-360.service';
import { environment } from '../../../../environments/environment';
import {
  IReviewerConfig,
  IFeedbackForm,
  IFeedback360Results,
  ICompletionTracker,
  ISaveReviewersRequest,
  ISubmitFeedbackRequest,
} from '../models/feedback-360.models';

describe('Feedback360Service', () => {
  let service: Feedback360Service;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/feedback-360`;

  const mockConfig: IReviewerConfig = {
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    reviewers: [
      {
        reviewerId: 'e-1',
        name: 'Alex Doe',
        category: 'Self',
        locked: true,
      },
    ],
    suggestedPeers: [],
    suggestedDirectReports: [],
    candidatePool: [],
    minimums: [{ category: 'Peer', minimum: 2 }],
    anonymous: true,
    editable: true,
  };

  const mockForm: IFeedbackForm = {
    assignmentId: 'a-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    revieweeId: 'e-1',
    revieweeName: 'Alex Doe',
    revieweeJobTitle: 'Engineer',
    category: 'Peer',
    ratingScaleMax: 5,
    submitted: false,
    submittedOn: null,
    anonymous: true,
    questions: [
      {
        questionId: 'q-1',
        kind: 'Competency',
        title: 'Communication',
        rating: null,
        comment: '',
      },
    ],
  };

  const mockResults: IFeedback360Results = {
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    ratingScaleMax: 5,
    anonymous: true,
    released: true,
    compositeScore: 82,
    competencies: [],
    categoryAverages: [],
    comments: [],
    exportAvailable: true,
  };

  const mockTracker: ICompletionTracker = {
    employeeId: 'e-1',
    categories: [
      { category: 'Peer', submitted: 1, pending: 1, overdue: 0, minimum: 2 },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        Feedback360Service,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(Feedback360Service);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getReviewerConfig() GETs the config route with credentials', () => {
    let result: IReviewerConfig | undefined;
    service.getReviewerConfig('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/employees/e-1/config`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockConfig);

    expect(result).toEqual(mockConfig);
  });

  it('saveReviewers() PUTs the full-replace reviewer set', () => {
    const body: ISaveReviewersRequest = {
      reviewers: [{ reviewerId: 'e-2', category: 'Peer' }],
    };
    let result: IReviewerConfig | undefined;
    service.saveReviewers('e-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/employees/e-1/reviewers`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(mockConfig);

    expect(result).toEqual(mockConfig);
  });

  it('getTracker() GETs the tracker route', () => {
    let result: ICompletionTracker | undefined;
    service.getTracker('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/employees/e-1/tracker`);
    expect(req.request.method).toBe('GET');
    req.flush(mockTracker);

    expect(result).toEqual(mockTracker);
  });

  it('getFeedbackForm() GETs the assignment form route', () => {
    let result: IFeedbackForm | undefined;
    service.getFeedbackForm('a-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/assignments/a-1/form`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockForm);

    expect(result).toEqual(mockForm);
  });

  it('submitFeedback() POSTs the answers to the submit route', () => {
    const body: ISubmitFeedbackRequest = {
      answers: [{ questionId: 'q-1', rating: 4, comment: 'Strong work' }],
    };
    const submitted: IFeedbackForm = {
      ...mockForm,
      submitted: true,
      submittedOn: '2026-06-10T10:00:00Z',
    };
    let result: IFeedbackForm | undefined;
    service.submitFeedback('a-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/assignments/a-1/submit`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(submitted);

    expect(result?.submitted).toBeTrue();
  });

  it('getResults() GETs the results route', () => {
    let result: IFeedback360Results | undefined;
    service.getResults('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/employees/e-1/results`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockResults);

    expect(result?.compositeScore).toBe(82);
  });

  it('exportResultsPdf() GETs a blob with response observed', () => {
    let status: number | undefined;
    service.exportResultsPdf('e-1').subscribe((r) => (status = r.status));

    const req = httpMock.expectOne(`${baseUrl}/employees/e-1/results/export`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['pdf']), {
      headers: { 'Content-Disposition': 'attachment; filename="360.pdf"' },
    });

    expect(status).toBe(200);
  });
});
