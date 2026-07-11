import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { PlanListComponent } from './plan-list.component';
import { BenefitService } from '../../services/benefit.service';
import { IBenefitPlan } from '../../models/benefit.models';

describe('PlanListComponent', () => {
  let component: PlanListComponent;
  let fixture: ComponentFixture<PlanListComponent>;
  let benefitServiceSpy: jasmine.SpyObj<BenefitService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockPlans: IBenefitPlan[] = [
    {
      id: 'p-1',
      name: 'Gold Health',
      type: 'Health',
      description: 'Comprehensive medical cover',
      coverageDetails: 'In & out-patient',
      employerCost: 400,
      employeeCost: 100,
      currency: 'USD',
      effectiveFrom: '2026-08-01',
      effectiveTo: '2027-07-31',
      enrollmentOpensAt: null,
      enrollmentClosesAt: null,
      status: 'Active',
      createdAt: '2026-07-01T00:00:00Z',
      updatedAt: null,
    },
    {
      id: 'p-2',
      name: 'Basic Dental',
      type: 'Dental',
      description: null,
      coverageDetails: null,
      employerCost: 50,
      employeeCost: null,
      currency: 'USD',
      effectiveFrom: '2026-09-01',
      effectiveTo: null,
      enrollmentOpensAt: null,
      enrollmentClosesAt: null,
      status: 'Draft',
      createdAt: '2026-07-02T00:00:00Z',
      updatedAt: null,
    },
    {
      id: 'p-3',
      name: 'Legacy Vision',
      type: 'Vision',
      description: null,
      coverageDetails: null,
      employerCost: null,
      employeeCost: null,
      currency: 'USD',
      effectiveFrom: '2025-01-01',
      effectiveTo: '2025-12-31',
      enrollmentOpensAt: null,
      enrollmentClosesAt: null,
      status: 'Archived',
      createdAt: '2025-01-01T00:00:00Z',
      updatedAt: null,
    },
  ];

  beforeEach(async () => {
    benefitServiceSpy = jasmine.createSpyObj('BenefitService', [
      'getPlans',
      'changePlanStatus',
    ]);
    benefitServiceSpy.getPlans.and.returnValue(of(mockPlans));
    benefitServiceSpy.changePlanStatus.and.returnValue(of(mockPlans[0]));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [PlanListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: BenefitService, useValue: benefitServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PlanListComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load plans on init', () => {
    fixture.detectChanges();
    expect(benefitServiceSpy.getPlans).toHaveBeenCalled();
    expect(component.plans().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error state when loading fails', () => {
    benefitServiceSpy.getPlans.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Internal server error' } }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Internal server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should use default error message when backend message is missing', () => {
    benefitServiceSpy.getPlans.and.returnValue(throwError(() => ({ status: 0 })));
    fixture.detectChanges();
    expect(component.loadError()).toBe(
      'Failed to load benefit plans. Please try again.'
    );
  });

  // --- Search / filter ----------------------------------------

  it('should filter plans by name', () => {
    fixture.detectChanges();
    component.searchQuery.set('gold');
    expect(component.filteredPlans().length).toBe(1);
    expect(component.filteredPlans()[0].name).toBe('Gold Health');
  });

  it('should filter plans by type', () => {
    fixture.detectChanges();
    component.searchQuery.set('dental');
    expect(component.filteredPlans().length).toBe(1);
    expect(component.filteredPlans()[0].name).toBe('Basic Dental');
  });

  it('should return all plans for an empty query', () => {
    fixture.detectChanges();
    component.searchQuery.set('');
    expect(component.filteredPlans().length).toBe(3);
  });

  // --- Display helpers ----------------------------------------

  it('should format a cost with currency and a dash for null', () => {
    expect(component.costLabel(400, 'USD')).toBe('USD 400.00');
    expect(component.costLabel(null, 'USD')).toBe('—');
  });

  it('should label an open-ended effective window', () => {
    expect(component.effectiveLabel(mockPlans[1])).toBe('2026-09-01 → open');
    expect(component.effectiveLabel(mockPlans[0])).toBe('2026-08-01 → 2027-07-31');
  });

  // --- Status transition rules (AC-2/AC-3/AC-6) ---------------

  it('should expose the legal transitions per status', () => {
    expect(component.allowedTransitions('Draft')).toEqual(['Active', 'Archived']);
    expect(component.allowedTransitions('Active')).toEqual(['Inactive', 'Archived']);
    expect(component.allowedTransitions('Inactive')).toEqual(['Active', 'Archived']);
    expect(component.allowedTransitions('Archived')).toEqual([]);
  });

  // --- Form slide-over ----------------------------------------

  it('should open create form with a null plan', () => {
    fixture.detectChanges();
    component.openCreate();
    expect(component.formOpen()).toBeTrue();
    expect(component.editingPlan()).toBeNull();
  });

  it('should open edit form with the selected plan', () => {
    fixture.detectChanges();
    component.openEdit(mockPlans[0]);
    expect(component.formOpen()).toBeTrue();
    expect(component.editingPlan()).toBe(mockPlans[0]);
  });

  it('should reload plans when form saved', () => {
    fixture.detectChanges();
    benefitServiceSpy.getPlans.calls.reset();
    component.onFormSaved();
    expect(component.formOpen()).toBeFalse();
    expect(benefitServiceSpy.getPlans).toHaveBeenCalled();
  });

  // --- Status change flow -------------------------------------

  it('should open the status dialog defaulting to the first legal transition', () => {
    fixture.detectChanges();
    component.openStatusChange(mockPlans[1]); // Draft
    expect(component.planToChangeStatus()).toBe(mockPlans[1]);
    expect(component.targetStatus()).toBe('Active');
  });

  it('should not open the status dialog for an archived (terminal) plan', () => {
    fixture.detectChanges();
    component.openStatusChange(mockPlans[2]); // Archived
    expect(component.planToChangeStatus()).toBeNull();
  });

  it('should change status and reload on confirm', () => {
    fixture.detectChanges();
    component.openStatusChange(mockPlans[1]); // Draft → Active
    benefitServiceSpy.getPlans.calls.reset();
    component.confirmStatusChange();

    expect(benefitServiceSpy.changePlanStatus).toHaveBeenCalledWith('p-2', {
      status: 'Active',
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.planToChangeStatus()).toBeNull();
    expect(benefitServiceSpy.getPlans).toHaveBeenCalled();
  });

  it('should show an error toast when status change fails', () => {
    fixture.detectChanges();
    benefitServiceSpy.changePlanStatus.and.returnValue(
      throwError(() => ({
        status: 409,
        error: { message: 'Illegal transition', code: 'invalid_status_transition' },
      }))
    );
    component.openStatusChange(mockPlans[1]);
    component.confirmStatusChange();

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isChangingStatus()).toBeFalse();
  });
});
