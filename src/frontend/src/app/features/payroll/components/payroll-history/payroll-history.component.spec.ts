import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { PayrollHistoryComponent } from './payroll-history.component';
import { AuditService } from '../../services/audit.service';
import { IPage, IPayrollHistoryRun } from '../../models/audit.models';

describe('PayrollHistoryComponent', () => {
  let fixture: ComponentFixture<PayrollHistoryComponent>;
  let component: PayrollHistoryComponent;
  let audit: jasmine.SpyObj<AuditService>;

  const may2026: IPayrollHistoryRun = {
    runId: 'r-1',
    payMonth: 5,
    payYear: 2026,
    period: '2026-05',
    status: 'Finalized',
    employeeCount: 250,
    totalNet: 800000,
    totalGross: 1000000,
    totalDeductions: 200000,
    initiatedBy: 'u-1',
    initiatedAt: '2026-05-25T09:00:00Z',
    approvedBy: 'u-2',
    approvedAt: '2026-05-30T09:00:00Z',
    finalizedAt: '2026-05-31T12:00:00Z',
  };
  const apr2026: IPayrollHistoryRun = {
    runId: 'r-2',
    payMonth: 4,
    payYear: 2026,
    period: '2026-04',
    status: 'AwaitingApproval',
    employeeCount: 240,
    totalNet: 760000,
    totalGross: 950000,
    totalDeductions: 190000,
    initiatedBy: 'u-1',
    initiatedAt: '2026-04-25T09:00:00Z',
    approvedBy: null,
    approvedAt: null,
    finalizedAt: null,
  };
  const dec2025: IPayrollHistoryRun = {
    runId: 'r-3',
    payMonth: 12,
    payYear: 2025,
    period: '2025-12',
    status: 'Finalized',
    employeeCount: 230,
    totalNet: 700000,
    totalGross: 880000,
    totalDeductions: 180000,
    initiatedBy: 'u-1',
    initiatedAt: '2025-12-25T09:00:00Z',
    approvedBy: 'u-2',
    approvedAt: '2025-12-30T09:00:00Z',
    finalizedAt: '2025-12-31T12:00:00Z',
  };

  function page(rows: IPayrollHistoryRun[]): IPage<IPayrollHistoryRun> {
    return { items: rows, totalCount: rows.length, page: 1, pageSize: 25 };
  }

  function setup(rows: IPayrollHistoryRun[] = [may2026, apr2026, dec2025]): void {
    audit = jasmine.createSpyObj<AuditService>('AuditService', ['getHistory']);
    audit.getHistory.and.returnValue(of(page(rows)));

    TestBed.configureTestingModule({
      imports: [PayrollHistoryComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: AuditService, useValue: audit },
      ],
    });

    fixture = TestBed.createComponent(PayrollHistoryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('loads history on init', () => {
    setup();
    expect(audit.getHistory).toHaveBeenCalled();
    expect(component.runs().length).toBe(3);
    expect(component.loading()).toBeFalse();
  });

  it('sets an error when history fails to load', () => {
    setup();
    audit.getHistory.and.returnValue(throwError(() => new Error('boom')));
    component.load();
    expect(component.error()).toBe('Could not load payroll history.');
  });

  it('derives the distinct years newest-first for the year filter', () => {
    setup();
    expect(component.years()).toEqual([2026, 2025]);
  });

  it('filters by year', () => {
    setup();
    component.setYear(2025);
    expect(component.visibleRuns().map((r) => r.runId)).toEqual(['r-3']);
  });

  it('filters by status', () => {
    setup();
    component.statusFilter.set('Finalized');
    expect(
      component.visibleRuns().every((r) => r.status === 'Finalized'),
    ).toBeTrue();
    expect(component.visibleRuns().length).toBe(2);
  });

  it('combines year + status filters', () => {
    setup();
    component.setYear(2026);
    component.statusFilter.set('Finalized');
    expect(component.visibleRuns().map((r) => r.runId)).toEqual(['r-1']);
  });

  it('onStatusChange maps an empty selection to null (all statuses)', () => {
    setup();
    component.onStatusChange({
      target: { value: 'Finalized' },
    } as unknown as Event);
    expect(component.statusFilter()).toBe('Finalized');
    component.onStatusChange({
      target: { value: '' },
    } as unknown as Event);
    expect(component.statusFilter()).toBeNull();
  });

  it('sorts by period (default desc: newest first)', () => {
    setup();
    // default sortKey is 'period', sortAsc false → newest period first
    expect(component.visibleRuns().map((r) => r.runId)).toEqual([
      'r-1',
      'r-2',
      'r-3',
    ]);
  });

  it('toggles sort direction when the same key is clicked twice', () => {
    setup();
    component.sortBy('net'); // first click → asc
    expect(component.sortAsc()).toBeTrue();
    expect(component.visibleRuns()[0].runId).toBe('r-3'); // lowest net
    component.sortBy('net'); // second click → desc
    expect(component.sortAsc()).toBeFalse();
    expect(component.visibleRuns()[0].runId).toBe('r-1'); // highest net
  });

  it('switches sort key resetting to ascending', () => {
    setup();
    component.sortBy('employees');
    expect(component.sortKey()).toBe('employees');
    expect(component.sortAsc()).toBeTrue();
    expect(component.visibleRuns()[0].runId).toBe('r-3'); // fewest employees
  });

  it('sortIndicator only shows an arrow on the active key', () => {
    setup();
    component.sortBy('employees');
    expect(component.sortIndicator('employees')).toBe('↑');
    expect(component.sortIndicator('net')).toBe('');
  });

  it('sorts finalized treating null finalizedAt as epoch', () => {
    setup();
    component.sortBy('finalized'); // asc
    // r-2 has null finalizedAt → epoch → first when ascending
    expect(component.visibleRuns()[0].runId).toBe('r-2');
  });

  it('formats the period label from month + year', () => {
    setup();
    expect(component.periodLabel(may2026)).toBe('May 2026');
  });

  it('renders a history row per run in the desktop table', () => {
    setup();
    const rows = fixture.nativeElement.querySelectorAll(
      '[data-test="history-row"]',
    );
    expect(rows.length).toBe(3);
  });

  it('shows the empty state when there is no history', () => {
    setup([]);
    expect(
      fixture.nativeElement.querySelector('[data-test="empty"]'),
    ).toBeTruthy();
  });
});
