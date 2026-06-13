import { TestBed, ComponentFixture, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService, provideToastr } from 'ngx-toastr';
import { EntitlementRulesComponent } from './entitlement-rules.component';
import { IEntitlementRule } from '../../models/leave-entitlement.models';
import { ILeaveType } from '../../models/leave-type.models';
import { environment } from '../../../../../environments/environment';

describe('EntitlementRulesComponent', () => {
  let fixture: ComponentFixture<EntitlementRulesComponent>;
  let component: EntitlementRulesComponent;
  let httpMock: HttpTestingController;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const rulesUrl = `${environment.apiBaseUrl}/tenant/leave-entitlements/rules`;
  const leaveTypesUrl = `${environment.apiBaseUrl}/tenant/leave-types`;

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
    tenureMinMonths: null,
    tenureMaxMonths: null,
    entitlementDays: 25,
    priority: 5,
    effectiveFrom: '2026-01-01',
    effectiveTo: null,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const mockRule2: IEntitlementRule = {
    ...mockRule,
    ruleId: 'rule-2',
    leaveTypeId: 'lt-2',
    leaveTypeName: 'Sick Leave',
    departmentId: null,
    departmentName: null,
    employmentType: null,
    entitlementDays: 10,
    priority: 1,
    isActive: false,
  };

  const mockLeaveType: ILeaveType = {
    leaveTypeId: 'lt-1',
    tenantId: 'tenant-1',
    name: 'Annual Leave',
    code: 'AL',
    color: '#2563eb',
    description: null,
    annualEntitlement: 20,
    accrualFrequency: 'monthly',
    carryForwardLimit: 5,
    carryForwardExpiryMonths: 3,
    probationEligible: false,
    documentsRequired: false,
    documentDayThreshold: null,
    encashable: false,
    maxEncashDays: null,
    halfDayAllowed: false,
    hourlyAllowed: false,
    gender: 'all',
    maxConsecutiveDays: null,
    negativeBalanceAllowed: false,
    negativeBalanceLimit: null,
    displayOrder: 1,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  function flushInitialRequests(rules: IEntitlementRule[] = [mockRule, mockRule2]): void {
    const rulesReq = httpMock.expectOne(rulesUrl);
    const leaveTypesReq = httpMock.expectOne(leaveTypesUrl);
    rulesReq.flush(rules);
    leaveTypesReq.flush([mockLeaveType]);
  }

  beforeEach(() => {
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    TestBed.configureTestingModule({
      imports: [EntitlementRulesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(EntitlementRulesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // ─── Rendering ──────────────────────────────────────────

  describe('Matrix render from rules', () => {
    it('should create the component', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();
      expect(component).toBeTruthy();
    }));

    it('should load and display rules', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      expect(component.rules().length).toBe(2);
      expect(component.filteredRules().length).toBe(2);
      expect(component.isLoading()).toBeFalse();
    }));

    it('should show loading skeleton while fetching', () => {
      fixture.detectChanges();
      expect(component.isLoading()).toBeTrue();
      flushInitialRequests();
    });

    it('should display empty state when no rules exist', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests([]);
      tick();

      expect(component.filteredRules().length).toBe(0);
    }));

    it('should extract department lookups from rules', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      const depts = component.departmentLookups();
      expect(depts.length).toBe(1);
      expect(depts[0].name).toBe('Engineering');
    }));

    it('should group rules by leave type for mobile view', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      const groups = component.groupedByLeaveType();
      expect(groups.length).toBe(2);
      expect(groups[0].leaveTypeName).toBe('Annual Leave');
    }));
  });

  // ─── Filtering ──────────────────────────────────────────

  describe('Filtering', () => {
    it('should filter by leave type', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.filterLeaveTypeId.set('lt-1');
      expect(component.filteredRules().length).toBe(1);
      expect(component.filteredRules()[0].leaveTypeName).toBe('Annual Leave');
    }));

    it('should filter by department', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.filterDepartmentId.set('dept-1');
      expect(component.filteredRules().length).toBe(1);
    }));

    it('should filter active only', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.filterActiveOnly.set(true);
      const active = component.filteredRules();
      expect(active.length).toBe(1);
      expect(active[0].isActive).toBeTrue();
    }));

    it('should filter by employment type', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.filterEmploymentType.set('Full-Time');
      expect(component.filteredRules().length).toBe(1);
    }));
  });

  // ─── Inline cell edit ───────────────────────────────────

  describe('Inline cell edit calls service', () => {
    it('should enter inline edit mode on cell click', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.editingCellRuleId.set('rule-1');
      expect(component.editingCellRuleId()).toBe('rule-1');
    }));

    it('should call updateRuleDays on save', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      const fakeEvent = {
        target: { value: '30' },
      } as unknown as Event;

      component.saveInlineEdit(mockRule, fakeEvent);

      const req = httpMock.expectOne(`${rulesUrl}/rule-1/days`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body.entitlementDays).toBe(30);
      req.flush({ ...mockRule, entitlementDays: 30 });
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith(
        'Days updated. Background recalculation triggered.'
      );
    }));

    it('should not call service if value unchanged', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      const fakeEvent = {
        target: { value: '25' },
      } as unknown as Event;

      component.saveInlineEdit(mockRule, fakeEvent);
      // No HTTP request should be made
      expect(component.editingCellRuleId()).toBeNull();
    }));

    it('should cancel inline edit on invalid value', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      const fakeEvent = {
        target: { value: '-5' },
      } as unknown as Event;

      component.saveInlineEdit(mockRule, fakeEvent);
      expect(component.editingCellRuleId()).toBeNull();
    }));
  });

  // ─── Create/Edit form ──────────────────────────────────

  describe('Create rule form', () => {
    it('should open create form', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.openCreateForm();
      expect(component.showForm()).toBeTrue();
      expect(component.editingRule()).toBeNull();
    }));

    it('should open edit form with existing rule', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.openEditForm(mockRule);
      expect(component.showForm()).toBeTrue();
      expect(component.editingRule()).toBe(mockRule);
    }));

    it('should close form', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.openCreateForm();
      component.closeForm();
      expect(component.showForm()).toBeFalse();
    }));

    it('should call createRule on form save for new rule', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.openCreateForm();
      component.onFormSave({
        leaveTypeId: 'lt-1',
        departmentId: 'dept-1',
        entitlementDays: 25,
        priority: 5,
        effectiveFrom: '2026-01-01',
      });

      const createReq = httpMock.expectOne(rulesUrl);
      expect(createReq.request.method).toBe('POST');
      createReq.flush(mockRule);
      tick();

      // loadData is called after create — flush those requests
      const rulesReq = httpMock.expectOne(rulesUrl);
      const leaveTypesReq = httpMock.expectOne(leaveTypesUrl);
      rulesReq.flush([mockRule]);
      leaveTypesReq.flush([mockLeaveType]);
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith(
        'Rule created. Background recalculation of affected employees has been triggered.'
      );
    }));

    it('should call updateRule on form save for existing rule', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.openEditForm(mockRule);
      component.onFormSave({
        leaveTypeId: 'lt-1',
        departmentId: 'dept-1',
        entitlementDays: 30,
        priority: 5,
        effectiveFrom: '2026-01-01',
      });

      const updateReq = httpMock.expectOne(`${rulesUrl}/rule-1`);
      expect(updateReq.request.method).toBe('PUT');
      updateReq.flush({ ...mockRule, entitlementDays: 30 });
      tick();

      // loadData is called after update
      const rulesReq = httpMock.expectOne(rulesUrl);
      const leaveTypesReq = httpMock.expectOne(leaveTypesUrl);
      rulesReq.flush([{ ...mockRule, entitlementDays: 30 }]);
      leaveTypesReq.flush([mockLeaveType]);
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith(
        'Rule updated. Background recalculation of affected employees has been triggered.'
      );
    }));
  });

  // ─── Delete ─────────────────────────────────────────────

  describe('Delete rule', () => {
    it('should delete a rule after confirmation', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      spyOn(window, 'confirm').and.returnValue(true);
      component.deleteRule(mockRule);

      const req = httpMock.expectOne(`${rulesUrl}/rule-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      tick();

      expect(component.rules().length).toBe(1);
      expect(toastrSpy.success).toHaveBeenCalledWith('Rule deleted.');
    }));

    it('should not delete when confirmation is cancelled', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      spyOn(window, 'confirm').and.returnValue(false);
      component.deleteRule(mockRule);

      // No HTTP request should be made
      expect(component.rules().length).toBe(2);
    }));
  });

  // ─── Bulk assign (FR-4) ────────────────────────────────

  describe('Bulk assignment', () => {
    it('should open bulk modal', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.showBulkModal.set(true);
      expect(component.showBulkModal()).toBeTrue();
    }));

    it('should submit bulk assignment', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.bulkLeaveTypeId = 'lt-1';
      component.bulkDays = 25;
      component.bulkEmployeeIds = 'emp-1, emp-2';
      component.bulkLeaveYear = 2026;
      component.bulkReason = 'Bulk';

      component.submitBulk();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/leave-entitlements/bulk`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.employeeIds).toEqual(['emp-1', 'emp-2']);
      req.flush({ totalProcessed: 2, totalSuccess: 2, totalFailed: 0 });
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith(
        'Bulk assignment complete: 2 succeeded, 0 failed.'
      );
    }));

    it('should warn when required bulk fields are empty', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.bulkLeaveTypeId = '';
      component.submitBulk();

      expect(toastrSpy.warning).toHaveBeenCalledWith('Please fill in all required fields.');
    }));
  });

  // ─── Recalculation toast (AC-5) ─────────────────────────

  describe('Recalculation toast (AC-5)', () => {
    it('should show recalculation message on rule create', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.openCreateForm();
      component.onFormSave({
        leaveTypeId: 'lt-1',
        entitlementDays: 25,
        priority: 5,
        effectiveFrom: '2026-01-01',
      });

      httpMock.expectOne(rulesUrl).flush(mockRule);
      tick();

      // Flush reload
      httpMock.expectOne(rulesUrl).flush([mockRule]);
      httpMock.expectOne(leaveTypesUrl).flush([mockLeaveType]);
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith(
        jasmine.stringContaining('Background recalculation')
      );
    }));

    it('should show recalculation message on inline edit', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      const fakeEvent = { target: { value: '30' } } as unknown as Event;
      component.saveInlineEdit(mockRule, fakeEvent);

      httpMock.expectOne(`${rulesUrl}/rule-1/days`).flush({ ...mockRule, entitlementDays: 30 });
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith(
        jasmine.stringContaining('recalculation')
      );
    }));
  });

  // ─── Helpers ────────────────────────────────────────────

  describe('formatTenure', () => {
    it('should return "Any" when no tenure specified', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      expect(component.formatTenure(mockRule)).toBe('Any');
    }));

    it('should format min-max range', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      expect(component.formatTenure({
        ...mockRule,
        tenureMinMonths: 12,
        tenureMaxMonths: 60,
      })).toBe('12-60 months');
    }));

    it('should format min-only (open-ended)', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      expect(component.formatTenure({
        ...mockRule,
        tenureMinMonths: 24,
        tenureMaxMonths: null,
      })).toBe('24+ months');
    }));
  });
});
