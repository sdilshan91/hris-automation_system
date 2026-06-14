import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LeaveApplicationComponent } from './leave-application.component';
import { LeaveRequestService } from '../../services/leave-request.service';
import { LeaveTypeService } from '../../services/leave-type.service';
import { ILeaveType } from '../../models/leave-type.models';
import { ILeaveBalance, ILeaveRequest } from '../../models/leave-request.models';

describe('LeaveApplicationComponent', () => {
  let component: LeaveApplicationComponent;
  let fixture: ComponentFixture<LeaveApplicationComponent>;
  let leaveRequestServiceSpy: jasmine.SpyObj<LeaveRequestService>;
  let leaveTypeServiceSpy: jasmine.SpyObj<LeaveTypeService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const annualLeave: ILeaveType = {
    leaveTypeId: 'lt-1',
    tenantId: 'tenant-1',
    name: 'Annual Leave',
    code: 'AL',
    color: '#2563eb',
    description: null,
    annualEntitlement: 14,
    accrualFrequency: 'monthly',
    carryForwardLimit: 5,
    carryForwardExpiryMonths: 3,
    probationEligible: false,
    documentsRequired: false,
    documentDayThreshold: null,
    encashable: false,
    maxEncashDays: null,
    halfDayAllowed: true,
    hourlyAllowed: false,
    gender: 'all',
    maxConsecutiveDays: null,
    negativeBalanceAllowed: false,
    negativeBalanceLimit: null,
    displayOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const sickLeave: ILeaveType = {
    ...annualLeave,
    leaveTypeId: 'lt-2',
    name: 'Sick Leave',
    code: 'SL',
    color: '#dc2626',
    documentsRequired: true,
    documentDayThreshold: 2,
  };

  const balances: ILeaveBalance[] = [
    { leaveTypeId: 'lt-1', entitlementDays: 14, usedDays: 4, remainingDays: 10 },
    { leaveTypeId: 'lt-2', entitlementDays: 7, usedDays: 6, remainingDays: 1 },
  ];

  const createdRequest: ILeaveRequest = {
    leaveRequestId: 'lr-99',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    leaveTypeColor: '#2563eb',
    startDate: '2026-07-06',
    endDate: '2026-07-08',
    isHalfDay: false,
    halfDaySession: null,
    totalDays: 3,
    reason: 'Vacation',
    status: 'Pending',
    requestedAt: '2026-06-13T10:00:00Z',
    attachmentUrls: [],
  };

  beforeEach(async () => {
    leaveTypeServiceSpy = jasmine.createSpyObj('LeaveTypeService', ['getLeaveTypes']);
    leaveTypeServiceSpy.getLeaveTypes.and.returnValue(of([annualLeave, sickLeave]));

    leaveRequestServiceSpy = jasmine.createSpyObj('LeaveRequestService', [
      'getMyBalances',
      'createLeaveRequest',
    ]);
    leaveRequestServiceSpy.getMyBalances.and.returnValue(of(balances));
    leaveRequestServiceSpy.createLeaveRequest.and.returnValue(of(createdRequest));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    await TestBed.configureTestingModule({
      imports: [LeaveApplicationComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LeaveTypeService, useValue: leaveTypeServiceSpy },
        { provide: LeaveRequestService, useValue: leaveRequestServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LeaveApplicationComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate');
  });

  it('should create and load only active leave types + balances', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
    expect(component.leaveTypes().length).toBe(2);
    expect(component.balances().length).toBe(2);
    expect(component.isLoading()).toBeFalse();
  });

  it('should expose the remaining balance per type', () => {
    fixture.detectChanges();
    expect(component.remainingFor('lt-1')).toBe(10);
    expect(component.remainingFor('lt-2')).toBe(1);
    expect(component.remainingFor('unknown')).toBe(0);
  });

  it('should require all mandatory fields (form invalid initially)', () => {
    fixture.detectChanges();
    expect(component.form.valid).toBeFalse();
  });

  it('should compute requested working days excluding weekends', () => {
    fixture.detectChanges();
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06', // Monday
      endDate: '2026-07-10', // Friday
      reason: 'Trip',
    });
    expect(component.requestedDays()).toBe(5);
  });

  it('should compute 0.5 day for a single-day half-day request (AC-4)', () => {
    fixture.detectChanges();
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06',
      endDate: '2026-07-06',
      isHalfDay: true,
      halfDaySession: 'AM',
      reason: 'Appointment',
    });
    expect(component.requestedDays()).toBe(0.5);
    expect(component.form.valid).toBeTrue();
  });

  it('should invalidate a half-day spanning multiple days (AC-4)', () => {
    fixture.detectChanges();
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06',
      endDate: '2026-07-07',
      isHalfDay: true,
      halfDaySession: 'AM',
      reason: 'x',
    });
    expect(component.form.errors?.['halfDaySingleDay']).toBeTrue();
  });

  it('should require a session when half-day is on (AC-4)', () => {
    fixture.detectChanges();
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06',
      endDate: '2026-07-06',
      isHalfDay: true,
      halfDaySession: '',
      reason: 'x',
    });
    expect(component.form.errors?.['halfDaySession']).toBeTrue();
  });

  it('should invalidate when start date is after end date', () => {
    fixture.detectChanges();
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-10',
      endDate: '2026-07-06',
      reason: 'x',
    });
    expect(component.form.errors?.['dateRange']).toBeTrue();
  });

  it('should flag insufficient balance and prompt for LOP instead of submitting (AC-2 / US-LV-011 AC-1)', () => {
    fixture.detectChanges();
    // Use lt-1 (Annual, no document requirement) with a low balance so the
    // LOP path — not the document-required block — is exercised.
    component.balances.set([{ leaveTypeId: 'lt-1', entitlementDays: 14, usedDays: 13, remainingDays: 1 }]);
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06', // Mon
      endDate: '2026-07-10', // Fri = 5 days
      reason: 'x',
    });
    const projection = component.projection();
    expect(projection.requestedDays).toBe(5);
    expect(projection.projectedRemaining).toBe(-4);
    expect(projection.insufficient).toBeTrue();

    component.submit();
    // US-LV-011 (AC-1): no hard block — the LOP confirmation prompt is shown,
    // the request is NOT yet sent.
    expect(leaveRequestServiceSpy.createLeaveRequest).not.toHaveBeenCalled();
    expect(component.lopPrompt()).not.toBeNull();
    expect(component.lopPrompt()!.message).toContain('Loss of Pay (LOP)');
  });

  it('should resubmit with confirmLop=true when the LOP prompt is confirmed (AC-1)', () => {
    fixture.detectChanges();
    component.balances.set([{ leaveTypeId: 'lt-1', entitlementDays: 14, usedDays: 13, remainingDays: 1 }]);
    component.form.patchValue({
      leaveTypeId: 'lt-1', // remaining 1, requesting 5 → insufficient
      startDate: '2026-07-06',
      endDate: '2026-07-10',
      reason: 'family emergency',
    });
    component.submit(); // opens the prompt
    expect(component.lopPrompt()).not.toBeNull();

    component.confirmLop();
    expect(leaveRequestServiceSpy.createLeaveRequest).toHaveBeenCalledTimes(1);
    const arg = leaveRequestServiceSpy.createLeaveRequest.calls.mostRecent().args[0];
    expect(arg.confirmLop).toBeTrue();
    expect(component.lopPrompt()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/leave/my-requests']);
  });

  it('should dismiss the LOP prompt without submitting when cancelled (AC-1)', () => {
    fixture.detectChanges();
    component.balances.set([{ leaveTypeId: 'lt-1', entitlementDays: 14, usedDays: 13, remainingDays: 1 }]);
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06',
      endDate: '2026-07-10',
      reason: 'x',
    });
    component.submit();
    expect(component.lopPrompt()).not.toBeNull();

    component.cancelLop();
    expect(component.lopPrompt()).toBeNull();
    expect(leaveRequestServiceSpy.createLeaveRequest).not.toHaveBeenCalled();
  });

  it('should open the LOP prompt when the backend signals insufficient_balance + lopOption (AC-1)', () => {
    fixture.detectChanges();
    const err = new HttpErrorResponse({
      status: 400,
      error: {
        message: 'Insufficient balance.',
        code: 'insufficient_balance',
        lopOption: true,
      },
    });
    leaveRequestServiceSpy.createLeaveRequest.and.returnValue(throwError(() => err));

    // Sufficient client-side projection (lt-1 remaining 10, request 3) so the
    // FE sends the request and the LOP branch is driven by the backend response.
    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06',
      endDate: '2026-07-08',
      reason: 'x',
    });
    component.submit();

    expect(leaveRequestServiceSpy.createLeaveRequest).toHaveBeenCalledTimes(1);
    expect(component.lopPrompt()).not.toBeNull();
    expect(toastrSpy.error).not.toHaveBeenCalled();
  });

  it('should surface a document-required hint for sick leave over threshold (AC-3)', () => {
    fixture.detectChanges();
    component.form.patchValue({
      leaveTypeId: 'lt-2', // Sick Leave, threshold 2
      startDate: '2026-07-06', // Mon
      endDate: '2026-07-08', // Wed = 3 days > 2
      reason: 'flu',
    });
    expect(component.documentHint()).toContain('Sick Leave exceeding 2 days');
  });

  it('should block submit when sick-leave document is required but missing (AC-3)', () => {
    fixture.detectChanges();
    // Use lt-1 quota path is fine; switch sick to have balance: give it enough via spy? remaining is 1.
    // Make document the blocker, not balance: 3 days on lt-2 would be insufficient too.
    // Use a type with enough balance + doc requirement instead: patch sickLeave balance via component.
    component.balances.set([
      { leaveTypeId: 'lt-2', entitlementDays: 10, usedDays: 0, remainingDays: 10 },
    ]);
    component.form.patchValue({
      leaveTypeId: 'lt-2',
      startDate: '2026-07-06', // Mon
      endDate: '2026-07-08', // Wed = 3 days > threshold 2
      reason: 'flu',
    });
    component.submit();
    expect(leaveRequestServiceSpy.createLeaveRequest).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('should submit successfully and navigate to my-requests (AC-1)', () => {
    fixture.detectChanges();
    component.form.patchValue({
      leaveTypeId: 'lt-1', // remaining 10
      startDate: '2026-07-06', // Mon
      endDate: '2026-07-08', // Wed = 3 days
      reason: 'Vacation',
    });
    component.submit();

    expect(leaveRequestServiceSpy.createLeaveRequest).toHaveBeenCalledTimes(1);
    const arg = leaveRequestServiceSpy.createLeaveRequest.calls.mostRecent().args[0];
    expect(arg.leaveTypeId).toBe('lt-1');
    expect(arg.halfDaySession).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/leave/my-requests']);
  });

  it('should surface API validation errors via toast (AC-5 overlap)', () => {
    fixture.detectChanges();
    const err = new HttpErrorResponse({
      status: 409,
      error: { message: 'You already have a leave request for the selected dates', code: 'overlap' },
    });
    leaveRequestServiceSpy.createLeaveRequest.and.returnValue(throwError(() => err));

    component.form.patchValue({
      leaveTypeId: 'lt-1',
      startDate: '2026-07-06',
      endDate: '2026-07-08',
      reason: 'Vacation',
    });
    component.submit();

    expect(toastrSpy.error).toHaveBeenCalledWith('You already have a leave request for the selected dates');
    expect(component.isSubmitting()).toBeFalse();
  });

  it('should warn and not submit when the form is invalid', () => {
    fixture.detectChanges();
    component.submit();
    expect(leaveRequestServiceSpy.createLeaveRequest).not.toHaveBeenCalled();
    expect(toastrSpy.warning).toHaveBeenCalled();
  });

  it('should manage attachments (add + remove)', () => {
    fixture.detectChanges();
    const fileList = {
      0: new File(['a'], 'cert.pdf'),
      length: 1,
      item: () => null,
    } as unknown as FileList;
    component['addFiles'](fileList);
    expect(component.attachments()).toEqual(['cert.pdf']);
    component.removeAttachment(0);
    expect(component.attachments().length).toBe(0);
  });

  it('should show an error toast when loading fails', () => {
    leaveRequestServiceSpy.getMyBalances.and.returnValue(throwError(() => new Error('boom')));
    fixture.detectChanges();
    expect(component.isLoading()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalled();
  });
});
