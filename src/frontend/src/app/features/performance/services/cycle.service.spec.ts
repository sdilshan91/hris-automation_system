import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { CycleService } from './cycle.service';
import { environment } from '../../../../environments/environment';
import {
  ICloneCycleRequest,
  ICycle,
  ICycleDashboard,
  ICycleSummary,
  ICycleTransitionRequest,
  ISaveCycleRequest,
} from '../models/cycle.models';

describe('CycleService', () => {
  let service: CycleService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/cycles`;

  const summaries: ICycleSummary[] = [
    {
      id: 'cyc-1',
      name: '2026 Annual',
      type: 'Annual',
      status: 'Active',
      startDate: '2026-01-01',
      endDate: '2026-12-31',
      participantCount: 120,
    },
  ];

  const cycle: ICycle = {
    id: 'cyc-1',
    name: '2026 Annual',
    type: 'Annual',
    status: 'Draft',
    startDate: '2026-01-01',
    endDate: '2026-12-31',
    phases: [
      { phaseType: 'GoalSetting', startDate: '2026-01-05', endDate: '2026-01-20' },
    ],
    scope: { scopeType: 'AllEmployees', departmentIds: [], employeeIds: [] },
    ratingScaleMax: 5,
    selfWeightPercent: 40,
    is360Enabled: false,
    isCalibrationEnabled: false,
    participantCount: 120,
    cancelledReason: null,
  };

  const dashboard: ICycleDashboard = {
    cycleId: 'cyc-1',
    name: '2026 Annual',
    status: 'Active',
    participantCount: 120,
    phases: [
      {
        phaseType: 'GoalSetting',
        startDate: '2026-01-05',
        endDate: '2026-01-20',
        completedCount: 80,
        totalParticipants: 120,
        overdueCount: 5,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        CycleService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(CycleService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list() GETs all cycles and returns the array', () => {
    let result: ICycleSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(summaries);

    expect(result).toEqual(summaries);
  });

  it('list() unwraps a { data } paged envelope', () => {
    let result: ICycleSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    req.flush({ data: summaries });

    expect(result).toEqual(summaries);
  });

  it('get() GETs the full cycle detail', () => {
    let result: ICycle | undefined;
    service.get('cyc-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cyc-1`);
    expect(req.request.method).toBe('GET');
    req.flush(cycle);

    expect(result).toEqual(cycle);
  });

  it('create() POSTs the save request', () => {
    const body: ISaveCycleRequest = {
      name: 'New cycle',
      type: 'Quarterly',
      startDate: '2026-01-01',
      endDate: '2026-03-31',
      phases: [
        { phaseType: 'GoalSetting', startDate: '2026-01-05', endDate: '2026-01-20' },
      ],
      scope: {
        scopeType: 'AllEmployees',
        departmentIds: [],
        employeeIds: [],
      },
      ratingScaleMax: 5,
      selfWeightPercent: 40,
      is360Enabled: false,
      isCalibrationEnabled: false,
    };
    let result: ICycle | undefined;
    service.create(body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    // BUG-257 regression: the corrected field names reach the wire verbatim.
    expect(req.request.body.selfWeightPercent).toBe(40);
    expect(req.request.body.is360Enabled).toBeFalse();
    expect(req.request.body.isCalibrationEnabled).toBeFalse();
    expect(req.request.body.phases[0].phaseType).toBe('GoalSetting');
    expect(req.request.body.scope.scopeType).toBe('AllEmployees');
    expect('gradeIds' in req.request.body.scope).toBeFalse();
    req.flush(cycle);

    expect(result).toEqual(cycle);
  });

  it('update() PUTs the save request to the cycle route', () => {
    const body = { ...cycle } as unknown as ISaveCycleRequest;
    service.update('cyc-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/cyc-1`);
    expect(req.request.method).toBe('PUT');
    req.flush(cycle);
  });

  it('dashboard() GETs the dashboard stats', () => {
    let result: ICycleDashboard | undefined;
    service.dashboard('cyc-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cyc-1/dashboard`);
    expect(req.request.method).toBe('GET');
    req.flush(dashboard);

    expect(result).toEqual(dashboard);
  });

  it('transition() POSTs the action (and reason for cancel)', () => {
    const body: ICycleTransitionRequest = {
      action: 'Cancel',
      reason: 'Reorg',
    };
    service.transition('cyc-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/cyc-1/status`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ ...cycle, status: 'Cancelled', cancelledReason: 'Reorg' });
  });

  it('clone() POSTs the clone request', () => {
    const body: ICloneCycleRequest = {
      name: '2027 Annual',
      startDate: '2027-01-01',
      endDate: '2027-12-31',
    };
    service.clone('cyc-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/clone`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ sourceCycleId: 'cyc-1', ...body });
    req.flush({ ...cycle, id: 'cyc-2' });
  });
});
