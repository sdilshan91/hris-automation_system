import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { EmployeeCompensationComponent } from './employee-compensation.component';
import { EmployeeSalaryService } from '../../services/employee-salary.service';
import { PayrollService } from '../../services/payroll.service';
import { AuthService } from '@core/auth/auth.service';
import { ISalaryStructure } from '../../models/payroll.models';
import {
  ICtcBreakdown,
  IEmployeeCompensation,
  ISalaryRevision,
} from '../../models/employee-salary.models';

describe('EmployeeCompensationComponent', () => {
  let fixture: ComponentFixture<EmployeeCompensationComponent>;
  let component: EmployeeCompensationComponent;
  let salary: jasmine.SpyObj<EmployeeSalaryService>;
  let payroll: jasmine.SpyObj<PayrollService>;
  let auth: jasmine.SpyObj<AuthService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const compWithStructure: IEmployeeCompensation = {
    employeeId: 'e-1',
    salaryStructureId: 's-1',
    salaryStructureName: 'Full-Time',
    annualCtc: 600000,
    monthlyCtc: 50000,
    effectiveFrom: '2026-07-01',
    lines: [
      {
        salaryComponentId: 'c-1',
        componentName: 'Basic',
        componentType: 'Earning',
        annualAmount: 240000,
        monthlyAmount: 20000,
        isOverride: false,
      },
      {
        salaryComponentId: 'c-2',
        componentName: 'HRA',
        componentType: 'Earning',
        annualAmount: 120000,
        monthlyAmount: 10000,
        isOverride: true,
      },
    ],
  };

  const emptyComp: IEmployeeCompensation = {
    employeeId: 'e-1',
    salaryStructureId: null,
    salaryStructureName: null,
    annualCtc: null,
    monthlyCtc: null,
    effectiveFrom: null,
    lines: [],
  };

  const revision: ISalaryRevision = {
    revisionId: 'r-1',
    oldStructureId: null,
    oldStructureName: null,
    oldAnnualCtc: null,
    newStructureId: 's-1',
    newStructureName: 'Full-Time',
    newAnnualCtc: 600000,
    effectiveFrom: '2026-07-01',
    reason: 'Initial',
    changedByName: 'HR',
    changedAt: '2026-06-15T10:00:00Z',
  };

  const structure: ISalaryStructure = {
    id: 's-1',
    name: 'Full-Time',
    code: 'FT',
    description: null,
    effectiveFrom: '2026-01-01',
    isDefault: true,
    isActive: true,
  };

  const breakdown: ICtcBreakdown = {
    annualCtc: 600000,
    totalAnnual: 600000,
    totalMonthly: 50000,
    balanced: true,
    lines: compWithStructure.lines,
  };

  async function setup(canAssign = true): Promise<void> {
    salary = jasmine.createSpyObj<EmployeeSalaryService>('EmployeeSalaryService', [
      'getCurrentCompensation',
      'getRevisionHistory',
      'previewBreakdown',
      'assign',
    ]);
    payroll = jasmine.createSpyObj<PayrollService>('PayrollService', [
      'listStructures',
    ]);
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['hasRole']);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    salary.getCurrentCompensation.and.returnValue(of(compWithStructure));
    salary.getRevisionHistory.and.returnValue(of([revision]));
    payroll.listStructures.and.returnValue(of([structure]));
    auth.hasRole.and.callFake((role: string) =>
      canAssign ? role === 'HR Officer' : false,
    );

    await TestBed.configureTestingModule({
      imports: [EmployeeCompensationComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: EmployeeSalaryService, useValue: salary },
        { provide: PayrollService, useValue: payroll },
        { provide: AuthService, useValue: auth },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(EmployeeCompensationComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('employeeId', 'e-1');
    fixture.detectChanges();
  }

  it('loads current compensation and revision history on init', async () => {
    await setup();
    expect(salary.getCurrentCompensation).toHaveBeenCalledWith('e-1');
    expect(salary.getRevisionHistory).toHaveBeenCalledWith('e-1');
    expect(component.compensation()).toEqual(compWithStructure);
    expect(component.revisions()).toEqual([revision]);
  });

  it('computes the monthly and annual totals from the lines (§8)', async () => {
    await setup();
    expect(component.compMonthlyTotal()).toBe(30000);
    expect(component.compAnnualTotal()).toBe(360000);
  });

  it('canAssign reflects HR Officer / Tenant Admin role', async () => {
    await setup(true);
    expect(component.canAssign()).toBeTrue();
  });

  it('canAssign is false for a non-HR viewer (read-only)', async () => {
    await setup(false);
    expect(component.canAssign()).toBeFalse();
  });

  it('renders the Payroll Incomplete warning when no structure assigned (BR-5)', async () => {
    await setup();
    salary.getCurrentCompensation.and.returnValue(of(emptyComp));
    component.loadCompensation();
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Payroll Incomplete');
  });

  it('toggleRevision expands and collapses an entry (FR-4)', async () => {
    await setup();
    component.toggleRevision('r-1');
    expect(component.expandedRevision()).toBe('r-1');
    component.toggleRevision('r-1');
    expect(component.expandedRevision()).toBeNull();
  });

  it('openAssignDrawer resets the form and loads active structures (FR-7)', async () => {
    await setup();
    component.openAssignDrawer();
    expect(component.showDrawer()).toBeTrue();
    expect(component.preview()).toBeNull();
    expect(component.overrides.length).toBe(0);
    expect(payroll.listStructures).toHaveBeenCalled();
    expect(component.activeStructures()).toEqual([structure]);
  });

  it('addOverride / removeOverride manage the overrides FormArray (AC-3)', async () => {
    await setup();
    component.openAssignDrawer();
    component.addOverride();
    component.addOverride();
    expect(component.overrides.length).toBe(2);
    component.removeOverride(0);
    expect(component.overrides.length).toBe(1);
  });

  it('loadPreview blocks on an invalid form and does not call the service (FR-3)', async () => {
    await setup();
    component.openAssignDrawer();
    component.loadPreview();
    expect(salary.previewBreakdown).not.toHaveBeenCalled();
  });

  it('loadPreview fetches the breakdown for a valid form (FR-3)', async () => {
    await setup();
    salary.previewBreakdown.and.returnValue(of(breakdown));
    component.openAssignDrawer();
    component.assignForm.patchValue({
      salaryStructureId: 's-1',
      annualCtc: 600000,
      effectiveFrom: '2026-07-01',
    });
    component.loadPreview();
    expect(salary.previewBreakdown).toHaveBeenCalled();
    expect(component.preview()).toEqual(breakdown);
  });

  it('confirmAssign blocks until a preview exists (FR-3 preview-before-confirm)', async () => {
    await setup();
    component.openAssignDrawer();
    component.assignForm.patchValue({
      salaryStructureId: 's-1',
      annualCtc: 600000,
      effectiveFrom: '2026-07-01',
    });
    // No preview yet
    component.confirmAssign();
    expect(salary.assign).not.toHaveBeenCalled();
  });

  it('confirmAssign assigns, closes the drawer and reloads (FR-1)', async () => {
    await setup();
    salary.previewBreakdown.and.returnValue(of(breakdown));
    salary.assign.and.returnValue(
      of({
        employeeId: 'e-1',
        salaryStructureId: 's-1',
        annualCtc: 600000,
        effectiveFrom: '2026-07-01',
        breakdown,
      }),
    );
    component.openAssignDrawer();
    component.assignForm.patchValue({
      salaryStructureId: 's-1',
      annualCtc: 600000,
      effectiveFrom: '2026-07-01',
    });
    component.loadPreview();
    salary.getCurrentCompensation.calls.reset();
    component.confirmAssign();

    expect(salary.assign).toHaveBeenCalled();
    expect(component.showDrawer()).toBeFalse();
    expect(toastr.success).toHaveBeenCalled();
    expect(salary.getCurrentCompensation).toHaveBeenCalled();
  });

  it('builds an assign request including per-component overrides (AC-3)', async () => {
    await setup();
    salary.previewBreakdown.and.returnValue(of(breakdown));
    component.openAssignDrawer();
    component.assignForm.patchValue({
      salaryStructureId: 's-1',
      annualCtc: 600000,
      effectiveFrom: '2026-07-01',
    });
    component.addOverride();
    component.overrides
      .at(0)
      .patchValue({ salaryComponentId: 'c-2', annualAmount: 130000 });
    component.loadPreview();

    const req = salary.previewBreakdown.calls.mostRecent().args[0];
    expect(req.overrides).toEqual([
      { salaryComponentId: 'c-2', annualAmount: 130000 },
    ]);
  });

  it('shows an error toast when assignment fails', async () => {
    await setup();
    salary.previewBreakdown.and.returnValue(of(breakdown));
    salary.assign.and.returnValue(throwError(() => new Error('boom')));
    component.openAssignDrawer();
    component.assignForm.patchValue({
      salaryStructureId: 's-1',
      annualCtc: 600000,
      effectiveFrom: '2026-07-01',
    });
    component.loadPreview();
    component.confirmAssign();
    expect(toastr.error).toHaveBeenCalled();
    expect(component.isSaving()).toBeFalse();
  });

  it('surfaces a compensation load error with retry', async () => {
    await setup();
    salary.getCurrentCompensation.and.returnValue(
      throwError(() => new Error('fail')),
    );
    component.loadCompensation();
    expect(component.compError()).toContain('Failed');
    expect(component.isLoadingComp()).toBeFalse();
  });
});
