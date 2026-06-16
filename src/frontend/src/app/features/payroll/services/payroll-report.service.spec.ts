import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PayrollReportService } from './payroll-report.service';
import { environment } from '../../../../environments/environment';
import {
  IBankAdvicePreview,
  IPayrollAnalyticsResult,
  IReportFilters,
  IReportResult,
  IReportTypeMeta,
} from '../models/payroll-report.models';

describe('PayrollReportService', () => {
  let service: PayrollReportService;
  let httpMock: HttpTestingController;
  const payrollUrl = `${environment.apiBaseUrl}/payroll`;
  const reportsUrl = `${payrollUrl}/reports`;
  const analyticsUrl = `${payrollUrl}/analytics`;

  const filters: IReportFilters = { period: '2026-05', departmentId: 'dept-1' };
  const allDepts: IReportFilters = { period: '2026-05', departmentId: null };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PayrollReportService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PayrollReportService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listReportTypes GETs /payroll/reports and returns a bare array', () => {
    const types: IReportTypeMeta[] = [
      { id: 'PayrollSummary', name: 'Payroll Summary', description: 'x', deferred: false },
    ];
    let result: IReportTypeMeta[] | undefined;
    service.listReportTypes().subscribe((r) => (result = r));

    const req = httpMock.expectOne(reportsUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(types);

    expect(result).toEqual(types);
  });

  it('listReportTypes tolerates a { data } envelope and defaults to []', () => {
    let result: IReportTypeMeta[] | undefined;
    service.listReportTypes().subscribe((r) => (result = r));
    httpMock.expectOne(reportsUrl).flush({ data: [] });
    expect(result).toEqual([]);

    let result2: IReportTypeMeta[] | undefined;
    service.listReportTypes().subscribe((r) => (result2 = r));
    httpMock.expectOne(reportsUrl).flush(null);
    expect(result2).toEqual([]);
  });

  it('getReport GETs :reportType with separate payMonth/payYear/departmentId params', () => {
    const payload: IReportResult = {
      reportType: 'PayrollSummary',
      title: 'Payroll Summary',
      payMonth: 5,
      payYear: 2026,
      columns: ['Department', 'Total Net'],
      rows: [{ cells: ['People', '5,000.00'] }],
      totalCount: 1,
    };
    let result: IReportResult | undefined;
    service.getReport('PayrollSummary', filters).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/PayrollSummary`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('payMonth')).toBe('5');
    expect(req.request.params.get('payYear')).toBe('2026');
    expect(req.request.params.get('departmentId')).toBe('dept-1');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(payload);

    expect(result).toEqual(payload);
  });

  it('getReport omits departmentId when no department is selected', () => {
    service.getReport('Ctc', allDepts).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/Ctc`);
    expect(req.request.params.get('payMonth')).toBe('5');
    expect(req.request.params.get('payYear')).toBe('2026');
    expect(req.request.params.has('departmentId')).toBeFalse();
    req.flush({});
  });

  it('getReport omits period params when the period is unparseable', () => {
    service.getReport('Ctc', { period: '', departmentId: null }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/Ctc`);
    expect(req.request.params.has('payMonth')).toBeFalse();
    expect(req.request.params.has('payYear')).toBeFalse();
    req.flush({});
  });

  it('exportReport requests a blob with the format + period params and reads the response', () => {
    let resp: { body: Blob | null } | undefined;
    service.exportReport('PayrollSummary', filters, 'xlsx').subscribe((r) => (resp = r));

    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/PayrollSummary/export`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.get('format')).toBe('xlsx');
    expect(req.request.params.get('payMonth')).toBe('5');
    expect(req.request.params.get('payYear')).toBe('2026');

    const blob = new Blob(['x'], { type: 'application/octet-stream' });
    req.flush(blob, { headers: { 'Content-Disposition': 'attachment; filename="r.xlsx"' } });

    expect(resp?.body).toBe(blob);
  });

  it('getAnalytics GETs /payroll/analytics/:chartType with the period params', () => {
    const chart: IPayrollAnalyticsResult = {
      chartType: 'MonthlyTrend',
      points: [],
      categories: ['Apr 2026', 'May 2026'],
      series: [{ name: 'Gross', points: [{ label: 'Apr 2026', value: 1000 }] }],
    };
    let result: IPayrollAnalyticsResult | undefined;
    service.getAnalytics('MonthlyTrend', filters).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === `${analyticsUrl}/MonthlyTrend`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('payMonth')).toBe('5');
    expect(req.request.params.get('departmentId')).toBe('dept-1');
    req.flush(chart);

    expect(result).toEqual(chart);
  });

  it('getDashboardAnalytics forkJoins the three analytics chart endpoints', () => {
    const make = (chartType: string): IPayrollAnalyticsResult => ({
      chartType,
      points: [],
      categories: [],
      series: [],
    });
    let result: { trend: IPayrollAnalyticsResult } | undefined;
    service.getDashboardAnalytics(allDepts).subscribe((r) => (result = r));

    const trend = httpMock.expectOne((r) => r.url === `${analyticsUrl}/MonthlyTrend`);
    const dept = httpMock.expectOne((r) => r.url === `${analyticsUrl}/DepartmentCostDistribution`);
    const stat = httpMock.expectOne((r) => r.url === `${analyticsUrl}/StatutoryBreakdown`);
    expect(trend.request.method).toBe('GET');
    trend.flush(make('MonthlyTrend'));
    dept.flush(make('DepartmentCostDistribution'));
    stat.flush(make('StatutoryBreakdown'));

    expect(result?.trend.chartType).toBe('MonthlyTrend');
  });

  it('getBankAdvicePreview GETs /reports/bank-advice/preview with masked lines', () => {
    const preview: IBankAdvicePreview = {
      payMonth: 5,
      payYear: 2026,
      employeeCount: 1,
      totalNetAmount: 5000,
      lines: [
        {
          employeeNo: 'EMP001',
          employeeName: 'Alex HR',
          bankName: 'Acme Bank',
          branchCode: 'AC001',
          accountNumber: '••••6789',
          netAmount: 5000,
          narration: 'Salary May 2026',
        },
      ],
    };
    let result: IBankAdvicePreview | undefined;
    service.getBankAdvicePreview(filters).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/bank-advice/preview`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('payMonth')).toBe('5');
    req.flush(preview);

    expect(result).toEqual(preview);
    expect(result?.lines[0].accountNumber).toBe('••••6789');
  });

  it('downloadBankAdvice exports the BankAdvice report (full file) as CSV by default', () => {
    let resp: { body: Blob | null } | undefined;
    service.downloadBankAdvice(filters).subscribe((r) => (resp = r));

    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/BankAdvice/export`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.get('format')).toBe('csv');
    expect(req.request.params.get('payMonth')).toBe('5');

    const blob = new Blob(['a,b'], { type: 'text/csv' });
    req.flush(blob);
    expect(resp?.body).toBe(blob);
  });

  it('downloadBankAdvice honours an explicit format', () => {
    service.downloadBankAdvice(filters, 'xlsx').subscribe();
    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/BankAdvice/export`);
    expect(req.request.params.get('format')).toBe('xlsx');
    req.flush(new Blob(['x']));
  });
});
