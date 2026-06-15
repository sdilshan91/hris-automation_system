import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { CdkDragDrop } from '@angular/cdk/drag-drop';

import { SalaryComponentsComponent } from './salary-components.component';
import { PayrollService } from '../../services/payroll.service';
import { ISalaryComponent } from '../../models/payroll.models';

describe('SalaryComponentsComponent', () => {
  let fixture: ComponentFixture<SalaryComponentsComponent>;
  let component: SalaryComponentsComponent;
  let payroll: jasmine.SpyObj<PayrollService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const compA: ISalaryComponent = {
    id: 'a',
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
  const compB: ISalaryComponent = {
    ...compA,
    id: 'b',
    name: 'PF',
    code: 'PF',
    type: 'Statutory',
    calculationMethod: 'Formula',
    defaultValue: null,
    formulaExpression: 'basic * 0.12',
    isStatutory: true,
    processingOrder: 2,
  };

  beforeEach(async () => {
    payroll = jasmine.createSpyObj<PayrollService>('PayrollService', [
      'listComponents',
      'reorderComponents',
      'deleteComponent',
      'parseInUseError',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    payroll.listComponents.and.returnValue(of([compB, compA]));

    await TestBed.configureTestingModule({
      imports: [SalaryComponentsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollService, useValue: payroll },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SalaryComponentsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads components sorted by processing order on init', () => {
    expect(payroll.listComponents).toHaveBeenCalled();
    expect(component.components().map((c) => c.id)).toEqual(['a', 'b']);
    expect(component.loading()).toBeFalse();
  });

  it('shows an error state when the load fails', () => {
    payroll.listComponents.and.returnValue(
      throwError(() => new Error('fail')),
    );
    component.load();
    expect(component.error()).toContain('Could not load');
  });

  it('renders type badges and the formula value summary', () => {
    const html: string = fixture.nativeElement.textContent;
    expect(html).toContain('Earning');
    expect(html).toContain('Statutory');
    expect(html).toContain('basic * 0.12');
  });

  describe('valueLabel', () => {
    it('returns the formula expression for Formula components', () => {
      expect(component.valueLabel(compB)).toBe('basic * 0.12');
    });
    it('appends % for percentage methods', () => {
      expect(
        component.valueLabel({
          ...compA,
          calculationMethod: 'PercentageOfBasic',
          defaultValue: 12,
        }),
      ).toBe('12%');
    });
    it('returns the raw amount for Fixed', () => {
      expect(component.valueLabel(compA)).toBe('1000');
    });
    it('returns a dash when no value', () => {
      expect(
        component.valueLabel({ ...compA, defaultValue: null }),
      ).toBe('—');
    });
  });

  describe('reorder (AC-4)', () => {
    it('persists a new order via reorderComponents on drop', () => {
      payroll.reorderComponents.and.returnValue(of([compA, compB]));
      component.onDrop({
        previousIndex: 0,
        currentIndex: 1,
      } as CdkDragDrop<ISalaryComponent[]>);

      expect(payroll.reorderComponents).toHaveBeenCalledWith(['b', 'a']);
    });

    it('ignores a no-op drop', () => {
      component.onDrop({
        previousIndex: 1,
        currentIndex: 1,
      } as CdkDragDrop<ISalaryComponent[]>);
      expect(payroll.reorderComponents).not.toHaveBeenCalled();
    });

    it('move() reorders via the up/down buttons', () => {
      payroll.reorderComponents.and.returnValue(of([compB, compA]));
      component.move(0, 1); // move 'a' down
      expect(payroll.reorderComponents).toHaveBeenCalledWith(['b', 'a']);
    });

    it('move() ignores out-of-range targets', () => {
      component.move(0, -1);
      expect(payroll.reorderComponents).not.toHaveBeenCalled();
    });

    it('rolls back the optimistic order when the reorder fails', () => {
      payroll.reorderComponents.and.returnValue(
        throwError(() => new Error('x')),
      );
      const before = component.components().map((c) => c.id);
      component.move(0, 1);
      expect(component.components().map((c) => c.id)).toEqual(before);
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  describe('delete (AC-5)', () => {
    it('removes the component on a successful delete', () => {
      payroll.deleteComponent.and.returnValue(of(void 0));
      component.confirmDelete(compA);
      expect(component.components().map((c) => c.id)).toEqual(['b']);
      expect(toastr.success).toHaveBeenCalled();
    });

    it('shows the affected-employee count when blocked', () => {
      const err = new HttpErrorResponse({ status: 409 });
      payroll.deleteComponent.and.returnValue(throwError(() => err));
      payroll.parseInUseError.and.returnValue({
        code: 'component_in_use',
        affectedEmployeeCount: 5,
      });

      component.confirmDelete(compA);

      expect(component.inUse()).toEqual({ name: 'Basic', count: 5 });
      // component is NOT removed
      expect(component.components().length).toBe(2);
    });

    it('falls back to a toast for an unrelated delete error', () => {
      payroll.deleteComponent.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500 })),
      );
      payroll.parseInUseError.and.returnValue(null);
      component.confirmDelete(compA);
      expect(component.inUse()).toBeNull();
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  describe('slide-over', () => {
    it('openCreate sets a null edit target and opens the form', () => {
      component.openCreate();
      expect(component.formOpen()).toBeTrue();
      expect(component.editTarget()).toBeNull();
    });

    it('openEdit sets the edit target', () => {
      component.openEdit(compB);
      expect(component.formOpen()).toBeTrue();
      expect(component.editTarget()).toBe(compB);
    });

    it('onSaved closes the form and reloads', () => {
      payroll.listComponents.calls.reset();
      component.openCreate();
      component.onSaved();
      expect(component.formOpen()).toBeFalse();
      expect(payroll.listComponents).toHaveBeenCalled();
    });
  });
});
