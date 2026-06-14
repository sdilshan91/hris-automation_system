import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';

import { MyLeaveRequestsComponent } from './my-leave-requests.component';
import { LeaveRequestService } from '../../services/leave-request.service';
import { ILeaveRequest } from '../../models/leave-request.models';

/** Build a request with sensible defaults, overridable per test. */
function makeRequest(overrides: Partial<ILeaveRequest> = {}): ILeaveRequest {
  return {
    leaveRequestId: 'lr-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    leaveTypeColor: '#2563eb',
    startDate: '2099-07-06',
    endDate: '2099-07-08',
    isHalfDay: false,
    halfDaySession: null,
    totalDays: 3,
    reason: 'Vacation',
    status: 'Pending',
    requestedAt: '2026-06-13T10:00:00Z',
    attachmentUrls: [],
    ...overrides,
  };
}

describe('MyLeaveRequestsComponent', () => {
  let component: MyLeaveRequestsComponent;
  let fixture: ComponentFixture<MyLeaveRequestsComponent>;
  let serviceSpy: jasmine.SpyObj<LeaveRequestService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const requests: ILeaveRequest[] = [
    makeRequest({ leaveRequestId: 'lr-1', status: 'Pending', startDate: '2099-07-06', endDate: '2099-07-08' }),
    makeRequest({
      leaveRequestId: 'lr-2',
      leaveTypeName: 'Sick Leave',
      leaveTypeColor: '#dc2626',
      startDate: '2099-05-01',
      endDate: '2099-05-01',
      isHalfDay: true,
      halfDaySession: 'AM',
      totalDays: 0.5,
      reason: 'Doctor',
      status: 'Approved',
    }),
  ];

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('LeaveRequestService', [
      'getMyLeaveRequests',
      'cancelLeaveRequest',
    ]);
    serviceSpy.getMyLeaveRequests.and.returnValue(of(requests));
    serviceSpy.cancelLeaveRequest.and.returnValue(
      of(makeRequest({ status: 'Cancelled' })),
    );
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    await TestBed.configureTestingModule({
      imports: [MyLeaveRequestsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LeaveRequestService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyLeaveRequestsComponent);
    component = fixture.componentInstance;
  });

  it('should create and load the employee requests', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
    expect(component.requests().length).toBe(2);
    expect(component.isLoading()).toBeFalse();
  });

  it('should map status to a badge class', () => {
    fixture.detectChanges();
    expect(component.badgeClass(requests[0])).toContain('amber'); // Pending
    expect(component.badgeClass(requests[1])).toContain('green'); // Approved
  });

  it('should show an error toast when loading fails', () => {
    serviceSpy.getMyLeaveRequests.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(component.isLoading()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  // --- US-LV-010: cancel eligibility -----------------------------

  describe('cancel eligibility (US-LV-010)', () => {
    beforeEach(() => fixture.detectChanges());

    it('marks a pending request as eligible', () => {
      const e = component.cancelEligibility(makeRequest({ status: 'Pending' }));
      expect(e.eligible).toBeTrue();
    });

    it('marks a future approved request as eligible', () => {
      const e = component.cancelEligibility(
        makeRequest({ status: 'Approved', startDate: '2099-01-01' }),
      );
      expect(e.eligible).toBeTrue();
    });

    it('marks a past/started approved request as ineligible with a tooltip', () => {
      const e = component.cancelEligibility(
        makeRequest({ status: 'Approved', startDate: '2000-01-01' }),
      );
      expect(e.eligible).toBeFalse();
      expect(e.reason).toContain('already started');
    });

    it('marks a rejected request as ineligible', () => {
      const e = component.cancelEligibility(makeRequest({ status: 'Rejected' }));
      expect(e.eligible).toBeFalse();
    });

    it('marks an already-cancelled request as ineligible', () => {
      const e = component.cancelEligibility(makeRequest({ status: 'Cancelled' }));
      expect(e.eligible).toBeFalse();
      expect(e.reason).toContain('already been cancelled');
    });

    it('renders a Cancel button for an eligible row and a disabled one for an ineligible row', () => {
      serviceSpy.getMyLeaveRequests.and.returnValue(
        of([
          makeRequest({ leaveRequestId: 'p', status: 'Pending', startDate: '2099-09-01' }),
          makeRequest({ leaveRequestId: 'a', status: 'Approved', startDate: '2000-01-01' }),
        ]),
      );
      component.load();
      fixture.detectChanges();
      const buttons: HTMLButtonElement[] = Array.from(
        fixture.nativeElement.querySelectorAll('table button.btn-cancel'),
      );
      expect(buttons.length).toBe(2);
      expect(buttons.some((b) => !b.disabled)).toBeTrue(); // eligible pending
      expect(buttons.some((b) => b.disabled)).toBeTrue(); // ineligible started
    });
  });

  // --- US-LV-010: confirm dialog + reason logic ------------------

  describe('cancel dialog reason logic (US-LV-010)', () => {
    beforeEach(() => fixture.detectChanges());

    it('does not require a reason for a pending request', () => {
      component.openCancel(makeRequest({ status: 'Pending' }));
      expect(component.reasonRequired()).toBeFalse();
    });

    it('requires a reason for an approved request', () => {
      component.openCancel(makeRequest({ status: 'Approved', startDate: '2099-01-01' }));
      expect(component.reasonRequired()).toBeTrue();
    });

    it('does not submit an approved cancellation without a reason', () => {
      component.openCancel(makeRequest({ status: 'Approved', startDate: '2099-01-01' }));
      component.cancelReason.set('   ');
      component.confirmCancel();
      expect(serviceSpy.cancelLeaveRequest).not.toHaveBeenCalled();
    });

    it('submits a pending cancellation with an empty reason', () => {
      component.openCancel(makeRequest({ leaveRequestId: 'lr-1', status: 'Pending' }));
      component.confirmCancel();
      expect(serviceSpy.cancelLeaveRequest).toHaveBeenCalledWith('lr-1', { reason: '' });
    });

    it('sends the trimmed reason for an approved cancellation', () => {
      component.openCancel(makeRequest({ leaveRequestId: 'lr-2', status: 'Approved', startDate: '2099-01-01' }));
      component.cancelReason.set('  family trip cancelled  ');
      component.confirmCancel();
      expect(serviceSpy.cancelLeaveRequest).toHaveBeenCalledWith('lr-2', {
        reason: 'family trip cancelled',
      });
    });

    it('closeCancel clears the dialog state', () => {
      component.openCancel(makeRequest({ status: 'Pending' }));
      component.cancelReason.set('x');
      component.closeCancel();
      expect(component.cancelTarget()).toBeNull();
      expect(component.cancelReason()).toBe('');
    });
  });

  // --- US-LV-010: success + error handling -----------------------

  describe('cancel outcomes (US-LV-010)', () => {
    beforeEach(() => fixture.detectChanges());

    it('on success: shows a toast, closes the dialog and refreshes the list', () => {
      serviceSpy.getMyLeaveRequests.calls.reset();
      component.openCancel(makeRequest({ leaveRequestId: 'lr-1', status: 'Pending' }));
      component.confirmCancel();

      expect(toastrSpy.success).toHaveBeenCalledWith('Leave request cancelled successfully.');
      expect(component.cancelTarget()).toBeNull();
      expect(component.isCancelling()).toBeFalse();
      expect(serviceSpy.getMyLeaveRequests).toHaveBeenCalledTimes(1); // refresh
    });

    it('renders a Cancelled badge with strikethrough after a successful cancel', () => {
      serviceSpy.getMyLeaveRequests.and.returnValue(
        of([makeRequest({ leaveRequestId: 'lr-1', status: 'Cancelled' })]),
      );
      component.load();
      fixture.detectChanges();
      const badge: HTMLElement = fixture.nativeElement.querySelector('table .status-badge');
      expect(badge.textContent?.trim()).toBe('Cancelled');
      expect(badge.className).toContain('neutral');
      const struck = fixture.nativeElement.querySelector('table .line-through');
      expect(struck).toBeTruthy();
    });

    it('on 400 ineligible: surfaces the API message verbatim and keeps the dialog open', () => {
      serviceSpy.getMyLeaveRequests.calls.reset();
      const apiMsg = 'Cannot cancel leave that has already started. Please contact HR for assistance.';
      serviceSpy.cancelLeaveRequest.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: { message: apiMsg, code: 'already_started' } })),
      );
      component.openCancel(makeRequest({ leaveRequestId: 'lr-1', status: 'Approved', startDate: '2099-01-01' }));
      component.cancelReason.set('changed plans');
      component.confirmCancel();

      expect(toastrSpy.error).toHaveBeenCalledWith(apiMsg);
      expect(component.isCancelling()).toBeFalse();
      expect(component.cancelTarget()).not.toBeNull(); // dialog stays open
      expect(serviceSpy.getMyLeaveRequests).not.toHaveBeenCalled(); // no refresh on 400
    });

    it('on 409 conflict: surfaces the message, closes the dialog and refreshes', () => {
      serviceSpy.getMyLeaveRequests.calls.reset();
      const apiMsg = 'This request has already been actioned by your manager.';
      serviceSpy.cancelLeaveRequest.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 409, error: { message: apiMsg } })),
      );
      component.openCancel(makeRequest({ leaveRequestId: 'lr-1', status: 'Pending' }));
      component.confirmCancel();

      expect(toastrSpy.error).toHaveBeenCalledWith(apiMsg);
      expect(component.cancelTarget()).toBeNull();
      expect(serviceSpy.getMyLeaveRequests).toHaveBeenCalledTimes(1); // refresh
    });

    it('on 409 with no body: falls back to a default conflict message and refreshes', () => {
      serviceSpy.cancelLeaveRequest.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 409 })),
      );
      component.openCancel(makeRequest({ leaveRequestId: 'lr-1', status: 'Pending' }));
      component.confirmCancel();
      expect(toastrSpy.error).toHaveBeenCalled();
      expect(component.cancelTarget()).toBeNull();
    });
  });
});
