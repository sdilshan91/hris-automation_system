import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { SalaryStructuresComponent } from './salary-structures.component';
import { PayrollService } from '../../services/payroll.service';
import { ISalaryStructure } from '../../models/payroll.models';

describe('SalaryStructuresComponent', () => {
  let fixture: ComponentFixture<SalaryStructuresComponent>;
  let component: SalaryStructuresComponent;
  let payroll: jasmine.SpyObj<PayrollService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const active: ISalaryStructure = {
    id: 's-1',
    name: 'Full-Time',
    code: 'FT',
    description: 'Standard',
    effectiveFrom: '2026-01-01',
    isDefault: true,
    isActive: true,
    componentCount: 3,
  };
  const inactive: ISalaryStructure = {
    ...active,
    id: 's-2',
    name: 'Legacy',
    code: 'LEG',
    isDefault: false,
    isActive: false,
  };

  beforeEach(async () => {
    payroll = jasmine.createSpyObj<PayrollService>('PayrollService', [
      'listStructures',
      'cloneStructure',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    payroll.listStructures.and.returnValue(of([active, inactive]));

    await TestBed.configureTestingModule({
      imports: [SalaryStructuresComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollService, useValue: payroll },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SalaryStructuresComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads structures on init', () => {
    expect(payroll.listStructures).toHaveBeenCalled();
    expect(component.structures().length).toBe(2);
    expect(component.loading()).toBeFalse();
  });

  it('renders Active and Inactive status badges (AC-3)', () => {
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Active');
    expect(text).toContain('Inactive');
    expect(text).toContain('Default');
  });

  it('shows an error state when loading fails', () => {
    payroll.listStructures.and.returnValue(throwError(() => new Error('x')));
    component.load();
    expect(component.error()).toContain('Could not load');
  });

  describe('clone (FR-6)', () => {
    it('prepends the cloned structure and clears the cloning flag', () => {
      const clone: ISalaryStructure = { ...active, id: 's-3', name: 'Full-Time (copy)' };
      payroll.cloneStructure.and.returnValue(of(clone));

      component.clone(active);

      expect(payroll.cloneStructure).toHaveBeenCalledWith('s-1');
      expect(component.structures()[0].id).toBe('s-3');
      expect(component.cloning()).toBeNull();
      expect(toastr.success).toHaveBeenCalled();
    });

    it('toasts and clears the flag on a clone error', () => {
      payroll.cloneStructure.and.returnValue(throwError(() => new Error('x')));
      component.clone(active);
      expect(toastr.error).toHaveBeenCalled();
      expect(component.cloning()).toBeNull();
    });
  });
});
