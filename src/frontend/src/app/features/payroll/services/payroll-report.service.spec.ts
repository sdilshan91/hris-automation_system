import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PayrollReportService } from './payroll-report.service';
import { environment } from '../../../../environments/environment';
import {
  BankAdvicePreviewWire,
  IBankAdvicePreview,
  IPayrollAnalyticsResult,
  IReportFilters,
  IReportResult,
  IReportTypeMeta,
  PayrollAnalyticsResultWire,
  ReportResultWire,
  ReportTypeMetaWire,
} from '../models/payroll-report.models';

/**
 * D1 payroll slice: every mock below is typed as the WIRE shape (the generated contract DTO), not the
 * view-model. Flushing a view-model here is what let FE/BE drift stay green elsewhere in this repo —
 * the test would assert the FE's own belief back at itself. Assertions therefore check the MAPPED
 * result, and the defaulting tests flush deliberately SPARSE bodies (the server omitting a field) to
 * pin the money/flag defaults down.
 */

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
    const types: ReportTypeMetaWire[] = [
      { id: 'PayrollSummary', name: 'Payroll Summary', description: 'x', deferred: false },
    ];
    let result: IReportTypeMeta[] | undefined;
    service.listReportTypes().subscribe((r) => (result = r));

    const req = httpMock.expectOne(reportsUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(types);

    expect(result).toEqual([
      { id: 'PayrollSummary', name: 'Payroll Summary', description: 'x', deferred: false },
    ]);
    // The descriptor is MAPPED, not passed through — a rename must break here, not silently render blank.
    expect(result?.[0]).not.toBe(types[0] as never);
  });

  it('listReportTypes defaults a sparse descriptor: name falls back to the id, deferred to false', () => {
    let result: IReportTypeMeta[] | undefined;
    service.listReportTypes().subscribe((r) => (result = r));
    // The server sends only the id — no name, no description, no deferred flag.
    httpMock.expectOne(reportsUrl).flush([{ id: 'Ctc' }]);

    expect(result?.[0].id).toBe('Ctc');
    // A blank name would render an unlabelled, unclickable sidebar row.
    expect(result?.[0].name).toBe('Ctc');
    expect(result?.[0].description).toBe('');
    // Least-claiming: an absent flag must NOT mark a synchronous report as deferred/bulk.
    expect(result?.[0].deferred).toBeFalse();
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
    const payload: ReportResultWire = {
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

    expect(result).toEqual({
      reportType: 'PayrollSummary',
      title: 'Payroll Summary',
      payMonth: 5,
      payYear: 2026,
      columns: ['Department', 'Total Net'],
      rows: [{ cells: ['People', '5,000.00'] }],
      // Omitted by the server → explicit nulls, added by the mapper.
      totalRow: null,
      totalCount: 1,
      note: null,
      summary: null,
    });
    expect(result).not.toBe(payload as unknown as IReportResult);
  });

  it('getReport defaults an EMPTY body to empty collections and explicit nulls', () => {
    let result: IReportResult | undefined;
    service.getReport('Ctc', allDepts).subscribe((r) => (result = r));
    httpMock.expectOne((r) => r.url === `${reportsUrl}/Ctc`).flush({});

    expect(result?.columns).toEqual([]);
    expect(result?.rows).toEqual([]);
    expect(result?.totalCount).toBe(0);
    // Absent footer row → null, NOT an empty row object (which renders a blank total line).
    expect(result?.totalRow).toBeNull();
    // Absent summary → null: the report renders the plain table with no KPI cards.
    expect(result?.summary).toBeNull();
    expect(result?.note).toBeNull();
  });

  it('getReport keeps an UNRECOGNISED reportType raw instead of coercing it to a known report', () => {
    let result: IReportResult | undefined;
    service.getReport('Ctc', allDepts).subscribe((r) => (result = r));
    httpMock
      .expectOne((r) => r.url === `${reportsUrl}/Ctc`)
      .flush({ reportType: 'SomeNewReport', title: 'x' });

    // The wire types reportType as a plain string; an unknown value must pass through untouched so
    // reportHasChart() simply returns false (no chart) rather than drawing the wrong one.
    expect(result?.reportType).toBe('SomeNewReport' as IReportResult['reportType']);
  });

  it('getReport maps a KPI summary and does NOT fabricate a variance when there is no prior run', () => {
    let result: IReportResult | undefined;
    service.getReport('PayrollSummary', filters).subscribe((r) => (result = r));
    httpMock.expectOne((r) => r.url === `${reportsUrl}/PayrollSummary`).flush({
      reportType: 'PayrollSummary',
      summary: {
        currency: 'LKR',
        currentLabel: 'May 2026',
        // previousLabel / previous / variance / isCost all omitted — a first-ever finalized run.
        metrics: [{ key: 'gross', label: 'Total Gross', current: 5000 }],
      },
    } satisfies ReportResultWire);

    expect(result?.summary?.currency).toBe('LKR');
    expect(result?.summary?.previousLabel).toBeNull();
    const metric = result?.summary?.metrics[0];
    expect(metric?.current).toBe(5000);
    // CRITICAL: null, never 0 — a 0 would render a fabricated "0.0% vs last period" on a first run.
    expect(metric?.previous).toBeNull();
    expect(metric?.variance).toBeNull();
    // Least-claiming: an absent isCost must not apply the red "cost increased" semantics.
    expect(metric?.isCost).toBeFalse();
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
    const chart: PayrollAnalyticsResultWire = {
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

    expect(result).toEqual({
      chartType: 'MonthlyTrend',
      points: [],
      categories: ['Apr 2026', 'May 2026'],
      series: [{ name: 'Gross', points: [{ label: 'Apr 2026', value: 1000 }] }],
    });
    expect(result).not.toBe(chart as unknown as IPayrollAnalyticsResult);
  });

  it('getAnalytics defaults an EMPTY chart body to empty arrays (no undefined .map crash)', () => {
    let result: IPayrollAnalyticsResult | undefined;
    service.getAnalytics('StatutoryBreakdown', allDepts).subscribe((r) => (result = r));
    httpMock.expectOne((r) => r.url === `${analyticsUrl}/StatutoryBreakdown`).flush({});

    expect(result?.chartType).toBe('');
    expect(result?.points).toEqual([]);
    expect(result?.categories).toEqual([]);
    expect(result?.series).toEqual([]);
  });

  it('getAnalytics defaults an absent point value to 0 (a zero-height bar, not NaN)', () => {
    let result: IPayrollAnalyticsResult | undefined;
    service.getAnalytics('DepartmentCostDistribution', allDepts).subscribe((r) => (result = r));
    httpMock
      .expectOne((r) => r.url === `${analyticsUrl}/DepartmentCostDistribution`)
      .flush({ chartType: 'DepartmentCostDistribution', points: [{ label: 'Ops' }] });

    expect(result?.points[0].label).toBe('Ops');
    expect(result?.points[0].value).toBe(0);
  });

  it('getDashboardAnalytics forkJoins the three analytics chart endpoints', () => {
    const make = (chartType: string): PayrollAnalyticsResultWire => ({
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
    const preview: BankAdvicePreviewWire = {
      payMonth: 5,
      payYear: 2026,
      employeeCount: 1,
      totalNetAmount: 5000,
      // The wire also carries `masked: true`, which the view-model has no field for (see the
      // OUT-OF-LANE ISSUE raised with this migration); it is dropped by the mapper today.
      masked: true,
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

    expect(result).toEqual({
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
      note: null,
    });
    expect(result?.lines[0].accountNumber).toBe('••••6789');
    expect(result?.lines[0]).not.toBe(preview.lines![0] as never);
  });

  it('getBankAdvicePreview never invents an account number for a sparse line', () => {
    let result: IBankAdvicePreview | undefined;
    service.getBankAdvicePreview(filters).subscribe((r) => (result = r));
    httpMock
      .expectOne((r) => r.url === `${reportsUrl}/bank-advice/preview`)
      .flush({ payMonth: 5, payYear: 2026, lines: [{ employeeNo: 'EMP002' }] });

    expect(result?.lines[0].employeeNo).toBe('EMP002');
    // Blank, not a plausible-looking fabricated number — this row feeds a bank disbursement file.
    expect(result?.lines[0].accountNumber).toBe('');
    expect(result?.lines[0].netAmount).toBe(0);
    expect(result?.employeeCount).toBe(0);
    expect(result?.totalNetAmount).toBe(0);
    expect(result?.note).toBeNull();
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

  // US-RPT-003 FR-6 / NFR-3: the un-masked reveal path.
  it('getBankAdviceFull GETs /reports/bank-advice/full with un-masked lines', () => {
    const full: BankAdvicePreviewWire = {
      payMonth: 5,
      payYear: 2026,
      employeeCount: 1,
      totalNetAmount: 5000,
      masked: false,
      lines: [
        {
          employeeNo: 'EMP001',
          employeeName: 'Alex HR',
          bankName: 'Acme Bank',
          branchCode: 'AC001',
          accountNumber: '1234566789',
          netAmount: 5000,
          narration: 'Salary May 2026',
        },
      ],
    };
    let result: IBankAdvicePreview | undefined;
    service.getBankAdviceFull(filters).subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/bank-advice/full`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('payMonth')).toBe('5');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(full);

    expect(result?.lines[0].accountNumber).toBe('1234566789');
  });

  // US-RPT-003 FR-4: a specific run id is forwarded as the payrollRunId param.
  it('forwards payrollRunId when a specific run is selected', () => {
    service
      .getReport('PayrollSummary', { period: '2026-05', departmentId: null, payrollRunId: 'run-9' })
      .subscribe();
    const req = httpMock.expectOne((r) => r.url === `${reportsUrl}/PayrollSummary`);
    expect(req.request.params.get('payrollRunId')).toBe('run-9');
    req.flush({});
  });
});
