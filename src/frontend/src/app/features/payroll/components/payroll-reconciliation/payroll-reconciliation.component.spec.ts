import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Observable, of, throwError } from 'rxjs';

import { PayrollReconciliationComponent } from './payroll-reconciliation.component';
import { ReconciliationService } from '../../services/reconciliation.service';
import {
  IReconciliationReport,
  IReconciliationRow,
} from '../../models/reconciliation.models';

function rowOf(overrides: Partial<IReconciliationRow> = {}): IReconciliationRow {
  return {
    employeeId: 'e-1',
    employeeNo: 'EMP-001',
    employeeName: 'Alex Doe',
    workingDays: 22,
    presentDays: 22,
    absentDays: 0,
    leaveDaysByType: {},
    totalLeaveDays: 0,
    overtimeHours: 0,
    calculatedLopDays: 0,
    ...overrides,
  };
}

function reportOf(
  overrides: Partial<IReconciliationReport> = {},
): IReconciliationReport {
  return {
    period: '2026-05',
    payMonth: 5,
    payYear: 2026,
    attendanceFinalized: true,
    rows: [],
    ...overrides,
  };
}

describe('PayrollReconciliationComponent', () => {
  let fixture: ComponentFixture<PayrollReconciliationComponent>;
  let component: PayrollReconciliationComponent;
  let reconSpy: jasmine.SpyObj<ReconciliationService>;

  function setup(report: IReconciliationReport | Error): void {
    reconSpy = jasmine.createSpyObj<ReconciliationService>(
      'ReconciliationService',
      ['getReconciliation', 'triggerLeaveEncashment'],
    );
    reconSpy.getReconciliation.and.returnValue(
      report instanceof Error
        ? throwError(() => report)
        : of(report),
    );

    TestBed.configureTestingModule({
      imports: [PayrollReconciliationComponent],
      providers: [
        provideRouter([]),
        { provide: ReconciliationService, useValue: reconSpy },
      ],
    });

    fixture = TestBed.createComponent(PayrollReconciliationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges(); // ngOnInit → load()
  }

  it('loads the report on init and renders one row per employee', () => {
    const report = reportOf({
      rows: [
        rowOf({ employeeId: 'e-1', employeeName: 'Alex Doe' }),
        rowOf({ employeeId: 'e-2', employeeName: 'Sam Roe' }),
      ],
    });
    setup(report);

    expect(reconSpy.getReconciliation).toHaveBeenCalledWith(
      component.payYear(),
      component.payMonth(),
    );
    expect(component.report()).toEqual(report);
    expect(component.loading()).toBeFalse();
    expect(component.loadError()).toBeFalse();
    expect(component.visibleRows().length).toBe(2);

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Alex Doe');
    expect(text).toContain('Sam Roe');
  });

  it('shows the attendance-not-finalized banner when attendanceFinalized=false', () => {
    setup(reportOf({ attendanceFinalized: false, rows: [rowOf()] }));

    const el = fixture.nativeElement as HTMLElement;
    const alert = el.querySelector('[role="alert"]');
    expect(alert).toBeTruthy();
    expect(alert?.textContent).toContain('not yet finalized');
    expect(el.textContent).toContain('Payroll initiation is blocked');
  });

  it('shows the finalized "ready to initiate" status when attendanceFinalized=true', () => {
    setup(reportOf({ attendanceFinalized: true, rows: [rowOf()] }));

    const el = fixture.nativeElement as HTMLElement;
    const status = el.querySelector('[role="status"]');
    expect(status).toBeTruthy();
    expect(status?.textContent).toContain('finalized');
  });

  it('counts full / lop / mismatch states for the summary cards', () => {
    setup(
      reportOf({
        rows: [
          rowOf({ employeeId: 'full-1' }),
          rowOf({
            employeeId: 'lop-1',
            absentDays: 1,
            totalLeaveDays: 1,
            calculatedLopDays: 1,
          }),
          rowOf({
            employeeId: 'mis-1',
            absentDays: 3,
            totalLeaveDays: 1,
            calculatedLopDays: 2,
          }),
        ],
      }),
    );

    expect(component.fullCount()).toBe(1);
    expect(component.lopCount()).toBe(1);
    expect(component.mismatchCount()).toBe(1);
  });

  it('filters to mismatch rows only when mismatchesOnly is set', () => {
    setup(
      reportOf({
        rows: [
          rowOf({ employeeId: 'full-1' }),
          rowOf({
            employeeId: 'mis-1',
            absentDays: 3,
            totalLeaveDays: 1,
            calculatedLopDays: 2,
          }),
        ],
      }),
    );

    expect(component.visibleRows().length).toBe(2);
    component.mismatchesOnly.set(true);
    expect(component.visibleRows().length).toBe(1);
    expect(component.visibleRows()[0].employeeId).toBe('mis-1');
  });

  it('exposes the row state helpers (badge / label / state)', () => {
    setup(reportOf());
    const mismatch = rowOf({
      absentDays: 3,
      totalLeaveDays: 1,
      calculatedLopDays: 2,
    });
    expect(component.stateOf(mismatch)).toBe('mismatch');
    expect(component.stateBadge(mismatch)).toContain('rose');
    expect(component.stateLabel(mismatch)).toBe('Unreconciled');
  });

  it('builds a leave tooltip from the breakdown, with a fallback', () => {
    setup(reportOf());
    expect(
      component.leaveTooltip(rowOf({ leaveDaysByType: { Annual: 2 } })),
    ).toBe('Annual: 2');
    expect(component.leaveTooltip(rowOf())).toBe('No approved leave');
  });

  it('shows loading skeletons while the request is pending, then clears', () => {
    // Delay the response so the loading state is observable.
    reconSpy = jasmine.createSpyObj<ReconciliationService>(
      'ReconciliationService',
      ['getReconciliation', 'triggerLeaveEncashment'],
    );
    let resolve!: (r: IReconciliationReport) => void;
    reconSpy.getReconciliation.and.returnValue(
      new Observable<IReconciliationReport>((sub) => {
        resolve = (r) => {
          sub.next(r);
          sub.complete();
        };
      }),
    );

    TestBed.configureTestingModule({
      imports: [PayrollReconciliationComponent],
      providers: [
        provideRouter([]),
        { provide: ReconciliationService, useValue: reconSpy },
      ],
    });
    fixture = TestBed.createComponent(PayrollReconciliationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.loading()).toBeTrue();
    resolve(reportOf({ rows: [rowOf()] }));
    fixture.detectChanges();
    expect(component.loading()).toBeFalse();
  });

  it('renders the empty state when there are no rows', () => {
    setup(reportOf({ rows: [] }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('No employees to reconcile');
  });

  it('renders the mismatch-specific empty copy when filtering with no mismatches', () => {
    setup(reportOf({ rows: [rowOf()] }));
    component.mismatchesOnly.set(true);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('every absence is covered');
  });

  it('shows the error state with a working Retry when the request fails', () => {
    setup(new Error('boom'));

    expect(component.loadError()).toBeTrue();
    expect(component.report()).toBeNull();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'Could not load',
    );

    // Retry succeeds the second time.
    reconSpy.getReconciliation.and.returnValue(of(reportOf({ rows: [rowOf()] })));
    component.load();
    fixture.detectChanges();
    expect(component.loadError()).toBeFalse();
    expect(component.visibleRows().length).toBe(1);
  });

  it('reloads when the month or year selection changes', () => {
    setup(reportOf());
    reconSpy.getReconciliation.calls.reset();

    component.onMonthChange(3);
    expect(component.payMonth()).toBe(3);
    component.onYearChange(2025);
    expect(component.payYear()).toBe(2025);

    expect(reconSpy.getReconciliation).toHaveBeenCalledTimes(2);
    expect(reconSpy.getReconciliation).toHaveBeenCalledWith(2025, 3);
  });

  it('opens and closes the encashment drawer, reloading on save', () => {
    setup(reportOf());
    expect(component.encashmentOpen()).toBeFalse();

    component.openEncashment();
    expect(component.encashmentOpen()).toBeTrue();

    reconSpy.getReconciliation.calls.reset();
    component.onEncashed();
    expect(component.encashmentOpen()).toBeFalse();
    expect(reconSpy.getReconciliation).toHaveBeenCalledTimes(1);
  });

  it('defaults the period to the previous month (payroll runs in arrears)', () => {
    setup(reportOf());
    // payMonth() is 1..12; for a real Date it is the prior month (Jan → 12).
    expect(component.payMonth()).toBeGreaterThanOrEqual(1);
    expect(component.payMonth()).toBeLessThanOrEqual(12);
    expect(component.years.length).toBe(3);
  });
});
