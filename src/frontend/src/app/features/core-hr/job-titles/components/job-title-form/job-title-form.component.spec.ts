import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { ComponentRef } from '@angular/core';

import { JobTitleFormComponent } from './job-title-form.component';
import { JobTitleService } from '../../services/job-title.service';
import { IJobTitle } from '../../models/job-title.models';

describe('JobTitleFormComponent', () => {
  let component: JobTitleFormComponent;
  let componentRef: ComponentRef<JobTitleFormComponent>;
  let fixture: ComponentFixture<JobTitleFormComponent>;
  let jobTitleServiceSpy: jasmine.SpyObj<JobTitleService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockJobTitle: IJobTitle = {
    jobTitleId: 'jt-1',
    tenantId: 'tenant-1',
    titleName: 'Software Engineer',
    description: 'Develops software applications',
    gradeId: null,
    gradeName: null,
    isActive: true,
    employeeCount: 10,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  beforeEach(async () => {
    jobTitleServiceSpy = jasmine.createSpyObj('JobTitleService', [
      'createJobTitle',
      'updateJobTitle',
    ]);
    jobTitleServiceSpy.createJobTitle.and.returnValue(of(mockJobTitle));
    jobTitleServiceSpy.updateJobTitle.and.returnValue(of(mockJobTitle));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [JobTitleFormComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: JobTitleService, useValue: jobTitleServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(JobTitleFormComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  // --- Create Mode -------------------------------------------

  describe('create mode', () => {
    beforeEach(() => {
      componentRef.setInput('jobTitle', null);
      fixture.detectChanges();
    });

    it('should create', () => {
      expect(component).toBeTruthy();
    });

    it('should initialize with empty form', () => {
      expect(component.form.value.titleName).toBe('');
      expect(component.form.value.description).toBe('');
      expect(component.form.value.isActive).toBeTrue();
    });

    it('should validate required titleName field', () => {
      const nameCtrl = component.form.get('titleName')!;
      expect(nameCtrl.valid).toBeFalse();

      nameCtrl.setValue('New Job Title');
      expect(nameCtrl.valid).toBeTrue();
    });

    it('should validate titleName max length (150 chars)', () => {
      const nameCtrl = component.form.get('titleName')!;
      nameCtrl.setValue('A'.repeat(151));
      expect(nameCtrl.hasError('maxlength')).toBeTrue();

      nameCtrl.setValue('A'.repeat(150));
      expect(nameCtrl.valid).toBeTrue();
    });

    it('should call createJobTitle on submit', () => {
      component.form.patchValue({
        titleName: 'UX Designer',
        description: 'User experience design',
        isActive: true,
      });
      component.form.markAsDirty();

      component.onSubmit();

      expect(jobTitleServiceSpy.createJobTitle).toHaveBeenCalledWith({
        titleName: 'UX Designer',
        description: 'User experience design',
        isActive: true,
      });
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('should not submit when form is invalid', () => {
      component.onSubmit();
      expect(jobTitleServiceSpy.createJobTitle).not.toHaveBeenCalled();
    });

    it('should handle duplicate name error (AC-3)', () => {
      jobTitleServiceSpy.createJobTitle.and.returnValue(
        throwError(() => ({
          status: 409,
          error: {
            message: 'A job title with this name already exists.',
            code: 'duplicate_name',
          },
        }))
      );

      component.form.patchValue({ titleName: 'Software Engineer' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(component.duplicateNameError()).toBe(
        'A job title with this name already exists.'
      );
      expect(component.isSaving()).toBeFalse();
    });

    it('should handle generic error', () => {
      jobTitleServiceSpy.createJobTitle.and.returnValue(
        throwError(() => ({
          status: 500,
          error: {
            message: 'Unexpected error',
          },
        }))
      );

      component.form.patchValue({ titleName: 'Test' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(toastrSpy.error).toHaveBeenCalledWith('Unexpected error');
    });

    it('should trim whitespace from titleName and description', () => {
      component.form.patchValue({
        titleName: '  Trimmed Title  ',
        description: '  Trimmed description  ',
      });
      component.form.markAsDirty();
      component.onSubmit();

      expect(jobTitleServiceSpy.createJobTitle).toHaveBeenCalledWith(
        jasmine.objectContaining({
          titleName: 'Trimmed Title',
          description: 'Trimmed description',
        })
      );
    });
  });

  // --- Edit Mode ---------------------------------------------

  describe('edit mode', () => {
    beforeEach(() => {
      componentRef.setInput('jobTitle', mockJobTitle);
      fixture.detectChanges();
    });

    it('should populate form with job title data', () => {
      expect(component.form.value.titleName).toBe('Software Engineer');
      expect(component.form.value.description).toBe(
        'Develops software applications'
      );
      expect(component.form.value.isActive).toBeTrue();
    });

    it('should call updateJobTitle on submit', () => {
      component.form.patchValue({ titleName: 'Senior Software Engineer' });
      component.form.markAsDirty();

      component.onSubmit();

      expect(jobTitleServiceSpy.updateJobTitle).toHaveBeenCalledWith(
        'jt-1',
        jasmine.objectContaining({
          titleName: 'Senior Software Engineer',
        })
      );
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('should show default error message when backend message is missing', () => {
      jobTitleServiceSpy.updateJobTitle.and.returnValue(
        throwError(() => ({
          status: 500,
          error: {},
        }))
      );

      component.form.patchValue({ titleName: 'Updated' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(toastrSpy.error).toHaveBeenCalledWith('Failed to save job title.');
    });
  });
});
