import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { HttpResponse, HttpHeaders } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { PayrollReportsComponent } from './payroll-reports.component';
import { PayrollReportService } from '../../services/payroll-report.service';
import { DepartmentService } from '../../../core-hr/departments/services/department.service';
import { IDepartment } from '../../../core-hr/departments/models/department.models';
import {
  IBankAdvicePreview,
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

  const departments = [
    { departmentId: 'd1', name: 'Engineering' } as IDepartment,
    { departmentId: 'd2', name: 'People' } as IDepartment,
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
    ]);
    deptSpy = jasmine.createSpyObj<DepartmentService>('DepartmentService', ['getDepartments']);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', ['error', 'success']);

    // Stub the anchor click so blob downloads never trigger real navigation.
    spyOn(HTMLAnchorElement.prototype, 'click').and.stub();

    TestBed.configureTestingModule({
      imports: [PayrollReportsComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: PayrollReportService, useValue: reportSpy },
        { provide: DepartmentService, useValue: deptSpy },
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
      { period: component.period(), departmentId: null },
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
});
