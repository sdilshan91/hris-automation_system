import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { CycleService } from './cycle.service';
import { environment } from '../../../../environments/environment';
import {
  CycleDashboardWire,
  CycleSummaryWire,
  CycleWire,
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

  // ─── WIRE fixtures (the real PerformanceCycle*Dto shapes the API sends) ──────
  const summaryWire: CycleSummaryWire = {
    id: 'cyc-1',
    name: '2026 Annual',
    type: 'Annual',
    typeName: 'Annual',
    status: 'Active',
    statusName: 'Active',
    startDate: '2026-01-01',
    endDate: '2026-12-31',
    participantCount: 120,
  };

  const cycleWire: CycleWire = {
    id: 'cyc-1',
    name: '2026 Annual',
    type: 'Annual',
    typeName: 'Annual',
    status: 'Draft',
    statusName: 'Draft',
    startDate: '2026-01-01',
    endDate: '2026-12-31',
    phases: [
      {
        phaseType: 'GoalSetting',
        phaseTypeName: 'GoalSetting',
        startDate: '2026-01-05',
        endDate: '2026-01-20',
        sequence: 1,
        isCurrent: false,
      },
    ],
    scope: {
      scopeType: 'AllEmployees',
      departmentIds: [],
      employeeIds: [],
    },
    ratingScaleMax: 5,
    selfWeightPercent: 40,
    managerWeightPercent: 60,
    is360Enabled: false,
    isCalibrationEnabled: false,
    participantCount: 120,
    // The API field is `cancellationReason` — the FE view-model exposes it as
    // `cancelledReason`. The mapper is the single place that bridges the rename.
    cancellationReason: null,
  };

  const dashboardWire: CycleDashboardWire = {
    cycleId: 'cyc-1',
    name: '2026 Annual',
    status: 'Active',
    statusName: 'Active',
    participantCount: 120,
    phases: [
      {
        phaseType: 'GoalSetting',
        phaseTypeName: 'GoalSetting',
        startDate: '2026-01-05',
        endDate: '2026-01-20',
        completedCount: 80,
        totalParticipants: 120,
        overdueCount: 5,
        completionPercent: 67,
        isCurrent: true,
      },
    ],
  };

  // ─── Expected VIEW-MODELS (what the mappers produce) ────────────────────────
  const expectedSummary: ICycleSummary = {
    id: 'cyc-1',
    name: '2026 Annual',
    type: 'Annual',
    status: 'Active',
    startDate: '2026-01-01',
    endDate: '2026-12-31',
    participantCount: 120,
  };

  const expectedCycle: ICycle = {
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

  const expectedDashboard: ICycleDashboard = {
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

  it('list() GETs all cycles and maps the wire rows to the view-model', () => {
    let result: ICycleSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([summaryWire]);

    expect(result).toEqual([expectedSummary]);
  });

  it('list() unwraps a { data } paged envelope', () => {
    let result: ICycleSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    req.flush({ data: [summaryWire] });

    expect(result).toEqual([expectedSummary]);
  });

  it('get() GETs the full cycle detail and maps it', () => {
    let result: ICycle | undefined;
    service.get('cyc-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cyc-1`);
    expect(req.request.method).toBe('GET');
    req.flush(cycleWire);

    expect(result).toEqual(expectedCycle);
  });

  it('get() maps the wire `cancellationReason` onto `cancelledReason` (rename bug)', () => {
    // MUTATION ARM: fails against the un-migrated code, which cast the raw body to
    // ICycle so `cancelledReason` was always undefined (the cancelled banner blank).
    let result: ICycle | undefined;
    service.get('cyc-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cyc-1`);
    req.flush({
      ...cycleWire,
      status: 'Cancelled',
      statusName: 'Cancelled',
      cancellationReason: 'Company reorganisation',
    });

    expect(result?.status).toBe('Cancelled');
    expect(result?.cancelledReason).toBe('Company reorganisation');
  });

  it('create() POSTs the save request and maps the response', () => {
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
    req.flush(cycleWire);

    expect(result).toEqual(expectedCycle);
  });

  it('update() PUTs the save request to the cycle route', () => {
    const body = { ...expectedCycle } as unknown as ISaveCycleRequest;
    let result: ICycle | undefined;
    service.update('cyc-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cyc-1`);
    expect(req.request.method).toBe('PUT');
    req.flush(cycleWire);

    expect(result).toEqual(expectedCycle);
  });

  it('dashboard() GETs the dashboard stats and maps the phase rows', () => {
    let result: ICycleDashboard | undefined;
    service.dashboard('cyc-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cyc-1/dashboard`);
    expect(req.request.method).toBe('GET');
    req.flush(dashboardWire);

    expect(result).toEqual(expectedDashboard);
  });

  it('transition() POSTs the action (and reason for cancel) and maps the result', () => {
    const body: ICycleTransitionRequest = {
      action: 'Cancel',
      reason: 'Reorg',
    };
    let result: ICycle | undefined;
    service.transition('cyc-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/cyc-1/status`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({
      ...cycleWire,
      status: 'Cancelled',
      statusName: 'Cancelled',
      cancellationReason: 'Reorg',
    });

    expect(result?.status).toBe('Cancelled');
    expect(result?.cancelledReason).toBe('Reorg');
  });

  it('clone() POSTs the clone request and maps the response', () => {
    const body: ICloneCycleRequest = {
      name: '2027 Annual',
      startDate: '2027-01-01',
      endDate: '2027-12-31',
    };
    let result: ICycle | undefined;
    service.clone('cyc-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/clone`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ sourceCycleId: 'cyc-1', ...body });
    req.flush({ ...cycleWire, id: 'cyc-2' });

    expect(result?.id).toBe('cyc-2');
  });
});
