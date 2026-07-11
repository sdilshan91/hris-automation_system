import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { EligibilityRulesComponent } from './eligibility-rules.component';
import { BenefitService } from '../../services/benefit.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { IEligibilityRule } from '../../models/benefit.models';

describe('EligibilityRulesComponent', () => {
  let component: EligibilityRulesComponent;
  let fixture: ComponentFixture<EligibilityRulesComponent>;
  let benefitServiceSpy: jasmine.SpyObj<BenefitService>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const rules: IEligibilityRule[] = [
    { id: 'r-1', benefitPlanId: 'p-1', attribute: 'TenureDays', operator: '>=', value: '90' },
    { id: 'r-2', benefitPlanId: 'p-1', attribute: 'EmploymentType', operator: '==', value: 'FullTime' },
  ];

  async function setup(canManage: boolean): Promise<void> {
    benefitServiceSpy = jasmine.createSpyObj('BenefitService', [
      'getEligibilityRules',
      'createEligibilityRule',
      'deleteEligibilityRule',
    ]);
    benefitServiceSpy.getEligibilityRules.and.returnValue(of(rules));
    benefitServiceSpy.createEligibilityRule.and.returnValue(of(rules[0]));
    benefitServiceSpy.deleteEligibilityRule.and.returnValue(of(void 0));

    authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.callFake(
      (p: string) => canManage && p === 'Benefits.Manage'
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    await TestBed.configureTestingModule({
      imports: [EligibilityRulesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        { provide: BenefitService, useValue: benefitServiceSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EligibilityRulesComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('planId', 'p-1');
  }

  it('should load the rules for the plan on init', async () => {
    await setup(true);
    fixture.detectChanges();
    expect(benefitServiceSpy.getEligibilityRules).toHaveBeenCalledWith('p-1');
    expect(component.rules().length).toBe(2);
    expect(component.canManage()).toBeTrue();
  });

  it('should fail soft to an empty list when the user cannot read rules', async () => {
    await setup(false);
    benefitServiceSpy.getEligibilityRules.and.returnValue(
      throwError(() => ({ status: 403 }))
    );
    fixture.detectChanges();
    expect(component.rules()).toEqual([]);
    expect(component.isLoading()).toBeFalse();
    expect(component.canManage()).toBeFalse();
  });

  it('should add a rule and append it, then clear the value', async () => {
    await setup(true);
    fixture.detectChanges();
    component.newAttribute.set('EmploymentType');
    component.newOperator.set('==');
    component.newValue.set('FullTime');

    component.add();

    expect(benefitServiceSpy.createEligibilityRule).toHaveBeenCalledWith('p-1', {
      attribute: 'EmploymentType',
      operator: '==',
      value: 'FullTime',
    });
    expect(component.rules().length).toBe(3);
    expect(component.newValue()).toBe('');
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('should not add when the value is blank', async () => {
    await setup(true);
    fixture.detectChanges();
    component.newValue.set('   ');
    component.add();
    expect(benefitServiceSpy.createEligibilityRule).not.toHaveBeenCalled();
  });

  it('should show an invalid_rule error toast when the add fails', async () => {
    await setup(true);
    fixture.detectChanges();
    benefitServiceSpy.createEligibilityRule.and.returnValue(
      throwError(() => ({
        status: 400,
        error: { message: 'Operator > is not valid for EmploymentType', code: 'invalid_rule' },
      }))
    );
    component.newValue.set('FullTime');
    component.add();

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'Operator > is not valid for EmploymentType'
    );
    expect(component.isAdding()).toBeFalse();
  });

  it('should remove a rule and drop it from the list', async () => {
    await setup(true);
    fixture.detectChanges();
    component.remove(rules[0]);

    expect(benefitServiceSpy.deleteEligibilityRule).toHaveBeenCalledWith('r-1');
    expect(component.rules().some((r) => r.id === 'r-1')).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('should provide attribute-specific value hints', async () => {
    await setup(true);
    fixture.detectChanges();
    component.newAttribute.set('TenureDays');
    expect(component.valuePlaceholder()).toContain('90');
    component.newAttribute.set('Department');
    expect(component.valueHint()).toContain('GUID');
  });
});
