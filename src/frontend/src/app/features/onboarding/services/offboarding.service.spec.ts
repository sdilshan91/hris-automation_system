import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';

import { OffboardingService } from './offboarding.service';
import { environment } from '../../../../environments/environment';
import {
  IOffboardingInstance,
  IInitiateOffboardingRequest,
} from '../models/offboarding.models';

describe('OffboardingService', () => {
  let service: OffboardingService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/offboarding`;

  const instance = (over: Partial<IOffboardingInstance> = {}): IOffboardingInstance => ({
    offboardingId: 'off-1',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    lastWorkingDay: '2026-07-31',
    reason: 'Resignation',
    status: 'in_progress',
    overallClearance: 'pending',
    progressPercent: 0,
    departments: [
      {
        department: 'IT',
        clearanceStatus: 'pending',
        tasks: [
          {
            taskId: 't-1',
            title: 'Return laptop',
            responsibleRole: 'IT',
            dueDate: '2026-07-30',
            status: 'pending',
            isMandatory: true,
            clearanceStatus: 'pending',
            remarks: null,
            linkedAssetId: 'a-1',
          },
        ],
      },
    ],
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

    expect(result!.offboardingId).toBe('off-1');
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
    req.flush(instance({ overallClearance: 'cleared', progressPercent: 100 }));

    expect(result!.overallClearance).toBe('cleared');
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
    req.flush(instance({ status: 'completed' }));

    expect(result!.status).toBe('completed');
  });

  it('complete surfaces a 409 pending-mandatory block to the subscriber (AC-5)', () => {
    let errored: HttpErrorResponse | undefined;
    service.complete('off-1').subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(`${base}/off-1/complete`);
    req.flush(
      { pending: ['Return laptop', 'Finance clearance'] },
      { status: 409, statusText: 'Conflict' },
    );

    expect(errored).toBeTruthy();
    const pending = OffboardingService.parseCompleteBlocked(errored!);
    expect(pending).toEqual(['Return laptop', 'Finance clearance']);
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
