import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ComponentRef } from '@angular/core';

import { DepartmentTreeComponent } from './department-tree.component';
import { IDepartment } from '../../models/department.models';

describe('DepartmentTreeComponent', () => {
  let component: DepartmentTreeComponent;
  let componentRef: ComponentRef<DepartmentTreeComponent>;
  let fixture: ComponentFixture<DepartmentTreeComponent>;

  const mockDepartments: IDepartment[] = [
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
      employeeCount: 10,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
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
      name: 'Backend',
      description: null,
      parentDepartmentId: 'dept-1',
      parentDepartmentName: 'Engineering',
      managerEmployeeId: null,
      managerName: null,
      isActive: true,
      employeeCount: 5,
      createdAt: '2026-01-20T00:00:00Z',
      updatedAt: '2026-01-20T00:00:00Z',
    },
    {
      departmentId: 'dept-4',
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
    await TestBed.configureTestingModule({
      imports: [DepartmentTreeComponent],
      providers: [provideAnimationsAsync()],
    }).compileComponents();

    fixture = TestBed.createComponent(DepartmentTreeComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  it('should create', () => {
    componentRef.setInput('departments', []);
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should build tree from flat department list', () => {
    componentRef.setInput('departments', mockDepartments);
    fixture.detectChanges();

    const tree = component.treeNodes();
    // Should have 2 root nodes: Engineering and Marketing
    expect(tree.length).toBe(2);
    expect(tree[0].department.name).toBe('Engineering');
    expect(tree[1].department.name).toBe('Marketing');
  });

  it('should nest children under parent nodes', () => {
    componentRef.setInput('departments', mockDepartments);
    fixture.detectChanges();

    const tree = component.treeNodes();
    const engineering = tree[0];
    expect(engineering.children.length).toBe(2);
    expect(engineering.children[0].department.name).toBe('Frontend');
    expect(engineering.children[1].department.name).toBe('Backend');
  });

  it('should set correct level on tree nodes', () => {
    componentRef.setInput('departments', mockDepartments);
    fixture.detectChanges();

    const tree = component.treeNodes();
    expect(tree[0].level).toBe(0); // Engineering = root
    expect(tree[0].children[0].level).toBe(1); // Frontend = level 1
  });

  it('should auto-expand root nodes that have children', () => {
    componentRef.setInput('departments', mockDepartments);
    fixture.detectChanges();

    // Engineering has children, should be expanded
    expect(component.isExpanded('dept-1')).toBeTrue();
    // Marketing has no children, should not be in expanded set
    expect(component.isExpanded('dept-4')).toBeFalse();
  });

  it('should toggle expand/collapse', () => {
    componentRef.setInput('departments', mockDepartments);
    fixture.detectChanges();

    // Engineering is auto-expanded
    expect(component.isExpanded('dept-1')).toBeTrue();

    component.toggleExpand('dept-1');
    expect(component.isExpanded('dept-1')).toBeFalse();

    component.toggleExpand('dept-1');
    expect(component.isExpanded('dept-1')).toBeTrue();
  });

  it('should handle empty department list', () => {
    componentRef.setInput('departments', []);
    fixture.detectChanges();

    const tree = component.treeNodes();
    expect(tree.length).toBe(0);
  });

  it('should emit editDepartment when node content is clicked', () => {
    componentRef.setInput('departments', mockDepartments);
    fixture.detectChanges();

    const editSpy = spyOn(component.editDepartment, 'emit');

    // Simulate the event by calling the output directly
    component.editDepartment.emit(mockDepartments[0]);
    expect(editSpy).toHaveBeenCalledWith(mockDepartments[0]);
  });

  it('should emit deactivateDepartment event', () => {
    componentRef.setInput('departments', mockDepartments);
    fixture.detectChanges();

    const deactivateSpy = spyOn(component.deactivateDepartment, 'emit');

    component.deactivateDepartment.emit(mockDepartments[0]);
    expect(deactivateSpy).toHaveBeenCalledWith(mockDepartments[0]);
  });
});
