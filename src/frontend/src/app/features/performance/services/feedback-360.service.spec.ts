import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { Feedback360Service } from './feedback-360.service';
import { environment } from '../../../../environments/environment';
import {
  CompletionTrackerWire,
  FeedbackFormWire,
  IReviewerConfig,
  IFeedbackForm,
  IFeedback360Results,
  IFeedback360ResultsRaw,
  IFeedback360ReleaseResult,
  ICompletionTracker,
  ISaveReviewersRequest,
  ISubmitFeedbackRequest,
  ReviewerConfigurationWire,
} from '../models/feedback-360.models';

describe('Feedback360Service', () => {
  let service: Feedback360Service;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/feedback-360`;
  const perfBase = `${environment.apiBaseUrl}/tenant/performance`;
  const activeCycleUrl = `${perfBase}/cycles/active`;

  // Real wire (PerformanceReviewerConfigurationDto): `assignments` (not `reviewers`),
  // suggested reviewers as bare {id,no,name}, a scalar `minPeerReviewers`, and NO
  // candidatePool / per-category minimums / cycleName / editable flag.
  const mockConfigWire: ReviewerConfigurationWire = {
    cycleId: 'cyc-1',
    revieweeEmployeeId: 'e-1',
    revieweeName: 'Alex Doe',
    is360Enabled: true,
    isAnonymousFeedback: true,
    minPeerReviewers: 2,
    assignments: [
      {
        id: 'as-1',
        cycleId: 'cyc-1',
        revieweeEmployeeId: 'e-1',
        reviewerEmployeeId: 'e-1',
        reviewerName: 'Alex Doe',
        category: 'Self',
        categoryName: 'Self',
        status: 'Pending',
        statusName: 'Pending',
      },
    ],
    suggestedPeers: [],
    suggestedDirectReports: [],
  };

  // Expected mapped view-model (the backend-gap fields are defaulted).
  const mockConfig: IReviewerConfig = {
    cycleId: 'cyc-1',
    cycleName: '',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    reviewers: [
      {
        reviewerId: 'e-1',
        name: 'Alex Doe',
        jobTitle: null,
        departmentName: null,
        category: 'Self',
        avatarUrl: null,
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

  const mockFormWire: FeedbackFormWire = {
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
        description: null,
        rating: null,
        comment: '',
      },
    ],
  };

  // The REAL backend Feedback360ResultsDto wire shape — the adapter under test maps this
  // into the FE IFeedback360Results. Deliberately exercises every drifted field name
  // (revieweeName, competencyAverages, categoryAverages[].averageRating, entries) plus
  // the anonymity null-out, so a regression in mapFeedback360Results fails here.
  const mockRawResults: IFeedback360ResultsRaw = {
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    revieweeEmployeeId: 'e-1',
    revieweeName: 'Alex Doe',
    jobTitle: 'Engineer',
    isAnonymousFeedback: true,
    ratingScaleMax: 5,
    compositeScore: 82,
    competencyAverages: [
      {
        goalId: null,
        competencyKey: 'communication',
        label: 'Communication',
        averageRating: 4.2,
        responseCount: 5,
      },
    ],
    categoryAverages: [
      {
        category: 'Peer',
        categoryName: 'Peers',
        averageRating: 4.1,
        responseCount: 3,
        weight: 40,
      },
    ],
    entries: [
      {
        feedbackId: 'f-1',
        category: 'Peer',
        categoryName: 'Peers',
        isAnonymous: true,
        reviewerEmployeeId: null,
        reviewerName: null,
        overallComment: 'Strong communicator overall.',
        submittedAt: '2026-06-10T10:00:00Z',
        items: [
          {
            goalId: null,
            competencyKey: 'communication',
            label: 'Communication',
            rating: 4,
            comment: 'Clear in standups.',
          },
          {
            goalId: null,
            competencyKey: 'delivery',
            label: 'Delivery',
            rating: 5,
            comment: null,
          },
        ],
      },
    ],
    minPeerReviewers: 3,
    peerResponseCount: 3,
    minPeerThresholdMet: true,
    releaseWarning: null,
    released: true,
    releasedAt: '2026-06-11T09:00:00Z',
    exportAvailable: true,
  };

  const mockTrackerWire: CompletionTrackerWire = {
    employeeId: 'e-1',
    categories: [
      { category: 'Peer', submitted: 1, pending: 1, overdue: 0, minimum: 2 },
    ],
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
    req.flush(mockConfigWire);

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
    req.flush(mockConfigWire);

    expect(result).toEqual(mockConfig);
  });

  it('getTracker() GETs the tracker route', () => {
    let result: ICompletionTracker | undefined;
    service.getTracker('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/employees/e-1/tracker`);
    expect(req.request.method).toBe('GET');
    req.flush(mockTrackerWire);

    expect(result).toEqual(mockTracker);
  });

  it('getFeedbackForm() GETs the assignment form route', () => {
    let result: IFeedbackForm | undefined;
    service.getFeedbackForm('a-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/assignments/a-1/form`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockFormWire);

    expect(result).toEqual(mockForm);
  });

  it('submitFeedback() POSTs the answers to the submit route', () => {
    const body: ISubmitFeedbackRequest = {
      items: [{ questionId: 'q-1', rating: 4, comment: 'Strong work' }],
    };
    const submitted: FeedbackFormWire = {
      ...mockFormWire,
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

  it('getResults() resolves the active cycle then GETs the cycle-keyed results route', () => {
    let result: IFeedback360Results | undefined;
    service.getResults('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    const req = httpMock.expectOne(
      `${perfBase}/360/cycles/cyc-1/employees/e-1/results`,
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockRawResults);

    expect(result?.compositeScore).toBe(82);
  });

  it('getResults() ADAPTS the raw backend payload into the FE vocabulary (S-1)', () => {
    let result: IFeedback360Results | undefined;
    service.getResults('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    httpMock
      .expectOne(`${perfBase}/360/cycles/cyc-1/employees/e-1/results`)
      .flush(mockRawResults);

    // renamed identity fields
    expect(result?.employeeId).toBe('e-1'); // ← revieweeEmployeeId
    expect(result?.employeeName).toBe('Alex Doe'); // ← revieweeName
    expect(result?.anonymous).toBeTrue(); // ← isAnonymousFeedback
    expect(result?.jobTitle).toBe('Engineer');
    // competencyAverages → competencies (flat, byCategory always [])
    expect(result?.competencies.length).toBe(1);
    expect(result?.competencies[0].title).toBe('Communication');
    expect(result?.competencies[0].overallAverage).toBe(4.2);
    expect(result?.competencies[0].kind).toBe('Competency');
    expect(result?.competencies[0].byCategory).toEqual([]);
    // categoryAverages[].averageRating → .average
    expect(result?.categoryAverages[0].category).toBe('Peer');
    expect(result?.categoryAverages[0].average).toBe(4.1);
    // release-gate + export fields carried straight through
    expect(result?.released).toBeTrue();
    expect(result?.releasedAt).toBe('2026-06-11T09:00:00Z');
    expect(result?.exportAvailable).toBeTrue();
    expect(result?.minPeerReviewers).toBe(3);
    expect(result?.peerResponseCount).toBe(3);
    expect(result?.minPeerThresholdMet).toBeTrue();
  });

  it('getResults() flattens entries into comments and NEVER reconstructs an anonymized identity (FR-5/NFR-3)', () => {
    let result: IFeedback360Results | undefined;
    service.getResults('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    httpMock
      .expectOne(`${perfBase}/360/cycles/cyc-1/employees/e-1/results`)
      .flush(mockRawResults);

    // overall comment + the one item that HAS a comment (the null-comment item drops)
    expect(result?.comments.length).toBe(2);
    const overall = result?.comments.find((c) => c.questionTitle === '');
    expect(overall?.comment).toBe('Strong communicator overall.');
    const item = result?.comments.find(
      (c) => c.questionTitle === 'Communication',
    );
    expect(item?.comment).toBe('Clear in standups.');
    // reviewerName was NULL on the wire (anonymity) → stays undefined, never invented
    expect(result?.comments.every((c) => c.reviewerName == null)).toBeTrue();
  });

  it('releaseResults() resolves the cycle then POSTs the release route with credentials', () => {
    const release: IFeedback360ReleaseResult = {
      cycleId: 'cyc-1',
      revieweeEmployeeId: 'e-1',
      releasedAt: '2026-06-11T09:00:00Z',
      releasedByEmployeeId: 'mgr-1',
    };
    let result: IFeedback360ReleaseResult | undefined;
    service.releaseResults('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    const req = httpMock.expectOne(
      `${perfBase}/360/cycles/cyc-1/employees/e-1/release`,
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(release);

    expect(result?.releasedByEmployeeId).toBe('mgr-1');
  });

  it('exportResultsPdf() resolves the cycle then GETs the report blob', () => {
    let status: number | undefined;
    service.exportResultsPdf('e-1').subscribe((r) => (status = r.status));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    const req = httpMock.expectOne(
      `${perfBase}/360/cycles/cyc-1/employees/e-1/report`,
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    req.flush(new Blob(['pdf']), {
      headers: { 'Content-Disposition': 'attachment; filename="360.pdf"' },
    });

    expect(status).toBe(200);
  });
});
