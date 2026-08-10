import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { HttpResponse, HttpHeaders } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { signal } from '@angular/core';

import { PayrollReportsComponent } from './payroll-reports.component';
import { PayrollReportService } from '../../services/payroll-report.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { DepartmentService } from '../../../core-hr/departments/services/department.service';
import { IDepartment } from '../../../core-hr/departments/models/department.models';
import {
  IBankAdvicePreview,
  IPayrollRunSummary,
  IReportResult,
} from '../../models/payroll-report.models';

/**
 * US-PAY-009 (§8, AC-1/AC-2/AC-4): payroll reports page spec. PayrollReportService +
 * DepartmentService mocked (no HttpClient). Anchor `.click()` is stubbed in the
 * export/download tests so no real navigation occurs.
 */
describe('PayrollReportsComponent', () => {
  let fixture: ComponentFixture<PayrollReportsComponent>;
  let component: PayrollReportsComponent;
  let reportSpy: jasmine.SpyObj<PayrollReportService>;
  let deptSpy: jasmine.SpyObj<DepartmentService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  // A minimal AuthService stand-in: only the `permissions` signal is read by the
  // component (for the FR-6 reveal gate). Mutate before setup() to grant/deny.
  let permissions: ReturnType<typeof signal<string[]>>;
  let authStub: { permissions: typeof permissions };

  const departments = [
    { id: 'd1', name: 'Engineering' } as IDepartment,
    { id: 'd2', name: 'People' } as IDepartment,
  ];

  // PayrollSummary is a chart-bearing report → the FE derives a bar chart from these
  // rows (label = first cell, value = last numeric cell).
  const summary: IReportResult = {
    reportType: 'PayrollSummary',
    title: 'Payroll Summary',
    payMonth: 5,
    payYear: 2026,
    columns: ['Department', 'Total Net'],
    rows: [
      { cells: ['Engineering', '3,000.00'] },
      { cells: ['People', '6,000.00'] },
    ],
    totalRow: { cells: ['Total', '9,000.00'] },
    totalCount: 2,
    note: 'Showing the most-recent finalized run.',
  };

  // US-RPT-003 AC-1/FR-3: Payroll Run Summary with KPI metrics + MoM comparison.
  const runSummaryMeta: IPayrollRunSummary = {
    currency: 'USD',
    currentLabel: 'Mar 2026',
    previousLabel: 'Feb 2026',
    metrics: [
      { key: 'gross', label: 'Total Gross', current: 12000, previous: 10000, variance: 2000, isCost: true },
      { key: 'deductions', label: 'Total Deductions', current: 2000, previous: 2500, variance: -500, isCost: true },
      { key: 'net', label: 'Total Net Pay', current: 10000, previous: 7500, variance: 2500, isCost: true },
      { key: 'employeeCount', label: 'Employees', current: 50, previous: 48, variance: 2, isCost: false },
    ],
  };

  const runSummary: IReportResult = {
    ...summary,
    summary: runSummaryMeta,
  };

  const bankAdvice: IBankAdvicePreview = {
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

  // Same shape as `bankAdvice` but with a FULL (un-masked) account number (FR-6).
  const bankAdviceFull: IBankAdvicePreview = {
    ...bankAdvice,
    lines: [{ ...bankAdvice.lines[0], accountNumber: '1234566789' }],
  };

  function blobResponse(name: string): HttpResponse<Blob> {
    return new HttpResponse({
      body: new Blob(['data'], { type: 'application/octet-stream' }),
      headers: new HttpHeaders({ 'Content-Disposition': `attachment; filename="${name}"` }),
    });
  }

  function setup(): void {
    deptSpy.getDepartments.and.returnValue(of(departments));
    reportSpy.listReportTypes.and.returnValue(of([]));
    fixture = TestBed.createComponent(PayrollReportsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    reportSpy = jasmine.createSpyObj<PayrollReportService>('PayrollReportService', [
      'listReportTypes',
      'getReport',
      'exportReport',
      'getBankAdvicePreview',
      'downloadBankAdvice',
      'getBankAdviceFull',
    ]);
    deptSpy = jasmine.createSpyObj<DepartmentService>('DepartmentService', ['getDepartments']);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', ['error', 'success']);
    permissions = signal<string[]>([]);
    authStub = { permissions };

    // Stub the anchor click so blob downloads never trigger real navigation.
    spyOn(HTMLAnchorElement.prototype, 'click').and.stub();

    TestBed.configureTestingModule({
      imports: [PayrollReportsComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: PayrollReportService, useValue: reportSpy },
        { provide: DepartmentService, useValue: deptSpy },
        { provide: AuthService, useValue: authStub },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });
  });

  it('loads departments + report types on init and defaults to Payroll Summary', () => {
    setup();
    expect(deptSpy.getDepartments).toHaveBeenCalled();
    expect(reportSpy.listReportTypes).toHaveBeenCalled();
    expect(component.departments()).toEqual(departments);
    expect(component.activeType()).toBe('PayrollSummary');
  });

  it('renders the fallback report types in the sidebar when the BE list is empty', () => {
    setup();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="rt-PayrollSummary"]')).toBeTruthy();
    expect(el.querySelector('[data-test="rt-BankAdvice"]')).toBeTruthy();
    expect(el.querySelectorAll('[data-test^="rt-"]').length).toBe(8);
  });

  it('uses the BE descriptor list when one is returned', () => {
    deptSpy.getDepartments.and.returnValue(of(departments));
    reportSpy.listReportTypes.and.returnValue(
      of([{ id: 'Ctc', name: 'CTC Report', description: 'x', deferred: false }]),
    );
    fixture = TestBed.createComponent(PayrollReportsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.reportTypes().length).toBe(1);
    expect(component.reportTypes()[0].id).toBe('Ctc');
  });

  it('selecting a report clears the stale preview', () => {
    setup();
    component.report.set(summary);
    component.selectType('Ctc');
    expect(component.activeType()).toBe('Ctc');
    expect(component.report()).toBeNull();
  });

  it('generate() fetches the report with the current filters (AC-1)', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(summary));
    component.setDepartment('d1');
    component.generate();

    expect(reportSpy.getReport).toHaveBeenCalledWith('PayrollSummary', {
      period: component.period(),
      departmentId: 'd1',
      payrollRunId: null,
    });
    expect(component.report()).toEqual(summary);
    expect(component.isLoading()).toBeFalse();
  });

  it('renders the bar chart, the data table and the total row (AC-1)', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(summary));
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="report-chart"]')).toBeTruthy();
    expect(el.querySelectorAll('[data-test="report-row"]').length).toBe(2);
    expect(el.querySelector('[data-test="report-total"]')).toBeTruthy();
    expect(el.querySelector('[data-test="report-note"]')?.textContent).toContain('finalized');
  });

  it('derives chart bars from the rows, sorts desc and scales the widest to 100%', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(summary));
    component.generate();

    expect(component.sortedBars().map((b) => b.label)).toEqual(['People', 'Engineering']);
    expect(component.sortedBars().map((b) => b.value)).toEqual([6000, 3000]);
    expect(component.barWidth(6000)).toBe(100);
    expect(component.barWidth(3000)).toBe(50);
  });

  it('shows a toast and keeps report null on a generate error', () => {
    setup();
    reportSpy.getReport.and.returnValue(throwError(() => new Error('fail')));
    component.generate();
    expect(component.report()).toBeNull();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  // ─── Bank advice (AC-2 / BR-2) ──────────────────────────────
  it('generate() for Bank Advice fetches the masked preview', () => {
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    component.selectType('BankAdvice');
    component.generate();

    expect(reportSpy.getBankAdvicePreview).toHaveBeenCalled();
    expect(component.bankAdvice()).toEqual(bankAdvice);
  });

  it('renders MASKED account numbers in the bank advice preview (BR-2)', () => {
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    component.selectType('BankAdvice');
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const masked = el.querySelector('[data-test="masked-account"]');
    expect(masked?.textContent?.trim()).toBe('••••6789');
    expect(el.querySelector('[data-test="mask-note"]')).toBeTruthy();
    expect(el.querySelector('[data-test="download-full-btn"]')).toBeTruthy();
  });

  it('downloadBankAdvice() saves the full file (AC-2)', () => {
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    reportSpy.downloadBankAdvice.and.returnValue(of(blobResponse('bank-advice.csv')));
    component.selectType('BankAdvice');
    component.generate();

    component.downloadBankAdvice();

    expect(reportSpy.downloadBankAdvice).toHaveBeenCalledWith(
      { period: component.period(), departmentId: null, payrollRunId: null },
      'csv',
    );
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.isExporting()).toBeFalse();
  });

  // ─── Export (FR-2 / AC-4) ───────────────────────────────────
  it('exportAs() downloads a blob and triggers the anchor click', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(summary));
    reportSpy.exportReport.and.returnValue(of(blobResponse('report.xlsx')));
    component.generate();

    component.exportAs('xlsx');

    expect(reportSpy.exportReport).toHaveBeenCalledWith('PayrollSummary', jasmine.any(Object), 'xlsx');
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.isExporting()).toBeFalse();
  });

  it('exportAs() does nothing when there is no report loaded', () => {
    setup();
    component.exportAs('csv');
    expect(reportSpy.exportReport).not.toHaveBeenCalled();
  });

  it('exportAs() toasts an error and clears exporting on failure', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(summary));
    reportSpy.exportReport.and.returnValue(throwError(() => new Error('fail')));
    component.generate();

    component.exportAs('pdf');
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isExporting()).toBeFalse();
  });

  it('isNumericColumn detects numeric columns from the first data row', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(summary));
    component.generate();

    expect(component.isNumericColumn(0)).toBeFalse(); // Department (text)
    expect(component.isNumericColumn(1)).toBeTrue(); // Total Net (numeric)
  });

  // ─── US-RPT-003: KPI summary cards + MoM (AC-1 / FR-3) ──────
  it('renders one KPI card per summary metric with the MoM delta', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(runSummary));
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="kpi-cards"]')).toBeTruthy();
    expect(el.querySelectorAll('.kpi-card').length).toBe(4);
    expect(el.querySelector('[data-test="kpi-gross"]')).toBeTruthy();
    expect(el.querySelector('[data-test="kpi-delta-gross"]')?.textContent).toContain('+2,000.00');
  });

  it('colours a cost INCREASE red and a cost DECREASE green (FR-3 variance)', () => {
    setup();
    // gross: +2000 (increase, cost → red); deductions: -500 (decrease, cost → green).
    expect(component.varColor(runSummaryMeta.metrics[0])).toContain('red');
    expect(component.varColor(runSummaryMeta.metrics[1])).toContain('emerald');
    expect(component.varArrow(runSummaryMeta.metrics[0])).toBe('▲');
    expect(component.varArrow(runSummaryMeta.metrics[1])).toBe('▼');
  });

  it('colours a headcount change neutrally (not a cost metric)', () => {
    setup();
    const headcount = runSummaryMeta.metrics[3];
    expect(component.varColor(headcount)).toContain('neutral');
    expect(component.varDir(headcount)).toBe('up');
  });

  it('shows "No prior period" when a metric has no previous value', () => {
    setup();
    const noPrior: IPayrollRunSummary = {
      ...runSummaryMeta,
      previousLabel: null,
      metrics: [{ key: 'gross', label: 'Total Gross', current: 12000, previous: null, variance: null, isCost: true }],
    };
    reportSpy.getReport.and.returnValue(of({ ...summary, summary: noPrior }));
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="kpi-delta-gross"]')?.textContent).toContain('No prior period');
    expect(component.varDir(noPrior.metrics[0])).toBe('none');
  });

  it('renders the MoM dual bar chart with current + previous bars and alt text', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(runSummary));
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="mom-chart"]')).toBeTruthy();
    expect(el.querySelector('[data-test="mom-alt"]')?.textContent).toContain('Month-over-month');
    expect(component.momBars().length).toBe(4);
    // gross current (12000) is the largest value across all current+previous, so it scales to 100%.
    expect(component.momWidth(12000)).toBe(100);
    expect(component.momWidth(6000)).toBe(50);
  });

  it('does NOT render KPI cards for a report without a summary block', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(summary)); // no `summary`
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="kpi-cards"]')).toBeNull();
    expect(el.querySelector('[data-test="mom-chart"]')).toBeNull();
  });

  // ─── US-RPT-003: bank advice reveal toggle (FR-6 / NFR-3) ───
  it('HIDES the reveal toggle without Payroll.ViewSensitive', () => {
    permissions.set(['Payroll.View']); // no ViewSensitive
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    component.selectType('BankAdvice');
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(component.canRevealSensitive()).toBeFalse();
    expect(el.querySelector('[data-test="reveal-btn"]')).toBeNull();
  });

  it('SHOWS the reveal toggle with Payroll.ViewSensitive', () => {
    permissions.set(['Payroll.ViewSensitive']);
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    component.selectType('BankAdvice');
    component.generate();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(component.canRevealSensitive()).toBeTrue();
    expect(el.querySelector('[data-test="reveal-btn"]')).toBeTruthy();
  });

  it('toggleReveal() calls the full endpoint and shows un-masked numbers when permitted', () => {
    permissions.set(['Payroll.ViewSensitive']);
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    reportSpy.getBankAdviceFull.and.returnValue(of(bankAdviceFull));
    component.selectType('BankAdvice');
    component.generate();

    component.toggleReveal();

    expect(reportSpy.getBankAdviceFull).toHaveBeenCalledWith({
      period: component.period(),
      departmentId: null,
      payrollRunId: null,
    });
    expect(component.isRevealed()).toBeTrue();
    expect(component.bankAdviceView()?.lines[0].accountNumber).toBe('1234566789');
    expect(component.isRevealing()).toBeFalse();
  });

  it('toggleReveal() a second time HIDES the full numbers without re-calling the endpoint', () => {
    permissions.set(['Payroll.ViewSensitive']);
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    reportSpy.getBankAdviceFull.and.returnValue(of(bankAdviceFull));
    component.selectType('BankAdvice');
    component.generate();

    component.toggleReveal(); // reveal
    component.toggleReveal(); // hide

    expect(reportSpy.getBankAdviceFull).toHaveBeenCalledTimes(1);
    expect(component.isRevealed()).toBeFalse();
    expect(component.bankAdviceView()?.lines[0].accountNumber).toBe('••••6789');
  });

  it('toggleReveal() does nothing when the user lacks the permission', () => {
    permissions.set([]); // no permission
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    component.selectType('BankAdvice');
    component.generate();

    component.toggleReveal();

    expect(reportSpy.getBankAdviceFull).not.toHaveBeenCalled();
    expect(component.isRevealed()).toBeFalse();
  });

  it('regenerating bank advice returns to the masked view', () => {
    permissions.set(['Payroll.ViewSensitive']);
    setup();
    reportSpy.getBankAdvicePreview.and.returnValue(of(bankAdvice));
    reportSpy.getBankAdviceFull.and.returnValue(of(bankAdviceFull));
    component.selectType('BankAdvice');
    component.generate();
    component.toggleReveal();
    expect(component.isRevealed()).toBeTrue();

    component.generate(); // re-generate
    expect(component.isRevealed()).toBeFalse();
  });

  it('carries the selected payroll run id into the filters (FR-4)', () => {
    setup();
    reportSpy.getReport.and.returnValue(of(runSummary));
    component.setPayrollRun('run-123');
    component.generate();

    expect(reportSpy.getReport).toHaveBeenCalledWith('PayrollSummary', {
      period: component.period(),
      departmentId: null,
      payrollRunId: 'run-123',
    });
  });
});
