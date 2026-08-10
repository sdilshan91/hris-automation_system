import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { BulkSalaryAssignmentComponent } from './bulk-salary-assignment.component';
import { PayrollService } from '../../services/payroll.service';
import { EmployeeSalaryService } from '../../services/employee-salary.service';
import { ISalaryStructure } from '../../models/payroll.models';
import { IBulkAssignmentResult } from '../../models/employee-salary.models';

describe('BulkSalaryAssignmentComponent', () => {
  let fixture: ComponentFixture<BulkSalaryAssignmentComponent>;
  let component: BulkSalaryAssignmentComponent;
  let payroll: jasmine.SpyObj<PayrollService>;
  let salary: jasmine.SpyObj<EmployeeSalaryService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const structure: ISalaryStructure = {
    id: 's-1',
    name: 'Full-Time',
    code: 'FT',
    description: null,
    effectiveFrom: '2026-01-01',
    isDefault: true,
    isActive: true,
  };
  const inactiveStructure: ISalaryStructure = {
    ...structure,
    id: 's-2',
    name: 'Legacy',
    isActive: false,
  };

  async function setup(): Promise<void> {
    payroll = jasmine.createSpyObj<PayrollService>('PayrollService', [
      'listStructures',
    ]);
    salary = jasmine.createSpyObj<EmployeeSalaryService>('EmployeeSalaryService', [
      'bulkAssign',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'warning',
      'error',
    ]);

    payroll.listStructures.and.returnValue(of([structure, inactiveStructure]));

    await TestBed.configureTestingModule({
      imports: [BulkSalaryAssignmentComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: PayrollService, useValue: payroll },
        { provide: EmployeeSalaryService, useValue: salary },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BulkSalaryAssignmentComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads only active structures on init (FR-7)', async () => {
    await setup();
    expect(component.activeStructures()).toEqual([structure]);
  });

  it('addRow / removeRow manage grid rows (FR-5)', async () => {
    await setup();
    component.addRow();
    component.addRow();
    expect(component.rows().length).toBe(2);
    component.removeRow(0);
    expect(component.rows().length).toBe(1);
  });

  it('updateRow coerces CTC to a number', async () => {
    await setup();
    component.addRow();
    component.updateRow(0, 'employeeId', 'emp-001');
    component.updateRow(0, 'annualCtc', '600000');
    expect(component.rows()[0].employeeId).toBe('emp-001');
    expect(component.rows()[0].annualCtc).toBe(600000);
  });

  it('parsePaste splits tab and comma separated rows (§8)', async () => {
    await setup();
    const rows = component.parsePaste('emp-001\t600000\nemp-002,720000\n\n');
    expect(rows.length).toBe(2);
    expect(rows[0]).toEqual(
      jasmine.objectContaining({ employeeId: 'emp-001', annualCtc: 600000 }),
    );
    expect(rows[1]).toEqual(
      jasmine.objectContaining({ employeeId: 'emp-002', annualCtc: 720000 }),
    );
  });

  it('parsePaste strips currency formatting from a tab-separated CTC cell', async () => {
    await setup();
    // Tab-separated paste from a spreadsheet, CTC formatted with a currency symbol.
    const rows = component.parsePaste('emp-003\t$1200000');
    expect(rows[0].employeeId).toBe('emp-003');
    expect(rows[0].annualCtc).toBe(1200000);
  });

  it('validRowCount counts only rows with an id and positive CTC (FR-5)', async () => {
    await setup();
    component.addRow();
    component.updateRow(0, 'employeeId', 'emp-001');
    component.updateRow(0, 'annualCtc', '600000');
    component.addRow();
    component.updateRow(1, 'employeeId', '');
    expect(component.validRowCount()).toBe(1);
  });

  it('canSubmit requires a structure, date and at least one valid row', async () => {
    await setup();
    expect(component.canSubmit()).toBeFalse();
    component.setupForm.patchValue({
      salaryStructureId: 's-1',
      effectiveFrom: '2026-07-01',
    });
    component.addRow();
    component.updateRow(0, 'employeeId', 'emp-001');
    component.updateRow(0, 'annualCtc', '600000');
    expect(component.canSubmit()).toBeTrue();
  });

  it('submit posts valid rows and reflects per-row results (AC-4)', async () => {
    await setup();
    const result: IBulkAssignmentResult = {
      totalRequested: 2,
      successCount: 1,
      failureCount: 1,
      results: [
        { employeeId: 'emp-001', success: true },
        { employeeId: 'emp-002', success: false, error: 'Inactive employee' },
      ],
    };
    salary.bulkAssign.and.returnValue(of(result));

    component.setupForm.patchValue({
      salaryStructureId: 's-1',
      effectiveFrom: '2026-07-01',
    });
    component.addRow();
    component.updateRow(0, 'employeeId', 'emp-001');
    component.updateRow(0, 'annualCtc', '600000');
    component.addRow();
    component.updateRow(1, 'employeeId', 'emp-002');
    component.updateRow(1, 'annualCtc', '720000');

    component.submit();

    const req = salary.bulkAssign.calls.mostRecent().args[0];
    expect(req.salaryStructureId).toBe('s-1');
    expect(req.employees.length).toBe(2);
    expect(component.result()).toEqual(result);
    expect(component.rows()[0].status).toBe('success');
    expect(component.rows()[1].status).toBe('failed');
    expect(toastr.warning).toHaveBeenCalled();
  });

  it('progress signals settle on completion (AC-4)', async () => {
    await setup();
    const result: IBulkAssignmentResult = {
      totalRequested: 2,
      successCount: 2,
      failureCount: 0,
      results: [
        { employeeId: 'emp-001', success: true },
        { employeeId: 'emp-002', success: true },
      ],
    };
    salary.bulkAssign.and.returnValue(of(result));
    component.setupForm.patchValue({
      salaryStructureId: 's-1',
      effectiveFrom: '2026-07-01',
    });
    component.addRow();
    component.updateRow(0, 'employeeId', 'emp-001');
    component.updateRow(0, 'annualCtc', '600000');
    component.addRow();
    component.updateRow(1, 'employeeId', 'emp-002');
    component.updateRow(1, 'annualCtc', '720000');

    component.submit();

    expect(component.progressTotal()).toBe(2);
    expect(component.progressDone()).toBe(2);
    expect(component.progressPercent()).toBe(100);
    expect(toastr.success).toHaveBeenCalled();
  });

  it('submit does nothing when the form is incomplete', async () => {
    await setup();
    component.submit();
    expect(salary.bulkAssign).not.toHaveBeenCalled();
  });

  it('shows an error toast when bulk assign fails', async () => {
    await setup();
    salary.bulkAssign.and.returnValue(throwError(() => new Error('boom')));
    component.setupForm.patchValue({
      salaryStructureId: 's-1',
      effectiveFrom: '2026-07-01',
    });
    component.addRow();
    component.updateRow(0, 'employeeId', 'emp-001');
    component.updateRow(0, 'annualCtc', '600000');
    component.submit();
    expect(toastr.error).toHaveBeenCalled();
    expect(component.isSubmitting()).toBeFalse();
  });
});
