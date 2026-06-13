import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { EmployeeWizardComponent } from './employee-wizard.component';
import { environment } from '../../../../../../environments/environment';

describe('EmployeeWizardComponent', () => {
  let component: EmployeeWizardComponent;
  let fixture: ComponentFixture<EmployeeWizardComponent>;
  let httpMock: HttpTestingController;
  let router: Router;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const deptsUrl = `${environment.apiBaseUrl}/departments`;
  const jtUrl = `${environment.apiBaseUrl}/job-titles`;
  const cfUrl = `${environment.apiBaseUrl}/tenant/custom-fields/active?entityType=employee`;
  const empUrl = `${environment.apiBaseUrl}/employees`;

  const mockDepartments = [
    {
      departmentId: 'dept-1',
      tenantId: 'tenant-1',
      name: 'Engineering',
      description: null,
      parentDepartmentId: null,
      parentDepartmentName: null,
      managerEmployeeId: null,
      managerName: null,
      isActive: true,
      employeeCount: 5,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
  ];

  const mockJobTitles = [
    {
      jobTitleId: 'jt-1',
      tenantId: 'tenant-1',
      titleName: 'Software Engineer',
      description: null,
      gradeId: null,
      gradeName: null,
      isActive: true,
      employeeCount: 0,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [EmployeeWizardComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'employees', children: [] },
          { path: 'employees/new', component: EmployeeWizardComponent },
        ]),
        provideAnimationsAsync(),
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    spyOn(router, 'navigate');

    fixture = TestBed.createComponent(EmployeeWizardComponent);
    component = fixture.componentInstance;
  });

  /** Flush the reference data requests that fire on ngOnInit */
  function flushReferenceData(): void {
    fixture.detectChanges(); // triggers ngOnInit

    const deptReq = httpMock.expectOne(deptsUrl);
    deptReq.flush(mockDepartments);

    const jtReq = httpMock.expectOne(jtUrl);
    jtReq.flush(mockJobTitles);

    // US-CHR-012: also flush custom fields
    const cfReq = httpMock.expectOne(cfUrl);
    cfReq.flush([]);

    fixture.detectChanges();
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('should create the component', () => {
    flushReferenceData();
    expect(component).toBeTruthy();
  });

  it('should start on step 0 (Personal Info)', () => {
    flushReferenceData();
    expect(component.currentStep()).toBe(0);
  });

  it('should load departments and job titles on init', () => {
    flushReferenceData();
    expect(component.departments().length).toBe(1);
    expect(component.departments()[0].name).toBe('Engineering');
    expect(component.jobTitles().length).toBe(1);
    expect(component.jobTitles()[0].titleName).toBe('Software Engineer');
  });

  it('should filter out inactive departments', () => {
    fixture.detectChanges();

    const deptReq = httpMock.expectOne(deptsUrl);
    deptReq.flush([
      ...mockDepartments,
      { ...mockDepartments[0], departmentId: 'dept-2', name: 'Inactive Dept', isActive: false },
    ]);

    const jtReq = httpMock.expectOne(jtUrl);
    jtReq.flush(mockJobTitles);

    const cfReq = httpMock.expectOne(cfUrl);
    cfReq.flush([]);
    fixture.detectChanges();

    expect(component.departments().length).toBe(1);
    expect(component.departments()[0].name).toBe('Engineering');
  });

  describe('step navigation', () => {
    beforeEach(() => {
      flushReferenceData();
    });

    it('should not proceed from step 0 if required fields are empty', () => {
      component.nextStep();
      expect(component.currentStep()).toBe(0);
    });

    it('should proceed from step 0 when required fields are valid', () => {
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
      });
      component.nextStep();
      expect(component.currentStep()).toBe(1);
    });

    it('should allow proceeding from step 1 (Contact) without fields (all optional)', () => {
      // Fill step 0
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
      });
      component.nextStep(); // go to step 1
      expect(component.currentStep()).toBe(1);

      component.nextStep(); // step 1 has no required fields
      expect(component.currentStep()).toBe(2);
    });

    it('should go back to previous step', () => {
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
      });
      component.nextStep();
      expect(component.currentStep()).toBe(1);

      component.previousStep();
      expect(component.currentStep()).toBe(0);
    });

    it('should not go to step beyond furthest visited step', () => {
      // Only step 0 and step 1 visited so far
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
      });
      component.nextStep(); // step 1
      expect(component.furthestStep()).toBe(1);

      // Trying to jump to step 3 should not work
      component.goToStep(3);
      expect(component.currentStep()).toBe(1);
    });

    it('should allow clicking back to a completed step', () => {
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
      });
      component.nextStep(); // 0 -> 1
      component.nextStep(); // 1 -> 2
      expect(component.currentStep()).toBe(2);

      component.goToStep(0); // jump back to step 0
      expect(component.currentStep()).toBe(0);
    });

    it('should track furthest step correctly', () => {
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
      });
      component.nextStep(); // 0->1
      component.nextStep(); // 1->2
      component.nextStep(); // 2->3
      expect(component.furthestStep()).toBe(3);

      component.goToStep(0); // go back
      expect(component.furthestStep()).toBe(3); // still 3
    });

    it('should return true for isLastStep on the last step', () => {
      expect(component.isLastStep()).toBeFalse();

      // Navigate to the last step
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
        dateOfJoining: '2026-07-01',
        departmentId: 'dept-1',
        jobTitleId: 'jt-1',
        employmentType: 'Full-Time',
      });
      for (let i = 0; i < 6; i++) {
        component.nextStep();
      }
      expect(component.isLastStep()).toBeTrue();
    });
  });

  describe('form validation', () => {
    beforeEach(() => {
      flushReferenceData();
    });

    it('should require firstName', () => {
      const control = component.form.get('firstName')!;
      control.setValue('');
      expect(control.valid).toBeFalse();
      control.setValue('John');
      expect(control.valid).toBeTrue();
    });

    it('should require lastName', () => {
      const control = component.form.get('lastName')!;
      control.setValue('');
      expect(control.valid).toBeFalse();
      control.setValue('Doe');
      expect(control.valid).toBeTrue();
    });

    it('should validate email format', () => {
      const control = component.form.get('email')!;
      control.setValue('invalid');
      expect(control.hasError('email')).toBeTrue();
      control.setValue('valid@test.com');
      expect(control.valid).toBeTrue();
    });

    it('should validate dateOfBirth age >= 16', () => {
      const control = component.form.get('dateOfBirth')!;
      // Set to a date that makes the person less than 16
      const today = new Date();
      const tooYoung = new Date(today.getFullYear() - 10, today.getMonth(), today.getDate());
      control.setValue(tooYoung.toISOString().split('T')[0]);
      expect(control.hasError('minAge')).toBeTrue();

      // Set to a date that makes the person >= 16
      const oldEnough = new Date(today.getFullYear() - 20, today.getMonth(), today.getDate());
      control.setValue(oldEnough.toISOString().split('T')[0]);
      expect(control.valid).toBeTrue();
    });

    it('should reject dateOfBirth in the future', () => {
      const control = component.form.get('dateOfBirth')!;
      const future = new Date();
      future.setFullYear(future.getFullYear() + 1);
      control.setValue(future.toISOString().split('T')[0]);
      expect(control.hasError('futureDate')).toBeTrue();
    });

    it('should validate dateOfJoining not > 90 days in the future (BR-4)', () => {
      const control = component.form.get('dateOfJoining')!;
      // 100 days in the future
      const tooFar = new Date();
      tooFar.setDate(tooFar.getDate() + 100);
      control.setValue(tooFar.toISOString().split('T')[0]);
      expect(control.hasError('maxFutureDate')).toBeTrue();

      // 30 days in the future (valid)
      const valid = new Date();
      valid.setDate(valid.getDate() + 30);
      control.setValue(valid.toISOString().split('T')[0]);
      expect(control.valid).toBeTrue();
    });

    it('should require departmentId', () => {
      const control = component.form.get('departmentId')!;
      expect(control.valid).toBeFalse();
      control.setValue('dept-1');
      expect(control.valid).toBeTrue();
    });

    it('should require jobTitleId', () => {
      const control = component.form.get('jobTitleId')!;
      expect(control.valid).toBeFalse();
      control.setValue('jt-1');
      expect(control.valid).toBeTrue();
    });

    it('should require employmentType', () => {
      const control = component.form.get('employmentType')!;
      expect(control.valid).toBeFalse();
      control.setValue('Full-Time');
      expect(control.valid).toBeTrue();
    });
  });

  describe('repeater sections', () => {
    beforeEach(() => {
      flushReferenceData();
    });

    it('should add and remove education entries', () => {
      expect(component.educationControls.length).toBe(0);
      component.addEducation();
      expect(component.educationControls.length).toBe(1);
      component.addEducation();
      expect(component.educationControls.length).toBe(2);
      component.removeEducation(0);
      expect(component.educationControls.length).toBe(1);
    });

    it('should add and remove work history entries', () => {
      expect(component.workHistoryControls.length).toBe(0);
      component.addWorkHistory();
      expect(component.workHistoryControls.length).toBe(1);
      component.removeWorkHistory(0);
      expect(component.workHistoryControls.length).toBe(0);
    });

    it('should add and remove dependent entries', () => {
      expect(component.dependentControls.length).toBe(0);
      component.addDependent();
      expect(component.dependentControls.length).toBe(1);
      component.removeDependent(0);
      expect(component.dependentControls.length).toBe(0);
    });
  });

  describe('photo handling', () => {
    beforeEach(() => {
      flushReferenceData();
    });

    it('should store selected photo file', () => {
      const mockFile = new File(['data'], 'photo.jpg', {
        type: 'image/jpeg',
      });
      component.onPhotoSelected(mockFile);
      expect(component.selectedPhoto()).toBe(mockFile);
    });

    it('should clear photo on remove', () => {
      const mockFile = new File(['data'], 'photo.jpg', {
        type: 'image/jpeg',
      });
      component.onPhotoSelected(mockFile);
      component.onPhotoRemoved();
      expect(component.selectedPhoto()).toBeNull();
    });
  });

  describe('form submission', () => {
    beforeEach(() => {
      flushReferenceData();
    });

    function fillAllRequired(): void {
      component.form.patchValue({
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
        dateOfJoining: '2026-07-01',
        departmentId: 'dept-1',
        jobTitleId: 'jt-1',
        employmentType: 'Full-Time',
      });
    }

    it('should submit successfully and navigate to list', fakeAsync(() => {
      fillAllRequired();
      component.onSubmit();

      const req = httpMock.expectOne(empUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.firstName).toBe('John');
      expect(req.request.body.email).toBe('john@test.com');

      req.flush({
        employeeId: 'emp-new',
        tenantId: 'tenant-1',
        employeeNo: 'EMP-0001',
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
        phone: null,
        dateOfBirth: null,
        gender: null,
        dateOfJoining: '2026-07-01',
        departmentId: 'dept-1',
        departmentName: 'Engineering',
        jobTitleId: 'jt-1',
        jobTitleName: 'Software Engineer',
        employmentType: 'Full-Time',
        status: 'active',
        profilePhotoUrl: null,
        customFields: null,
        isActive: true,
        createdAt: '2026-07-01T00:00:00Z',
        updatedAt: '2026-07-01T00:00:00Z',
      });
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith(
        'Employee "John Doe" created successfully.'
      );
      expect(router.navigate).toHaveBeenCalledWith(['/employees']);
    }));

    it('should not submit if required fields are missing', () => {
      component.onSubmit();
      httpMock.expectNone(empUrl);
      expect(component.isSaving()).toBeFalse();
    });

    it('should handle duplicate email error (AC-3)', fakeAsync(() => {
      fillAllRequired();
      component.onSubmit();

      const req = httpMock.expectOne(empUrl);
      req.flush(
        {
          message: 'An employee with this email already exists.',
          code: 'duplicate_email',
        },
        { status: 409, statusText: 'Conflict' }
      );
      tick();

      expect(component.duplicateEmailError()).toBe(
        'An employee with this email already exists.'
      );
      // Should navigate back to step 0 where email field is
      expect(component.currentStep()).toBe(0);
    }));

    it('should handle plan limit error (AC-5)', fakeAsync(() => {
      fillAllRequired();
      component.onSubmit();

      const req = httpMock.expectOne(empUrl);
      req.flush(
        {
          message:
            'Employee limit reached for your current plan. Please upgrade or contact your administrator.',
          code: 'plan_limit_reached',
        },
        { status: 403, statusText: 'Forbidden' }
      );
      tick();

      expect(component.planLimitError()).toBe(
        'Employee limit reached for your current plan. Please upgrade or contact your administrator.'
      );
    }));

    it('should show generic error toast for unrecognized errors', fakeAsync(() => {
      fillAllRequired();
      component.onSubmit();

      const req = httpMock.expectOne(empUrl);
      req.flush(
        { message: 'Internal server error' },
        { status: 500, statusText: 'Server Error' }
      );
      tick();

      expect(toastrSpy.error).toHaveBeenCalledWith('Internal server error');
    }));

    it('should submit with FormData when photo is attached', fakeAsync(() => {
      fillAllRequired();
      const photo = new File(['img-data'], 'avatar.jpg', {
        type: 'image/jpeg',
      });
      component.onPhotoSelected(photo);

      component.onSubmit();

      const req = httpMock.expectOne(empUrl);
      expect(req.request.body instanceof FormData).toBeTrue();

      req.flush({
        employeeId: 'emp-new',
        tenantId: 'tenant-1',
        employeeNo: 'EMP-0001',
        firstName: 'John',
        lastName: 'Doe',
        email: 'john@test.com',
        phone: null,
        dateOfBirth: null,
        gender: null,
        dateOfJoining: '2026-07-01',
        departmentId: 'dept-1',
        departmentName: 'Engineering',
        jobTitleId: 'jt-1',
        jobTitleName: 'Software Engineer',
        employmentType: 'Full-Time',
        status: 'active',
        profilePhotoUrl: 'https://storage.example.com/tenant-1/profile.jpg',
        customFields: null,
        isActive: true,
        createdAt: '2026-07-01T00:00:00Z',
        updatedAt: '2026-07-01T00:00:00Z',
      });
      tick();

      expect(toastrSpy.success).toHaveBeenCalled();
    }));
  });

  describe('save as draft', () => {
    beforeEach(() => {
      flushReferenceData();
    });

    it('should show info toast when saving draft', () => {
      component.saveDraft();
      expect(toastrSpy.info).toHaveBeenCalledWith('Draft saved locally.');
    });
  });

  describe('navigation', () => {
    beforeEach(() => {
      flushReferenceData();
    });

    it('should navigate back to employee list when goBack is called', () => {
      component.goBack();
      expect(router.navigate).toHaveBeenCalledWith(['/employees']);
    });
  });
});
