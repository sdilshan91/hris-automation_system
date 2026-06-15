import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { ComponentFormComponent } from './component-form.component';
import { PayrollService } from '../../services/payroll.service';
import { ISalaryComponent } from '../../models/payroll.models';

describe('ComponentFormComponent', () => {
  let fixture: ComponentFixture<ComponentFormComponent>;
  let component: ComponentFormComponent;
  let payroll: jasmine.SpyObj<PayrollService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const existing: ISalaryComponent = {
    id: 'c-1',
    name: 'Basic',
    code: 'BASIC',
    type: 'Earning',
    calculationMethod: 'Fixed',
    defaultValue: 1000,
    formulaExpression: null,
    isTaxable: true,
    isStatutory: false,
    isActive: true,
    processingOrder: 1,
  };

  beforeEach(async () => {
    payroll = jasmine.createSpyObj<PayrollService>('PayrollService', [
      'createComponent',
      'updateComponent',
      'testFormula',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    await TestBed.configureTestingModule({
      imports: [ComponentFormComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollService, useValue: payroll },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ComponentFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('starts in create mode with a Fixed method requiring defaultValue', () => {
    expect(component.isEdit()).toBeFalse();
    expect(component.isFormula()).toBeFalse();
    expect(component.form.controls.defaultValue.hasError('required')).toBeTrue();
  });

  it('blocks save when required fields are missing', () => {
    component.save();
    expect(payroll.createComponent).not.toHaveBeenCalled();
    expect(component.form.controls.name.touched).toBeTrue();
  });

  it('switches validators to formula when calculationMethod is Formula', () => {
    component.form.controls.calculationMethod.setValue('Formula');
    fixture.detectChanges();

    expect(component.isFormula()).toBeTrue();
    expect(
      component.form.controls.formulaExpression.hasError('required'),
    ).toBeTrue();
    // defaultValue is no longer required
    expect(component.form.controls.defaultValue.hasError('required')).toBeFalse();
  });

  it('isPercentage reflects percentage methods', () => {
    component.form.controls.calculationMethod.setValue('PercentageOfBasic');
    expect(component.isPercentage()).toBeTrue();
  });

  it('patches the form and method when an edit target is provided', () => {
    fixture.componentRef.setInput('component', existing);
    fixture.detectChanges();

    expect(component.isEdit()).toBeTrue();
    expect(component.form.controls.name.value).toBe('Basic');
    expect(component.form.controls.code.value).toBe('BASIC');
  });

  describe('save (create)', () => {
    it('POSTs a normalized request and emits saved', () => {
      payroll.createComponent.and.returnValue(of(existing));
      const saved = jasmine.createSpy('saved');
      component.saved.subscribe(saved);

      component.form.patchValue({
        name: '  Basic  ',
        code: 'basic',
        type: 'Earning',
        calculationMethod: 'Fixed',
        defaultValue: 1000,
      });
      component.save();

      expect(payroll.createComponent).toHaveBeenCalledWith(
        jasmine.objectContaining({
          name: 'Basic',
          code: 'BASIC', // uppercased
          formulaExpression: null,
          defaultValue: 1000,
        }),
      );
      expect(saved).toHaveBeenCalledWith(existing);
      expect(toastr.success).toHaveBeenCalled();
    });

    it('nulls defaultValue and sends the formula for a Formula component', () => {
      payroll.createComponent.and.returnValue(of(existing));
      component.form.patchValue({
        name: 'PF',
        code: 'PF',
        type: 'Statutory',
        calculationMethod: 'Formula',
        formulaExpression: 'basic * 0.12',
      });
      component.save();

      expect(payroll.createComponent).toHaveBeenCalledWith(
        jasmine.objectContaining({
          defaultValue: null,
          formulaExpression: 'basic * 0.12',
        }),
      );
    });

    it('shows a toast on a save error', () => {
      payroll.createComponent.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400 })),
      );
      component.form.patchValue({
        name: 'X',
        code: 'X',
        type: 'Earning',
        calculationMethod: 'Fixed',
        defaultValue: 1,
      });
      component.save();
      expect(toastr.error).toHaveBeenCalled();
      expect(component.saving()).toBeFalse();
    });
  });

  describe('save (edit)', () => {
    it('PUTs when an existing component is set', () => {
      fixture.componentRef.setInput('component', existing);
      fixture.detectChanges();
      payroll.updateComponent.and.returnValue(of(existing));

      component.save();

      expect(payroll.updateComponent).toHaveBeenCalledWith(
        'c-1',
        jasmine.any(Object),
      );
    });
  });

  describe('testFormula (FR-4)', () => {
    it('calls the service with the expression + sample values and stores the result', () => {
      component.form.controls.calculationMethod.setValue('Formula');
      component.form.controls.formulaExpression.setValue('basic * 0.12');
      payroll.testFormula.and.returnValue(of({ valid: true, result: 120 }));

      component.testFormula();

      expect(payroll.testFormula).toHaveBeenCalledWith({
        expression: 'basic * 0.12',
        sampleValues: { basic: 1000, gross: 1500 },
      });
      expect(component.testResult()).toEqual({ valid: true, result: 120 });
    });

    it('records an invalid result on a test error', () => {
      component.form.controls.formulaExpression.setValue('basic *');
      payroll.testFormula.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400 })),
      );
      component.testFormula();
      expect(component.testResult()?.valid).toBeFalse();
    });

    it('does nothing when there is no expression', () => {
      component.testFormula();
      expect(payroll.testFormula).not.toHaveBeenCalled();
    });
  });
});
