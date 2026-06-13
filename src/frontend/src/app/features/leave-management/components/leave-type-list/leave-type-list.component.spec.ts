import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LeaveTypeListComponent } from './leave-type-list.component';
import { LeaveTypeService } from '../../services/leave-type.service';
import { ILeaveType } from '../../models/leave-type.models';

describe('LeaveTypeListComponent', () => {
  let component: LeaveTypeListComponent;
  let fixture: ComponentFixture<LeaveTypeListComponent>;
  let leaveTypeServiceSpy: jasmine.SpyObj<LeaveTypeService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockLeaveTypes: ILeaveType[] = [
    {
      leaveTypeId: 'lt-1',
      tenantId: 'tenant-1',
      name: 'Annual Leave',
      code: 'AL',
      color: '#2563eb',
      description: 'Paid annual leave',
      annualEntitlement: 20,
      accrualFrequency: 'monthly',
      carryForwardLimit: 5,
      carryForwardExpiryMonths: 3,
      probationEligible: false,
      documentsRequired: false,
      documentDayThreshold: null,
      encashable: true,
      maxEncashDays: 10,
      halfDayAllowed: true,
      hourlyAllowed: false,
      gender: 'all',
      maxConsecutiveDays: 15,
      negativeBalanceAllowed: false,
      negativeBalanceLimit: null,
      displayOrder: 0,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
    {
      leaveTypeId: 'lt-2',
      tenantId: 'tenant-1',
      name: 'Sick Leave',
      code: 'SL',
      color: '#dc2626',
      description: 'Sick leave with medical certificate',
      annualEntitlement: 10,
      accrualFrequency: 'yearly',
      carryForwardLimit: 0,
      carryForwardExpiryMonths: 0,
      probationEligible: false,
      documentsRequired: true,
      documentDayThreshold: 2,
      encashable: false,
      maxEncashDays: null,
      halfDayAllowed: true,
      hourlyAllowed: false,
      gender: 'all',
      maxConsecutiveDays: null,
      negativeBalanceAllowed: false,
      negativeBalanceLimit: null,
      displayOrder: 1,
      isActive: true,
      createdAt: '2026-02-01T00:00:00Z',
      updatedAt: '2026-02-01T00:00:00Z',
    },
    {
      leaveTypeId: 'lt-3',
      tenantId: 'tenant-1',
      name: 'Maternity Leave',
      code: 'ML',
      color: '#db2777',
      description: null,
      annualEntitlement: 90,
      accrualFrequency: 'upfront',
      carryForwardLimit: 0,
      carryForwardExpiryMonths: 0,
      probationEligible: false,
      documentsRequired: true,
      documentDayThreshold: 1,
      encashable: false,
      maxEncashDays: null,
      halfDayAllowed: false,
      hourlyAllowed: false,
      gender: 'female',
      maxConsecutiveDays: 90,
      negativeBalanceAllowed: false,
      negativeBalanceLimit: null,
      displayOrder: 2,
      isActive: false,
      createdAt: '2026-03-01T00:00:00Z',
      updatedAt: '2026-03-01T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    leaveTypeServiceSpy = jasmine.createSpyObj('LeaveTypeService', [
      'getLeaveTypes',
      'deactivateLeaveType',
      'activateLeaveType',
      'reorderLeaveTypes',
    ]);
    leaveTypeServiceSpy.getLeaveTypes.and.returnValue(of(mockLeaveTypes));
    leaveTypeServiceSpy.reorderLeaveTypes.and.returnValue(of(undefined as unknown as void));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [LeaveTypeListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: LeaveTypeService, useValue: leaveTypeServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(LeaveTypeListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load leave types on init', () => {
    fixture.detectChanges();
    expect(leaveTypeServiceSpy.getLeaveTypes).toHaveBeenCalled();
    expect(component.leaveTypes().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error state when loading fails', () => {
    leaveTypeServiceSpy.getLeaveTypes.and.returnValue(
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
    leaveTypeServiceSpy.getLeaveTypes.and.returnValue(
      throwError(() => ({ status: 0 }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe(
      'Failed to load leave types. Please try again.'
    );
  });

  // --- Rendering with color tags ---

  it('should render leave types with color tags in the DOM', () => {
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const colorTags = compiled.querySelectorAll('.color-tag');
    // Desktop table has color tags (mobile hidden but still in DOM)
    expect(colorTags.length).toBeGreaterThan(0);
  });

  // --- Search / Filter ---

  it('should filter leave types by name', () => {
    fixture.detectChanges();
    expect(component.filteredLeaveTypes().length).toBe(3);

    component.searchQuery.set('Annual');
    expect(component.filteredLeaveTypes().length).toBe(1);
    expect(component.filteredLeaveTypes()[0].name).toBe('Annual Leave');
  });

  it('should filter leave types by code', () => {
    fixture.detectChanges();
    component.searchQuery.set('SL');
    expect(component.filteredLeaveTypes().length).toBe(1);
    expect(component.filteredLeaveTypes()[0].name).toBe('Sick Leave');
  });

  it('should filter leave types by description', () => {
    fixture.detectChanges();
    component.searchQuery.set('medical');
    expect(component.filteredLeaveTypes().length).toBe(1);
    expect(component.filteredLeaveTypes()[0].name).toBe('Sick Leave');
  });

  it('should return all leave types when search query is empty', () => {
    fixture.detectChanges();
    component.searchQuery.set('');
    expect(component.filteredLeaveTypes().length).toBe(3);
  });

  it('should return no results for non-matching query', () => {
    fixture.detectChanges();
    component.searchQuery.set('nonexistent');
    expect(component.filteredLeaveTypes().length).toBe(0);
  });

  it('should disable reorder when search is active', () => {
    fixture.detectChanges();
    expect(component.isSearchActive()).toBeFalse();
    component.searchQuery.set('Annual');
    expect(component.isSearchActive()).toBeTrue();
  });

  // --- Form slide-over ---

  it('should open create form with null leave type', () => {
    fixture.detectChanges();
    component.openCreate();
    expect(component.formOpen()).toBeTrue();
    expect(component.editingLeaveType()).toBeNull();
  });

  it('should open edit form with the selected leave type', () => {
    fixture.detectChanges();
    const lt = mockLeaveTypes[0];
    component.openEdit(lt);
    expect(component.formOpen()).toBeTrue();
    expect(component.editingLeaveType()).toBe(lt);
  });

  it('should close form and clear editing state', () => {
    fixture.detectChanges();
    component.openEdit(mockLeaveTypes[0]);
    component.closeForm();
    expect(component.formOpen()).toBeFalse();
    expect(component.editingLeaveType()).toBeNull();
  });

  it('should reload leave types when form saved', () => {
    fixture.detectChanges();
    leaveTypeServiceSpy.getLeaveTypes.calls.reset();

    component.onFormSaved();
    expect(component.formOpen()).toBeFalse();
    expect(leaveTypeServiceSpy.getLeaveTypes).toHaveBeenCalled();
  });

  // --- Active/Inactive Toggle (AC-4) ---

  it('should deactivate an active leave type', () => {
    fixture.detectChanges();
    const deactivated = { ...mockLeaveTypes[0], isActive: false };
    leaveTypeServiceSpy.deactivateLeaveType.and.returnValue(of(deactivated));

    const event = new Event('click');
    spyOn(event, 'stopPropagation');
    component.toggleActive(mockLeaveTypes[0], event);

    expect(event.stopPropagation).toHaveBeenCalled();
    expect(leaveTypeServiceSpy.deactivateLeaveType).toHaveBeenCalledWith('lt-1');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.isTogglingId()).toBeNull();
    // Check the state was updated
    expect(component.leaveTypes().find(t => t.leaveTypeId === 'lt-1')?.isActive).toBeFalse();
  });

  it('should activate an inactive leave type', () => {
    fixture.detectChanges();
    const activated = { ...mockLeaveTypes[2], isActive: true };
    leaveTypeServiceSpy.activateLeaveType.and.returnValue(of(activated));

    const event = new Event('click');
    spyOn(event, 'stopPropagation');
    component.toggleActive(mockLeaveTypes[2], event);

    expect(leaveTypeServiceSpy.activateLeaveType).toHaveBeenCalledWith('lt-3');
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('should handle toggle error gracefully', () => {
    fixture.detectChanges();
    leaveTypeServiceSpy.deactivateLeaveType.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Unexpected error' },
      }))
    );

    const event = new Event('click');
    spyOn(event, 'stopPropagation');
    component.toggleActive(mockLeaveTypes[0], event);

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isTogglingId()).toBeNull();
  });

  // --- Reorder (FR-3) ---

  it('should move an item up via arrow buttons', () => {
    fixture.detectChanges();
    component.moveItem(1, -1);

    expect(component.leaveTypes()[0].leaveTypeId).toBe('lt-2');
    expect(component.leaveTypes()[1].leaveTypeId).toBe('lt-1');
    expect(leaveTypeServiceSpy.reorderLeaveTypes).toHaveBeenCalledWith({
      orderedIds: ['lt-2', 'lt-1', 'lt-3'],
    });
  });

  it('should move an item down via arrow buttons', () => {
    fixture.detectChanges();
    component.moveItem(0, 1);

    expect(component.leaveTypes()[0].leaveTypeId).toBe('lt-2');
    expect(component.leaveTypes()[1].leaveTypeId).toBe('lt-1');
    expect(leaveTypeServiceSpy.reorderLeaveTypes).toHaveBeenCalled();
  });

  it('should not move item beyond boundaries', () => {
    fixture.detectChanges();
    // Try to move first item up
    component.moveItem(0, -1);
    expect(component.leaveTypes()[0].leaveTypeId).toBe('lt-1');

    // Try to move last item down
    component.moveItem(2, 1);
    expect(component.leaveTypes()[2].leaveTypeId).toBe('lt-3');

    // No reorder calls should have been made
    expect(leaveTypeServiceSpy.reorderLeaveTypes).not.toHaveBeenCalled();
  });

  it('should rollback on reorder failure', () => {
    fixture.detectChanges();
    leaveTypeServiceSpy.reorderLeaveTypes.and.returnValue(
      throwError(() => ({ status: 500 }))
    );
    leaveTypeServiceSpy.getLeaveTypes.calls.reset();

    component.moveItem(1, -1);

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'Failed to reorder leave types. Please try again.'
    );
    expect(leaveTypeServiceSpy.getLeaveTypes).toHaveBeenCalled();
  });

  // --- getContrastColor (rendering helper) ---

  it('should return white for dark colors and black for light', () => {
    expect(component.getContrastColor('#000000')).toBe('#ffffff');
    expect(component.getContrastColor('#ffffff')).toBe('#000000');
  });
});
