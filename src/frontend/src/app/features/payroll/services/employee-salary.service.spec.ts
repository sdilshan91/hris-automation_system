import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EmployeeSalaryService } from './employee-salary.service';
import { environment } from '../../../../environments/environment';
import {
  ICtcBreakdown,
  IEmployeeCompensation,
  ISalaryAssignmentRequest,
  ISalaryAssignmentResult,
  ISalaryRevision,
  IBulkAssignmentRequest,
  IBulkAssignmentResult,
} from '../models/employee-salary.models';

describe('EmployeeSalaryService', () => {
  let service: EmployeeSalaryService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/payroll`;
  const assignmentsUrl = `${base}/salary-assignments`;

  const mockBreakdown: ICtcBreakdown = {
    annualCtc: 600000,
    totalAnnual: 600000,
    totalMonthly: 50000,
    balanced: true,
    lines: [
      {
        salaryComponentId: 'c-1',
        componentName: 'Basic',
        componentType: 'Earning',
        annualAmount: 240000,
        monthlyAmount: 20000,
        isOverride: false,
      },
    ],
  };

  const mockRequest: ISalaryAssignmentRequest = {
    employeeId: 'e-1',
    salaryStructureId: 's-1',
    annualCtc: 600000,
    effectiveFrom: '2026-07-01',
    overrides: [],
    reason: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        EmployeeSalaryService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(EmployeeSalaryService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('previewBreakdown POSTs the request and returns the breakdown (FR-3)', () => {
    let result: ICtcBreakdown | undefined;
    service.previewBreakdown(mockRequest).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${assignmentsUrl}/preview`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(mockRequest);
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockBreakdown);

    expect(result).toEqual(mockBreakdown);
  });

  it('assign POSTs to the assignments endpoint (FR-1)', () => {
    const mockResult: ISalaryAssignmentResult = {
      employeeId: 'e-1',
      salaryStructureId: 's-1',
      annualCtc: 600000,
      effectiveFrom: '2026-07-01',
      breakdown: mockBreakdown,
    };
    let result: ISalaryAssignmentResult | undefined;
    service.assign(mockRequest).subscribe((r) => (result = r));

    const req = httpMock.expectOne(assignmentsUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(mockRequest);
    req.flush(mockResult);

    expect(result).toEqual(mockResult);
  });

  it('bulkAssign POSTs to the bulk endpoint (FR-5)', () => {
    const request: IBulkAssignmentRequest = {
      salaryStructureId: 's-1',
      effectiveFrom: '2026-07-01',
      employees: [
        { employeeId: 'e-1', annualCtc: 600000 },
        { employeeId: 'e-2', annualCtc: 720000 },
      ],
    };
    const mockResult: IBulkAssignmentResult = {
      totalRequested: 2,
      successCount: 2,
      failureCount: 0,
      results: [
        { employeeId: 'e-1', success: true },
        { employeeId: 'e-2', success: true },
      ],
    };
    let result: IBulkAssignmentResult | undefined;
    service.bulkAssign(request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${assignmentsUrl}/bulk`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(mockResult);

    expect(result).toEqual(mockResult);
  });

  it('getCurrentCompensation GETs the employee compensation (§8)', () => {
    const mockComp: IEmployeeCompensation = {
      employeeId: 'e-1',
      salaryStructureId: 's-1',
      salaryStructureName: 'Full-Time',
      annualCtc: 600000,
      monthlyCtc: 50000,
      effectiveFrom: '2026-07-01',
      lines: mockBreakdown.lines,
    };
    let result: IEmployeeCompensation | undefined;
    service.getCurrentCompensation('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employees/e-1/compensation`);
    expect(req.request.method).toBe('GET');
    req.flush(mockComp);

    expect(result).toEqual(mockComp);
  });

  it('getRevisionHistory returns a bare array (FR-4)', () => {
    const mockRevs: ISalaryRevision[] = [
      {
        revisionId: 'r-1',
        oldStructureId: null,
        oldStructureName: null,
        oldAnnualCtc: null,
        newStructureId: 's-1',
        newStructureName: 'Full-Time',
        newAnnualCtc: 600000,
        effectiveFrom: '2026-07-01',
        reason: 'Initial assignment',
        changedByName: 'HR Admin',
        changedAt: '2026-06-15T10:00:00Z',
      },
    ];
    let result: ISalaryRevision[] | undefined;
    service.getRevisionHistory('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employees/e-1/revision-history`);
    expect(req.request.method).toBe('GET');
    req.flush(mockRevs);

    expect(result).toEqual(mockRevs);
  });

  it('getRevisionHistory unwraps a { data } envelope', () => {
    let result: ISalaryRevision[] | undefined;
    service.getRevisionHistory('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employees/e-1/revision-history`);
    req.flush({ data: [] });

    expect(result).toEqual([]);
  });

  it('getRevisionHistory defaults to [] for an unexpected payload', () => {
    let result: ISalaryRevision[] | undefined;
    service.getRevisionHistory('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employees/e-1/revision-history`);
    req.flush(null);

    expect(result).toEqual([]);
  });
});
