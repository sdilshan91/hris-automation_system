import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { of, throwError, Subject } from 'rxjs';

import { MyPayslipsComponent } from './my-payslips.component';
import { MyPayslipService } from '../../services/my-payslip.service';
import {
  IMyPayslipDetail,
  IMyPayslipListItem,
  IMyPayslipPage,
} from '../../models/my-payslip.models';

describe('MyPayslipsComponent', () => {
  let fixture: ComponentFixture<MyPayslipsComponent>;
  let component: MyPayslipsComponent;
  let service: jasmine.SpyObj<MyPayslipService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const items: IMyPayslipListItem[] = [
    {
      payslipId: 'p-2025-12',
      payMonth: 12,
      payYear: 2025,
      grossEarnings: 48000,
      totalDeductions: 8000,
      netSalary: 40000,
      paidDays: 21,
      lopDays: 0,
      pdfAvailable: true,
    },
    {
      payslipId: 'p-2026-05',
      payMonth: 5,
      payYear: 2026,
      grossEarnings: 50000,
      totalDeductions: 8500,
      netSalary: 41500,
      paidDays: 22,
      lopDays: 0,
      pdfAvailable: true,
    },
    {
      payslipId: 'p-2026-01',
      payMonth: 1,
      payYear: 2026,
      grossEarnings: 50000,
      totalDeductions: 8500,
      netSalary: 41500,
      paidDays: 22,
      lopDays: 1,
      pdfAvailable: false,
    },
  ];

  function pageOf(rows: IMyPayslipListItem[]): IMyPayslipPage {
    return { items: rows, totalCount: rows.length, page: 1, pageSize: 12 };
  }

  const detail: IMyPayslipDetail = {
    payslipId: 'p-2026-05',
    payMonth: 5,
    payYear: 2026,
    employee: {
      name: 'Alex Doe',
      employeeNo: 'EMP001',
      department: 'People',
      designation: 'Engineer',
    },
    earnings: [
      { componentName: 'Basic Salary', amount: 25000, ytdAmount: 125000 },
      { componentName: 'HRA', amount: 25000, ytdAmount: 125000 },
    ],
    deductions: [{ componentName: 'EPF (Employee)', amount: 3000, ytdAmount: 15000 }],
    grossEarnings: 50000,
    totalDeductions: 8500,
    netSalary: 41500,
    workingDays: 22,
    paidDays: 22,
    lopDays: 0,
  };

  function setup(initial: IMyPayslipListItem[] = items): void {
    service = jasmine.createSpyObj<MyPayslipService>('MyPayslipService', [
      'listMyPayslips',
      'getMyPayslip',
      'downloadMyPayslipPdf',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);

    service.listMyPayslips.and.returnValue(of(pageOf(initial)));
    service.getMyPayslip.and.returnValue(of(detail));

    TestBed.configureTestingModule({
      imports: [MyPayslipsComponent],
      providers: [
        provideNoopAnimations(),
        { provide: MyPayslipService, useValue: service },
        { provide: ToastrService, useValue: toastr },
      ],
    });

    fixture = TestBed.createComponent(MyPayslipsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads payslips on init sorted most-recent-first (AC-1)', () => {
    setup();
    expect(service.listMyPayslips).toHaveBeenCalledWith(null);
    const ids = component.payslips().map((p) => p.payslipId);
    expect(ids).toEqual(['p-2026-05', 'p-2026-01', 'p-2025-12']);
    expect(component.isLoading()).toBeFalse();
  });

  it('derives the unique descending year filter tabs (FR-6)', () => {
    setup();
    expect(component.years()).toEqual([2026, 2025]);
  });

  it('renders a row per payslip with the pay-period label', () => {
    setup();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('May 2026');
    expect(text).toContain('December 2025');
  });

  it('shows the empty state when there are no payslips', () => {
    setup([]);
    const el = fixture.nativeElement.querySelector('[data-test="empty"]');
    expect(el).toBeTruthy();
  });

  it('shows an error toast when the list load fails', () => {
    service = jasmine.createSpyObj<MyPayslipService>('MyPayslipService', [
      'listMyPayslips',
      'getMyPayslip',
      'downloadMyPayslipPdf',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', ['success', 'error']);
    service.listMyPayslips.and.returnValue(throwError(() => new Error('boom')));

    TestBed.configureTestingModule({
      imports: [MyPayslipsComponent],
      providers: [
        provideNoopAnimations(),
        { provide: MyPayslipService, useValue: service },
        { provide: ToastrService, useValue: toastr },
      ],
    });
    fixture = TestBed.createComponent(MyPayslipsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(toastr.error).toHaveBeenCalled();
    expect(component.isLoading()).toBeFalse();
    expect(component.payslips()).toEqual([]);
  });

  it('selectYear reloads filtered by that year (FR-6)', () => {
    setup();
    service.listMyPayslips.calls.reset();
    component.selectYear(2025);
    expect(component.selectedYear()).toBe(2025);
    expect(service.listMyPayslips).toHaveBeenCalledWith(2025);
  });

  it('selectYear ignores re-selecting the active year', () => {
    setup();
    component.selectYear(2025);
    service.listMyPayslips.calls.reset();
    component.selectYear(2025);
    expect(service.listMyPayslips).not.toHaveBeenCalled();
  });

  it('toggle expands a row and lazy-loads the detail (AC-2)', () => {
    setup();
    component.toggle(items[1]);
    expect(component.expandedId()).toBe('p-2026-05');
    expect(service.getMyPayslip).toHaveBeenCalledWith('p-2026-05');
    expect(component.detail()).toEqual(detail);
    expect(component.detailLoading()).toBeFalse();
  });

  it('toggle collapses an already-open row without refetching', () => {
    setup();
    component.toggle(items[1]);
    service.getMyPayslip.calls.reset();
    component.toggle(items[1]);
    expect(component.expandedId()).toBeNull();
    expect(service.getMyPayslip).not.toHaveBeenCalled();
  });

  it('showYtd is true when detail lines carry YTD amounts (FR-7)', () => {
    setup();
    component.toggle(items[1]);
    expect(component.showYtd()).toBeTrue();
  });

  it('showYtd is false when no detail line carries a YTD amount', () => {
    setup();
    service.getMyPayslip.and.returnValue(
      of({
        ...detail,
        earnings: [{ componentName: 'Basic', amount: 25000 }],
        deductions: [{ componentName: 'EPF', amount: 3000 }],
      }),
    );
    component.toggle(items[1]);
    expect(component.showYtd()).toBeFalse();
  });

  it('sets detailError when the detail load fails', () => {
    setup();
    service.getMyPayslip.and.returnValue(throwError(() => new Error('nope')));
    component.toggle(items[1]);
    expect(component.detailError()).toBeTrue();
    expect(component.detailLoading()).toBeFalse();
  });

  it('renders the detail breakdown tables on expand', () => {
    setup();
    component.toggle(items[1]);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Basic Salary');
    expect(text).toContain('EPF (Employee)');
    expect(text).toContain('Earnings');
    expect(text).toContain('Deductions');
  });

  it('download streams the PDF and triggers an anchor click (FR-4/AC-3)', () => {
    setup();
    const blob = new Blob(['%PDF'], { type: 'application/pdf' });
    service.downloadMyPayslipPdf.and.returnValue(
      of(
        new HttpResponse<Blob>({
          body: blob,
          headers: undefined,
        }),
      ),
    );
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    spyOn(URL, 'revokeObjectURL');
    const click = jasmine.createSpy('click');
    const anchor = { href: '', download: '', click } as unknown as HTMLAnchorElement;
    spyOn(document, 'createElement').and.returnValue(anchor);

    component.download(items[1]);

    expect(service.downloadMyPayslipPdf).toHaveBeenCalledWith('p-2026-05');
    expect(click).toHaveBeenCalled();
    expect(anchor.download).toContain('May_2026');
    expect(component.downloadingId()).toBeNull();
  });

  it('download uses the Content-Disposition filename when present', () => {
    setup();
    const blob = new Blob(['%PDF'], { type: 'application/pdf' });
    service.downloadMyPayslipPdf.and.returnValue(
      of(
        new HttpResponse<Blob>({
          body: blob,
          headers: { get: () => 'attachment; filename="EMP001_5_2026.pdf"' } as never,
        }),
      ),
    );
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    spyOn(URL, 'revokeObjectURL');
    const click = jasmine.createSpy('click');
    const anchor = { href: '', download: '', click } as unknown as HTMLAnchorElement;
    spyOn(document, 'createElement').and.returnValue(anchor);

    component.download(items[1]);

    expect(anchor.download).toBe('EMP001_5_2026.pdf');
  });

  it('download shows an error toast on failure', () => {
    setup();
    service.downloadMyPayslipPdf.and.returnValue(throwError(() => new Error('x')));
    component.download(items[1]);
    expect(toastr.error).toHaveBeenCalled();
    expect(component.downloadingId()).toBeNull();
  });

  it('download is a no-op when the PDF is not available', () => {
    setup();
    component.download(items[2]); // pdfAvailable: false
    expect(service.downloadMyPayslipPdf).not.toHaveBeenCalled();
  });

  it('does not set stale detail if the row is collapsed before the response arrives', () => {
    setup();
    const subject = new Subject<IMyPayslipDetail>();
    service.getMyPayslip.and.returnValue(subject.asObservable());
    component.toggle(items[1]);
    component.expandedId.set(null); // user collapsed before response
    subject.next(detail);
    subject.complete();
    expect(component.detail()).toBeNull();
  });

  it('period formats month + year', () => {
    setup();
    expect(component.period(items[1])).toBe('May 2026');
  });
});
