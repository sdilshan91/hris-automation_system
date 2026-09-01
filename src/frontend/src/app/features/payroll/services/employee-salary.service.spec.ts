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
  BulkAssignResultWire,
  CtcBreakdownWire,
  EmployeeCompensationWire,
  SalaryRevisionWire,
} from '../models/employee-salary.models';

describe('EmployeeSalaryService', () => {
  let service: EmployeeSalaryService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/payroll`;
  const assignmentsUrl = `${base}/salary-assignments`;

  // WIRE shape — `PayrollCtcBreakdownDto`. Note the three names that differ from the view-model:
  // `components` (not `lines`), `totalAnnualEarnings`, `totalMonthlyEarnings`. The old mocks in this
  // spec flushed the VIEW-MODEL shape, which the server has never sent — that is precisely why the
  // unchecked `http.post<ICtcBreakdown>` cast stayed green while returning undefined totals.
  const wireBreakdown: CtcBreakdownWire = {
    annualCtc: 600000,
    salaryStructureId: 's-1',
    totalAnnualEarnings: 600000,
    totalMonthlyEarnings: 50000,
    balanced: true,
    components: [
      {
        salaryComponentId: 'c-1',
        componentName: 'Basic',
        componentCode: 'BASIC',
        componentType: 'Earning',
        annualAmount: 240000,
        monthlyAmount: 20000,
        processingOrder: 1,
        isOverride: false,
      },
    ],
  };

  /** What `wireBreakdown` maps to. */
  const mappedBreakdown: ICtcBreakdown = {
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

  it('previewBreakdown POSTs the request and maps the wire breakdown (FR-3)', () => {
    let result: ICtcBreakdown | undefined;
    service.previewBreakdown(mockRequest).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${assignmentsUrl}/preview`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(mockRequest);
    expect(req.request.withCredentials).toBeTrue();
    req.flush(wireBreakdown);

    expect(result).toEqual(mappedBreakdown);
    expect(result).not.toBe(wireBreakdown as unknown as ICtcBreakdown);
  });

  it('previewBreakdown renames components/totalAnnualEarnings/totalMonthlyEarnings', () => {
    // The three renames are the whole point of the mapper — assert them individually, because a
    // regression here is a blank CTC preview, not a compile error.
    let result: ICtcBreakdown | undefined;
    service.previewBreakdown(mockRequest).subscribe((r) => (result = r));
    httpMock.expectOne(`${assignmentsUrl}/preview`).flush(wireBreakdown);

    expect(result!.lines.length).toBe(1);
    expect(result!.totalAnnual).toBe(600000);
    expect(result!.totalMonthly).toBe(50000);
  });

  it('previewBreakdown fails CLOSED on an omitted balanced flag (FR-6)', () => {
    // An absent reconciliation flag must never tell HR the CTC balances.
    const { balanced: _omitted, ...withoutBalanced } = wireBreakdown;
    let result: ICtcBreakdown | undefined;
    service.previewBreakdown(mockRequest).subscribe((r) => (result = r));
    httpMock.expectOne(`${assignmentsUrl}/preview`).flush(withoutBalanced);

    expect(result!.balanced).toBeFalse();
  });

  it('previewBreakdown never labels a calculated line as an HR override (AC-3)', () => {
    let result: ICtcBreakdown | undefined;
    service.previewBreakdown(mockRequest).subscribe((r) => (result = r));
    httpMock.expectOne(`${assignmentsUrl}/preview`).flush({
      ...wireBreakdown,
      components: [{ salaryComponentId: 'c-1', componentName: 'Basic' }],
    });

    expect(result!.lines[0].isOverride).toBeFalse();
  });

  it('previewBreakdown blanks an unrecognised componentType rather than guessing', () => {
    // The wire field is a bare string built with `Type.ToString() ?? string.Empty`, so an empty or
    // unknown value is reachable. Guessing 'Earning' vs 'Deduction' would flip the sign of a pay line.
    let result: ICtcBreakdown | undefined;
    service.previewBreakdown(mockRequest).subscribe((r) => (result = r));
    httpMock.expectOne(`${assignmentsUrl}/preview`).flush({
      ...wireBreakdown,
      components: [
        { ...wireBreakdown.components![0], componentType: 'NotAThing' },
      ],
    });

    expect(result!.lines[0].componentType as string).toBe('');
  });

  it('previewBreakdown maps a null components collection to an empty line list', () => {
    let result: ICtcBreakdown | undefined;
    service.previewBreakdown(mockRequest).subscribe((r) => (result = r));
    httpMock
      .expectOne(`${assignmentsUrl}/preview`)
      .flush({ ...wireBreakdown, components: null });

    expect(result!.lines).toEqual([]);
  });

  it('assign POSTs to the assignments endpoint (FR-1)', () => {
    // ⚠ The endpoint answers with a bare PayrollCtcBreakdownDto — there is NO assignment-result DTO on
    // the API. The previous version of this test flushed an invented `{ employeeId, effectiveFrom,
    // breakdown }` object, which is exactly the shape the server does not send.
    let result: ISalaryAssignmentResult | undefined;
    service.assign(mockRequest).subscribe((r) => (result = r));

    const req = httpMock.expectOne(assignmentsUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(mockRequest);
    req.flush(wireBreakdown);

    expect(result!.salaryStructureId).toBe('s-1');
    expect(result!.annualCtc).toBe(600000);
    expect(result!.breakdown).toEqual(mappedBreakdown);
  });

  it('assign leaves employeeId/effectiveFrom empty — the response carries neither', () => {
    // Pinned so nobody starts binding to a field the API never confirms. See the D1 report.
    let result: ISalaryAssignmentResult | undefined;
    service.assign(mockRequest).subscribe((r) => (result = r));
    httpMock.expectOne(assignmentsUrl).flush(wireBreakdown);

    expect(result!.employeeId).toBe('');
    expect(result!.effectiveFrom).toBe('');
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
    // WIRE shape — `PayrollBulkAssignResultDto` calls the counters `succeededCount` / `failedCount`.
    const wireResult: BulkAssignResultWire = {
      totalRequested: 2,
      succeededCount: 2,
      failedCount: 0,
      results: [
        { employeeId: 'e-1', success: true, error: null, errorCode: null },
        { employeeId: 'e-2', success: true, error: null, errorCode: null },
      ],
    };
    let result: IBulkAssignmentResult | undefined;
    service.bulkAssign(request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${assignmentsUrl}/bulk`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush(wireResult);

    expect(result).toEqual({
      totalRequested: 2,
      successCount: 2,
      failureCount: 0,
      results: [
        { employeeId: 'e-1', success: true, error: null },
        { employeeId: 'e-2', success: true, error: null },
      ],
    });
    expect(result).not.toBe(wireResult as unknown as IBulkAssignmentResult);
  });

  it('bulkAssign renames succeededCount/failedCount and fails CLOSED on a missing success flag', () => {
    // Both renames drive the progress indicator; an absent `success` must never report an employee's
    // salary as assigned when the server did not say so.
    let result: IBulkAssignmentResult | undefined;
    service
      .bulkAssign({
        salaryStructureId: 's-1',
        effectiveFrom: '2026-07-01',
        employees: [{ employeeId: 'e-1', annualCtc: 600000 }],
      })
      .subscribe((r) => (result = r));

    httpMock.expectOne(`${assignmentsUrl}/bulk`).flush({
      totalRequested: 1,
      succeededCount: 0,
      failedCount: 1,
      results: [{ employeeId: 'e-1', error: 'Employee is terminated.' }],
    });

    expect(result!.successCount).toBe(0);
    expect(result!.failureCount).toBe(1);
    expect(result!.results[0].success).toBeFalse();
    expect(result!.results[0].error).toBe('Employee is terminated.');
  });

  it('getCurrentCompensation GETs the employee compensation and renames components → lines (§8)', () => {
    // WIRE shape — `PayrollEmployeeCompensationDto` calls the array `components`, not `lines`.
    const wireComp: EmployeeCompensationWire = {
      employeeId: 'e-1',
      salaryStructureId: 's-1',
      salaryStructureName: 'Full-Time',
      annualCtc: 600000,
      monthlyCtc: 50000,
      effectiveFrom: '2026-07-01',
      components: wireBreakdown.components,
    };
    let result: IEmployeeCompensation | undefined;
    service.getCurrentCompensation('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employees/e-1/compensation`);
    expect(req.request.method).toBe('GET');
    req.flush(wireComp);

    expect(result).toEqual({
      employeeId: 'e-1',
      salaryStructureId: 's-1',
      salaryStructureName: 'Full-Time',
      annualCtc: 600000,
      monthlyCtc: 50000,
      effectiveFrom: '2026-07-01',
      lines: mappedBreakdown.lines,
    });
    expect(result).not.toBe(wireComp as unknown as IEmployeeCompensation);
  });

  it('getCurrentCompensation maps absent money to NULL, never to 0 (BR-5)', () => {
    // This is an employee's actual pay. A fabricated 0 would render as a real salary of zero on the
    // Compensation tab; null is what the tab renders as "not set" / "Payroll Incomplete".
    let result: IEmployeeCompensation | undefined;
    service.getCurrentCompensation('e-1').subscribe((r) => (result = r));

    httpMock
      .expectOne(`${base}/employees/e-1/compensation`)
      .flush({ employeeId: 'e-1' });

    expect(result!.annualCtc).toBeNull();
    expect(result!.monthlyCtc).toBeNull();
    expect(result!.salaryStructureId).toBeNull();
    expect(result!.salaryStructureName).toBeNull();
    expect(result!.effectiveFrom).toBeNull();
    expect(result!.lines).toEqual([]);
  });

  it('getRevisionHistory maps a bare array and renames id → revisionId (FR-4)', () => {
    // WIRE shape — `PayrollSalaryRevisionDto`. It sends IDs ONLY: there is no `oldStructureName`,
    // `newStructureName` or `changedByName` anywhere on this DTO. The previous mock invented all three,
    // which is why the empty revision-row title never showed up in a test.
    const wireRevs: SalaryRevisionWire[] = [
      {
        id: 'r-1',
        employeeId: 'e-1',
        oldStructureId: null,
        oldAnnualCtc: null,
        newStructureId: 's-1',
        newAnnualCtc: 600000,
        effectiveFrom: '2026-07-01',
        reason: 'Initial assignment',
        changedBy: 'u-1',
        changedAt: '2026-06-15T10:00:00Z',
      },
    ];
    let result: ISalaryRevision[] | undefined;
    service.getRevisionHistory('e-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employees/e-1/revision-history`);
    expect(req.request.method).toBe('GET');
    req.flush(wireRevs);

    expect(result).toEqual([
      {
        revisionId: 'r-1',
        oldStructureId: null,
        oldStructureName: null,
        oldAnnualCtc: null,
        newStructureId: 's-1',
        newStructureName: '',
        newAnnualCtc: 600000,
        effectiveFrom: '2026-07-01',
        reason: 'Initial assignment',
        changedByName: null,
        changedAt: '2026-06-15T10:00:00Z',
      },
    ]);
  });

  it('getRevisionHistory has NO wire source for the three display names', () => {
    // Pinned deliberately: `newStructureName` is the TITLE of every timeline row and renders EMPTY.
    // This assertion is the ledger entry for that gap — see the D1 report — not an endorsement.
    let result: ISalaryRevision[] | undefined;
    service.getRevisionHistory('e-1').subscribe((r) => (result = r));

    httpMock.expectOne(`${base}/employees/e-1/revision-history`).flush([
      {
        id: 'r-1',
        newStructureId: 's-1',
        newAnnualCtc: 600000,
        changedBy: 'u-1',
        changedAt: '2026-06-15T10:00:00Z',
      },
    ]);

    expect(result![0].newStructureName).toBe('');
    expect(result![0].oldStructureName).toBeNull();
    expect(result![0].changedByName).toBeNull();
    // `oldAnnualCtc` is schema-nullable: null genuinely means "no previous CTC", not "unknown".
    expect(result![0].oldAnnualCtc).toBeNull();
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
