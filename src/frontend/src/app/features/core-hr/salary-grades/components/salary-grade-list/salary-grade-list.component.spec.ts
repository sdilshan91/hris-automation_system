import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { SalaryGradeListComponent } from './salary-grade-list.component';
import { SalaryGradeService } from '../../services/salary-grade.service';
import { ISalaryGrade } from '../../models/salary-grade.models';

describe('SalaryGradeListComponent', () => {
  let component: SalaryGradeListComponent;
  let fixture: ComponentFixture<SalaryGradeListComponent>;
  let gradeServiceSpy: jasmine.SpyObj<SalaryGradeService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockGrades: ISalaryGrade[] = [
    {
      id: 'sg-1',
      code: 'G1',
      name: 'Grade 1',
      minAmount: 30000,
      midAmount: 40000,
      maxAmount: 50000,
      currency: 'USD',
      description: 'Entry level',
      isActive: true,
    },
    {
      id: 'sg-2',
      code: 'G2',
      name: 'Grade 2',
      minAmount: 50000,
      midAmount: null,
      maxAmount: 70000,
      currency: 'USD',
      description: null,
      isActive: true,
    },
  ];

  beforeEach(async () => {
    gradeServiceSpy = jasmine.createSpyObj('SalaryGradeService', [
      'list',
      'deactivate',
    ]);
    gradeServiceSpy.list.and.returnValue(of(mockGrades));
    gradeServiceSpy.deactivate.and.returnValue(of(undefined));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [SalaryGradeListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: SalaryGradeService, useValue: gradeServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SalaryGradeListComponent);
    component = fixture.componentInstance;
  });

  it('should create and load grades on init', () => {
    fixture.detectChanges();
    expect(gradeServiceSpy.list).toHaveBeenCalledWith(false);
    expect(component.grades().length).toBe(2);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error state when loading fails', () => {
    gradeServiceSpy.list.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Boom' } }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Boom');
  });

  it('should filter grades by code or name', () => {
    fixture.detectChanges();
    component.searchQuery.set('grade 2');
    expect(component.filteredGrades().length).toBe(1);
    expect(component.filteredGrades()[0].code).toBe('G2');

    component.searchQuery.set('G1');
    expect(component.filteredGrades().length).toBe(1);
  });

  it('should reload with includeInactive when toggled', () => {
    fixture.detectChanges();
    gradeServiceSpy.list.calls.reset();

    component.onIncludeInactiveChange(true);
    expect(component.includeInactive()).toBeTrue();
    expect(gradeServiceSpy.list).toHaveBeenCalledWith(true);
  });

  it('should open create and edit forms', () => {
    fixture.detectChanges();
    component.openCreate();
    expect(component.formOpen()).toBeTrue();
    expect(component.editingGrade()).toBeNull();

    component.openEdit(mockGrades[0]);
    expect(component.editingGrade()).toBe(mockGrades[0]);
  });

  it('should reload grades when a form is saved', () => {
    fixture.detectChanges();
    gradeServiceSpy.list.calls.reset();

    component.onFormSaved();
    expect(component.formOpen()).toBeFalse();
    expect(gradeServiceSpy.list).toHaveBeenCalled();
  });

  it('should deactivate a grade via soft-delete', () => {
    fixture.detectChanges();
    component.confirmDeactivate(mockGrades[0]);
    component.deactivateGrade();

    expect(gradeServiceSpy.deactivate).toHaveBeenCalledWith('sg-1');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(component.gradeToDeactivate()).toBeNull();
  });

  it('should surface a deactivation error via toast', () => {
    fixture.detectChanges();
    gradeServiceSpy.deactivate.and.returnValue(
      throwError(() => ({ status: 500, error: { message: 'Nope' } }))
    );

    component.confirmDeactivate(mockGrades[0]);
    component.deactivateGrade();

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.isDeactivating()).toBeFalse();
  });

  it('should do nothing if no grade is selected for deactivation', () => {
    fixture.detectChanges();
    component.deactivateGrade();
    expect(gradeServiceSpy.deactivate).not.toHaveBeenCalled();
  });
});
