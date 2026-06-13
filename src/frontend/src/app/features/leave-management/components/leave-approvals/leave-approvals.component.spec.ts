import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { HttpErrorResponse } from '@angular/common/http';
import { LeaveApprovalsComponent } from './leave-approvals.component';
import { LeaveApprovalsService } from '../../services/leave-approvals.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import {
  IPendingLeaveRequest,
  IPendingLeaveResponse,
  ILeaveActionResult,
  balanceTier,
} from '../../models/pending-leave.models';

function makeReq(overrides: Partial<IPendingLeaveRequest> = {}): IPendingLeaveRequest {
  return {
    requestId: 'lr-1',
    employeeId: 'emp-1',
    employeeName: 'Ada Lovelace',
    employeePhoto: null,
    leaveTypeName: 'Annual Leave',
    leaveTypeColor: '#2563eb',
    startDate: '2026-07-06',
    endDate: '2026-07-08',
    totalDays: 3,
    reason: 'Vacation',
    hasAttachments: false,
    currentBalance: 10,
    entitlementDays: 14,
    requestedAt: '2026-06-13T10:00:00Z',
    isOverdue: false,
    teamConflictCount: 0,
    ...overrides,
  };
}

describe('LeaveApprovalsComponent', () => {
  let component: LeaveApprovalsComponent;
  let fixture: ComponentFixture<LeaveApprovalsComponent>;
  let approvalsSpy: jasmine.SpyObj<LeaveApprovalsService>;
  let typeSpy: jasmine.SpyObj<LeaveTypeService>;

  const page1: IPendingLeaveResponse = {
    items: [
      makeReq(),
      makeReq({
        requestId: 'lr-2',
        employeeId: 'emp-2',
        employeeName: 'Alan Turing',
        isOverdue: true,
        currentBalance: 1,
        entitlementDays: 14,
      }),
    ],
    totalCount: 45,
  };

  beforeEach(async () => {
    approvalsSpy = jasmine.createSpyObj('LeaveApprovalsService', [
      'getPendingQueue',
      'approve',
      'reject',
    ]);
    approvalsSpy.getPendingQueue.and.returnValue(of(page1));
    approvalsSpy.approve.and.returnValue(
      of({ requestId: 'lr-1', status: 'Approved' } as ILeaveActionResult)
    );
    approvalsSpy.reject.and.returnValue(
      of({ requestId: 'lr-1', status: 'Rejected' } as ILeaveActionResult)
    );
    typeSpy = jasmine.createSpyObj('LeaveTypeService', ['getLeaveTypes']);
    typeSpy.getLeaveTypes.and.returnValue(
      of([{ leaveTypeId: 'lt-1', name: 'Annual Leave', color: '#2563eb' } as any])
    );

    await TestBed.configureTestingModule({
      imports: [LeaveApprovalsComponent],
      providers: [
        { provide: LeaveApprovalsService, useValue: approvalsSpy },
        { provide: LeaveTypeService, useValue: typeSpy },
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LeaveApprovalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load the queue on init', () => {
    expect(component).toBeTruthy();
    expect(approvalsSpy.getPendingQueue).toHaveBeenCalledTimes(1);
    expect(component.requests().length).toBe(2);
    expect(component.totalCount()).toBe(45);
  });

  it('should render a row per request and an overdue tag for overdue requests', () => {
    const rows = fixture.nativeElement.querySelectorAll('[data-test="queue-row"]');
    expect(rows.length).toBe(2);
    const overdueTags = fixture.nativeElement.querySelectorAll('[data-test="overdue-tag"]');
    expect(overdueTags.length).toBe(1);
  });

  it('should apply red left-border to overdue rows', () => {
    const rows = fixture.nativeElement.querySelectorAll('[data-test="queue-row"]');
    expect(rows[1].classList).toContain('row-overdue');
    expect(rows[0].classList).not.toContain('row-overdue');
  });

  // ─── Balance pill color thresholds (§8) ────────────────────────
  describe('balance pill tiers', () => {
    it('green when remaining > 50% of entitlement', () => {
      expect(balanceTier(10, 14)).toBe('high');
    });
    it('yellow when remaining is 20-50%', () => {
      expect(balanceTier(3, 10)).toBe('medium');
      expect(balanceTier(2, 10)).toBe('medium');
    });
    it('red when remaining < 20%', () => {
      expect(balanceTier(1, 14)).toBe('low');
    });
    it('red when remaining is negative', () => {
      expect(balanceTier(-2, 14)).toBe('low');
    });
    it('neutral when entitlement is unknown/zero and remaining >= 0', () => {
      expect(balanceTier(5, undefined)).toBe('none');
      expect(balanceTier(0, 0)).toBe('none');
    });
    it('red (sign-based) when entitlement is unknown but remaining is negative', () => {
      expect(balanceTier(-1, undefined)).toBe('low');
    });
    it('balanceClass returns a tier class string', () => {
      const cls = component.balanceClass(makeReq());
      expect(cls).toContain('green');
    });
    it('balanceLabel shows remaining/entitlement when entitlement is known', () => {
      expect(component.balanceLabel(makeReq())).toBe('10/14');
    });
    it('balanceLabel shows just remaining when entitlement is unknown', () => {
      expect(component.balanceLabel(makeReq({ entitlementDays: undefined }))).toBe('10');
    });
  });

  // ─── Pagination (AC-2, server-side) ────────────────────────────
  describe('pagination', () => {
    it('computes total pages from totalCount / pageSize', () => {
      expect(component.totalPages()).toBe(3); // 45 / 20 -> 3
    });

    it('goToPage triggers a reload with the new page', () => {
      approvalsSpy.getPendingQueue.calls.reset();
      component.goToPage(2);
      expect(component.currentPage()).toBe(2);
      expect(approvalsSpy.getPendingQueue).toHaveBeenCalledTimes(1);
      expect(approvalsSpy.getPendingQueue.calls.mostRecent().args[0].page).toBe(2);
    });

    it('does not navigate past bounds', () => {
      approvalsSpy.getPendingQueue.calls.reset();
      component.goToPage(0);
      component.goToPage(99);
      expect(approvalsSpy.getPendingQueue).not.toHaveBeenCalled();
      expect(component.currentPage()).toBe(1);
    });

    it('changing page size resets to page 1 and reloads', () => {
      component.goToPage(2);
      approvalsSpy.getPendingQueue.calls.reset();
      component.onPageSizeChange(50);
      expect(component.pageSize()).toBe(50);
      expect(component.currentPage()).toBe(1);
      expect(approvalsSpy.getPendingQueue.calls.mostRecent().args[0].pageSize).toBe(50);
    });
  });

  // ─── Filters (AC-3, server-side round-trip) ────────────────────
  describe('filters', () => {
    it('applyFilters sends the chosen filters to the server and resets page', () => {
      component.goToPage(3);
      component.filterLeaveTypeId.set('lt-1');
      component.filterStartDate.set('2026-07-01');
      approvalsSpy.getPendingQueue.calls.reset();

      component.applyFilters();

      expect(component.currentPage()).toBe(1);
      const arg = approvalsSpy.getPendingQueue.calls.mostRecent().args[0];
      expect(arg.leaveTypeId).toBe('lt-1');
      expect(arg.startDate).toBe('2026-07-01');
    });

    it('builds active filter chips from applied filters', () => {
      component.filterLeaveTypeId.set('lt-1');
      component.applyFilters();
      const chips = component.activeFilterChips();
      expect(chips.some((c) => c.filterKey === 'leaveTypeId')).toBeTrue();
      expect(component.hasActiveFilters()).toBeTrue();
    });

    it('removeChip clears that filter and reloads', () => {
      component.filterLeaveTypeId.set('lt-1');
      component.applyFilters();
      approvalsSpy.getPendingQueue.calls.reset();

      component.removeChip({ category: 'Type', label: 'Annual Leave', filterKey: 'leaveTypeId' });

      expect(component.filterLeaveTypeId()).toBeNull();
      expect(approvalsSpy.getPendingQueue).toHaveBeenCalledTimes(1);
      expect(component.activeFilterChips().length).toBe(0);
    });

    it('clearFilters resets every filter and reloads from page 1', () => {
      component.goToPage(2);
      component.filterEmployeeId.set('emp-2');
      component.applyFilters();
      approvalsSpy.getPendingQueue.calls.reset();

      component.clearFilters();

      expect(component.filterEmployeeId()).toBeNull();
      expect(component.currentPage()).toBe(1);
      expect(component.hasActiveFilters()).toBeFalse();
      expect(approvalsSpy.getPendingQueue).toHaveBeenCalled();
    });

    it('onSortChange resets page and reloads with the new sort', () => {
      approvalsSpy.getPendingQueue.calls.reset();
      component.onSortChange('startDate');
      expect(component.sortBy()).toBe('startDate');
      expect(approvalsSpy.getPendingQueue.calls.mostRecent().args[0].sortBy).toBe('startDate');
    });

    it('derives employee filter options from the result set', () => {
      const opts = component.employeeOptions();
      expect(opts.length).toBe(2);
      expect(opts.map((o) => o.id)).toContain('emp-2');
    });
  });

  // ─── Detail panel (AC-4) ───────────────────────────────────────
  describe('detail panel', () => {
    it('opens the slide-over with the selected request', () => {
      component.openDetail(page1.items[0]);
      fixture.detectChanges();
      expect(component.selected()?.requestId).toBe('lr-1');
      const panel = fixture.nativeElement.querySelector('[data-test="detail-panel"]');
      expect(panel).toBeTruthy();
    });

    it('renders enabled approve/reject buttons (US-LV-005)', () => {
      component.openDetail(page1.items[0]);
      fixture.detectChanges();
      const approve = fixture.nativeElement.querySelector('[data-test="approve-btn"]');
      const reject = fixture.nativeElement.querySelector('[data-test="reject-btn"]');
      expect(approve.disabled).toBeFalse();
      expect(reject.disabled).toBeFalse();
    });

    it('closeDetail clears the selection', () => {
      component.openDetail(page1.items[0]);
      component.closeDetail();
      fixture.detectChanges();
      expect(component.selected()).toBeNull();
      expect(fixture.nativeElement.querySelector('[data-test="detail-panel"]')).toBeNull();
    });
  });

  // ─── US-LV-005: Approve / Reject actions ───────────────────────
  describe('approve / reject actions', () => {
    let toastr: ToastrService;
    let successSpy: jasmine.Spy;
    let errorSpy: jasmine.Spy;
    let infoSpy: jasmine.Spy;

    beforeEach(() => {
      toastr = TestBed.inject(ToastrService);
      successSpy = spyOn(toastr, 'success');
      errorSpy = spyOn(toastr, 'error');
      infoSpy = spyOn(toastr, 'info');
      component.openDetail(page1.items[0]); // lr-1 / Ada Lovelace
      fixture.detectChanges();
    });

    it('starts idle with both action buttons visible', () => {
      expect(component.actionMode()).toBe('none');
      expect(fixture.nativeElement.querySelector('[data-test="approve-btn"]')).toBeTruthy();
      expect(fixture.nativeElement.querySelector('[data-test="reject-btn"]')).toBeTruthy();
    });

    // ── Approve (AC-1) ───────────────────────────────────────────
    it('clicking Approve expands the optional comment textarea', () => {
      component.startApprove();
      fixture.detectChanges();
      expect(component.actionMode()).toBe('approve');
      expect(fixture.nativeElement.querySelector('[data-test="approve-comment"]')).toBeTruthy();
    });

    it('approves WITHOUT a comment (comment omitted from body)', () => {
      component.startApprove();
      component.confirmApprove();
      expect(approvalsSpy.approve).toHaveBeenCalledWith('lr-1', { comment: undefined });
    });

    it('approves WITH a comment', () => {
      component.startApprove();
      component.comment.set('  Enjoy your break  ');
      component.confirmApprove();
      expect(approvalsSpy.approve).toHaveBeenCalledWith('lr-1', { comment: 'Enjoy your break' });
    });

    it('on approve success removes the item from the queue + decrements count + toasts', () => {
      component.startApprove();
      component.confirmApprove();
      fixture.detectChanges();
      expect(component.requests().some((r) => r.requestId === 'lr-1')).toBeFalse();
      expect(component.requests().length).toBe(1);
      expect(component.totalCount()).toBe(44); // 45 -> 44
      expect(component.selected()).toBeNull();
      expect(successSpy).toHaveBeenCalledWith('Leave request approved for Ada Lovelace');
    });

    // ── Reject (AC-2, BR-2) ──────────────────────────────────────
    it('clicking Reject reveals the mandatory reason textarea', () => {
      component.startReject();
      fixture.detectChanges();
      expect(component.actionMode()).toBe('reject');
      expect(fixture.nativeElement.querySelector('[data-test="reject-reason"]')).toBeTruthy();
    });

    it('reject confirm button is DISABLED until the reason is non-empty', () => {
      component.startReject();
      fixture.detectChanges();
      const btn = fixture.nativeElement.querySelector('[data-test="confirm-reject"]');
      expect(btn.disabled).toBeTrue();

      component.rejectReason.set('Not enough cover this week');
      fixture.detectChanges();
      expect(btn.disabled).toBeFalse();
    });

    it('confirmReject is a no-op when the reason is only whitespace (BR-2)', () => {
      component.startReject();
      component.rejectReason.set('   ');
      component.confirmReject();
      expect(approvalsSpy.reject).not.toHaveBeenCalled();
    });

    it('rejects with the reason and removes the item + toasts', () => {
      component.startReject();
      component.rejectReason.set('Insufficient cover');
      component.confirmReject();
      fixture.detectChanges();
      expect(approvalsSpy.reject).toHaveBeenCalledWith('lr-1', { reason: 'Insufficient cover' });
      expect(component.requests().some((r) => r.requestId === 'lr-1')).toBeFalse();
      expect(successSpy).toHaveBeenCalledWith('Leave request rejected for Ada Lovelace');
    });

    // ── Insufficient balance (AC-3) ──────────────────────────────
    it('shows the confirm modal on a 400 insufficient_balance with negative allowed', () => {
      approvalsSpy.approve.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: {
                message: 'Balance is now negative.',
                code: 'insufficient_balance',
                negativeBalanceAllowed: true,
              },
            })
        )
      );
      component.startApprove();
      component.confirmApprove();
      fixture.detectChanges();
      expect(component.confirmNegative()?.message).toBe('Balance is now negative.');
      expect(fixture.nativeElement.querySelector('[data-test="negative-modal"]')).toBeTruthy();
      // item NOT removed yet — awaiting confirmation
      expect(component.requests().some((r) => r.requestId === 'lr-1')).toBeTrue();
    });

    it('confirming the negative-balance modal retries with confirmNegativeBalance:true', () => {
      approvalsSpy.approve.and.returnValues(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { message: 'Negative.', code: 'insufficient_balance', negativeBalanceAllowed: true },
            })
        ),
        of({ requestId: 'lr-1', status: 'Approved' } as ILeaveActionResult)
      );
      component.startApprove();
      component.comment.set('approved despite balance');
      component.confirmApprove();
      fixture.detectChanges();
      component.approveWithNegative();
      fixture.detectChanges();
      expect(approvalsSpy.approve).toHaveBeenCalledTimes(2);
      expect(approvalsSpy.approve.calls.mostRecent().args[1]).toEqual({
        comment: 'approved despite balance',
        confirmNegativeBalance: true,
      });
      expect(component.requests().some((r) => r.requestId === 'lr-1')).toBeFalse();
      expect(successSpy).toHaveBeenCalled();
    });

    it('HARD-blocks (toast, no modal) on insufficient_balance when negative NOT allowed', () => {
      approvalsSpy.approve.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { message: 'No balance and negative not allowed.', code: 'insufficient_balance' },
            })
        )
      );
      component.startApprove();
      component.confirmApprove();
      fixture.detectChanges();
      expect(component.confirmNegative()).toBeNull();
      expect(errorSpy).toHaveBeenCalledWith('No balance and negative not allowed.');
      expect(component.requests().some((r) => r.requestId === 'lr-1')).toBeTrue();
    });

    // ── Concurrency conflict (AC-5) ──────────────────────────────
    it('on 409 toasts "already actioned" + refreshes the queue', () => {
      approvalsSpy.approve.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: { message: 'This request has already been actioned' },
            })
        )
      );
      approvalsSpy.getPendingQueue.calls.reset();
      component.startApprove();
      component.confirmApprove();
      fixture.detectChanges();
      expect(errorSpy).toHaveBeenCalledWith('This request has already been actioned');
      expect(approvalsSpy.getPendingQueue).toHaveBeenCalledTimes(1); // refresh
      expect(component.selected()).toBeNull();
    });

    // ── Multi-level (AC-4, DEFERRED) ─────────────────────────────
    it('treats a "Pending L2 Approval" status as info + removes the item (AC-4 deferred)', () => {
      approvalsSpy.approve.and.returnValue(
        of({ requestId: 'lr-1', status: 'Pending L2 Approval' } as ILeaveActionResult)
      );
      component.startApprove();
      component.confirmApprove();
      fixture.detectChanges();
      expect(infoSpy).toHaveBeenCalled();
      expect(component.requests().some((r) => r.requestId === 'lr-1')).toBeFalse();
    });

    // ── Generic error ────────────────────────────────────────────
    it('surfaces a generic toast on an unexpected error', () => {
      component.startReject();
      component.rejectReason.set('reason');
      approvalsSpy.reject.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      component.confirmReject();
      expect(errorSpy).toHaveBeenCalledWith('Failed to action this leave request.');
    });

    it('cancelAction collapses the panel and clears inputs', () => {
      component.startReject();
      component.rejectReason.set('typing');
      component.cancelAction();
      expect(component.actionMode()).toBe('none');
      expect(component.rejectReason()).toBe('');
    });
  });

  // ─── Refresh (AC-5 manual seam) ────────────────────────────────
  it('refresh re-fetches the queue', () => {
    approvalsSpy.getPendingQueue.calls.reset();
    component.refresh();
    expect(approvalsSpy.getPendingQueue).toHaveBeenCalledTimes(1);
  });

  // ─── Error + empty states ──────────────────────────────────────
  it('shows an error toast when the queue fails to load', () => {
    const toastr = TestBed.inject(ToastrService);
    const errSpy = spyOn(toastr, 'error');
    approvalsSpy.getPendingQueue.and.returnValue(throwError(() => new Error('boom')));
    component.load();
    expect(errSpy).toHaveBeenCalled();
    expect(component.isLoading()).toBeFalse();
  });

  it('renders the empty state when there are no requests', () => {
    approvalsSpy.getPendingQueue.and.returnValue(of({ items: [], totalCount: 0 }));
    component.load();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-test="empty"]')).toBeTruthy();
  });

  // ─── helpers ───────────────────────────────────────────────────
  it('initials handles single and multi-word names', () => {
    expect(component.initials('Ada Lovelace')).toBe('AL');
    expect(component.initials('Cher')).toBe('C');
    expect(component.initials('')).toBe('?');
  });

  it('fileName extracts a readable name from a URL', () => {
    expect(component.fileName('https://cdn.example.com/a/b/medical-cert.pdf?sig=x')).toBe('medical-cert.pdf');
  });
});
