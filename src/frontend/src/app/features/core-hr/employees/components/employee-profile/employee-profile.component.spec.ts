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

    it('should show all 10 section tabs', fakeAsync(() => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(mockProfile);
      tick();
      fixture.detectChanges();

      expect(component.sectionList.length).toBe(10);
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

  // ─── isSectionEditable utility ─────────────────────────────

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
});
