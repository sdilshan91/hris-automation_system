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
  ManagerReviewWire,
  TeamReviewsDashboardWire,
} from '../models/manager-review.models';

describe('ManagerReviewService', () => {
  let service: ManagerReviewService;
  let httpMock: HttpTestingController;
  const perfBase = `${environment.apiBaseUrl}/tenant/performance`;
  const reviewsBase = `${perfBase}/reviews`;
  const activeCycleUrl = `${perfBase}/cycles/active`;

  // ── Real wire shapes (generated contract) ──────────────────────────────────
  // The dashboard endpoint returns PerformanceTeamReviewsDashboardDto — an OBJECT
  // with a `members` array, NOT a bare array. The per-employee review nests goals
  // under `items` and renames the descriptor/score fields.
  const mockTeamWire: TeamReviewsDashboardWire = {
    cycleId: 'cyc-1',
    members: [
      {
        employeeId: 'e-1',
        employeeName: 'Alex Doe',
        employeeNo: 'E-001',
        status: 'SelfAssessmentSubmitted',
        goalCount: 2,
        weightedSelfScore: 80,
        weightedManagerScore: null,
        finalScore: null,
      },
    ],
  };

  const mockReviewWire: ManagerReviewWire = {
    id: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    employeeNo: 'E-001',
    jobTitle: 'Engineer',
    status: 'Draft',
    statusName: 'Draft',
    isReviewWindowOpen: true,
    isLocked: false,
    selfAssessmentSubmitted: true,
    ratingScaleMax: 5,
    weightedSelfScore: 80,
    weightedManagerScore: null,
    finalScore: null,
    summaryComment: '',
    flag: 'None',
    flagName: 'None',
    submittedAt: null,
    selfWeightPercent: 30,
    managerWeightPercent: 70,
    items: [
      {
        goalId: 'g-1',
        goalTitle: 'Improve NPS',
        goalDescription: 'Lift NPS',
        goalWeight: 100,
        goalTargetValue: '85',
        goalMeasurementUnit: 'score',
        goalDueDate: '2026-06-30',
        selfRating: 4,
        selfComment: 'Hit the target.',
        managerRating: null,
        managerComment: '',
      },
    ],
  };

  // ── Expected mapped view-models ────────────────────────────────────────────
  const mockTeam: IManagerTeamRow[] = [
    {
      reviewId: null,
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
      jobTitle: null,
      status: 'SelfAssessmentSubmitted',
      goalCount: 2,
      selfSubmittedOn: null,
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

  it('getTeam() resolves the active cycle then maps the dashboard `members` to rows', () => {
    let result: IManagerTeamRow[] | undefined;
    service.getTeam().subscribe((r) => (result = r));

    const cycleReq = httpMock.expectOne(activeCycleUrl);
    expect(cycleReq.request.method).toBe('GET');
    cycleReq.flush({ id: 'cyc-1' });

    const req = httpMock.expectOne(`${reviewsBase}/cycles/cyc-1/team`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockTeamWire);

    expect(result).toEqual(mockTeam);
  });

  it('getTeam() unpacks the `members` array from the dashboard object (fails on the un-migrated toArray)', () => {
    // The un-migrated code typed this call as `IManagerTeamRow[] | { data }` and ran
    // `toArray`, which saw an object that was neither an array nor `{ data }` and
    // returned `[]` — the Team Reviews dashboard was permanently empty. A non-empty
    // result here proves the mapper reads `.members`.
    let result: IManagerTeamRow[] | undefined;
    service.getTeam().subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    httpMock.expectOne(`${reviewsBase}/cycles/cyc-1/team`).flush(mockTeamWire);

    expect(result?.length).toBe(1);
    expect(result?.[0].employeeName).toBe('Alex Doe');
    expect(result?.[0].goalCount).toBe(2);
    expect(result?.[0].status).toBe('SelfAssessmentSubmitted');
  });

  it('getEmployeeReview() resolves the active cycle then maps the per-employee review', () => {
    let result: IManagerReview | undefined;
    service.getEmployeeReview('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    const req = httpMock.expectOne(`${reviewsBase}/cycles/cyc-1/employees/e-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockReviewWire);

    expect(result).toEqual(mockReview);
  });

  it('getEmployeeReview() reads goals from `items` and the renamed descriptor/score fields (fails on the un-migrated pass-through)', () => {
    // The un-migrated code returned the raw payload, so `review.goals` was `undefined`
    // (the wire nests them under `items`) and `windowOpen`/`selfScore` were undefined
    // (the wire uses `isReviewWindowOpen`/`weightedSelfScore`).
    let result: IManagerReview | undefined;
    service.getEmployeeReview('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    httpMock
      .expectOne(`${reviewsBase}/cycles/cyc-1/employees/e-1`)
      .flush(mockReviewWire);

    expect(result?.goals.length).toBe(1);
    expect(result?.goals[0].title).toBe('Improve NPS');
    expect(result?.goals[0].weight).toBe(100);
    expect(result?.windowOpen).toBeTrue();
    expect(result?.selfScore).toBe(80);
  });

  it('getEmployeeReview() derives ManagerReviewSubmitted once the review is submitted', () => {
    let result: IManagerReview | undefined;
    service.getEmployeeReview('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    httpMock.expectOne(`${reviewsBase}/cycles/cyc-1/employees/e-1`).flush({
      ...mockReviewWire,
      status: 'Submitted',
      statusName: 'Submitted',
      submittedAt: '2026-06-10T10:00:00Z',
      weightedManagerScore: 90,
      finalScore: 87,
    });

    expect(result?.status).toBe('ManagerReviewSubmitted');
    expect(result?.managerScore).toBe(90);
    expect(result?.submittedOn).toBe('2026-06-10T10:00:00Z');
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
    req.flush(mockReviewWire);

    expect(result).toEqual(mockReview);
  });

  it('submit() POSTs the request body and maps the locked review response', () => {
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
      flag: 'Promotion',
    };
    const submittedWire: ManagerReviewWire = {
      ...mockReviewWire,
      status: 'Submitted',
      statusName: 'Submitted',
      weightedManagerScore: 90,
      finalScore: 87,
      submittedAt: '2026-06-10T10:00:00Z',
    };
    let result: IManagerReview | undefined;
    service.submit(body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${reviewsBase}/submit`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(submittedWire);

    expect(result?.status).toBe('ManagerReviewSubmitted');
    expect(result?.finalScore).toBe(87);
  });
});
