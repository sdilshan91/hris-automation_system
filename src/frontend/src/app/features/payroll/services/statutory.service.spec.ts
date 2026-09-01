import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { StatutoryService } from './statutory.service';
import { environment } from '../../../../environments/environment';
import {
  IStatutoryRule,
  IStatutoryRuleRequest,
  ITestCalculationResult,
  StatutoryCalculationResultWire,
  StatutoryRuleListItemWire,
  StatutoryRuleWire,
} from '../models/statutory.models';

describe('StatutoryService', () => {
  let service: StatutoryService;
  let httpMock: HttpTestingController;
  const rulesUrl = `${environment.apiBaseUrl}/payroll/statutory-rules`;

  // D1 wire-types: the mocks below are the WIRE shapes the server actually sends, not the
  // view-models. `GET /statutory-rules` returns PagedResultOfPayrollStatutoryRuleListItemDto, whose
  // items are LIST ITEMS — they carry no taxSlabs/socialSecurity/updatedAt. Only the by-id, create,
  // update and clone responses carry the full PayrollStatutoryRuleDto.
  const mockListItem: StatutoryRuleListItemWire = {
    id: 'r-1',
    ruleType: 'IncomeTax',
    ruleTypeName: 'Income Tax',
    ruleName: 'Income Tax',
    countryCode: 'LK',
    fiscalYear: '2026-2027',
    effectiveFrom: '2026-04-01',
    effectiveTo: null,
    isActive: true,
    slabCount: 1,
  };

  const mockRule: StatutoryRuleWire = {
    id: 'r-1',
    ruleType: 'IncomeTax',
    ruleName: 'Income Tax',
    countryCode: 'LK',
    fiscalYear: '2026-2027',
    effectiveFrom: '2026-04-01',
    effectiveTo: null,
    isActive: true,
    isCumulative: false,
    taxSlabs: [
      { id: 's-1', orderIndex: 0, slabFrom: 0, slabTo: 250000, ratePercentage: 0 },
      { id: 's-2', orderIndex: 1, slabFrom: 250000, slabTo: null, ratePercentage: 6 },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        StatutoryService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(StatutoryService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('lists rules for a fiscal year with the filter query', () => {
    let result: IStatutoryRule[] = [];
    service.listRules('2026-2027').subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${rulesUrl}?fiscalYear=2026-2027`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([mockListItem]);
    expect(result.length).toBe(1);
    expect(result[0].ruleType).toBe('IncomeTax');
    // Mapping produces a NEW object, not a pass-through of the wire body.
    expect(result[0] as unknown).not.toBe(mockListItem);
  });

  it('lists rules without a filter when no fiscal year is given', () => {
    service.listRules().subscribe();
    const req = httpMock.expectOne(rulesUrl);
    expect(req.request.method).toBe('GET');
    req.flush([mockListItem]);
  });

  it('reads the new { items, totalCount } page envelope', () => {
    let result: IStatutoryRule[] = [];
    service.listRules('2026-2027').subscribe((r) => (result = r));
    httpMock
      .expectOne(`${rulesUrl}?fiscalYear=2026-2027`)
      .flush({ items: [mockListItem], totalCount: 1, page: 1, pageSize: 20 });
    expect(result.length).toBe(1);
  });

  it('still tolerates the legacy { data } envelope (US-PLT-001)', () => {
    let result: IStatutoryRule[] = [];
    service.listRules('2026-2027').subscribe((r) => (result = r));
    httpMock
      .expectOne(`${rulesUrl}?fiscalYear=2026-2027`)
      .flush({ data: [mockListItem] });
    expect(result.length).toBe(1);
  });

  it('defaults to [] on a null payload', () => {
    let result: IStatutoryRule[] | undefined;
    service.listRules('2026-2027').subscribe((r) => (result = r));
    httpMock.expectOne(`${rulesUrl}?fiscalYear=2026-2027`).flush(null);
    expect(result).toEqual([]);
  });

  it('leaves taxSlabs/socialSecurity absent for a LIST item — the list endpoint never sends them', () => {
    let result: IStatutoryRule[] = [];
    service.listRules('2026-2027').subscribe((r) => (result = r));
    httpMock
      .expectOne(`${rulesUrl}?fiscalYear=2026-2027`)
      .flush({ items: [mockListItem], totalCount: 1, page: 1, pageSize: 20 });
    // Deliberately NOT faked as [] — "this response carries no bands" is a different claim from
    // "this rule has zero bands". The editor hydrating from the list is a flagged defect.
    expect(result[0].taxSlabs).toBeUndefined();
    expect(result[0].socialSecurity).toBeNull();
  });

  it('defaults an absent isActive to false and an absent ruleType to Custom', () => {
    let result: IStatutoryRule[] = [];
    service.listRules().subscribe((r) => (result = r));
    // A wire body that omits both flags — exactly what a partial/legacy row looks like.
    httpMock.expectOne(rulesUrl).flush([
      { id: 'r-9', ruleName: 'Unnamed', fiscalYear: '2026-2027' },
    ]);
    expect(result[0].isActive).toBeFalse();
    // 'Custom' is the catch-all: an unknown rule must not land in the IncomeTax/EPF editor.
    expect(result[0].ruleType).toBe('Custom');
    expect(result[0].countryCode).toBe('');
  });

  it('lists distinct fiscal years', () => {
    let years: string[] = [];
    service.listFiscalYears().subscribe((y) => (years = y));
    const req = httpMock.expectOne(`${rulesUrl}/fiscal-years`);
    expect(req.request.method).toBe('GET');
    req.flush(['2026-2027', '2025-2026']);
    expect(years).toEqual(['2026-2027', '2025-2026']);
  });

  it('creates a statutory rule', () => {
    const request: IStatutoryRuleRequest = {
      ruleType: 'IncomeTax',
      ruleName: 'Income Tax',
      countryCode: 'LK',
      fiscalYear: '2026-2027',
      effectiveFrom: '2026-04-01',
      taxSlabs: [{ slabFrom: 0, slabTo: null, ratePercentage: 10 }],
    };
    service.createRule(request).subscribe();
    const req = httpMock.expectOne(rulesUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.ruleType).toBe('IncomeTax');
    req.flush(mockRule);
  });

  it('sends a create body matching PayrollCreateStatutoryRuleRequest', () => {
    const request: IStatutoryRuleRequest = {
      ruleType: 'IncomeTax',
      ruleName: 'Income Tax',
      countryCode: 'LK',
      fiscalYear: '2026-2027',
      effectiveFrom: '2026-04-01',
      taxSlabs: [
        // The editor's rows carry a server id; the create DTO has no id field.
        { id: 's-1', slabFrom: 0, slabTo: 250000, ratePercentage: 0 },
        { slabFrom: 250000, slabTo: null, ratePercentage: 6 },
      ],
    };
    service.createRule(request).subscribe();
    const req = httpMock.expectOne(rulesUrl);
    const body = req.request.body;
    // Positional order is stamped explicitly rather than relying on an all-zero index sorting stably.
    expect(body.taxSlabs[0].orderIndex).toBe(0);
    expect(body.taxSlabs[1].orderIndex).toBe(1);
    // The unbounded top band stays null — 0 would collapse it to an empty range.
    expect(body.taxSlabs[1].slabTo).toBeNull();
    expect('id' in body.taxSlabs[0]).toBeFalse();
    // isActive is OMITTED so the server applies its own `= true` default; sending false here would
    // create every statutory rule inactive.
    expect('isActive' in body).toBeFalse();
    req.flush(mockRule);
  });

  it('maps the created rule from the full PayrollStatutoryRuleDto response', () => {
    let created: IStatutoryRule | undefined;
    service
      .createRule({
        ruleType: 'IncomeTax',
        ruleName: 'Income Tax',
        countryCode: 'LK',
        fiscalYear: '2026-2027',
        effectiveFrom: '2026-04-01',
      })
      .subscribe((r) => (created = r));
    httpMock.expectOne(rulesUrl).flush(mockRule);
    expect(created?.taxSlabs?.length).toBe(2);
    // null upper bound survives the mapping (unlimited top band).
    expect(created?.taxSlabs?.[1].slabTo).toBeNull();
    expect(created?.taxSlabs?.[0].slabTo).toBe(250000);
    expect(created?.isActive).toBeTrue();
  });

  it('updates a statutory rule by id', () => {
    const request: IStatutoryRuleRequest = {
      ruleType: 'EPF',
      ruleName: 'Employee Provident Fund',
      countryCode: 'LK',
      fiscalYear: '2026-2027',
      effectiveFrom: '2026-04-01',
      socialSecurity: {
        employeeRate: 12,
        employerRate: 12,
        wageCeilingAnnual: 180000,
        applicableOn: 'Basic',
      },
    };
    service.updateRule('r-9', request).subscribe();
    const req = httpMock.expectOne(`${rulesUrl}/r-9`);
    expect(req.request.method).toBe('PUT');
    // PayrollUpdateStatutoryRuleRequest has no ruleType — the rule's type is immutable server-side.
    expect('ruleType' in req.request.body).toBeFalse();
    expect(req.request.body.socialSecurity.employeeRate).toBe(12);
    expect(req.request.body.socialSecurity.applicableOn).toBe('Basic');
    req.flush({ ...mockRule, id: 'r-9' });
  });

  it('preserves a null wageCeilingAnnual as null (no ceiling), never 0', () => {
    let updated: IStatutoryRule | undefined;
    service
      .updateRule('r-9', {
        ruleType: 'EPF',
        ruleName: 'EPF',
        countryCode: 'LK',
        fiscalYear: '2026-2027',
        effectiveFrom: '2026-04-01',
        socialSecurity: {
          employeeRate: 8,
          employerRate: 12,
          wageCeilingAnnual: null,
          applicableOn: 'Basic',
        },
      })
      .subscribe((r) => (updated = r));
    const req = httpMock.expectOne(`${rulesUrl}/r-9`);
    expect(req.request.body.socialSecurity.wageCeilingAnnual).toBeNull();
    req.flush({
      id: 'r-9',
      ruleType: 'EPF',
      ruleName: 'EPF',
      countryCode: 'LK',
      fiscalYear: '2026-2027',
      effectiveFrom: '2026-04-01',
      isActive: true,
      socialSecurity: {
        employeeRate: 8,
        employerRate: 12,
        wageCeilingAnnual: null,
        applicableOn: 'Basic',
      },
    } satisfies StatutoryRuleWire);
    // A 0 ceiling would mean "no contribution at all" — null must survive the round trip.
    expect(updated?.socialSecurity?.wageCeilingAnnual).toBeNull();
    expect(updated?.socialSecurity?.employeeRate).toBe(8);
  });

  it('deletes a statutory rule', () => {
    service.deleteRule('r-3').subscribe();
    const req = httpMock.expectOne(`${rulesUrl}/r-3`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('runs a test calculation (FR-5)', () => {
    // The WIRE shape: PayrollStatutoryCalculationResultDto carries otherStatutory /
    // totalEmployeeDeductions — NOT the view-model's otherDeductions / totalDeductions — and carries
    // neither monthlyGross nor netPay at all.
    const wire: StatutoryCalculationResultWire = {
      fiscalYear: '2026-2027',
      taxableIncome: 100000,
      incomeTax: 5000,
      employeeEpf: 12000,
      employerEpf: 12000,
      etf: 3000,
      professionalTax: 0,
      otherStatutory: 0,
      totalEmployeeDeductions: 17000,
      totalEmployerContributions: 15000,
    };
    let result: ITestCalculationResult | undefined;
    service
      .testCalculation({ fiscalYear: '2026-2027', monthlyGross: 100000 })
      .subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${rulesUrl}/test-calculation`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.monthlyGross).toBe(100000);
    req.flush(wire);
    // RENAME: totalEmployeeDeductions → totalDeductions.
    expect(result?.totalDeductions).toBe(17000);
    // netPay has no wire source: derived as gross − employee-borne deductions (never defaulted to 0).
    expect(result?.netPay).toBe(83000);
    // monthlyGross has no wire source either: echoed from the request the caller just sent.
    expect(result?.monthlyGross).toBe(100000);
  });

  it('maps otherStatutory → otherDeductions and derives netPay from the request gross', () => {
    let result: ITestCalculationResult | undefined;
    service
      .testCalculation({ fiscalYear: '2026-2027', monthlyGross: 50000 })
      .subscribe((r) => (result = r));
    httpMock.expectOne(`${rulesUrl}/test-calculation`).flush({
      incomeTax: 1000,
      employeeEpf: 4000,
      employerEpf: 6000,
      etf: 1500,
      professionalTax: 200,
      otherStatutory: 750,
      totalEmployeeDeductions: 5950,
      totalEmployerContributions: 7500,
    } satisfies StatutoryCalculationResultWire);
    expect(result?.otherDeductions).toBe(750);
    expect(result?.netPay).toBe(50000 - 5950);
  });

  it('does not send a countryCode on the test-calculation body (flagged contract gap)', () => {
    service
      .testCalculation({ fiscalYear: '2026-2027', monthlyGross: 100000 })
      .subscribe();
    const req = httpMock.expectOne(`${rulesUrl}/test-calculation`);
    // Pinned deliberately: ITestCalculationRequest has no countryCode, and the resolver treats a null
    // country as "resolve NOTHING", so the preview returns zeros. This assertion documents the live
    // gap so that adding countryCode is a conscious, test-visible change rather than a silent one.
    expect(req.request.body.countryCode).toBeUndefined();
    req.flush({
      incomeTax: 0,
      employeeEpf: 0,
      employerEpf: 0,
      etf: 0,
      professionalTax: 0,
      otherStatutory: 0,
      totalEmployeeDeductions: 0,
      totalEmployerContributions: 0,
    } satisfies StatutoryCalculationResultWire);
  });
});
