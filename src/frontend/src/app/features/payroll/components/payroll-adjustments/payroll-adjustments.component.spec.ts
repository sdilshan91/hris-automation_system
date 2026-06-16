import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpResponse, HttpHeaders } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { PayrollAdjustmentsComponent } from './payroll-adjustments.component';
import { AdjustmentService } from '../../services/adjustment.service';
import { IAdjustment } from '../../models/adjustment.models';

function adj(over: Partial<IAdjustment>): IAdjustment {
  return {
    id: 'a-1',
    employeeId: 'e-1',
    employeeName: 'Alex HR',
    employeeNo: 'EMP001',
    type: 'Bonus',
    amount: 10000,
    description: 'bonus',
    applicablePayMonth: 6,
    applicablePayYear: 2026,
    isTaxable: true,
    isRecurring: false,
    recurrenceEndMonth: null,
    recurrenceEndYear: null,
    status: 'Pending',
    hasDocument: false,
    createdAt: '2026-06-01T10:00:00Z',
    ...over,
  };
}

const rows: IAdjustment[] = [
  adj({ id: 'a-1', employeeName: 'Zoe Carter', amount: 500, status: 'Pending', applicablePayMonth: 6, applicablePayYear: 2026 }),
  adj({ id: 'a-2', employeeName: 'Alex HR', amount: 9000, status: 'Applied', applicablePayMonth: 5, applicablePayYear: 2026 }),
  adj({ id: 'a-3', employeeName: 'Bob Lin', amount: 200, status: 'Cancelled', applicablePayMonth: 7, applicablePayYear: 2026 }),
];

describe('PayrollAdjustmentsComponent', () => {
  let fixture: ComponentFixture<PayrollAdjustmentsComponent>;
  let component: PayrollAdjustmentsComponent;
  let service: jasmine.SpyObj<AdjustmentService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  beforeEach(async () => {
    service = jasmine.createSpyObj('AdjustmentService', [
      'listAdjustments',
      'cancelAdjustment',
      'downloadDocument',
    ]);
    toastr = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);
    service.listAdjustments.and.returnValue(of(rows));

    await TestBed.configureTestingModule({
      imports: [PayrollAdjustmentsComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AdjustmentService, useValue: service },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PayrollAdjustmentsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads adjustments on init', () => {
    expect(service.listAdjustments).toHaveBeenCalled();
    expect(component.rows().length).toBe(3);
    expect(component.loading()).toBeFalse();
  });

  it('shows an error toast when the load fails', () => {
    service.listAdjustments.and.returnValue(throwError(() => new Error('x')));
    component.reload();
    expect(toastr.error).toHaveBeenCalled();
    expect(component.loading()).toBeFalse();
  });

  describe('tabs (§8 Applied separated)', () => {
    it('Active tab excludes Applied rows', () => {
      expect(component.tab()).toBe('active');
      const ids = component.tabRows().map((r) => r.id);
      expect(ids).toContain('a-1');
      expect(ids).toContain('a-3');
      expect(ids).not.toContain('a-2');
    });

    it('Applied tab shows only Applied rows', () => {
      component.setTab('applied');
      const ids = component.tabRows().map((r) => r.id);
      expect(ids).toEqual(['a-2']);
    });

    it('switching tabs resets the status filter and reloads', () => {
      component.filterStatus.set('Pending');
      service.listAdjustments.calls.reset();
      component.setTab('applied');
      expect(component.filterStatus()).toBeNull();
      expect(service.listAdjustments).toHaveBeenCalled();
    });

    it('offers tab-appropriate status filter options', () => {
      expect(component.statusFilterOptions()).toEqual(['Pending', 'Cancelled']);
      component.setTab('applied');
      expect(component.statusFilterOptions()).toEqual(['Applied']);
    });
  });

  describe('filters (§8)', () => {
    it('passes status/type/period filters to the service', () => {
      service.listAdjustments.calls.reset();
      component.filterStatus.set('Pending');
      component.filterType.set('Bonus');
      component.filterPeriod.set('2026-06');
      component.reload();

      const args = service.listAdjustments.calls.mostRecent().args[0];
      expect(args).toEqual(
        jasmine.objectContaining({
          status: 'Pending',
          type: 'Bonus',
          period: '2026-06',
        }),
      );
    });

    it('narrows by the employee free-text filter client-side', () => {
      component.filterEmployee.set('zoe');
      component.reload();
      expect(component.rows().length).toBe(1);
      expect(component.rows()[0].employeeName).toBe('Zoe Carter');
    });
  });

  describe('sorting (§8)', () => {
    it('toggles direction when the same column is clicked twice', () => {
      component.sort('amount');
      expect(component.sortKey()).toBe('amount');
      expect(component.sortDir()).toBe('asc');
      component.sort('amount');
      expect(component.sortDir()).toBe('desc');
    });

    it('sorts the Active tab rows by amount ascending', () => {
      component.sort('amount');
      const amounts = component.sortedRows().map((r) => r.amount);
      expect(amounts).toEqual([200, 500]);
    });

    it('sorts by employee name', () => {
      component.sort('employee');
      const names = component.sortedRows().map((r) => r.employeeName);
      expect(names).toEqual(['Bob Lin', 'Zoe Carter']);
    });

    it('shows a direction icon only for the active sort column', () => {
      component.sort('amount');
      expect(component.sortIcon('amount')).toBe('↑');
      expect(component.sortIcon('period')).toBe('');
    });
  });

  describe('cancel (FR-6)', () => {
    it('cancels a pending adjustment and updates the row in place', () => {
      service.cancelAdjustment.and.returnValue(
        of(adj({ id: 'a-1', employeeName: 'Zoe Carter', status: 'Cancelled' })),
      );
      component.cancel(component.rows()[0]);

      expect(service.cancelAdjustment).toHaveBeenCalledWith('a-1');
      const updated = component.rows().find((r) => r.id === 'a-1');
      expect(updated!.status).toBe('Cancelled');
      expect(toastr.success).toHaveBeenCalled();
      expect(component.cancellingId()).toBeNull();
    });

    it('shows an error and clears the spinner when cancel fails', () => {
      service.cancelAdjustment.and.returnValue(
        throwError(() => new Error('x')),
      );
      component.cancel(component.rows()[0]);
      expect(toastr.error).toHaveBeenCalled();
      expect(component.cancellingId()).toBeNull();
    });
  });

  describe('document download (AC-3)', () => {
    it('downloads the document blob and triggers an anchor click', () => {
      const resp = new HttpResponse({
        body: new Blob(['%PDF'], { type: 'application/pdf' }),
        headers: new HttpHeaders({
          'Content-Disposition': 'attachment; filename="receipt.pdf"',
        }),
      });
      service.downloadDocument.and.returnValue(of(resp));
      const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');
      spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
      spyOn(URL, 'revokeObjectURL');

      component.downloadDocument(component.rows()[0]);

      expect(service.downloadDocument).toHaveBeenCalledWith('a-1');
      expect(clickSpy).toHaveBeenCalled();
    });

    it('errors when the document download fails', () => {
      service.downloadDocument.and.returnValue(
        throwError(() => new Error('x')),
      );
      component.downloadDocument(component.rows()[0]);
      expect(toastr.error).toHaveBeenCalled();
    });
  });

  describe('form host', () => {
    it('opens and closes the new-adjustment slide-over', () => {
      expect(component.formOpen()).toBeFalse();
      component.openForm();
      expect(component.formOpen()).toBeTrue();
      component.onSaved();
      expect(component.formOpen()).toBeFalse();
    });

    it('reloads after a save', () => {
      service.listAdjustments.calls.reset();
      component.onSaved();
      expect(service.listAdjustments).toHaveBeenCalled();
    });
  });

  it('renders a period label for a row', () => {
    expect(component.period(component.rows()[0])).toBe('Jun 2026');
  });
});
