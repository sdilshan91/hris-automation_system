import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { ManagerReviewService } from './manager-review.service';
import { environment } from '../../../../environments/environment';
import {
  IManagerReview,
  IManagerTeamRow,
  ISaveManagerReviewRequest,
} from '../models/manager-review.models';

describe('ManagerReviewService', () => {
  let service: ManagerReviewService;
  let httpMock: HttpTestingController;
  const perfBase = `${environment.apiBaseUrl}/tenant/performance`;
  const reviewsBase = `${perfBase}/reviews`;
  const activeCycleUrl = `${perfBase}/cycles/active`;

  const mockTeam: IManagerTeamRow[] = [
    {
      reviewId: 'rv-1',
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
      jobTitle: 'Engineer',
      status: 'SelfAssessmentSubmitted',
      goalCount: 2,
      selfSubmittedOn: '2026-06-01T10:00:00Z',
    },
  ];

  const mockReview: IManagerReview = {
    id: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    status: 'SelfAssessmentSubmitted',
    windowOpen: true,
    ratingScaleMax: 5,
    selfScore: 80,
    managerScore: null,
    finalScore: null,
    summaryComment: '',
    flag: 'None',
    submittedOn: null,
    goals: [
      {
        goalId: 'g-1',
        title: 'Improve NPS',
        description: 'Lift NPS',
        weight: 100,
        targetValue: '85',
        measurementUnit: 'score',
        dueDate: '2026-06-30',
        selfRating: 4,
        selfComment: 'Hit the target.',
        managerRating: null,
        managerComment: '',
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ManagerReviewService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ManagerReviewService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getTeam() resolves the active cycle then GETs the team and returns the array', () => {
    let result: IManagerTeamRow[] | undefined;
    service.getTeam().subscribe((r) => (result = r));

    const cycleReq = httpMock.expectOne(activeCycleUrl);
    expect(cycleReq.request.method).toBe('GET');
    cycleReq.flush({ id: 'cyc-1' });

    const req = httpMock.expectOne(`${reviewsBase}/cycles/cyc-1/team`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockTeam);

    expect(result).toEqual(mockTeam);
  });

  it('getTeam() unwraps a { data } paged envelope to an array', () => {
    let result: IManagerTeamRow[] | undefined;
    service.getTeam().subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    const req = httpMock.expectOne(`${reviewsBase}/cycles/cyc-1/team`);
    req.flush({ data: mockTeam });

    expect(result).toEqual(mockTeam);
  });

  it('getEmployeeReview() resolves the active cycle then GETs the per-employee review', () => {
    let result: IManagerReview | undefined;
    service.getEmployeeReview('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    const req = httpMock.expectOne(`${reviewsBase}/cycles/cyc-1/employees/e-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockReview);

    expect(result).toEqual(mockReview);
  });

  it('saveDraft() PUTs the request body (cycleId + employeeId + items) to the draft route', () => {
    const body: ISaveManagerReviewRequest = {
      cycleId: 'cyc-1',
      employeeId: 'e-1',
      items: [{ goalId: 'g-1', managerRating: 4, managerComment: 'Solid work' }],
      summaryComment: 'Overall good',
      flag: 'Recognition',
    };
    let result: IManagerReview | undefined;
    service.saveDraft(body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${reviewsBase}/draft`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(mockReview);

    expect(result).toEqual(mockReview);
  });

  it('submit() POSTs the request body to the submit route', () => {
    const body: ISaveManagerReviewRequest = {
      cycleId: 'cyc-1',
      employeeId: 'e-1',
      items: [
        {
          goalId: 'g-1',
          managerRating: 5,
          managerComment: 'Exceeded expectations clearly',
        },
      ],
      summaryComment: 'Promote next cycle',
      flag: 'PromotionConsideration',
    };
    const submitted: IManagerReview = {
      ...mockReview,
      status: 'ManagerReviewSubmitted',
      managerScore: 90,
      finalScore: 87,
      submittedOn: '2026-06-10T10:00:00Z',
    };
    let result: IManagerReview | undefined;
    service.submit(body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${reviewsBase}/submit`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(submitted);

    expect(result?.status).toBe('ManagerReviewSubmitted');
    expect(result?.finalScore).toBe(87);
  });
});
