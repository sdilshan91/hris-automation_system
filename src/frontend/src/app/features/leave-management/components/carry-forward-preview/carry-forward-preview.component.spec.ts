import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { CarryForwardPreviewComponent } from './carry-forward-preview.component';
import { CarryForwardPreviewService } from '../../services/carry-forward-preview.service';
import { ICarryForwardPreviewRow } from '../../models/carry-forward-preview.models';

describe('CarryForwardPreviewComponent (US-LV-008)', () => {
  let component: CarryForwardPreviewComponent;
  let fixture: ComponentFixture<CarryForwardPreviewComponent>;
  let previewSpy: jasmine.SpyObj<CarryForwardPreviewService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const rows: ICarryForwardPreviewRow[] = [
    {
      employeeId: 'emp-1',
      employeeName: 'Alice Anderson',
      departmentName: 'Engineering',
      leaveTypeId: 'lt-1',
      leaveTypeName: 'Annual Leave',
      projectedCarryForward: 5,
      projectedForfeiture: 3,
    },
    {
      employeeId: 'emp-2',
      employeeName: 'Bob Brown',
      departmentName: 'Sales',
      leaveTypeId: 'lt-1',
      leaveTypeName: 'Annual Leave',
      projectedCarryForward: 0,
      projectedForfeiture: 4,
    },
    {
      employeeId: 'emp-3',
      employeeName: 'Carol Clark',
      departmentName: 'Engineering',
      leaveTypeId: 'lt-2',
      leaveTypeName: 'Sick Leave',
      projectedCarryForward: 2,
      projectedForfeiture: 0,
    },
  ];

  function setup(initial: ICarryForwardPreviewRow[] = rows): void {
    previewSpy = jasmine.createSpyObj('CarryForwardPreviewService', ['getPreview']);
    previewSpy.getPreview.and.returnValue(of(initial));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    TestBed.configureTestingModule({
      imports: [CarryForwardPreviewComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: CarryForwardPreviewService, useValue: previewSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(CarryForwardPreviewComponent);
    component = fixture.componentInstance;
  }

  it('loads the preview for the current year on init', () => {
    setup();
    fixture.detectChanges();
    expect(previewSpy.getPreview).toHaveBeenCalledWith(component.selectedYear());
    expect(component.allRows().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('renders a table row per preview row (AC-5)', () => {
    setup();
    fixture.detectChanges();
    const table = fixture.nativeElement.querySelector('[data-testid="preview-table"]');
    expect(table).toBeTruthy();
    const bodyRows = table.querySelectorAll('tbody tr');
    expect(bodyRows.length).toBe(3);
  });

  it('color-codes carry-forward in blue and forfeiture as gray strikethrough (§8)', () => {
    setup();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    const cf = el.querySelector('[data-testid="cf-amount"]');
    const ff = el.querySelector('[data-testid="ff-amount"]');
    expect(cf?.classList.contains('cf-amount')).toBeTrue();
    expect(cf?.textContent?.trim()).toBe('+5');
    expect(ff?.classList.contains('ff-amount')).toBeTrue();
    // strikethrough is applied via the ff-amount class (line-through)
    expect(ff?.textContent?.trim()).toBe('3');
  });

  it('computes summary totals across the filtered rows', () => {
    setup();
    fixture.detectChanges();
    expect(component.totals().carryForward).toBe(7); // 5 + 0 + 2
    expect(component.totals().forfeiture).toBe(7); // 3 + 4 + 0
    expect(component.totals().rows).toBe(3);
  });

  it('derives department + leave-type filter options from the rows', () => {
    setup();
    fixture.detectChanges();
    expect(component.departmentOptions()).toEqual(['Engineering', 'Sales']);
    expect(component.leaveTypeOptions().map((l) => l.name)).toEqual(['Annual Leave', 'Sick Leave']);
  });

  it('filters by department', () => {
    setup();
    fixture.detectChanges();
    component.filterDepartment.set('Engineering');
    fixture.detectChanges();
    expect(component.filteredRows().length).toBe(2);
    expect(component.filteredRows().every((r) => r.departmentName === 'Engineering')).toBeTrue();
  });

  it('filters by employee name (case-insensitive substring)', () => {
    setup();
    fixture.detectChanges();
    component.filterEmployee.set('bob');
    fixture.detectChanges();
    expect(component.filteredRows().length).toBe(1);
    expect(component.filteredRows()[0].employeeName).toBe('Bob Brown');
  });

  it('filters by leave type', () => {
    setup();
    fixture.detectChanges();
    component.filterLeaveTypeId.set('lt-2');
    fixture.detectChanges();
    expect(component.filteredRows().length).toBe(1);
    expect(component.filteredRows()[0].leaveTypeName).toBe('Sick Leave');
  });

  it('combines filters', () => {
    setup();
    fixture.detectChanges();
    component.filterDepartment.set('Engineering');
    component.filterLeaveTypeId.set('lt-1');
    fixture.detectChanges();
    expect(component.filteredRows().length).toBe(1);
    expect(component.filteredRows()[0].employeeName).toBe('Alice Anderson');
  });

  it('year selector reloads the preview for the chosen year', () => {
    setup();
    fixture.detectChanges();
    const current = component.selectedYear();
    const previous = current - 1;
    previewSpy.getPreview.calls.reset();

    component.selectYear(previous);
    expect(component.selectedYear()).toBe(previous);
    expect(previewSpy.getPreview).toHaveBeenCalledWith(previous);
  });

  it('does not reload when the same year is re-selected', () => {
    setup();
    fixture.detectChanges();
    previewSpy.getPreview.calls.reset();
    component.selectYear(component.selectedYear());
    expect(previewSpy.getPreview).not.toHaveBeenCalled();
  });

  it('renders the empty state when there are no rows', () => {
    setup([]);
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No projected carry-forward');
  });

  it('renders a "no rows match" empty state when filters exclude everything', () => {
    setup();
    fixture.detectChanges();
    component.filterEmployee.set('nobody-matches-this');
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No rows match the current filters');
  });

  it('shows an error toast and clears rows when the load fails', () => {
    setup();
    previewSpy.getPreview.and.returnValue(throwError(() => new Error('boom')));
    component.loadYear(component.selectedYear());
    expect(component.allRows().length).toBe(0);
    expect(component.isLoading()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalled();
  });
});
