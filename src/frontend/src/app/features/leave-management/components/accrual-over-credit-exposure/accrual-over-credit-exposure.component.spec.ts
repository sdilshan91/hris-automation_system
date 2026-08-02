import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { AccrualOverCreditExposureComponent } from './accrual-over-credit-exposure.component';
import { AccrualOverCreditExposureService } from '../../services/accrual-over-credit-exposure.service';
import {
  IAccrualOverCreditExposureReport,
  IAccrualOverCreditExposureRow,
} from '../../models/accrual-over-credit-exposure.models';

describe('AccrualOverCreditExposureComponent (BUG-291)', () => {
  let component: AccrualOverCreditExposureComponent;
  let fixture: ComponentFixture<AccrualOverCreditExposureComponent>;
  let serviceSpy: jasmine.SpyObj<AccrualOverCreditExposureService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  function row(over: Partial<IAccrualOverCreditExposureRow> & { overCreditedDays: number }): IAccrualOverCreditExposureRow {
    return {
      employeeId: 'emp-' + over.overCreditedDays,
      employeeNo: 'E-' + over.overCreditedDays,
      employeeName: 'Emp ' + over.overCreditedDays,
      leaveTypeId: 'lt-1',
      leaveTypeName: 'Annual Leave',
      leaveYear: 2026,
      accrualFrequency: 'Monthly',
      creditedDays: 24,
      shouldHaveAccruedDays: 24 - over.overCreditedDays,
      isEmployeeActive: true,
      ...over,
    };
  }

  function report(rows: IAccrualOverCreditExposureRow[]): IAccrualOverCreditExposureReport {
    return { asOfDate: '2026-07-30', leaveYear: 2026, rows };
  }

  function setup(getReturn = of(report([row({ overCreditedDays: 10 })]))): void {
    serviceSpy = jasmine.createSpyObj('AccrualOverCreditExposureService', [
      'getExposure',
      'exportExposure',
    ]);
    serviceSpy.getExposure.and.returnValue(getReturn);
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning', 'info']);

    TestBed.configureTestingModule({
      imports: [AccrualOverCreditExposureComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AccrualOverCreditExposureService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(AccrualOverCreditExposureComponent);
    component = fixture.componentInstance;
  }

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('loads the exposure on init and renders a row', () => {
    setup();
    fixture.detectChanges();

    expect(serviceSpy.getExposure).toHaveBeenCalledWith(component.asOfDate());
    expect(text()).toContain('Emp 10');
  });

  it('sorts rows by over-credited days descending', () => {
    setup(of(report([
      row({ overCreditedDays: 3 }),
      row({ overCreditedDays: 10 }),
      row({ overCreditedDays: 7 }),
    ])));
    fixture.detectChanges();

    expect(component.sortedRows().map((r) => r.overCreditedDays)).toEqual([10, 7, 3]);

    const figures = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.over-credit-figure'),
    ).map((el) => el.textContent?.trim());
    // First rendered figure is the largest over-credit.
    expect(figures[0]).toContain('10');
  });

  it('renders the over-credited figure bound to overCreditedDays (money arm)', () => {
    // creditedDays=24, shouldHaveAccruedDays=14, overCreditedDays=10.
    // Binding the cell to the wrong field (e.g. creditedDays) would render 24 and fail this.
    setup(of(report([
      row({ overCreditedDays: 10, creditedDays: 24, shouldHaveAccruedDays: 14 }),
    ])));
    fixture.detectChanges();

    const figure = (fixture.nativeElement as HTMLElement).querySelector('.over-credit-figure');
    expect(figure?.textContent?.trim()).toBe('+10');
  });

  it('shows the empty state (distinct from loading) when there are no affected employees', () => {
    setup(of(report([])));
    fixture.detectChanges();

    expect(component.status()).toBe('loaded');
    expect(text()).toContain('No affected employees');
    // Not the loading skeleton.
    expect((fixture.nativeElement as HTMLElement).querySelector('.skeleton-line')).toBeNull();
  });

  it('surfaces an error state when the load fails', () => {
    setup(throwError(() => ({ error: { message: 'boom' } })));
    fixture.detectChanges();

    expect(component.status()).toBe('error');
    expect(text()).toContain("Couldn't load the exposure");
    expect(text()).not.toContain('No affected employees');
  });

  it('re-queries when the as-of date changes', () => {
    setup();
    fixture.detectChanges();
    serviceSpy.getExposure.calls.reset();

    component.onDateChange('2026-06-01');

    expect(component.asOfDate()).toBe('2026-06-01');
    expect(serviceSpy.getExposure).toHaveBeenCalledWith('2026-06-01');
  });

  it('CSV and Excel buttons call export with the right format', () => {
    setup();
    serviceSpy.exportExposure.and.returnValue(of({ blob: new Blob(['x']), filename: 'f.csv' }));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    spyOn(URL, 'revokeObjectURL');
    const anchor = { href: '', download: '', click: jasmine.createSpy('click') };
    const realCreate = document.createElement.bind(document);
    spyOn(document, 'createElement').and.callFake((tag: string) =>
      tag === 'a' ? (anchor as unknown as HTMLAnchorElement) : realCreate(tag),
    );
    fixture.detectChanges();

    component.download('csv');
    expect(serviceSpy.exportExposure).toHaveBeenCalledWith(component.asOfDate(), 'csv');

    component.download('xlsx');
    expect(serviceSpy.exportExposure).toHaveBeenCalledWith(component.asOfDate(), 'xlsx');
    expect(anchor.click).toHaveBeenCalled();
  });

  it('states plainly that balances are not auto-corrected', () => {
    setup();
    fixture.detectChanges();
    expect(text()).toContain('NOT corrected automatically');
  });
});
