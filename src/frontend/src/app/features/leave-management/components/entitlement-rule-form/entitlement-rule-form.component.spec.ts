import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { EntitlementRuleFormComponent } from './entitlement-rule-form.component';
import { IEntitlementRule, ILookupItem, ICreateEntitlementRuleRequest } from '../../models/leave-entitlement.models';

describe('EntitlementRuleFormComponent', () => {
  let fixture: ComponentFixture<EntitlementRuleFormComponent>;
  let component: EntitlementRuleFormComponent;

  const mockLeaveTypes: ILookupItem[] = [
    { id: 'lt-1', name: 'Annual Leave' },
    { id: 'lt-2', name: 'Sick Leave' },
  ];

  const mockDepartments: ILookupItem[] = [
    { id: 'dept-1', name: 'Engineering' },
    { id: 'dept-2', name: 'HR' },
  ];

  const mockJobTitles: ILookupItem[] = [
    { id: 'jt-1', name: 'Software Engineer' },
  ];

  const mockRule: IEntitlementRule = {
    ruleId: 'rule-1',
    tenantId: 'tenant-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: null,
    jobTitleName: null,
    employmentType: 'Full-Time',
    tenureMinMonths: 12,
    tenureMaxMonths: 60,
    entitlementDays: 25,
    priority: 5,
    effectiveFrom: '2026-01-01',
    effectiveTo: '2026-12-31',
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  function createComponent(rule: IEntitlementRule | null = null): void {
    TestBed.configureTestingModule({
      imports: [EntitlementRuleFormComponent],
      providers: [provideAnimationsAsync()],
    });

    fixture = TestBed.createComponent(EntitlementRuleFormComponent);
    component = fixture.componentInstance;

    // Set inputs using fixture.componentRef
    fixture.componentRef.setInput('rule', rule);
    fixture.componentRef.setInput('leaveTypes', mockLeaveTypes);
    fixture.componentRef.setInput('departments', mockDepartments);
    fixture.componentRef.setInput('jobTitles', mockJobTitles);

    fixture.detectChanges();
  }

  // ─── Create mode ────────────────────────────────────────

  describe('Create mode', () => {
    beforeEach(() => {
      createComponent(null);
    });

    it('should create the component', () => {
      expect(component).toBeTruthy();
    });

    it('should initialize form with empty values', () => {
      expect(component.form.value.leaveTypeId).toBe('');
      expect(component.form.value.entitlementDays).toBeNull();
      expect(component.form.value.priority).toBe(1);
    });

    it('should require leaveTypeId', () => {
      component.form.get('leaveTypeId')!.markAsTouched();
      expect(component.form.get('leaveTypeId')!.hasError('required')).toBeTrue();
    });

    it('should require entitlementDays', () => {
      component.form.get('entitlementDays')!.markAsTouched();
      expect(component.form.get('entitlementDays')!.hasError('required')).toBeTrue();
    });

    it('should require effectiveFrom', () => {
      component.form.get('effectiveFrom')!.markAsTouched();
      expect(component.form.get('effectiveFrom')!.hasError('required')).toBeTrue();
    });

    it('should reject negative entitlement days', () => {
      component.form.patchValue({ entitlementDays: -5 });
      expect(component.form.get('entitlementDays')!.hasError('min')).toBeTrue();
    });

    it('should reject priority less than 1', () => {
      component.form.patchValue({ priority: 0 });
      expect(component.form.get('priority')!.hasError('min')).toBeTrue();
    });

    it('should not emit save when form is invalid', () => {
      const saveSpy = spyOn(component.save, 'emit');
      component.onSubmit();
      expect(saveSpy).not.toHaveBeenCalled();
    });

    it('should emit save with correct request when form is valid', () => {
      const saveSpy = spyOn(component.save, 'emit');

      component.form.patchValue({
        leaveTypeId: 'lt-1',
        departmentId: 'dept-1',
        jobTitleId: '',
        employmentType: 'Full-Time',
        tenureMinMonths: 12,
        tenureMaxMonths: 60,
        entitlementDays: 25,
        priority: 5,
        effectiveFrom: '2026-01-01',
        effectiveTo: '',
      });

      component.onSubmit();

      expect(saveSpy).toHaveBeenCalledWith(jasmine.objectContaining({
        leaveTypeId: 'lt-1',
        departmentId: 'dept-1',
        jobTitleId: null,
        employmentType: 'Full-Time',
        entitlementDays: 25,
        priority: 5,
        effectiveFrom: '2026-01-01',
        effectiveTo: null,
      } as ICreateEntitlementRuleRequest));
    });

    it('should emit close event', () => {
      const closeSpy = spyOn(component.close, 'emit');
      component.onClose();
      expect(closeSpy).toHaveBeenCalled();
    });
  });

  // ─── Edit mode ──────────────────────────────────────────

  describe('Edit mode', () => {
    beforeEach(() => {
      createComponent(mockRule);
    });

    it('should populate form with existing rule data', () => {
      expect(component.form.value.leaveTypeId).toBe('lt-1');
      expect(component.form.value.departmentId).toBe('dept-1');
      expect(component.form.value.employmentType).toBe('Full-Time');
      expect(component.form.value.entitlementDays).toBe(25);
      expect(component.form.value.priority).toBe(5);
      expect(component.form.value.effectiveFrom).toBe('2026-01-01');
      expect(component.form.value.effectiveTo).toBe('2026-12-31');
      expect(component.form.value.tenureMinMonths).toBe(12);
      expect(component.form.value.tenureMaxMonths).toBe(60);
    });

    it('should emit save on valid submit', () => {
      const saveSpy = spyOn(component.save, 'emit');
      component.onSubmit();
      expect(saveSpy).toHaveBeenCalled();
    });
  });
});
