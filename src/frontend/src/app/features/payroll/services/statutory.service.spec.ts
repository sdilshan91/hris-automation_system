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
} from '../models/statutory.models';

describe('StatutoryService', () => {
  let service: StatutoryService;
  let httpMock: HttpTestingController;
  const rulesUrl = `${environment.apiBaseUrl}/payroll/statutory-rules`;

  const mockRule: IStatutoryRule = {
    id: 'r-1',
    ruleType: 'IncomeTax',
    ruleName: 'Income Tax',
    countryCode: 'LK',
    fiscalYear: '2026-2027',
    effectiveFrom: '2026-04-01',
    effectiveTo: null,
    isActive: true,
    taxSlabs: [{ id: 's-1', slabFrom: 0, slabTo: 250000, ratePercentage: 0 }],
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
    req.flush([mockRule]);
    expect(result.length).toBe(1);
    expect(result[0].ruleType).toBe('IncomeTax');
  });

  it('lists rules without a filter when no fiscal year is given', () => {
    service.listRules().subscribe();
    const req = httpMock.expectOne(rulesUrl);
    expect(req.request.method).toBe('GET');
    req.flush([mockRule]);
  });

  it('reads the new { items, totalCount } page envelope', () => {
    let result: IStatutoryRule[] = [];
    service.listRules('2026-2027').subscribe((r) => (result = r));
    httpMock
      .expectOne(`${rulesUrl}?fiscalYear=2026-2027`)
      .flush({ items: [mockRule], totalCount: 1, page: 1, pageSize: 20 });
    expect(result.length).toBe(1);
  });

  it('still tolerates the legacy { data } envelope (US-PLT-001)', () => {
    let result: IStatutoryRule[] = [];
    service.listRules('2026-2027').subscribe((r) => (result = r));
    httpMock
      .expectOne(`${rulesUrl}?fiscalYear=2026-2027`)
      .flush({ data: [mockRule] });
    expect(result.length).toBe(1);
  });

  it('defaults to [] on a null payload', () => {
    let result: IStatutoryRule[] | undefined;
    service.listRules('2026-2027').subscribe((r) => (result = r));
    httpMock.expectOne(`${rulesUrl}?fiscalYear=2026-2027`).flush(null);
    expect(result).toEqual([]);
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
    req.flush({ ...mockRule, id: 'r-9' });
  });

  it('deletes a statutory rule', () => {
    service.deleteRule('r-3').subscribe();
    const req = httpMock.expectOne(`${rulesUrl}/r-3`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('runs a test calculation (FR-5)', () => {
    const expected: ITestCalculationResult = {
      monthlyGross: 100000,
      incomeTax: 5000,
      employeeEpf: 12000,
      employerEpf: 12000,
      etf: 3000,
      otherDeductions: 0,
      totalDeductions: 17000,
      netPay: 83000,
    };
    let result: ITestCalculationResult | undefined;
    service
      .testCalculation({ fiscalYear: '2026-2027', monthlyGross: 100000 })
      .subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${rulesUrl}/test-calculation`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.monthlyGross).toBe(100000);
    req.flush(expected);
    expect(result?.totalDeductions).toBe(17000);
    expect(result?.netPay).toBe(83000);
  });
});
