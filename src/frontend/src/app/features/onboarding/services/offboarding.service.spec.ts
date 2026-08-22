import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';

import { OffboardingService } from './offboarding.service';
import { environment } from '../../../../environments/environment';
import type { Schema } from '@core/api';
import {
  IOffboardingInstance,
  IInitiateOffboardingRequest,
} from '../models/offboarding.models';

type InstanceWire = Schema<'OnboardingOffboardingInstanceDto'>;
type CompleteResultWire = Schema<'OnboardingCompleteOffboardingResultDto'>;

describe('OffboardingService', () => {
  let service: OffboardingService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/offboarding`;

  // The flushed body is the WIRE payload, not the view-model. It used to be the view-model, which meant
  // the suite could never have caught the field renames the service is now responsible for translating:
  // it asserted that what it invented came back unchanged. `Schema<>` makes the fixture the contract.
  const instance = (over: Partial<InstanceWire> = {}): InstanceWire => ({
    id: 'off-1',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    lastWorkingDay: '2026-07-31',
    reasonName: 'Resignation',
    statusName: 'InProgress',
    progressPercent: 0,
    clearanceSummary: {
      fullyCleared: false,
      totalDepartments: 1,
      clearedDepartments: 0,
      pendingDepartments: 1,
    },
    departments: [
      {
        clearanceCategoryName: 'IT',
        status: 'pending',
        tasks: [
          {
            id: 't-1',
            title: 'Return laptop',
            responsibleRoleName: 'IT',
            dueDate: '2026-07-30',
            statusName: 'Pending',
            isMandatory: true,
            clearanceStatus: null,
            remarks: null,
            linkedAssetId: 'a-1',
          },
        ],
      },
    ],
    pendingMandatoryItems: [
      { taskId: 't-1', title: 'Return laptop', clearanceCategoryName: 'IT', reason: 'not_completed' },
    ],
    canComplete: false,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        OffboardingService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(OffboardingService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── initiate (AC-1 / FR-8) ────────────────────────────────

  it('initiate POSTs the request to /initiate with credentials', () => {
    const request: IInitiateOffboardingRequest = {
      employeeId: 'emp-1',
      lastWorkingDay: '2026-07-31',
      offboardingTemplateId: null,
      reason: 'Resignation',
      notes: 'Smooth handover',
    };
    let result: IOffboardingInstance | undefined;
    service.initiate(request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/initiate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual(request);
    req.flush(instance());

    expect(result!.id).toBe('off-1');
  });

  it('initiate surfaces a BR-1 invalid-status 4xx to the subscriber', () => {
    let errored: HttpErrorResponse | undefined;
    service
      .initiate({
        employeeId: 'emp-1',
        lastWorkingDay: '2026-07-31',
        reason: 'Termination',
      })
      .subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(`${base}/initiate`);
    req.flush(
      { message: 'Employee is not in a resignation/termination state.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(errored).toBeTruthy();
    expect(OffboardingService.parseErrorMessage(errored!)).toContain('resignation/termination');
  });

  // ─── getById / getByEmployee ───────────────────────────────

  it('getById GETs /{id} with credentials', () => {
    let result: IOffboardingInstance | undefined;
    service.getById('off-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/off-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(instance());

    expect(result!.departments.length).toBe(1);
    // The service MAPS; it does not hand the wire body through. These four assertions each name a field
    // whose wire spelling differs from the view-model's — the renames a passthrough would leave undefined.
    expect(result!.departments[0].department).toBe('IT');
    expect(result!.departments[0].tasks[0].responsibleRole).toBe('IT');
    expect(result!.pendingMandatory[0].department).toBe('IT');
    expect(result!.canComplete).toBeFalse();
  });

  it('getByEmployee GETs the collection filtered by employeeId', () => {
    service.getByEmployee('emp-1').subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === base && r.params.get('employeeId') === 'emp-1',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(instance());
  });

  // ─── recordClearance (AC-3) ────────────────────────────────

  it('recordClearance POSTs the decision to /tasks/{id}/clearance', () => {
    let result: IOffboardingInstance | undefined;
    service
      .recordClearance('t-1', { status: 'approved', remarks: 'OK' })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/tasks/t-1/clearance`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ status: 'approved', remarks: 'OK' });
    expect(req.request.withCredentials).toBeTrue();
    req.flush(
      instance({
        clearanceSummary: {
          fullyCleared: true,
          totalDepartments: 1,
          clearedDepartments: 1,
          pendingDepartments: 0,
        },
        progressPercent: 100,
      }),
    );

    expect(result!.overallClearance)
      .withContext('the wire sends a fullyCleared flag; the traffic-light token is derived here')
      .toBe('cleared');
  });

  // ─── returnAsset (AC-2) ────────────────────────────────────

  it('returnAsset POSTs the asset payload to /tasks/{id}/return-asset', () => {
    service
      .returnAsset('t-1', { assetId: 'a-1', condition: 'Good', disposed: false })
      .subscribe();

    const req = httpMock.expectOne(`${base}/tasks/t-1/return-asset`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ assetId: 'a-1', condition: 'Good', disposed: false });
    expect(req.request.withCredentials).toBeTrue();
    req.flush(instance());
  });

  // ─── complete (AC-4 / AC-5) ────────────────────────────────

  it('complete POSTs to /{id}/complete with an empty body', () => {
    let result: IOffboardingInstance | undefined;
    service.complete('off-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/off-1/complete`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    expect(req.request.withCredentials).toBeTrue();
    // THE FIXTURE THAT USED TO LIE. This endpoint does NOT return the instance — it returns
    // CompleteOffboardingResultDto, with the instance NESTED. The old spec flushed a bare instance, so it
    // asserted the service handled a body the API has never sent, and the real one (which the service
    // mapped straight through `mapOffboardingInstance`, producing an all-default blank) went untested.
    const completed: CompleteResultWire = {
      completed: true,
      finalSettlementRef: '11111111-1111-1111-1111-111111111111',
      pendingItems: [],
      instance: instance({
        statusName: 'Completed',
        pendingMandatoryItems: [],
        canComplete: false,
      }),
    };
    req.flush(completed);

    expect(result!.status)
      .withContext('the instance is nested under `instance`; reading the result object itself yields blanks')
      .toBe('Completed');
    expect(result!.id).toBe('off-1');
    expect(result!.departments.length)
      .withContext('a blanked instance would render an empty dashboard after a SUCCESSFUL completion')
      .toBe(1);
  });

  it('complete surfaces a 409 pending-mandatory block to the subscriber (AC-5)', () => {
    let errored: HttpErrorResponse | undefined;
    service.complete('off-1').subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(`${base}/off-1/complete`);
    // The REAL 409 body: the standard failure envelope carrying the result DTO. It is NOT unwrapped by
    // apiEnvelopeInterceptor (that only rewrites 2xx), so `data` has to be stepped through. The old
    // fixture invented a flat `{ pending: [titles] }` that the API has never sent — which is why
    // parseCompleteBlocked returned null on every real block and the user saw a generic error instead of
    // the list AC-5 promises.
    req.flush(
      {
        success: false,
        code: 'pending_mandatory_items',
        message: 'Cannot complete offboarding. The following mandatory items are pending.',
        data: {
          completed: false,
          pendingItems: [
            { taskId: 't-1', title: 'Return laptop', clearanceCategoryName: 'IT', reason: 'not_completed' },
            {
              taskId: 't-2',
              title: 'Finance clearance',
              clearanceCategoryName: 'Finance',
              reason: 'clearance_not_approved',
            },
          ],
        },
      },
      { status: 409, statusText: 'Conflict' },
    );

    expect(errored).toBeTruthy();
    const pending = OffboardingService.parseCompleteBlocked(errored!);
    expect(pending).toEqual([
      { taskId: 't-1', title: 'Return laptop', department: 'IT', reason: 'not_completed' },
      {
        taskId: 't-2',
        title: 'Finance clearance',
        department: 'Finance',
        reason: 'clearance_not_approved',
      },
    ]);
  });

  it('returns null for a 409 that is not the pending-mandatory block', () => {
    const other = new HttpErrorResponse({
      error: { success: false, code: 'offboarding_completed', message: 'Already completed.' },
      status: 409,
    });
    expect(OffboardingService.parseCompleteBlocked(other))
      .withContext('a different 409 must fall back to the generic message, not claim an empty block list')
      .toBeNull();
  });

  // ─── helpers ───────────────────────────────────────────────

  it('parseCompleteBlocked returns null for a non-409 or unstructured error', () => {
    const notConflict = new HttpErrorResponse({ error: { pending: ['x'] }, status: 400 });
    expect(OffboardingService.parseCompleteBlocked(notConflict)).toBeNull();
    const noList = new HttpErrorResponse({ error: { message: 'boom' }, status: 409 });
    expect(OffboardingService.parseCompleteBlocked(noList)).toBeNull();
  });

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({ error: { message: 'Boom' }, status: 400 });
    expect(OffboardingService.parseErrorMessage(withMsg)).toBe('Boom');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(OffboardingService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
