import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { ComponentRef } from '@angular/core';

import { DepartmentFormComponent } from './department-form.component';
import { DepartmentService } from '../../services/department.service';
import { IDepartment } from '../../models/department.models';

describe('DepartmentFormComponent', () => {
  let component: DepartmentFormComponent;
  let componentRef: ComponentRef<DepartmentFormComponent>;
  let fixture: ComponentFixture<DepartmentFormComponent>;
  let departmentServiceSpy: jasmine.SpyObj<DepartmentService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockDepartment: IDepartment = {
    departmentId: 'dept-1',
    tenantId: 'tenant-1',
    name: 'Engineering',
    description: 'Software engineering team',
    parentDepartmentId: null,
    parentDepartmentName: null,
    managerEmployeeId: null,
    managerName: null,
    isActive: true,
    employeeCount: 10,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const allDepartments: IDepartment[] = [
    mockDepartment,
    {
      departmentId: 'dept-2',
      tenantId: 'tenant-1',
      name: 'Frontend',
      description: null,
      parentDepartmentId: 'dept-1',
      parentDepartmentName: 'Engineering',
      managerEmployeeId: null,
      managerName: null,
      isActive: true,
      employeeCount: 5,
      createdAt: '2026-01-15T00:00:00Z',
      updatedAt: '2026-01-15T00:00:00Z',
    },
    {
      departmentId: 'dept-3',
      tenantId: 'tenant-1',
      name: 'Design',
      description: null,
      parentDepartmentId: null,
      parentDepartmentName: null,
      managerEmployeeId: null,
      managerName: null,
      isActive: true,
      employeeCount: 3,
      createdAt: '2026-02-01T00:00:00Z',
      updatedAt: '2026-02-01T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    departmentServiceSpy = jasmine.createSpyObj('DepartmentService', [
      'createDepartment',
      'updateDepartment',
    ]);
    departmentServiceSpy.createDepartment.and.returnValue(of(mockDepartment));
    departmentServiceSpy.updateDepartment.and.returnValue(of(mockDepartment));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [DepartmentFormComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: DepartmentService, useValue: departmentServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DepartmentFormComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  // ─── Create Mode ──────────────────────────────────────────

  describe('create mode', () => {
    beforeEach(() => {
      componentRef.setInput('department', null);
      componentRef.setInput('allDepartments', allDepartments);
      fixture.detectChanges();
    });

    it('should create', () => {
      expect(component).toBeTruthy();
    });

    it('should initialize with empty form', () => {
      expect(component.form.value.name).toBe('');
      expect(component.form.value.description).toBe('');
      expect(component.form.value.parentDepartmentId).toBeNull();
      expect(component.form.value.isActive).toBeTrue();
    });

    it('should validate required name field', () => {
      const nameCtrl = component.form.get('name')!;
      expect(nameCtrl.valid).toBeFalse();

      nameCtrl.setValue('New Department');
      expect(nameCtrl.valid).toBeTrue();
    });

    it('should validate name max length (150 chars)', () => {
      const nameCtrl = component.form.get('name')!;
      nameCtrl.setValue('A'.repeat(151));
      expect(nameCtrl.hasError('maxlength')).toBeTrue();

      nameCtrl.setValue('A'.repeat(150));
      expect(nameCtrl.valid).toBeTrue();
    });

    it('should call createDepartment on submit', () => {
      component.form.patchValue({
        name: 'New Department',
        description: 'Test description',
        parentDepartmentId: null,
        isActive: true,
      });
      component.form.markAsDirty();

      component.onSubmit();

      expect(departmentServiceSpy.createDepartment).toHaveBeenCalledWith({
        name: 'New Department',
        description: 'Test description',
        parentDepartmentId: null,
        isActive: true,
      });
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('should not submit when form is invalid', () => {
      component.onSubmit();
      expect(departmentServiceSpy.createDepartment).not.toHaveBeenCalled();
    });

    it('should handle duplicate name error (AC-3)', () => {
      departmentServiceSpy.createDepartment.and.returnValue(
        throwError(() => ({
          status: 409,
          error: {
            message: 'A department with this name already exists.',
            code: 'duplicate_name',
          },
        }))
      );

      component.form.patchValue({ name: 'Engineering' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(component.duplicateNameError()).toBe(
        'A department with this name already exists.'
      );
      expect(component.isSaving()).toBeFalse();
    });

    it('should handle circular reference error (FR-5)', () => {
      departmentServiceSpy.createDepartment.and.returnValue(
        throwError(() => ({
          status: 422,
          error: {
            message: 'Circular reference detected.',
            code: 'circular_reference',
          },
        }))
      );

      component.form.patchValue({ name: 'Test' });
      component.form.markAsDirty();
      component.onSubmit();

      expect(toastrSpy.error).toHaveBeenCalledWith(
        'Circular reference detected.'
      );
    });

    it('should trim whitespace from name and description', () => {
      component.form.patchValue({
        name: '  Trimmed Name  ',
        description: '  Trimmed description  ',
      });
      component.form.markAsDirty();
      component.onSubmit();

      expect(departmentServiceSpy.createDepartment).toHaveBeenCalledWith(
        jasmine.objectContaining({
          name: 'Trimmed Name',
          description: 'Trimmed description',
        })
      );
    });

    it('should provide all departments as parent options', () => {
      const options = component.parentOptions();
      expect(options.length).toBe(3); // all three active departments
    });
  });

  // ─── Edit Mode ────────────────────────────────────────────

  describe('edit mode', () => {
    beforeEach(() => {
      componentRef.setInput('department', mockDepartment);
      componentRef.setInput('allDepartments', allDepartments);
      fixture.detectChanges();
    });

    it('should populate form with department data', () => {
      expect(component.form.value.name).toBe('Engineering');
      expect(component.form.value.description).toBe(
        'Software engineering team'
      );
      expect(component.form.value.isActive).toBeTrue();
    });

    it('should call updateDepartment on submit', () => {
      component.form.patchValue({ name: 'Engineering (Updated)' });
      component.form.markAsDirty();

      component.onSubmit();

      expect(departmentServiceSpy.updateDepartment).toHaveBeenCalledWith(
        'dept-1',
        jasmine.objectContaining({
          name: 'Engineering (Updated)',
        })
      );
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('should exclude current department and descendants from parent options (FR-5)', () => {
      // dept-1 (Engineering) and dept-2 (Frontend, child of Engineering) should be excluded
      const options = component.parentOptions();
      const optionIds = options.map((o) => o.department.departmentId);
      expect(optionIds).not.toContain('dept-1');
      expect(optionIds).not.toContain('dept-2');
      // Only dept-3 (Design) should remain
      expect(optionIds).toContain('dept-3');
    });
  });
});
