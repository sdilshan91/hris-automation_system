import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { OvertimeReportComponent } from './overtime-report.component';
import { AttendanceService } from '../../services/attendance.service';
import {
  IOvertimeReportResult,
  IOvertimeReportRow,
} from '../../models/attendance.models';

/**
 * US-ATT-006 (AC-5) HR overtime-report spec. AttendanceService is mocked — no
 * HttpClient. Covers: report renders per-employee rows + totals, column sorting,
 * a month change reloads, and the client-side CSV export builds a download.
 */
describe('OvertimeReportComponent', () => {
  let fixture: ComponentFixture<OvertimeReportComponent>;
  let component: OvertimeReportComponent;
  let attendanceSpy: jasmine.SpyObj<AttendanceService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const rows: IOvertimeReportRow[] = [
    { employeeId: 'e1', employeeName: 'Ada Lovelace', approvedMinutes: 120, pendingMinutes: 60, rejectedMinutes: 0, recordCount: 3 },
    { employeeId: 'e2', employeeName: 'Alan Turing', approvedMinutes: 300, pendingMinutes: 0, rejectedMinutes: 30, recordCount: 4 },
  ];

  const result: IOvertimeReportResult = {
    month: '2026-06',
    items: rows,
    totals: { approvedMinutes: 420, pendingMinutes: 60, rejectedMinutes: 30, recordCount: 7 },
  };

  function setup(res: IOvertimeReportResult = result): void {
    attendanceSpy.getOvertimeReport.and.returnValue(of(res));
    fixture = TestBed.createComponent(OvertimeReportComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    attendanceSpy = jasmine.createSpyObj<AttendanceService>('AttendanceService', [
      'getOvertimeReport',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [OvertimeReportComponent],
      providers: [
        provideNoopAnimations(),
        { provide: AttendanceService, useValue: attendanceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  it('renders the per-employee rows and totals (AC-5)', () => {
    setup();
    expect(component.rows().length).toBe(2);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Ada Lovelace');
    expect(text).toContain('Alan Turing');
    expect(text).toContain('Total');
  });

  it('shows the empty state for a month with no overtime', () => {
    setup({ month: '2026-06', items: [], totals: { approvedMinutes: 0, pendingMinutes: 0, rejectedMinutes: 0, recordCount: 0 } });
    expect((fixture.nativeElement.textContent as string)).toContain('No overtime this month');
  });

  it('sorts by approved minutes descending by default', () => {
    setup();
    // Default sortKey approvedMinutes desc -> Alan (300) before Ada (120).
    expect(component.rows()[0].employeeName).toBe('Alan Turing');
  });

  it('toggles sort direction on repeated header clicks', () => {
    setup();
    component.sortBy('approvedMinutes'); // already the key -> flips to asc
    expect(component.rows()[0].employeeName).toBe('Ada Lovelace');
    expect(component.sortIndicator('approvedMinutes')).toBe('↑');
  });

  it('sorts by employee name ascending', () => {
    setup();
    component.sortBy('employeeName');
    expect(component.rows()[0].employeeName).toBe('Ada Lovelace');
  });

  it('reloads the report when the month changes', () => {
    setup();
    attendanceSpy.getOvertimeReport.calls.reset();
    component.onMonthChange('2026-05');
    expect(component.month()).toBe('2026-05');
    expect(attendanceSpy.getOvertimeReport).toHaveBeenCalledWith('2026-05');
  });

  it('exports the report rows to a CSV download (client-side)', () => {
    setup();
    const createSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    const revokeSpy = spyOn(URL, 'revokeObjectURL');
    const clickSpy = jasmine.createSpy('click');
    spyOn(document, 'createElement').and.returnValue({
      href: '',
      download: '',
      click: clickSpy,
    } as unknown as HTMLAnchorElement);

    component.exportCsv();

    expect(createSpy).toHaveBeenCalled();
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeSpy).toHaveBeenCalled();
    expect(toastrSpy.success).toHaveBeenCalled();
  });
});
