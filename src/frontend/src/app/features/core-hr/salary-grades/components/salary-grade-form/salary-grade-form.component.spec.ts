import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { ComponentRef } from '@angular/core';

import { SalaryGradeFormComponent } from './salary-grade-form.component';
import { SalaryGradeService } from '../../services/salary-grade.service';
import { ISalaryGrade } from '../../models/salary-grade.models';

describe('SalaryGradeFormComponent', () => {
  let component: SalaryGradeFormComponent;
  let componentRef: ComponentRef<SalaryGradeFormComponent>;
  let fixture: ComponentFixture<SalaryGradeFormComponent>;
  let gradeServiceSpy: jasmine.SpyObj<SalaryGradeService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockGrade: ISalaryGrade = {
    id: 'sg-1',
    code: 'G1',
    name: 'Grade 1',
    minAmount: 30000,
    midAmount: 40000,
    maxAmount: 50000,
    currency: 'USD',
    description: 'Entry level',
    isActive: true,
  };

  beforeEach(async () => {
    gradeServiceSpy = jasmine.createSpyObj('SalaryGradeService', [
      'create',
      'update',
    ]);
    gradeServiceSpy.create.and.returnValue(of(mockGrade));
    gradeServiceSpy.update.and.returnValue(of(mockGrade));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [SalaryGradeFormComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: SalaryGradeService, useValue: gradeServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SalaryGradeFormComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  // --- Create mode -------------------------------------------

  describe('create mode', () => {
    beforeEach(() => {
      componentRef.setInput('grade', null);
      fixture.detectChanges();
    });

    it('should create', () => {
      expect(component).toBeTruthy();
    });

    it('should initialize with an empty form defaulting currency to USD and active', () => {
      expect(component.form.value.code).toBe('');
      expect(component.form.value.name).toBe('');
      expect(component.form.value.currency).toBe('USD');
      expect(component.form.value.isActive).toBeTrue();
      expect(component.form.valid).toBeFalse();
    });

    it('should require code and name', () => {
      component.form.patchValue({
        minAmount: 10,
        maxAmount: 20,
        currency: 'USD',
      });
      expect(component.form.get('code')!.valid).toBeFalse();
      expect(component.form.get('name')!.valid).toBeFalse();
    });

    it('should reject a 2-char currency and accept a 3-letter code', () => {
      const cur = component.form.get('currency')!;
      cur.setValue('US');
      expect(cur.valid).toBeFalse();
      cur.setValue('EUR');
      expect(cur.valid).toBeTrue();
    });

    it('should block submit when min > max (bandOrder)', () => {
      component.form.patchValue({
        code: 'G9',
        name: 'Bad Grade',
        minAmount: 90000,
        maxAmount: 10000,
        currency: 'USD',
      });
      component.form.markAsDirty();

      expect(component.form.hasError('bandOrder')).toBeTrue();

      component.onSubmit();
      expect(gradeServiceSpy.create).not.toHaveBeenCalled();
    });

    it('should block submit when mid is outside [min, max]', () => {
      component.form.patchValue({
        code: 'G9',
        name: 'Bad Mid',
        minAmount: 10000,
        midAmount: 99000,
        maxAmount: 50000,
        currency: 'USD',
      });
      expect(component.form.hasError('bandOrder')).toBeTrue();
    });

    it('should POST the trimmed, upper-cased payload on valid submit', () => {
      component.form.patchValue({
        code: '  G3 ',
        name: '  Grade 3 ',
        minAmount: 70000,
        midAmount: 85000,
        maxAmount: 100000,
        currency: 'usd',
        description: '  Senior  ',
      });
      component.form.markAsDirty();

      component.onSubmit();

      expect(gradeServiceSpy.create).toHaveBeenCalledWith({
        code: 'G3',
        name: 'Grade 3',
        minAmount: 70000,
        midAmount: 85000,
        maxAmount: 100000,
        currency: 'USD',
        description: 'Senior',
        isActive: true,
      });
      expect(toastrSpy.success).toHaveBeenCalled();
    });

    it('should send midAmount null when left blank', () => {
      component.form.patchValue({
        code: 'G4',
        name: 'Grade 4',
        minAmount: 10000,
        maxAmount: 20000,
        currency: 'USD',
      });
      component.form.markAsDirty();

      component.onSubmit();

      expect(gradeServiceSpy.create).toHaveBeenCalledWith(
        jasmine.objectContaining({ midAmount: null })
      );
    });

    it('should surface a 409 duplicate-code error as a field message', () => {
      gradeServiceSpy.create.and.returnValue(
        throwError(() => ({
          status: 409,
          error: {
            message: 'A salary grade with this code already exists.',
            code: 'duplicate_code',
          },
        }))
      );

      component.form.patchValue({
        code: 'G1',
        name: 'Dup',
        minAmount: 1,
        maxAmount: 2,
        currency: 'USD',
      });
      component.form.markAsDirty();
      component.onSubmit();

      expect(component.duplicateCodeError()).toBe(
        'A salary grade with this code already exists.'
      );
      expect(component.isSaving()).toBeFalse();
      expect(toastrSpy.error).not.toHaveBeenCalled();
    });

    it('should surface a 422 invalid_grade error via toast', () => {
      gradeServiceSpy.create.and.returnValue(
        throwError(() => ({
          status: 422,
          error: {
            message: 'Invalid salary band.',
            code: 'invalid_grade',
          },
        }))
      );

      component.form.patchValue({
        code: 'G5',
        name: 'Grade 5',
        minAmount: 1,
        maxAmount: 2,
        currency: 'USD',
      });
      component.form.markAsDirty();
      component.onSubmit();

      expect(toastrSpy.error).toHaveBeenCalledWith('Invalid salary band.');
    });
  });

  // --- Edit mode ---------------------------------------------

  describe('edit mode', () => {
    beforeEach(() => {
      componentRef.setInput('grade', mockGrade);
      fixture.detectChanges();
    });

    it('should populate the form from the grade', () => {
      expect(component.form.value.code).toBe('G1');
      expect(component.form.value.name).toBe('Grade 1');
      expect(component.form.value.minAmount).toBe(30000);
      expect(component.form.value.midAmount).toBe(40000);
      expect(component.form.value.maxAmount).toBe(50000);
      expect(component.form.value.currency).toBe('USD');
    });

    it('should PUT the updated payload on submit', () => {
      component.form.patchValue({ name: 'Grade 1 (revised)' });
      component.form.markAsDirty();

      component.onSubmit();

      expect(gradeServiceSpy.update).toHaveBeenCalledWith(
        'sg-1',
        jasmine.objectContaining({ name: 'Grade 1 (revised)', code: 'G1' })
      );
      expect(toastrSpy.success).toHaveBeenCalled();
    });
  });
});
