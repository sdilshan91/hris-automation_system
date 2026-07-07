import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PerformanceGoalService } from './performance-goal.service';
import { environment } from '../../../../environments/environment';
import {
  IAppraisalCycle,
  IGoal,
  ISaveGoalsRequest,
  ITeamGoalStatus,
} from '../models/goal.models';

describe('PerformanceGoalService', () => {
  let service: PerformanceGoalService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance`;

  const mockCycle: IAppraisalCycle = {
    id: 'cyc-1',
    name: '2026 Annual',
    goalSettingOpensOn: '2026-01-01',
    goalSettingClosesOn: '2026-01-31',
    goalSettingOpen: true,
  };

  const mockGoal: IGoal = {
    id: 'g-1',
    title: 'Improve NPS',
    description: 'Lift NPS',
    category: 'KPI',
    weight: 100,
    targetValue: '85',
    measurementUnit: 'score',
    dueDate: '2026-06-30',
    parentGoalId: null,
  };

  const mockTeam: ITeamGoalStatus[] = [
    {
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
      jobTitle: 'Engineer',
      status: 'Draft',
      goalCount: 2,
      totalWeight: 60,
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PerformanceGoalService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PerformanceGoalService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getActiveCycle GETs the active cycle (bare payload)', () => {
    let result: IAppraisalCycle | undefined;
    service.getActiveCycle().subscribe((c) => (result = c));

    const req = httpMock.expectOne(`${baseUrl}/cycles/active`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockCycle);

    expect(result).toEqual(mockCycle);
  });

  it('getTeamStatus GETs the team list for a cycle', () => {
    let result: ITeamGoalStatus[] | undefined;
    service.getTeamStatus('cyc-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cycles/cyc-1/team-dashboard`);
    expect(req.request.method).toBe('GET');
    req.flush(mockTeam);

    expect(result).toEqual(mockTeam);
  });

  it('getTeamStatus tolerates a { data } envelope', () => {
    let result: ITeamGoalStatus[] | undefined;
    service.getTeamStatus('cyc-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cycles/cyc-1/team-dashboard`);
    req.flush({ data: mockTeam });

    expect(result).toEqual(mockTeam);
  });

  it('getEmployeeGoals GETs goals for an employee in a cycle', () => {
    let result: IGoal[] | undefined;
    service.getEmployeeGoals('cyc-1', 'e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      `${baseUrl}/employees/e-1/cycles/cyc-1/goals`,
    );
    expect(req.request.method).toBe('GET');
    req.flush([mockGoal]);

    expect(result).toEqual([mockGoal]);
  });

  it('saveGoals PUTs the full goal set and returns the persisted goals', () => {
    const request: ISaveGoalsRequest = {
      goals: [
        {
          title: 'Improve NPS',
          description: 'Lift NPS',
          category: 'KPI',
          weight: 100,
          targetValue: '85',
          measurementUnit: 'score',
          dueDate: '2026-06-30',
        },
      ],
    };
    let result: IGoal[] | undefined;
    service.saveGoals('cyc-1', 'e-1', request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      `${baseUrl}/employees/e-1/cycles/cyc-1/goals`,
    );
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush([mockGoal]);

    expect(result).toEqual([mockGoal]);
  });

  it('getEmployeeGoals returns [] for a null/empty payload', () => {
    let result: IGoal[] | undefined;
    service.getEmployeeGoals('cyc-1', 'e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      `${baseUrl}/employees/e-1/cycles/cyc-1/goals`,
    );
    req.flush(null);

    expect(result).toEqual([]);
  });
});
