import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { MyBenefitsComponent } from './my-benefits.component';
import { BenefitService } from '../../services/benefit.service';
import {
  IEligiblePlan,
  IBenefitEnrollment,
} from '../../models/benefit.models';

describe('MyBenefitsComponent', () => {
  let component: MyBenefitsComponent;
  let fixture: ComponentFixture<MyBenefitsComponent>;
  let benefitServiceSpy: jasmine.SpyObj<BenefitService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const eligiblePlans: IEligiblePlan[] = [
    {
      planId: 'p-1',
      name: 'Gold Health',
      type: 'Health',
      currency: 'USD',
      employeeCost: 100,
      employerCost: 400,
      effectiveFrom: '2026-08-01',
      effectiveTo: '2027-07-31',
      enrollmentOpen: true,
    },
    {
      planId: 'p-2',
      name: 'Vision Care',
      type: 'Vision',
      currency: 'USD',
      employeeCost: null,
      employerCost: 30,
      effectiveFrom: '2026-08-01',
      effectiveTo: null,
      enrollmentOpen: false,
    },
  ];

  const enrollments: IBenefitEnrollment[] = [
    {
      id: 'e-1',
      planId: 'p-3',
      planName: 'Basic Dental',
      employeeId: 'emp-1',
      employeeName: 'Ada Lovelace',
      status: 'Active',
      coverageLevel: 'EmployeeOnly',
      effectiveDate: '2026-08-01',
      endDate: null,
      electedAt: '2026-07-15T10:00:00Z',
    },
    {
      id: 'e-2',
      planId: 'p-4',
      planName: 'Old Life',
      employeeId: 'emp-1',
      employeeName: 'Ada Lovelace',
      status: 'Terminated',
      coverageLevel: 'Family',
      effectiveDate: '2025-01-01',
      endDate: '2025-12-31',
      electedAt: '2025-01-01T10:00:00Z',
    },
  ];

  beforeEach(async () => {
    benefitServiceSpy = jasmine.createSpyObj('BenefitService', [
      'getEligiblePlans',
      'getMyEnrollments',
      'enroll',
      'terminate',
    ]);
    benefitServiceSpy.getEligiblePlans.and.returnValue(of(eligiblePlans));
    benefitServiceSpy.getMyEnrollments.and.returnValue(of(enrollments));
    benefitServiceSpy.enroll.and.returnValue(of(enrollments[0]));
    benefitServiceSpy.terminate.and.returnValue(of(enrollments[1]));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [MyBenefitsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideToastr(),
        { provide: BenefitService, useValue: benefitServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyBenefitsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load eligible plans and enrollments on init', () => {
    fixture.detectChanges();
    expect(benefitServiceSpy.getEligiblePlans).toHaveBeenCalled();
    expect(benefitServiceSpy.getMyEnrollments).toHaveBeenCalled();
    expect(component.eligiblePlans().length).toBe(2);
    expect(component.enrollments().length).toBe(2);
    expect(component.isLoading()).toBeFalse();
  });

  it('should surface a load error when a request fails', () => {
    benefitServiceSpy.getEligiblePlans.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'boom' } }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('boom');
    expect(component.isLoading()).toBeFalse();
  });

  // --- Display helpers ----------------------------------------

  it('should label cost with currency and "free" for null', () => {
    expect(component.costLabel(100, 'USD')).toBe('USD 100.00');
    expect(component.costLabel(null, 'USD')).toBe('free');
  });

  it('should map coverage level to a friendly label', () => {
    expect(component.coverageLabel('EmployeeOnly')).toBe('Employee only');
    expect(component.coverageLabel('EmployeeSpouse')).toBe('Employee + spouse');
    expect(component.coverageLabel('Family')).toBe('Family');
  });

  it('should treat Active/Pending as active enrollments', () => {
    expect(component.isActive('Active')).toBeTrue();
    expect(component.isActive('Pending')).toBeTrue();
    expect(component.isActive('Terminated')).toBeFalse();
    expect(component.isActive('Declined')).toBeFalse();
  });

  it('should default coverage to EmployeeOnly and update on selection', () => {
    fixture.detectChanges();
    expect(component.coverageFor('p-1')).toBe('EmployeeOnly');
    component.setCoverage('p-1', 'Family');
    expect(component.coverageFor('p-1')).toBe('Family');
  });

  // --- Enroll (happy path + error branches) -------------------

  it('should enroll with the selected coverage and reload', () => {
    fixture.detectChanges();
    component.setCoverage('p-1', 'EmployeeSpouse');
    benefitServiceSpy.getMyEnrollments.calls.reset();

    component.enroll(eligiblePlans[0]);

    expect(benefitServiceSpy.enroll).toHaveBeenCalledWith({
      planId: 'p-1',
      coverageLevel: 'EmployeeSpouse',
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.enrollingId()).toBeNull();
    expect(benefitServiceSpy.getMyEnrollments).toHaveBeenCalled();
  });

  it('should show a not_eligible toast on 422 with the server message', () => {
    fixture.detectChanges();
    benefitServiceSpy.enroll.and.returnValue(
      throwError(() => ({
        status: 422,
        error: { message: 'Not eligible: TenureDays >= 90 not satisfied', code: 'not_eligible' },
      }))
    );

    component.enroll(eligiblePlans[0]);

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'Not eligible: TenureDays >= 90 not satisfied'
    );
    expect(component.enrollingId()).toBeNull();
  });

  it('should fall back to a not_eligible message when the body has no message', () => {
    fixture.detectChanges();
    benefitServiceSpy.enroll.and.returnValue(
      throwError(() => ({ status: 422, error: { code: 'not_eligible' } }))
    );

    component.enroll(eligiblePlans[0]);

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'You are not eligible for this plan.'
    );
  });

  it('should show an already_enrolled toast on 409', () => {
    fixture.detectChanges();
    benefitServiceSpy.enroll.and.returnValue(
      throwError(() => ({ status: 409, error: { code: 'already_enrolled' } }))
    );

    component.enroll(eligiblePlans[0]);

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'You are already enrolled in this plan.'
    );
  });

  it('should show an enrollment_window_closed toast on 422', () => {
    fixture.detectChanges();
    benefitServiceSpy.enroll.and.returnValue(
      throwError(() => ({ status: 422, error: { code: 'enrollment_window_closed' } }))
    );

    component.enroll(eligiblePlans[0]);

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'The enrollment window for this plan is closed.'
    );
  });

  // --- Terminate ----------------------------------------------

  it('should terminate an active enrollment and reload', () => {
    fixture.detectChanges();
    benefitServiceSpy.getMyEnrollments.calls.reset();

    component.terminate(enrollments[0]);

    expect(benefitServiceSpy.terminate).toHaveBeenCalledWith('e-1');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.terminatingId()).toBeNull();
    expect(benefitServiceSpy.getMyEnrollments).toHaveBeenCalled();
  });

  it('should show an error toast when terminate fails', () => {
    fixture.detectChanges();
    benefitServiceSpy.terminate.and.returnValue(
      throwError(() => ({ status: 400, error: { message: 'cannot terminate' } }))
    );

    component.terminate(enrollments[0]);

    expect(toastrSpy.error).toHaveBeenCalledWith('cannot terminate');
    expect(component.terminatingId()).toBeNull();
  });
});
