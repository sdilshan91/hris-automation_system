import { TestBed, ComponentFixture, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService, provideToastr } from 'ngx-toastr';
import { EmployeeProfileComponent } from './employee-profile.component';
import { AuthService } from '@core/auth/auth.service';
import {
  IEmployeeProfile,
  isSectionEditable,
  getStatusBadgeClasses,
  getInitialsFromName,
} from '../../models/employee.models';
import { environment } from '../../../../../../environments/environment';

/**
 * US-CHR-002: Tests for EmployeeProfileComponent.
 *
 * Covers:
 *  - Profile section rendering (AC-1)
 *  - Edit permitted vs. restricted by role (AC-4, AC-5, FR-3)
 *  - Concurrency conflict handling (AC-3)
 *  - Save success (AC-2)
 *  - isSectionEditable utility
 */
describe('EmployeeProfileComponent', () => {
  let fixture: ComponentFixture<EmployeeProfileComponent>;
  let component: EmployeeProfileComponent;
  let httpMock: HttpTestingController;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const profileUrl = `${environment.apiBaseUrl}/employees/emp-1/profile`;
  const customFieldsUrl = `${environment.apiBaseUrl}/tenant/custom-fields/active?entityType=employee`;

  /** Minimal profile fixture matching IEmployeeProfile */
  const mockProfile: IEmployeeProfile = {
    employeeId: 'emp-1',
    tenantId: 'tenant-1',
    employeeNo: 'EMP-0001',
    firstName: 'John',
    lastName: 'Doe',
    email: 'john.doe@company.com',
    phone: '+94771234567',
    dateOfBirth: '1990-01-15',
    gender: 'Male',
    dateOfJoining: '2026-06-01',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: 'jt-1',
    jobTitleName: 'Software Engineer',
    employmentType: 'Full-Time',
    status: 'active',
    profilePhotoUrl: null,
    customFields: null,
    isActive: true,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
    xmin: '12345',
    personalEmail: 'john.personal@example.com',
    address: '123 Main St',
    city: 'Colombo',
    state: 'Western',
    postalCode: '10100',
    country: 'Sri Lanka',
    reportingManagerId: null,
    reportingManagerName: null,
    reportingManagerJobTitle: null,
    reportingManagerPhotoUrl: null,
    reportingChain: [],
    emergencyContacts: [
      { id: 'ec-1', name: 'Jane Doe', relationship: 'Spouse', phone: '+94779876543' },
    ],
    education: [
      { id: 'edu-1', institution: 'University of Colombo', degree: 'BSc CS', endYear: '2012' },
    ],
    workHistory: [
      { id: 'wh-1', company: 'Google', position: 'Senior Engineer', fromDate: '2015-01-01', toDate: '2020-12-31' },
    ],
    dependents: [
      { id: 'dep-1', name: 'Baby Doe', relationship: 'Child', dateOfBirth: '2022-05-20' },
    ],
    employmentHistory: [
      {
        id: 'eh-1',
        effectiveDate: '2026-06-01',
        changeType: 'department',
        previousValue: null,
        newValue: 'Engineering',
        changedBy: 'Admin',
        changedAt: '2026-06-01T00:00:00Z',
      },
    ],
  };

  /**
   * Helper to configure the AuthService mock with a given role.
   */
  function createAuthServiceMock(role: 'HR Officer' | 'Employee' | 'Manager'): jasmine.SpyObj<AuthService> {
    const mock = jasmine.createSpyObj('AuthService', [
      'hasRole',
      'hasPermission',
      'hasAnyPermission',
    ], {
      isAuthenticated: jasmine.createSpy().and.returnValue(true),
      currentUser: jasmine.createSpy().and.returnValue({ userId: 'u-1', email: 'test@test.com', displayName: 'Test', mfaEnabled: false }),
      permissions: jasmine.createSpy().and.returnValue([]),
      roles: jasmine.createSpy().and.returnValue([role]),
    });

    mock.hasRole.and.callFake((r: string) => {
      if (role === 'HR Officer' && (r === 'HR Officer' || r === 'Tenant Admin')) return true;
      return r === role;
    });
    mock.hasPermission.and.returnValue(true);
    mock.hasAnyPermission.and.returnValue(true);

    return mock;
  }

  function setupTestBed(role: 'HR Officer' | 'Employee' | 'Manager' = 'HR Officer'): void {
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'info', 'warning']);
    const authMock = createAuthServiceMock(role);

    TestBed.configureTestingModule({
      imports: [EmployeeProfileComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideAnimationsAsync(),
        provideToastr(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: (_key: string) => 'emp-1' } } },
        },
        { provide: AuthService, useValue: authMock },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(EmployeeProfileComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    // US-CHR-012: Flush any outstanding custom field requests before verify
    const cfReqs = httpMock.match(customFieldsUrl);
    cfReqs.forEach(r => { if (!r.cancelled) { r.flush([]); } });
    httpMock.verify();
  });

  // ─── Section rendering (AC-1) ──────────────────────────────

  describe('AC-1: Profile section rendering', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should create the component', () => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(mockProfile);
      expect(component).toBeTruthy();
    });

    it('should show loading skeleton while fetching', () => {
      fixture.detectChanges();
      expect(component.isLoading()).toBeTrue();
      const req = httpMock.expectOne(profileUrl);
      req.flush(mockProfile);
      expect(component.isLoading()).toBeFalse();
    });

    it('should display profile data after loading', fakeAsync(() => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(mockProfile);
      tick();
      fixture.detectChanges();

      expect(component.profile()).toBeTruthy();
      expect(component.profile()!.firstName).toBe('John');
      expect(component.profile()!.lastName).toBe('Doe');
      expect(component.profile()!.employeeNo).toBe('EMP-0001');
    }));

    it('should show error state on HTTP failure', fakeAsync(() => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(null, { status: 500, statusText: 'Server Error' });
      tick();
      fixture.detectChanges();

      expect(component.loadError()).toBeTruthy();
      expect(component.isLoading()).toBeFalse();
    }));

    it('should display 404 error for missing employee', fakeAsync(() => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(null, { status: 404, statusText: 'Not Found' });
      tick();
      fixture.detectChanges();

      expect(component.loadError()).toBe('Employee not found.');
    }));

    it('should show all 11 section tabs (including Leave tab from US-LV-002)', fakeAsync(() => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(mockProfile);
      tick();
      fixture.detectChanges();

      expect(component.sectionList.length).toBe(11);
    }));

    it('should display employee initials when no photo URL', fakeAsync(() => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(mockProfile);
      tick();

      expect(component.getInitials()).toBe('JD');
    }));

    it('should format status badge class correctly', () => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);

      expect(component.getStatusBadgeClass('active')).toBe('badge-active');
      expect(component.getStatusBadgeClass('probation')).toBe('badge-probation');
      expect(component.getStatusBadgeClass('terminated')).toBe('badge-terminated');
      expect(component.getStatusBadgeClass('suspended')).toBe('badge-suspended');
    });

    it('should format address correctly', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.formatAddress()).toBe('123 Main St, Colombo, Western, 10100, Sri Lanka');
    }));

    it('should format employment history change types', () => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);

      expect(component.formatChangeType('department')).toBe('Department Change');
      expect(component.formatChangeType('job_title')).toBe('Job Title Change');
      expect(component.formatChangeType('status')).toBe('Status Change');
      expect(component.formatChangeType('reporting_manager')).toBe('Reporting Manager Change');
      expect(component.formatChangeType('unknown')).toBe('unknown');
    });
  });

  // ─── Edit permissions by role (AC-4, AC-5, FR-3) ──────────

  describe('AC-4 / AC-5: Field-level permissions', () => {
    it('HR Officer can edit all sections', () => {
      setupTestBed('HR Officer');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);

      expect(component.canEditSection('personal-info')).toBeTrue();
      expect(component.canEditSection('contact')).toBeTrue();
      expect(component.canEditSection('emergency-contacts')).toBeTrue();
      expect(component.canEditSection('employment')).toBeTrue();
      expect(component.canEditSection('education')).toBeTrue();
      expect(component.canEditSection('work-history')).toBeTrue();
      expect(component.canEditSection('dependents')).toBeTrue();
    });

    it('Employee can edit only permitted sections (contact, emergency, education, work history, dependents)', () => {
      setupTestBed('Employee');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);

      // Editable
      expect(component.canEditSection('contact')).toBeTrue();
      expect(component.canEditSection('emergency-contacts')).toBeTrue();
      expect(component.canEditSection('education')).toBeTrue();
      expect(component.canEditSection('work-history')).toBeTrue();
      expect(component.canEditSection('dependents')).toBeTrue();

      // NOT editable
      expect(component.canEditSection('personal-info')).toBeFalse();
      expect(component.canEditSection('employment')).toBeFalse();
    });

    it('Manager has read-only access — no editable sections', () => {
      setupTestBed('Manager');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);

      expect(component.canEditSection('personal-info')).toBeFalse();
      expect(component.canEditSection('contact')).toBeFalse();
      expect(component.canEditSection('emergency-contacts')).toBeFalse();
      expect(component.canEditSection('employment')).toBeFalse();
      expect(component.canEditSection('education')).toBeFalse();
      expect(component.canEditSection('work-history')).toBeFalse();
      expect(component.canEditSection('dependents')).toBeFalse();
    });
  });

  // ─── Concurrency conflict handling (AC-3) ──────────────────

  describe('AC-3: Optimistic concurrency conflict', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should show conflict toast on 409 response', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      // Enter edit mode for personal-info
      component.toggleEdit('personal-info');
      fixture.detectChanges();

      // Submit the section
      component.saveSection('personal-info');
      tick();

      const patchReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/sections/personal-info`
      );
      expect(patchReq.request.method).toBe('PATCH');
      expect(patchReq.request.body.xmin).toBe('12345');

      // Simulate 409 conflict
      patchReq.flush(
        { message: 'Concurrency conflict' },
        { status: 409, statusText: 'Conflict' }
      );
      tick();

      expect(toastrSpy.error).toHaveBeenCalledWith(
        'This record was modified by another user. Please refresh and try again.'
      );
    }));

    it('should show permission error toast on 403 response', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('personal-info');
      component.saveSection('personal-info');
      tick();

      const patchReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/sections/personal-info`
      );
      patchReq.flush(
        { message: 'Forbidden' },
        { status: 403, statusText: 'Forbidden' }
      );
      tick();

      expect(toastrSpy.error).toHaveBeenCalledWith(
        'You do not have permission to edit these fields.'
      );
    }));
  });

  // ─── Save success (AC-2) ───────────────────────────────────

  describe('AC-2: Save section success', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should update profile and show success toast after save', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      // Enter edit mode
      component.toggleEdit('contact');
      fixture.detectChanges();

      // Modify form
      component.contactForm.patchValue({ phone: '+94779999999' });

      // Submit
      component.saveSection('contact');
      tick();

      const patchReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/sections/contact`
      );
      expect(patchReq.request.method).toBe('PATCH');

      const updatedProfile = { ...mockProfile, phone: '+94779999999', xmin: '12346' };
      patchReq.flush({ xmin: '12346', profile: updatedProfile });
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith('Changes saved successfully.');
      expect(component.profile()!.xmin).toBe('12346');
      expect(component.editingSection()).toBeNull();
    }));

    it('should send xmin token in the request body', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('personal-info');
      component.saveSection('personal-info');
      tick();

      const patchReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/sections/personal-info`
      );
      expect(patchReq.request.body.xmin).toBe('12345');
      patchReq.flush({ xmin: '12346', profile: { ...mockProfile, xmin: '12346' } });
    }));
  });

  // ─── Edit mode interactions ────────────────────────────────

  describe('Edit mode interactions', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should toggle edit mode for a section', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.editingSection()).toBeNull();

      component.toggleEdit('personal-info');
      expect(component.editingSection()).toBe('personal-info');

      component.toggleEdit('personal-info');
      expect(component.editingSection()).toBeNull();
    }));

    it('should cancel edit and clear the editing section', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('contact');
      expect(component.editingSection()).toBe('contact');

      component.cancelEdit();
      expect(component.editingSection()).toBeNull();
    }));

    it('should populate personal info form from profile data', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('personal-info');

      expect(component.personalInfoForm.value.firstName).toBe('John');
      expect(component.personalInfoForm.value.lastName).toBe('Doe');
      expect(component.personalInfoForm.value.dateOfBirth).toBe('1990-01-15');
      expect(component.personalInfoForm.value.gender).toBe('Male');
    }));

    it('should populate contact form from profile data', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('contact');

      expect(component.contactForm.value.phone).toBe('+94771234567');
      expect(component.contactForm.value.city).toBe('Colombo');
      expect(component.contactForm.value.personalEmail).toBe('john.personal@example.com');
    }));

    it('should populate emergency contacts repeater from profile data', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('emergency-contacts');

      expect(component.emergencyContactControls.length).toBe(1);
      expect(component.emergencyContactControls.at(0).value.name).toBe('Jane Doe');
    }));

    it('should add and remove emergency contacts in edit mode', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('emergency-contacts');
      expect(component.emergencyContactControls.length).toBe(1);

      component.addEmergencyContact();
      expect(component.emergencyContactControls.length).toBe(2);

      component.removeEmergencyContact(1);
      expect(component.emergencyContactControls.length).toBe(1);
    }));
  });

  // ─── US-CHR-009: Status management ──────────────────────────

  describe('US-CHR-009: Status badge colors', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should return correct badge class for all 5 statuses including inactive', () => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);

      expect(component.getStatusBadgeClass('active')).toBe('badge-active');
      expect(component.getStatusBadgeClass('probation')).toBe('badge-probation');
      expect(component.getStatusBadgeClass('terminated')).toBe('badge-terminated');
      expect(component.getStatusBadgeClass('suspended')).toBe('badge-suspended');
      expect(component.getStatusBadgeClass('inactive')).toBe('badge-inactive');
      expect(component.getStatusBadgeClass('unknown')).toBe('badge-neutral');
    });
  });

  describe('US-CHR-009: Change Status button visibility (BR-2)', () => {
    it('should show Change Status button for HR Officer', fakeAsync(() => {
      setupTestBed('HR Officer');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.canChangeStatus()).toBeTrue();
    }));

    it('should hide Change Status button for Employee role', fakeAsync(() => {
      setupTestBed('Employee');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.canChangeStatus()).toBeFalse();
    }));

    it('should hide Change Status button for Manager role', fakeAsync(() => {
      setupTestBed('Manager');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.canChangeStatus()).toBeFalse();
    }));
  });

  describe('US-CHR-009: Status change modal', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should open modal and load valid transitions', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      expect(component.showStatusModal()).toBeTrue();
      expect(component.isLoadingTransitions()).toBeTrue();

      const transReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      );
      expect(transReq.request.method).toBe('GET');
      transReq.flush([
        { targetStatus: 'suspended', label: 'Suspended', sideEffects: ['Disable portal access'] },
        { targetStatus: 'terminated', label: 'Terminated', sideEffects: ['Disable portal access', 'Exclude from payroll'] },
      ]);
      tick();

      expect(component.isLoadingTransitions()).toBeFalse();
      expect(component.validTransitions().length).toBe(2);
    }));

    it('should show only valid transitions from backend (not hardcoded)', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      const transReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      );
      // Backend returns only one transition
      transReq.flush([
        { targetStatus: 'inactive', label: 'Inactive', sideEffects: [] },
      ]);
      tick();

      expect(component.validTransitions().length).toBe(1);
      expect(component.validTransitions()[0].targetStatus).toBe('inactive');
    }));

    it('should close modal on closeStatusModal', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      ).flush([]);
      tick();

      component.closeStatusModal();
      expect(component.showStatusModal()).toBeFalse();
    }));

    it('should require newStatus, effectiveDate, and reason fields', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      ).flush([
        { targetStatus: 'suspended', label: 'Suspended', sideEffects: [] },
      ]);
      tick();

      // Form should be invalid with empty values
      expect(component.statusChangeForm.valid).toBeFalse();
      expect(component.statusChangeForm.get('newStatus')?.hasError('required')).toBeTrue();
      expect(component.statusChangeForm.get('effectiveDate')?.hasError('required')).toBeTrue();
      expect(component.statusChangeForm.get('reason')?.hasError('required')).toBeTrue();

      // Attempt to proceed with invalid form
      component.proceedToConfirmation();
      expect(component.showConfirmation()).toBeFalse();
    }));

    it('should proceed to confirmation when form is valid', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      ).flush([
        { targetStatus: 'suspended', label: 'Suspended', sideEffects: ['Disable portal access'] },
      ]);
      tick();

      component.statusChangeForm.patchValue({
        newStatus: 'suspended',
        effectiveDate: '2026-06-15',
        reason: 'Pending investigation',
      });

      component.proceedToConfirmation();
      expect(component.showConfirmation()).toBeTrue();
    }));

    it('should go back from confirmation to form', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      ).flush([
        { targetStatus: 'suspended', label: 'Suspended', sideEffects: [] },
      ]);
      tick();

      component.statusChangeForm.patchValue({
        newStatus: 'suspended',
        effectiveDate: '2026-06-15',
        reason: 'Test reason',
      });
      component.proceedToConfirmation();
      expect(component.showConfirmation()).toBeTrue();

      component.backToForm();
      expect(component.showConfirmation()).toBeFalse();
    }));

    it('should submit status change with Idempotency-Key header', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      ).flush([
        { targetStatus: 'suspended', label: 'Suspended', sideEffects: [] },
      ]);
      tick();

      component.statusChangeForm.patchValue({
        newStatus: 'suspended',
        effectiveDate: '2026-06-15',
        reason: 'Pending investigation',
      });
      component.proceedToConfirmation();
      component.submitStatusChange();

      const statusReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status`
      );
      expect(statusReq.request.method).toBe('POST');
      expect(statusReq.request.headers.has('Idempotency-Key')).toBeTrue();
      expect(statusReq.request.headers.get('Idempotency-Key')).toBeTruthy();
      expect(statusReq.request.body.newStatus).toBe('suspended');
      expect(statusReq.request.body.effectiveDate).toBe('2026-06-15');
      expect(statusReq.request.body.reason).toBe('Pending investigation');

      const updatedProfile = { ...mockProfile, status: 'suspended' };
      statusReq.flush({ profile: updatedProfile });
      tick();

      expect(component.profile()!.status).toBe('suspended');
      expect(component.showStatusModal()).toBeFalse();
      expect(toastrSpy.success).toHaveBeenCalledWith('Status changed to suspended successfully.');
    }));

    it('should handle 400 invalid transition error from backend (AC-5)', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      ).flush([
        { targetStatus: 'probation', label: 'Probation', sideEffects: [] },
      ]);
      tick();

      component.statusChangeForm.patchValue({
        newStatus: 'probation',
        effectiveDate: '2026-06-15',
        reason: 'Attempt invalid transition',
      });
      component.proceedToConfirmation();
      component.submitStatusChange();

      const statusReq = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status`
      );
      statusReq.flush(
        { message: 'Invalid status transition. Terminated employees cannot be moved to probation.' },
        { status: 400, statusText: 'Bad Request' }
      );
      tick();

      expect(toastrSpy.error).toHaveBeenCalledWith(
        'Invalid status transition. Terminated employees cannot be moved to probation.'
      );
      expect(component.isSubmittingStatus()).toBeFalse();
    }));

    it('should compute side effects for selected transition', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openStatusChangeModal();
      httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/status/transitions`
      ).flush([
        { targetStatus: 'suspended', label: 'Suspended', sideEffects: ['Disable portal access', 'Pause leave accrual'] },
        { targetStatus: 'terminated', label: 'Terminated', sideEffects: ['Disable portal access', 'Exclude from payroll'] },
      ]);
      tick();

      component.statusChangeForm.patchValue({ newStatus: 'suspended' });
      expect(component.selectedTransitionSideEffects()).toEqual(['Disable portal access', 'Pause leave accrual']);

      component.statusChangeForm.patchValue({ newStatus: 'terminated' });
      expect(component.selectedTransitionSideEffects()).toEqual(['Disable portal access', 'Exclude from payroll']);
    }));
  });

  // ─── US-CHR-011: Reporting Manager field ──────────────────

  describe('US-CHR-011: Reporting manager display (AC-1)', () => {
    it('should show "Not Assigned" when no manager is set', fakeAsync(() => {
      setupTestBed('HR Officer');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();
      fixture.detectChanges();

      // Navigate to Employment tab (index 3)
      component.activeTab.set(3);
      fixture.detectChanges();

      expect(component.profile()!.reportingManagerId).toBeNull();
    }));

    it('should show manager mini-card when manager is assigned', fakeAsync(() => {
      setupTestBed('HR Officer');
      fixture.detectChanges();

      const profileWithManager = {
        ...mockProfile,
        reportingManagerId: 'mgr-1',
        reportingManagerName: 'Alice Manager',
        reportingManagerJobTitle: 'Engineering Lead',
        reportingManagerPhotoUrl: null,
        reportingChain: [
          { employeeId: 'mgr-1', firstName: 'Alice', lastName: 'Manager', jobTitleName: 'Engineering Lead', profilePhotoUrl: null },
        ],
      };
      httpMock.expectOne(profileUrl).flush(profileWithManager);
      tick();
      fixture.detectChanges();

      component.activeTab.set(3);
      fixture.detectChanges();

      expect(component.profile()!.reportingManagerName).toBe('Alice Manager');
      expect(component.reportingChain().length).toBe(1);
    }));

    it('should show change button only for HR Officer role', fakeAsync(() => {
      setupTestBed('HR Officer');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.canEditSection('employment')).toBeTrue();
    }));

    it('should not show change button for Employee role', fakeAsync(() => {
      setupTestBed('Employee');
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.canEditSection('employment')).toBeFalse();
    }));
  });

  describe('US-CHR-011: Manager assignment via modal', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should open and close manager selector modal', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openManagerSelector();
      expect(component.showManagerSelector()).toBeTrue();

      component.closeManagerSelector();
      expect(component.showManagerSelector()).toBeFalse();
    }));

    it('should search for active employees on input', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openManagerSelector();
      component.onManagerSearch('Al');
      tick(350);

      const searchReq = httpMock.expectOne(
        (r) => r.url === `${environment.apiBaseUrl}/employees` &&
          r.params.get('search') === 'Al' &&
          r.params.get('statuses') === 'active'
      );
      searchReq.flush({
        data: [{ ...mockProfile, employeeId: 'mgr-1', firstName: 'Alice', lastName: 'Boss' }],
        total: 1,
        page: 1,
        pageSize: 10,
      });
      tick();

      expect(component.managerSearchResults().length).toBe(1);
      expect(component.isSearchingManagers()).toBeFalse();
    }));

    it('should not search when term is less than 2 characters', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openManagerSelector();
      component.onManagerSearch('A');
      tick(350);

      expect(component.managerSearchResults().length).toBe(0);
      expect(component.isSearchingManagers()).toBeFalse();
    }));

    it('should call assignManager service and update profile on success', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openManagerSelector();
      component.assignManagerToEmployee('mgr-1');

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/manager`
      );
      expect(req.request.method).toBe('POST');
      expect(req.request.body.managerEmployeeId).toBe('mgr-1');

      const updatedProfile = {
        ...mockProfile,
        reportingManagerId: 'mgr-1',
        reportingManagerName: 'Alice Manager',
        reportingManagerJobTitle: 'Lead',
        reportingManagerPhotoUrl: null,
        reportingChain: [{ employeeId: 'mgr-1', firstName: 'Alice', lastName: 'Manager', jobTitleName: 'Lead', profilePhotoUrl: null }],
      };
      req.flush({ profile: updatedProfile });
      tick();

      expect(component.profile()!.reportingManagerId).toBe('mgr-1');
      expect(component.showManagerSelector()).toBeFalse();
      expect(toastrSpy.success).toHaveBeenCalledWith('Reporting manager assigned successfully.');
    }));

    it('should show circular chain error from backend (AC-3)', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.openManagerSelector();
      component.assignManagerToEmployee('emp-1');

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/manager`
      );
      req.flush(
        { message: 'Circular reporting chain detected. Employee A cannot report to Employee B because Employee B already reports to Employee A.' },
        { status: 400, statusText: 'Bad Request' }
      );
      tick();

      expect(toastrSpy.error).toHaveBeenCalledWith(
        jasmine.stringContaining('Circular reporting chain detected')
      );
      expect(component.isAssigningManager()).toBeFalse();
    }));

    it('should remove manager when assigning null', fakeAsync(() => {
      fixture.detectChanges();
      const profileWithManager = {
        ...mockProfile,
        reportingManagerId: 'mgr-1',
        reportingManagerName: 'Alice',
        reportingManagerJobTitle: 'Lead',
        reportingManagerPhotoUrl: null,
        reportingChain: [],
      };
      httpMock.expectOne(profileUrl).flush(profileWithManager);
      tick();

      component.openManagerSelector();
      component.assignManagerToEmployee(null);

      const req = httpMock.expectOne(
        `${environment.apiBaseUrl}/employees/emp-1/manager`
      );
      expect(req.request.body.managerEmployeeId).toBeNull();
      req.flush({ profile: { ...mockProfile, reportingManagerId: null, reportingManagerName: null } });
      tick();

      expect(component.profile()!.reportingManagerId).toBeNull();
      expect(toastrSpy.success).toHaveBeenCalledWith('Reporting manager removed successfully.');
    }));
  });

  describe('US-CHR-011: Reporting chain breadcrumb', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should compute empty chain when profile has no chain data', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.reportingChain().length).toBe(0);
    }));

    it('should compute chain from profile data', fakeAsync(() => {
      fixture.detectChanges();
      const chainProfile = {
        ...mockProfile,
        reportingChain: [
          { employeeId: 'mgr-1', firstName: 'Alice', lastName: 'Manager', jobTitleName: 'Lead', profilePhotoUrl: null },
          { employeeId: 'dir-1', firstName: 'Bob', lastName: 'Director', jobTitleName: 'Director', profilePhotoUrl: null },
        ],
      };
      httpMock.expectOne(profileUrl).flush(chainProfile);
      tick();

      expect(component.reportingChain().length).toBe(2);
      expect(component.reportingChain()[0].firstName).toBe('Alice');
      expect(component.reportingChain()[1].firstName).toBe('Bob');
    }));
  });

  describe('US-CHR-009: formatChangeType includes status_change', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should format status_change as Status Change', () => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);

      expect(component.formatChangeType('status_change')).toBe('Status Change');
    });
  });
});

// ─── isSectionEditable utility (pure function — no TestBed/HTTP afterEach) ───
describe('isSectionEditable utility function', () => {
    it('HR Officer can edit all sections', () => {
      expect(isSectionEditable('personal-info', 'hr_officer')).toBeTrue();
      expect(isSectionEditable('contact', 'hr_officer')).toBeTrue();
      expect(isSectionEditable('employment', 'hr_officer')).toBeTrue();
      expect(isSectionEditable('emergency-contacts', 'hr_officer')).toBeTrue();
      expect(isSectionEditable('education', 'hr_officer')).toBeTrue();
      expect(isSectionEditable('work-history', 'hr_officer')).toBeTrue();
      expect(isSectionEditable('dependents', 'hr_officer')).toBeTrue();
      expect(isSectionEditable('custom-fields', 'hr_officer')).toBeTrue();
    });

    it('Employee can only edit limited sections', () => {
      expect(isSectionEditable('contact', 'employee')).toBeTrue();
      expect(isSectionEditable('emergency-contacts', 'employee')).toBeTrue();
      expect(isSectionEditable('education', 'employee')).toBeTrue();
      expect(isSectionEditable('work-history', 'employee')).toBeTrue();
      expect(isSectionEditable('dependents', 'employee')).toBeTrue();

      expect(isSectionEditable('personal-info', 'employee')).toBeFalse();
      expect(isSectionEditable('employment', 'employee')).toBeFalse();
      expect(isSectionEditable('custom-fields', 'employee')).toBeFalse();
    });

    it('Manager cannot edit any sections', () => {
      expect(isSectionEditable('personal-info', 'manager')).toBeFalse();
      expect(isSectionEditable('contact', 'manager')).toBeFalse();
      expect(isSectionEditable('employment', 'manager')).toBeFalse();
      expect(isSectionEditable('emergency-contacts', 'manager')).toBeFalse();
      expect(isSectionEditable('education', 'manager')).toBeFalse();
      expect(isSectionEditable('work-history', 'manager')).toBeFalse();
      expect(isSectionEditable('dependents', 'manager')).toBeFalse();
      expect(isSectionEditable('custom-fields', 'manager')).toBeFalse();
    });
  });

// ─── US-CHR-009: getStatusBadgeClasses (pure function — no TestBed/HTTP) ───
describe('getStatusBadgeClasses utility function (US-CHR-009)', () => {
  it('should return green classes for active', () => {
    expect(getStatusBadgeClasses('active')).toBe('bg-green-100 text-green-800');
  });

  it('should return amber classes for probation', () => {
    expect(getStatusBadgeClasses('probation')).toBe('bg-amber-100 text-amber-800');
  });

  it('should return gray classes for suspended', () => {
    expect(getStatusBadgeClasses('suspended')).toBe('bg-gray-100 text-gray-800');
  });

  it('should return red classes for terminated', () => {
    expect(getStatusBadgeClasses('terminated')).toBe('bg-red-100 text-red-800');
  });

  it('should return slate classes for inactive', () => {
    expect(getStatusBadgeClasses('inactive')).toBe('bg-slate-100 text-slate-800');
  });

  it('should return neutral classes for unknown status', () => {
    expect(getStatusBadgeClasses('unknown')).toBe('bg-neutral-100 text-neutral-600');
  });
});

// ─── US-CHR-011: getInitialsFromName (pure function — no TestBed/HTTP) ───
describe('getInitialsFromName utility function (US-CHR-011)', () => {
  it('should return initials from first and last name', () => {
    expect(getInitialsFromName('John', 'Doe')).toBe('JD');
  });

  it('should handle empty strings', () => {
    expect(getInitialsFromName('', '')).toBe('');
  });

  it('should handle single-char names', () => {
    expect(getInitialsFromName('A', 'B')).toBe('AB');
  });

  it('should uppercase initials', () => {
    expect(getInitialsFromName('jane', 'smith')).toBe('JS');
  });
});
