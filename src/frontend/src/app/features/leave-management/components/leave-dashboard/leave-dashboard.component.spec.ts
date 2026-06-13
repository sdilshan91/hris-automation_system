import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LeaveDashboardComponent } from './leave-dashboard.component';
import { LeaveDashboardService } from '../../services/leave-dashboard.service';
import { LeaveRequestService } from '../../services/leave-request.service';
import { ILeaveBalanceSummary, ILeaveLedgerEntry } from '../../models/leave-dashboard.models';
import { ILeaveRequest } from '../../models/leave-request.models';

describe('LeaveDashboardComponent (US-LV-006)', () => {
  let component: LeaveDashboardComponent;
  let fixture: ComponentFixture<LeaveDashboardComponent>;
  let dashSpy: jasmine.SpyObj<LeaveDashboardService>;
  let reqSpy: jasmine.SpyObj<LeaveRequestService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const balances: ILeaveBalanceSummary[] = [
    {
      leaveTypeId: 'lt-1',
      leaveTypeName: 'Annual Leave',
      color: '#2563eb',
      entitlement: 14,
      used: 4,
      pending: 2,
      balance: 10,
      carryForward: 0,
      expired: 0,
    },
    {
      leaveTypeId: 'lt-2',
      leaveTypeName: 'Sick Leave',
      color: '#dc2626',
      entitlement: 7,
      used: 1,
      pending: 0,
      balance: 6,
      carryForward: 0,
      expired: 0,
    },
    {
      leaveTypeId: 'lt-3',
      leaveTypeName: 'Old Casual Leave',
      color: '#16a34a',
      entitlement: 0,
      used: 0,
      pending: 0,
      balance: 2,
      carryForward: 2,
      expired: 0,
      isArchived: true,
    },
  ];

  const ledger: ILeaveLedgerEntry[] = [
    {
      ledgerId: 'led-1',
      leaveTypeId: 'lt-1',
      leaveYear: 2026,
      entryType: 'Accrual',
      amount: 14,
      balanceAfter: 14,
      description: 'Upfront allocation',
      occurredAt: '2026-01-01T00:00:00Z',
    },
    {
      ledgerId: 'led-2',
      leaveTypeId: 'lt-1',
      leaveYear: 2026,
      entryType: 'Used',
      amount: -4,
      balanceAfter: 10,
      description: 'July trip',
      occurredAt: '2026-07-06T00:00:00Z',
    },
  ];

  const upcoming: ILeaveRequest[] = [
    {
      leaveRequestId: 'lr-1',
      tenantId: 'tenant-1',
      employeeId: 'emp-1',
      leaveTypeId: 'lt-1',
      leaveTypeName: 'Annual Leave',
      leaveTypeColor: '#2563eb',
      startDate: '2026-08-01',
      endDate: '2026-08-03',
      isHalfDay: false,
      halfDaySession: null,
      totalDays: 3,
      reason: 'Trip',
      status: 'Approved',
      requestedAt: '2026-06-13T10:00:00Z',
      attachmentUrls: [],
    },
  ];

  const myRequests: ILeaveRequest[] = [
    { ...upcoming[0], leaveRequestId: 'lr-2', status: 'Approved', startDate: '2026-02-01', endDate: '2026-02-02' },
    { ...upcoming[0], leaveRequestId: 'lr-3', status: 'Rejected', startDate: '2026-03-01', endDate: '2026-03-02' },
    { ...upcoming[0], leaveRequestId: 'lr-4', status: 'Pending', startDate: '2026-09-01', endDate: '2026-09-02' },
  ];

  function setup(initialBalances: ILeaveBalanceSummary[] = balances): void {
    dashSpy = jasmine.createSpyObj('LeaveDashboardService', [
      'getMyBalance',
      'getMyLedger',
      'getMyUpcoming',
    ]);
    dashSpy.getMyBalance.and.returnValue(of(initialBalances));
    dashSpy.getMyLedger.and.returnValue(of(ledger));
    dashSpy.getMyUpcoming.and.returnValue(of(upcoming));

    reqSpy = jasmine.createSpyObj('LeaveRequestService', ['getMyLeaveRequests']);
    reqSpy.getMyLeaveRequests.and.returnValue(of(myRequests));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    TestBed.configureTestingModule({
      imports: [LeaveDashboardComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LeaveDashboardService, useValue: dashSpy },
        { provide: LeaveRequestService, useValue: reqSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(LeaveDashboardComponent);
    component = fixture.componentInstance;
  }

  it('loads balances, upcoming and history on init', () => {
    setup();
    fixture.detectChanges();
    expect(component.balances().length).toBe(3);
    expect(dashSpy.getMyBalance).toHaveBeenCalledWith(component.selectedYear());
    expect(dashSpy.getMyUpcoming).toHaveBeenCalled();
    expect(reqSpy.getMyLeaveRequests).toHaveBeenCalled();
  });

  it('renders a balance card per active leave type (AC-1, AC-4 grid)', () => {
    setup();
    fixture.detectChanges();
    expect(component.activeBalances().length).toBe(2); // archived excluded
    const cards = fixture.nativeElement.querySelectorAll('.balance-card');
    // 2 active cards; archived collapsed by default
    expect(cards.length).toBe(2);
  });

  it('separates archived leave types (BR-3)', () => {
    setup();
    fixture.detectChanges();
    expect(component.archivedBalances().length).toBe(1);
    expect(component.archivedBalances()[0].leaveTypeId).toBe('lt-3');
  });

  it('shows the awaiting-approval value separately from balance (AC-1, BR-2)', () => {
    setup();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const cell = el.querySelector('[data-testid="pending-value"]');
    expect(cell?.textContent?.trim()).toBe('2');
    // balance still 10 — not reduced by the 2 awaiting-approval days
    expect(component.activeBalances()[0].balance).toBe(10);
  });

  it('computes the arc dash-offset from used/entitlement (4/14)', () => {
    setup();
    fixture.detectChanges();
    const b = component.activeBalances()[0];
    const expected = component.circumference * (1 - 4 / 14);
    expect(component.dashOffset(b)).toBeCloseTo(expected, 5);
  });

  it('arc offset is full circumference (0% used) for a zero-entitlement type', () => {
    setup();
    fixture.detectChanges();
    const archived = component.archivedBalances()[0]; // entitlement 0
    expect(component.dashOffset(archived)).toBeCloseTo(component.circumference, 5);
  });

  it('renders the empty state when there are no balances (AC-5)', () => {
    setup([]);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const empty = el.querySelector('[data-testid="empty-state"]');
    expect(empty).toBeTruthy();
    expect(empty?.textContent).toContain('Your leave balances are being set up');
  });

  it('year selector triggers a reload for the chosen year (BR-5)', () => {
    setup();
    fixture.detectChanges();
    const current = component.selectedYear();
    const previous = current - 1;
    dashSpy.getMyBalance.calls.reset();

    component.selectYear(previous);
    expect(component.selectedYear()).toBe(previous);
    expect(dashSpy.getMyBalance).toHaveBeenCalledWith(previous);
  });

  it('does not reload when the same year is re-selected', () => {
    setup();
    fixture.detectChanges();
    dashSpy.getMyBalance.calls.reset();
    component.selectYear(component.selectedYear());
    expect(dashSpy.getMyBalance).not.toHaveBeenCalled();
  });

  it('opens the ledger detail for a card and loads its entries (AC-2)', () => {
    setup();
    fixture.detectChanges();
    component.openLedger(component.activeBalances()[0]);
    fixture.detectChanges();
    expect(component.ledgerOpen()).toBeTrue();
    expect(component.selectedBalance()?.leaveTypeId).toBe('lt-1');
    expect(dashSpy.getMyLedger).toHaveBeenCalledWith('lt-1', component.selectedYear());
    expect(component.ledger().length).toBe(2);
    const table = fixture.nativeElement.querySelector('[data-testid="ledger-table"]');
    expect(table).toBeTruthy();
  });

  it('closes the ledger', () => {
    setup();
    fixture.detectChanges();
    component.openLedger(component.activeBalances()[0]);
    component.closeLedger();
    expect(component.ledgerOpen()).toBeFalse();
    expect(component.selectedBalance()).toBeNull();
  });

  it('renders the upcoming leaves timeline (AC-3)', () => {
    setup();
    fixture.detectChanges();
    expect(component.upcoming().length).toBe(1);
    const list = fixture.nativeElement.querySelector('[data-testid="upcoming-list"]');
    expect(list).toBeTruthy();
  });

  it('history keeps only terminal-state past requests (FR-6)', () => {
    setup();
    fixture.detectChanges();
    // myRequests has Approved, Rejected, and an awaiting-approval one -> history excludes the latter
    expect(component.history().length).toBe(2);
    expect(component.history().some((h) => h.status === 'Pending')).toBeFalse();
  });

  it('filters history by status', () => {
    setup();
    fixture.detectChanges();
    component.setHistoryFilter('Rejected');
    expect(component.filteredHistory().length).toBe(1);
    expect(component.filteredHistory()[0].status).toBe('Rejected');
    component.setHistoryFilter('All');
    expect(component.filteredHistory().length).toBe(2);
  });

  it('arc carries an aria-label with text values (NFR-4 a11y)', () => {
    setup();
    fixture.detectChanges();
    const svg = fixture.nativeElement.querySelector('svg[role="img"]');
    expect(svg).toBeTruthy();
    const label = svg?.getAttribute('aria-label') ?? '';
    expect(label).toContain('Annual Leave');
    expect(label).toContain('4 of 14 days used');
    expect(label).toContain('10 remaining');
  });

  it('shows an error toast when balance load fails', () => {
    setup();
    dashSpy.getMyBalance.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(component.isLoadingBalances()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalled();
  });
});
