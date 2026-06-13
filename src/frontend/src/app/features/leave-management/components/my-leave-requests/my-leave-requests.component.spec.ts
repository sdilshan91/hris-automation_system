import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { MyLeaveRequestsComponent } from './my-leave-requests.component';
import { LeaveRequestService } from '../../services/leave-request.service';
import { ILeaveRequest } from '../../models/leave-request.models';

describe('MyLeaveRequestsComponent', () => {
  let component: MyLeaveRequestsComponent;
  let fixture: ComponentFixture<MyLeaveRequestsComponent>;
  let serviceSpy: jasmine.SpyObj<LeaveRequestService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const requests: ILeaveRequest[] = [
    {
      leaveRequestId: 'lr-1',
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
    },
    {
      leaveRequestId: 'lr-2',
      tenantId: 'tenant-1',
      employeeId: 'emp-1',
      leaveTypeId: 'lt-2',
      leaveTypeName: 'Sick Leave',
      leaveTypeColor: '#dc2626',
      startDate: '2026-05-01',
      endDate: '2026-05-01',
      isHalfDay: true,
      halfDaySession: 'AM',
      totalDays: 0.5,
      reason: 'Doctor',
      status: 'Approved',
      requestedAt: '2026-04-30T09:00:00Z',
      attachmentUrls: [],
    },
  ];

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('LeaveRequestService', ['getMyLeaveRequests']);
    serviceSpy.getMyLeaveRequests.and.returnValue(of(requests));
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
});
