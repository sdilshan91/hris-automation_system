import { TestBed, ComponentFixture, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService, provideToastr } from 'ngx-toastr';
import { EmployeeLeaveOverridesComponent } from './employee-leave-overrides.component';
import {
  IEntitlementOverride,
  IEffectiveEntitlement,
} from '../../models/leave-entitlement.models';
import { ILeaveType } from '../../models/leave-type.models';
import { environment } from '../../../../../environments/environment';

describe('EmployeeLeaveOverridesComponent', () => {
  let fixture: ComponentFixture<EmployeeLeaveOverridesComponent>;
  let component: EmployeeLeaveOverridesComponent;
  let httpMock: HttpTestingController;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const baseUrl = `${environment.apiBaseUrl}/tenant/leave-entitlements`;
  const leaveTypesUrl = `${environment.apiBaseUrl}/tenant/leave-types`;

  const mockOverride: IEntitlementOverride = {
    overrideId: 'ov-1',
    tenantId: 'tenant-1',
    employeeId: 'emp-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    leaveYear: 2026,
    entitlementDays: 30,
    reason: 'Senior adjustment',
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const mockEffective: IEffectiveEntitlement = {
    employeeId: 'emp-1',
    leaveTypeId: 'lt-1',
    leaveTypeName: 'Annual Leave',
    entitlementDays: 30,
    source: 'override',
    ruleId: null,
    overrideId: 'ov-1',
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

  function flushInitialRequests(
    overrides: IEntitlementOverride[] = [mockOverride],
    effective: IEffectiveEntitlement[] = [mockEffective],
  ): void {
    const overridesReq = httpMock.expectOne(r =>
      r.url === `${baseUrl}/overrides` &&
      r.params.get('employeeId') === 'emp-1'
    );
    const effectiveReq = httpMock.expectOne(r =>
      r.url === `${baseUrl}/compute-effective` &&
      r.params.get('employeeId') === 'emp-1'
    );
    const leaveTypesReq = httpMock.expectOne(leaveTypesUrl);

    overridesReq.flush(overrides);
    effectiveReq.flush(effective);
    leaveTypesReq.flush([mockLeaveType]);
  }

  beforeEach(() => {
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning']);

    TestBed.configureTestingModule({
      imports: [EmployeeLeaveOverridesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(EmployeeLeaveOverridesComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);

    fixture.componentRef.setInput('employeeId', 'emp-1');
  });

  afterEach(() => {
    httpMock.verify();
  });

  // ─── Loading ────────────────────────────────────────────

  describe('Loading overrides and effective entitlements', () => {
    it('should create the component', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();
      expect(component).toBeTruthy();
    }));

    it('should load overrides for the employee', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      expect(component.overrides().length).toBe(1);
      expect(component.overrides()[0].overrideId).toBe('ov-1');
    }));

    it('should load effective entitlements', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      expect(component.effectiveEntitlements().length).toBe(1);
      expect(component.effectiveEntitlements()[0].source).toBe('override');
    }));

    it('should load leave type lookups', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      expect(component.leaveTypeLookups().length).toBe(1);
      expect(component.leaveTypeLookups()[0].name).toBe('Annual Leave');
    }));

    it('should show empty state when no overrides', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests([], []);
      tick();

      expect(component.overrides().length).toBe(0);
      expect(component.effectiveEntitlements().length).toBe(0);
    }));
  });

  // ─── Set override (AC-3) ────────────────────────────────

  describe('Per-employee override set and read', () => {
    it('should submit a new override', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.overrideForm.patchValue({
        leaveTypeId: 'lt-1',
        leaveYear: 2026,
        entitlementDays: 35,
        reason: 'Special case',
      });

      component.submitOverride();

      const req = httpMock.expectOne(`${baseUrl}/overrides`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body.employeeId).toBe('emp-1');
      expect(req.request.body.leaveTypeId).toBe('lt-1');
      expect(req.request.body.entitlementDays).toBe(35);
      req.flush({
        ...mockOverride,
        entitlementDays: 35,
        reason: 'Special case',
      });
      tick();

      expect(toastrSpy.success).toHaveBeenCalledWith('Override saved successfully.');

      // loadData is called after save -- flush those
      flushInitialRequests();
      tick();
    }));

    it('should not submit when form is invalid', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.overrideForm.patchValue({
        leaveTypeId: '',
        leaveYear: 2026,
        entitlementDays: null,
      });

      component.submitOverride();
      // No HTTP request
      expect(component.isSaving()).toBeFalse();
    }));

    it('should delete an override', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      spyOn(window, 'confirm').and.returnValue(true);
      component.deleteOverride(mockOverride);

      const req = httpMock.expectOne(`${baseUrl}/overrides/ov-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      tick();

      expect(component.overrides().length).toBe(0);
      expect(toastrSpy.success).toHaveBeenCalledWith('Override deleted.');

      // Refresh effective entitlements
      const effReq = httpMock.expectOne(r =>
        r.url === `${baseUrl}/compute-effective` &&
        r.params.get('employeeId') === 'emp-1'
      );
      effReq.flush([]);
      tick();
    }));
  });

  // ─── Form validation ───────────────────────────────────

  describe('Override form validation', () => {
    it('should require leaveTypeId', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.overrideForm.get('leaveTypeId')!.markAsTouched();
      expect(component.overrideForm.get('leaveTypeId')!.hasError('required')).toBeTrue();
    }));

    it('should require entitlementDays >= 0', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.overrideForm.patchValue({ entitlementDays: -1 });
      expect(component.overrideForm.get('entitlementDays')!.hasError('min')).toBeTrue();
    }));

    it('should require leaveYear >= 2020', fakeAsync(() => {
      fixture.detectChanges();
      flushInitialRequests();
      tick();

      component.overrideForm.patchValue({ leaveYear: 2019 });
      expect(component.overrideForm.get('leaveYear')!.hasError('min')).toBeTrue();
    }));
  });
});
