import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { DepartmentListComponent } from './department-list.component';
import { DepartmentService } from '../../services/department.service';
import { IDepartment } from '../../models/department.models';

describe('DepartmentListComponent', () => {
  let component: DepartmentListComponent;
  let fixture: ComponentFixture<DepartmentListComponent>;
  let departmentServiceSpy: jasmine.SpyObj<DepartmentService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockDepartments: IDepartment[] = [
    {
      departmentId: 'dept-1',
      tenantId: 'tenant-1',
      name: 'Engineering',
      description: 'Software engineering',
      parentDepartmentId: null,
      parentDepartmentName: null,
      managerEmployeeId: null,
      managerName: null,
      isActive: true,
      employeeCount: 10,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
    {
      departmentId: 'dept-2',
      tenantId: 'tenant-1',
      name: 'Frontend',
      description: 'Frontend development',
      parentDepartmentId: 'dept-1',
      parentDepartmentName: 'Engineering',
      managerEmployeeId: null,
      managerName: null,
      isActive: true,
      employeeCount: 0,
      createdAt: '2026-01-15T00:00:00Z',
      updatedAt: '2026-01-15T00:00:00Z',
    },
    {
      departmentId: 'dept-3',
      tenantId: 'tenant-1',
      name: 'Marketing',
      description: null,
      parentDepartmentId: null,
      parentDepartmentName: null,
      managerEmployeeId: null,
      managerName: null,
      isActive: false,
      employeeCount: 0,
      createdAt: '2026-02-01T00:00:00Z',
      updatedAt: '2026-02-01T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    departmentServiceSpy = jasmine.createSpyObj('DepartmentService', [
      'getDepartments',
      'deactivateDepartment',
    ]);
    departmentServiceSpy.getDepartments.and.returnValue(of(mockDepartments));
    departmentServiceSpy.deactivateDepartment.and.returnValue(of(undefined));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [DepartmentListComponent],
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

    fixture = TestBed.createComponent(DepartmentListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load departments on init', () => {
    fixture.detectChanges();
    expect(departmentServiceSpy.getDepartments).toHaveBeenCalled();
    expect(component.departments().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error state when loading fails', () => {
    departmentServiceSpy.getDepartments.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Internal server error' },
      }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Internal server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should use default error message when backend message is missing', () => {
    departmentServiceSpy.getDepartments.and.returnValue(
      throwError(() => ({ status: 0 }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe(
      'Failed to load departments. Please try again.'
    );
  });

  it('should filter active departments for parent options', () => {
    fixture.detectChanges();
    // dept-3 (Marketing) is inactive and should be excluded
    const active = component.activeDepartments();
    expect(active.length).toBe(2);
    expect(active.every((d) => d.isActive)).toBeTrue();
  });

  it('should toggle view mode between list and tree', () => {
    fixture.detectChanges();
    expect(component.viewMode()).toBe('list');

    component.viewMode.set('tree');
    expect(component.viewMode()).toBe('tree');

    component.viewMode.set('list');
    expect(component.viewMode()).toBe('list');
  });

  // ─── Form slide-over ─────────────────────────────────────

  it('should open create form with null department', () => {
    fixture.detectChanges();
    component.openCreate();
    expect(component.formOpen()).toBeTrue();
    expect(component.editingDepartment()).toBeNull();
  });

  it('should open edit form with the selected department', () => {
    fixture.detectChanges();
    const dept = mockDepartments[0];
    component.openEdit(dept);
    expect(component.formOpen()).toBeTrue();
    expect(component.editingDepartment()).toBe(dept);
  });

  it('should close form and clear editing state', () => {
    fixture.detectChanges();
    component.openEdit(mockDepartments[0]);
    component.closeForm();
    expect(component.formOpen()).toBeFalse();
    expect(component.editingDepartment()).toBeNull();
  });

  it('should reload departments when form saved', () => {
    fixture.detectChanges();
    departmentServiceSpy.getDepartments.calls.reset();

    component.onFormSaved();
    expect(component.formOpen()).toBeFalse();
    expect(departmentServiceSpy.getDepartments).toHaveBeenCalled();
  });

  // ─── Deactivation ────────────────────────────────────────

  it('should open deactivation dialog', () => {
    fixture.detectChanges();
    const dept = mockDepartments[1]; // Frontend, 0 employees
    component.confirmDeactivate(dept);
    expect(component.departmentToDeactivate()).toBe(dept);
  });

  it('should cancel deactivation', () => {
    fixture.detectChanges();
    component.confirmDeactivate(mockDepartments[1]);
    component.cancelDeactivate();
    expect(component.departmentToDeactivate()).toBeNull();
  });

  it('should deactivate department with zero employees', () => {
    fixture.detectChanges();
    const dept = mockDepartments[1]; // 0 employees
    component.confirmDeactivate(dept);
    component.deactivateDepartment();

    expect(departmentServiceSpy.deactivateDepartment).toHaveBeenCalledWith(
      dept.departmentId
    );
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.departmentToDeactivate()).toBeNull();
  });

  it('should NOT deactivate department with active employees (AC-5)', () => {
    fixture.detectChanges();
    const dept = mockDepartments[0]; // 10 employees
    component.confirmDeactivate(dept);
    component.deactivateDepartment();

    expect(departmentServiceSpy.deactivateDepartment).not.toHaveBeenCalled();
  });

  it('should handle deactivation error from backend', () => {
    fixture.detectChanges();
    departmentServiceSpy.deactivateDepartment.and.returnValue(
      throwError(() => ({
        status: 422,
        error: {
          message: 'Department has active employees.',
          code: 'has_active_employees',
          employeeCount: 5,
        },
      }))
    );

    const dept = mockDepartments[1]; // 0 employees on client side
    component.confirmDeactivate(dept);
    component.deactivateDepartment();

    expect(toastrSpy.warning).toHaveBeenCalled();
    expect(component.isDeactivating()).toBeFalse();
  });

  it('should show generic error toast on unexpected deactivation failure', () => {
    fixture.detectChanges();
    departmentServiceSpy.deactivateDepartment.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Unexpected error' },
      }))
    );

    const dept = mockDepartments[1];
    component.confirmDeactivate(dept);
    component.deactivateDepartment();

    expect(toastrSpy.error).toHaveBeenCalled();
  });
});
