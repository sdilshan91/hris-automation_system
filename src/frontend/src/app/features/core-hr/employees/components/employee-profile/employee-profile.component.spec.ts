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

  const profileUrl = `${environment.apiBaseUrl}/tenant/employees/emp-1/profile`;
  const customFieldsUrl = `${environment.apiBaseUrl}/tenant/custom-fields/active?entityType=employee`;
  const locationsUrl = `${environment.apiBaseUrl}/tenant/locations`;
  // DF-38: id-select / enum option sources for the employment section
  const departmentsUrl = `${environment.apiBaseUrl}/tenant/departments`;
  const jobTitlesUrl = `${environment.apiBaseUrl}/tenant/job-titles`;
  const employmentTypesUrl = `${environment.apiBaseUrl}/tenant/job-titles/employment-types`;

  const mockDepartments = [
    { departmentId: 'dept-1', tenantId: 'tenant-1', name: 'Engineering', code: 'ENG', description: null, parentDepartmentId: null, parentDepartmentName: null, managerEmployeeId: null, managerName: null, isActive: true, employeeCount: 0, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' },
    { departmentId: 'dept-2', tenantId: 'tenant-1', name: 'Finance', code: 'FIN', description: null, parentDepartmentId: null, parentDepartmentName: null, managerEmployeeId: null, managerName: null, isActive: true, employeeCount: 0, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' },
  ];
  const mockJobTitles = [
    { jobTitleId: 'jt-1', tenantId: 'tenant-1', titleName: 'Software Engineer', description: null, gradeId: null, gradeName: null, isActive: true, employeeCount: 0, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' },
    { jobTitleId: 'jt-2', tenantId: 'tenant-1', titleName: 'Accountant', description: null, gradeId: null, gradeName: null, isActive: true, employeeCount: 0, createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z' },
  ];
  // DF-38/GAP-A: the REAL BE shape (EmploymentTypeDto, camelCase). The service
  // maps { id, name, displayName } → the consumer's { value, label } — mocking
  // the mapped shape here (as the old fixture did) masked the missing map().
  const mockEmploymentTypes = [
    { id: '1', name: 'FullTime', displayName: 'Full-Time' },
    { id: '2', name: 'PartTime', displayName: 'Part-Time' },
    { id: '3', name: 'Contract', displayName: 'Contract' },
    { id: '4', name: 'Intern', displayName: 'Intern' },
  ];

  // BUG-113: active locations feeding the employment-section Location select
  const mockLocations = [
    {
      locationId: 'loc-1',
      tenantId: 'tenant-1',
      name: 'Colombo HQ',
      addressLine1: null,
      addressLine2: null,
      city: null,
      stateProvince: null,
      country: null,
      postalCode: null,
      timeZone: 'Asia/Colombo',
      phone: null,
      isActive: true,
      employeeCount: 0,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
  ];

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
    locationId: null,
    locationName: null,
    employmentType: 'FullTime',
    // GAP-023: required on IEmployee since the FTE / work-arrangement UI shipped.
    fte: 1,
    workArrangement: 'OnSite',
    status: 'Active',
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
      { id: 'edu-1', institution: 'University of Colombo', degree: 'BSc CS', fieldOfStudy: 'Computer Science', startYear: '2008', endYear: '2012' },
    ],
    workHistory: [
      { id: 'wh-1', company: 'Google', position: 'Senior Engineer', fromDate: '2015-01-01', toDate: '2020-12-31', description: 'Led backend systems' },
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
    // BUG-113: flush any outstanding locations requests before verify
    const locReqs = httpMock.match(locationsUrl);
    locReqs.forEach(r => { if (!r.cancelled) { r.flush(mockLocations); } });
    // DF-38: flush any outstanding option-source requests before verify
    httpMock.match(departmentsUrl).forEach(r => { if (!r.cancelled) { r.flush(mockDepartments); } });
    httpMock.match(jobTitlesUrl).forEach(r => { if (!r.cancelled) { r.flush(mockJobTitles); } });
    httpMock.match(employmentTypesUrl).forEach(r => { if (!r.cancelled) { r.flush(mockEmploymentTypes); } });
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

    it('should show all 12 section tabs (including Compensation tab from US-PAY-002)', fakeAsync(() => {
      fixture.detectChanges();
      const req = httpMock.expectOne(profileUrl);
      req.flush(mockProfile);
      tick();
      fixture.detectChanges();

      expect(component.sectionList.length).toBe(12);
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

      expect(component.getStatusBadgeClass('Active')).toBe('badge-active');
      expect(component.getStatusBadgeClass('Probation')).toBe('badge-probation');
      expect(component.getStatusBadgeClass('Terminated')).toBe('badge-terminated');
      expect(component.getStatusBadgeClass('Suspended')).toBe('badge-suspended');
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

      // DF-36: inline saves now PATCH {id}/profile with a numeric rowVersion.
      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
      expect(patchReq.request.body.rowVersion).toBe(12345);
      expect(patchReq.request.body.personalInfo).toBeTruthy();

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

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
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

      // DF-36: contact edits PATCH {id}/profile with a `contactInfo` payload —
      // only phone/personalEmail/address are backend-supported.
      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
      expect(patchReq.request.body.contactInfo.phone).toBe('+94779999999');
      expect(patchReq.request.body.rowVersion).toBe(12345);

      const updatedProfile = { ...mockProfile, phone: '+94779999999', xmin: '12346' };
      patchReq.flush(updatedProfile);
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith('Changes saved successfully.');
      expect(component.profile()!.xmin).toBe('12346');
      expect(component.editingSection()).toBeNull();
    }));

    it('should send a numeric rowVersion in the request body', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('personal-info');
      component.saveSection('personal-info');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
      // BE rowVersion is a uint; the FE xmin string is converted with Number(...).
      expect(patchReq.request.body.rowVersion).toBe(12345);
      patchReq.flush({ ...mockProfile, xmin: '12346' });
    }));

    it('personal-info: omits nationalId when left blank (ISSUE-293)', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('personal-info');
      // populateForm leaves nationalId blank ("keep current")
      component.saveSection('personal-info');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.personalInfo.firstName).toBe('John');
      expect('nationalId' in patchReq.request.body.personalInfo).toBeFalse();
      patchReq.flush({ ...mockProfile, xmin: '12346' });
    }));

    it('emergency-contacts: maps the form `name` to the BE `contactName` key', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('emergency-contacts');
      component.saveSection('emergency-contacts');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
      expect(patchReq.request.body.emergencyContacts.length).toBe(1);
      expect(patchReq.request.body.emergencyContacts[0].contactName).toBe('Jane Doe');
      expect(patchReq.request.body.emergencyContacts[0].phone).toBe('+94779876543');
      patchReq.flush({ ...mockProfile, xmin: '12346' });
    }));
  });

  // ─── DF-38/39: custom fields + backed education/work-history/dependents ──
  describe('DF-38/39: custom fields serialization + backed sections', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('custom-fields: sends a JSON string + updateCustomFields=true', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      // Custom-field definitions drive the custom-fields form.
      httpMock.expectOne(customFieldsUrl).flush([
        { id: 'cf-1', fieldKey: 'tshirt_size', fieldName: 'T-Shirt Size', fieldType: 'text', entityType: 'employee', isRequired: false, isActive: true, displayOrder: 1, options: [] },
      ]);
      httpMock.match(locationsUrl).forEach(r => r.flush(mockLocations));
      tick();

      component.toggleEdit('custom-fields');
      component.customFieldsForm.patchValue({ tshirt_size: 'L' });
      component.saveSection('custom-fields');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
      expect(patchReq.request.body.updateCustomFields).toBeTrue();
      expect(typeof patchReq.request.body.customFields).toBe('string');
      expect(JSON.parse(patchReq.request.body.customFields).tshirt_size).toBe('L');
      patchReq.flush({ ...mockProfile, xmin: '12346' });
    }));

    it('DF-39: marks education/work-history/dependents as persistable', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      // DF-39: the backend now backs these three sections.
      expect(component.isSectionPersistable('education')).toBeTrue();
      expect(component.isSectionPersistable('work-history')).toBeTrue();
      expect(component.isSectionPersistable('dependents')).toBeTrue();
      expect(component.isSectionPersistable('contact')).toBeTrue();
      expect(component.isSectionPersistable('personal-info')).toBeTrue();
    }));

    it('DF-39: fires a PATCH with the update flag for each backed section', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      const cases: Array<{ section: 'education' | 'work-history' | 'dependents'; flag: string; list: string }> = [
        { section: 'education', flag: 'updateEducation', list: 'education' },
        { section: 'work-history', flag: 'updateWorkHistory', list: 'workHistory' },
        { section: 'dependents', flag: 'updateDependents', list: 'dependents' },
      ];

      for (const c of cases) {
        component.toggleEdit(c.section);
        component.saveSection(c.section);
        tick();

        const patchReq = httpMock.expectOne(profileUrl);
        expect(patchReq.request.method).toBe('PATCH');
        expect(patchReq.request.body[c.flag]).toBeTrue();
        expect(Array.isArray(patchReq.request.body[c.list])).toBeTrue();
        patchReq.flush({ ...mockProfile, xmin: '12346' });
        tick();
      }
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

      expect(component.getStatusBadgeClass('Active')).toBe('badge-active');
      expect(component.getStatusBadgeClass('Probation')).toBe('badge-probation');
      expect(component.getStatusBadgeClass('Terminated')).toBe('badge-terminated');
      expect(component.getStatusBadgeClass('Suspended')).toBe('badge-suspended');
      expect(component.getStatusBadgeClass('Inactive')).toBe('badge-inactive');
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/status/transitions`
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
        (r) => r.url === `${environment.apiBaseUrl}/tenant/employees` &&
          r.params.get('search') === 'Al' &&
          r.params.get('statuses') === 'Active'
      );
      searchReq.flush({
        items: [{ ...mockProfile, employeeId: 'mgr-1', firstName: 'Alice', lastName: 'Boss' }],
        totalCount: 1,
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/manager`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/manager`
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
        `${environment.apiBaseUrl}/tenant/employees/emp-1/manager`
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

  // ─── BUG-113: work-location assignment on the employment section ────
  describe('BUG-113: employment-section location', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('should load active locations for the Location select', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(locationsUrl).flush(mockLocations);
      tick();

      expect(component.locations().length).toBe(1);
      expect(component.locations()[0].name).toBe('Colombo HQ');
    }));

    it('should expose a locationId control on the employment form populated from the profile', fakeAsync(() => {
      fixture.detectChanges();
      httpMock
        .expectOne(profileUrl)
        .flush({ ...mockProfile, locationId: 'loc-1', locationName: 'Colombo HQ' });
      httpMock.expectOne(locationsUrl).flush(mockLocations);
      tick();

      component.toggleEdit('employment');

      expect(component.employmentForm.get('locationId')).toBeTruthy();
      expect(component.employmentForm.value.locationId).toBe('loc-1');
    }));

    it('should include locationId in the employment-update payload when a location is selected', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(locationsUrl).flush(mockLocations);
      tick();

      component.toggleEdit('employment');
      component.employmentForm.patchValue({ locationId: 'loc-1' });
      component.saveSection('employment');
      tick();

      // DF-36: employment edits PATCH {id}/profile with an `employmentInfo` payload.
      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
      expect(patchReq.request.body.employmentInfo.locationId).toBe('loc-1');

      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    it('should send locationId null in the employment-update payload when cleared', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(locationsUrl).flush(mockLocations);
      tick();

      component.toggleEdit('employment');
      component.employmentForm.patchValue({ locationId: '' });
      component.saveSection('employment');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.employmentInfo.locationId).toBeNull();

      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));
  });

  // ─── DF-38: employment id-selects + address detail + customFields parse ──
  describe('DF-38: employment id-selects, address detail, customFields parse', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('loads department / job-title / employment-type options from their services', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(departmentsUrl).flush(mockDepartments);
      httpMock.expectOne(jobTitlesUrl).flush(mockJobTitles);
      httpMock.expectOne(employmentTypesUrl).flush(mockEmploymentTypes);
      tick();

      expect(component.departments().length).toBe(2);
      expect(component.departments()[0].name).toBe('Engineering');
      expect(component.jobTitles().length).toBe(2);
      expect(component.jobTitles()[0].titleName).toBe('Software Engineer');
      expect(component.employmentTypes().length).toBe(4);
      expect(component.employmentTypes()[0].value).toBe('FullTime');
    }));

    it('filters out inactive departments and job titles', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(departmentsUrl).flush([
        ...mockDepartments,
        { ...mockDepartments[0], departmentId: 'dept-x', name: 'Archived', isActive: false },
      ]);
      httpMock.expectOne(jobTitlesUrl).flush([
        ...mockJobTitles,
        { ...mockJobTitles[0], jobTitleId: 'jt-x', titleName: 'Archived Title', isActive: false },
      ]);
      httpMock.expectOne(employmentTypesUrl).flush(mockEmploymentTypes);
      tick();

      expect(component.departments().every(d => d.isActive)).toBeTrue();
      expect(component.jobTitles().every(j => j.isActive)).toBeTrue();
    }));

    it('populates the employment form with departmentId / jobTitleId / employmentType (not names)', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(departmentsUrl).flush(mockDepartments);
      httpMock.expectOne(jobTitlesUrl).flush(mockJobTitles);
      httpMock.expectOne(employmentTypesUrl).flush(mockEmploymentTypes);
      tick();

      component.toggleEdit('employment');

      expect(component.employmentForm.value.departmentId).toBe('dept-1');
      expect(component.employmentForm.value.jobTitleId).toBe('jt-1');
      expect(component.employmentForm.value.employmentType).toBe('FullTime');
      // Status and dateOfJoining are no longer form controls (DF-38).
      expect(component.employmentForm.get('status')).toBeNull();
      expect(component.employmentForm.get('dateOfJoining')).toBeNull();
    }));

    it('sends the SELECTED departmentId / jobTitleId / employmentType enum in the PATCH body', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(departmentsUrl).flush(mockDepartments);
      httpMock.expectOne(jobTitlesUrl).flush(mockJobTitles);
      httpMock.expectOne(employmentTypesUrl).flush(mockEmploymentTypes);
      tick();

      component.toggleEdit('employment');
      component.employmentForm.patchValue({
        departmentId: 'dept-2',
        jobTitleId: 'jt-2',
        employmentType: 'Contract',
      });
      component.saveSection('employment');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.method).toBe('PATCH');
      expect(patchReq.request.body.employmentInfo.departmentId).toBe('dept-2');
      expect(patchReq.request.body.employmentInfo.jobTitleId).toBe('jt-2');
      expect(patchReq.request.body.employmentInfo.employmentType).toBe('Contract');
      // DF-38: status is never sent from the employment edit form.
      expect('status' in patchReq.request.body.employmentInfo).toBeFalse();
      // DF-38: dateOfJoining is read-only and never sent from the employment edit form.
      expect('dateOfJoining' in patchReq.request.body.employmentInfo).toBeFalse();
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    // GAP-A: the employment-type select must render non-blank labels AND send the
    // exact enum NAME (not the display text). This exercises the service map()
    // over the real BE { id, name, displayName } shape — it would fail if the
    // map() were removed (options would be undefined value/label).
    it('maps BE employment-types { id, name, displayName } → { value, label } and sends the enum name', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      httpMock.expectOne(departmentsUrl).flush(mockDepartments);
      httpMock.expectOne(jobTitlesUrl).flush(mockJobTitles);
      httpMock.expectOne(employmentTypesUrl).flush(mockEmploymentTypes);
      tick();

      // Options are populated with a non-blank label and the enum name as value.
      const opts = component.employmentTypes();
      expect(opts.length).toBe(4);
      expect(opts[0].value).toBe('FullTime');
      expect(opts[0].label).toBe('Full-Time');
      expect(opts.every(o => !!o.label && !!o.value)).toBeTrue();

      component.toggleEdit('employment');
      // Select the "Contract" option BY ITS MAPPED value (the enum name).
      const contract = opts.find(o => o.label === 'Contract')!;
      component.employmentForm.patchValue({ employmentType: contract.value });
      component.saveSection('employment');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      // The BE binds the enum member NAME, so that is what must be sent.
      expect(patchReq.request.body.employmentInfo.employmentType).toBe('Contract');
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    it('includes city / state / postalCode / country in the contact PATCH body', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('contact');
      component.contactForm.patchValue({
        city: 'Kandy',
        state: 'Central',
        postalCode: '20000',
        country: 'Sri Lanka',
      });
      component.saveSection('contact');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.contactInfo.city).toBe('Kandy');
      expect(patchReq.request.body.contactInfo.state).toBe('Central');
      expect(patchReq.request.body.contactInfo.postalCode).toBe('20000');
      expect(patchReq.request.body.contactInfo.country).toBe('Sri Lanka');
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    it('parses a raw JSON-string customFields from the read API into an object', fakeAsync(() => {
      fixture.detectChanges();
      // BE returns customFields as a raw JSON string.
      httpMock.expectOne(profileUrl).flush({
        ...mockProfile,
        customFields: '{"tshirt_size":"XL","remote":true}',
      });
      tick();

      const cf = component.profile()!.customFields as Record<string, unknown>;
      expect(cf).toEqual(jasmine.objectContaining({ tshirt_size: 'XL', remote: true }));
    }));

    it('tolerates an invalid customFields JSON string (parses to null)', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush({
        ...mockProfile,
        customFields: 'not-json{',
      });
      tick();

      expect(component.profile()!.customFields).toBeNull();
    }));

    it('prefills a custom-field control from the parsed customFields object', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush({
        ...mockProfile,
        customFields: '{"tshirt_size":"XL"}',
      });
      httpMock.expectOne(customFieldsUrl).flush([
        { id: 'cf-1', fieldKey: 'tshirt_size', fieldName: 'T-Shirt Size', fieldType: 'text', entityType: 'employee', isRequired: false, isActive: true, displayOrder: 1, options: [] },
      ]);
      tick();

      component.toggleEdit('custom-fields');
      expect(component.customFieldsForm.get('tshirt_size')!.value).toBe('XL');
    }));
  });

  // ─── DF-39: education / work-history / dependents send-mappings ──
  describe('DF-39: education / work-history / dependents send-mappings', () => {
    beforeEach(() => {
      setupTestBed('HR Officer');
    });

    it('is editable for all three sections (HR Officer)', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      expect(component.canEditSection('education')).toBeTrue();
      expect(component.canEditSection('work-history')).toBeTrue();
      expect(component.canEditSection('dependents')).toBeTrue();
    }));

    it('maps the education form-array + updateEducation flag into the PATCH body', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('education');
      component.saveSection('education');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.updateEducation).toBeTrue();
      expect(patchReq.request.body.education.length).toBe(1);
      const eduRow = patchReq.request.body.education[0];
      expect(eduRow.institution).toBe('University of Colombo');
      expect(eduRow.degree).toBe('BSc CS');
      expect(eduRow.endYear).toBe('2012');
      // GAP-B: existing row must carry its id (else the BE churns a new PK) plus
      // fieldOfStudy/startYear (else the full-replace write nulls those columns).
      expect(eduRow.id).toBe('edu-1');
      expect(eduRow.fieldOfStudy).toBe('Computer Science');
      expect(eduRow.startYear).toBe('2008');
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    it('maps the work-history form-array + updateWorkHistory flag into the PATCH body', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('work-history');
      component.saveSection('work-history');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.updateWorkHistory).toBeTrue();
      expect(patchReq.request.body.workHistory.length).toBe(1);
      const whRow = patchReq.request.body.workHistory[0];
      expect(whRow.company).toBe('Google');
      expect(whRow.position).toBe('Senior Engineer');
      expect(whRow.fromDate).toBe('2015-01-01');
      // GAP-B: existing row must carry its id (avoid PK churn) + description (else
      // the full-replace write nulls it).
      expect(whRow.id).toBe('wh-1');
      expect(whRow.description).toBe('Led backend systems');
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    it('maps the dependents form-array + updateDependents flag into the PATCH body', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('dependents');
      component.saveSection('dependents');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.updateDependents).toBeTrue();
      expect(patchReq.request.body.dependents.length).toBe(1);
      const depRow = patchReq.request.body.dependents[0];
      expect(depRow.name).toBe('Baby Doe');
      expect(depRow.relationship).toBe('Child');
      expect(depRow.dateOfBirth).toBe('2022-05-20');
      // GAP-B: existing row must carry its id so the BE updates in place.
      expect(depRow.id).toBe('dep-1');
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    it('sends an added education row with a null endYear when left blank AND omits its id', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('education');
      component.addEducationRecord();
      component.educationFormControls.at(1).patchValue({ institution: 'MIT', degree: 'MSc' });
      component.saveSection('education');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.education.length).toBe(2);
      const newRow = patchReq.request.body.education[1];
      expect(newRow.institution).toBe('MIT');
      expect(newRow.endYear).toBeNull();
      // GAP-B: a genuinely-new row must NOT carry an id (the BE mints one).
      expect('id' in newRow).toBeFalse();
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));

    // GAP-B: read-hydration must prefill the newly-added fields so an edit
    // round-trips instead of dropping fieldOfStudy/startYear/description.
    it('hydrates education fieldOfStudy/startYear and work-history description from the profile', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('education');
      const eduRow = component.educationFormControls.at(0).value;
      expect(eduRow.id).toBe('edu-1');
      expect(eduRow.fieldOfStudy).toBe('Computer Science');
      expect(eduRow.startYear).toBe('2008');
      component.cancelEdit();

      component.toggleEdit('work-history');
      const whRow = component.workHistoryFormControls.at(0).value;
      expect(whRow.id).toBe('wh-1');
      expect(whRow.description).toBe('Led backend systems');
    }));

    it('sends an added work-history row without an id and preserves an edited description', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(profileUrl).flush(mockProfile);
      tick();

      component.toggleEdit('work-history');
      // Edit the existing row's description...
      component.workHistoryFormControls.at(0).patchValue({ description: 'Updated summary' });
      // ...and add a brand-new row.
      component.addWorkHistoryRecord();
      component.workHistoryFormControls.at(1).patchValue({ company: 'Meta', position: 'Staff Eng' });
      component.saveSection('work-history');
      tick();

      const patchReq = httpMock.expectOne(profileUrl);
      expect(patchReq.request.body.workHistory.length).toBe(2);
      expect(patchReq.request.body.workHistory[0].id).toBe('wh-1');
      expect(patchReq.request.body.workHistory[0].description).toBe('Updated summary');
      expect('id' in patchReq.request.body.workHistory[1]).toBeFalse();
      patchReq.flush({ ...mockProfile, xmin: '12346' });
      tick();
    }));
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
    expect(getStatusBadgeClasses('Active')).toBe('bg-green-100 text-green-800');
  });

  it('should return amber classes for probation', () => {
    expect(getStatusBadgeClasses('Probation')).toBe('bg-amber-100 text-amber-800');
  });

  it('should return gray classes for suspended', () => {
    expect(getStatusBadgeClasses('Suspended')).toBe('bg-gray-100 text-gray-800');
  });

  it('should return red classes for terminated', () => {
    expect(getStatusBadgeClasses('Terminated')).toBe('bg-red-100 text-red-800');
  });

  it('should return slate classes for inactive', () => {
    expect(getStatusBadgeClasses('Inactive')).toBe('bg-slate-100 text-slate-800');
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
