import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError, Subject } from 'rxjs';
import { ToastrService } from 'ngx-toastr';
import { HttpResponse, HttpHeaders } from '@angular/common/http';

import { RecruitmentDashboardComponent } from './recruitment-dashboard.component';
import { RecruitmentDashboardService } from '../../services/dashboard.service';
import { IRecruitmentDashboard } from '../../models/dashboard.models';

/**
 * US-REC-009 dashboard spec. RecruitmentDashboardService mocked (no HttpClient).
 * Covers: KPI cards + funnel + source + trend + donut + activity render from data,
 * empty states, preset/custom range + drill-down reloads, and CSV export download.
 */
describe('RecruitmentDashboardComponent', () => {
  let fixture: ComponentFixture<RecruitmentDashboardComponent>;
  let component: RecruitmentDashboardComponent;
  let svc: jasmine.SpyObj<RecruitmentDashboardService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const data = (over: Partial<IRecruitmentDashboard> = {}): IRecruitmentDashboard => ({
    range: { from: '2026-05-16', to: '2026-06-15' },
    kpis: {
      openVacancies: 4,
      totalApplicants: 100,
      hires: 5,
      avgTimeToHireDays: 18,
      offerAcceptanceRate: 70,
      offersPending: 2,
      previousTotalApplicants: 80,
      previousHires: 7,
    },
    funnel: [
      { stage: 'Applied', count: 100, conversionRate: null },
      { stage: 'Screening', count: 60, conversionRate: 60 },
      { stage: 'Interview', count: 30, conversionRate: 50 },
    ],
    sources: [{ source: 'Referral', applicants: 20, hires: 5, conversionRate: 25 }],
    timeToHireTrend: [
      { label: 'Apr', avgDays: 22 },
      { label: 'May', avgDays: 18 },
    ],
    vacancyStatus: [
      { status: 'Open', count: 4 },
      { status: 'Closed', count: 1 },
    ],
    recentActivity: [
      { id: 'e1', type: 'Hired', description: 'Ada was hired', occurredAt: '2020-01-01T10:00:00Z' },
    ],
    ...over,
  });

  function setup(): void {
    svc.getFilterOptions.and.returnValue(of({ departments: [], vacancies: [] }));
    svc.getDashboard.and.returnValue(of(data()));
    fixture = TestBed.createComponent(RecruitmentDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(async () => {
    svc = jasmine.createSpyObj<RecruitmentDashboardService>('RecruitmentDashboardService', [
      'getDashboard',
      'getFilterOptions',
      'exportDashboard',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
      'warning',
      'info',
    ]);

    await TestBed.configureTestingModule({
      imports: [RecruitmentDashboardComponent],
      providers: [
        provideNoopAnimations(),
        { provide: RecruitmentDashboardService, useValue: svc },
        { provide: ToastrService, useValue: toastr },
      ],
    }).compileComponents();
  });

  it('loads the dashboard on init with the default 30d range', () => {
    setup();
    expect(svc.getDashboard).toHaveBeenCalledTimes(1);
    const arg = svc.getDashboard.calls.mostRecent().args[0];
    expect(arg.from).toBe(component.range().from);
    expect(arg.to).toBe(component.range().to);
  });

  it('renders KPI cards from the data (AC-1)', () => {
    setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-test="kpi-cards"]')).toBeTruthy();
    expect(el.querySelector('[data-test="kpi-openVacancies"]')!.textContent).toContain('4');
    expect(el.querySelector('[data-test="kpi-totalApplicants"]')!.textContent).toContain('100');
    expect(el.querySelector('[data-test="kpi-avgTimeToHire"]')!.textContent).toContain('18 days');
    expect(el.querySelector('[data-test="kpi-offerAcceptance"]')!.textContent).toContain('70');
  });

  it('shows a trend-up arrow for applicants (100 vs 80) and down for hires (5 vs 7)', () => {
    setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-test="kpi-totalApplicants"] [data-test="trend-up"]')).toBeTruthy();
    expect(el.querySelector('[data-test="kpi-hires"] [data-test="trend-down"]')).toBeTruthy();
  });

  it('renders the funnel bars with conversion labels (AC-3)', () => {
    setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('[data-test="funnel-bar"]').length).toBe(3);
    const conv = el.querySelectorAll('[data-test="funnel-conversion"]');
    expect(conv.length).toBe(2); // first stage has no conversion
    expect(conv[0].textContent).toContain('60%');
  });

  it('renders source effectiveness bars (AC-4)', () => {
    setup();
    const el: HTMLElement = fixture.nativeElement;
    const section = el.querySelector('[data-test="source-section"]')!;
    expect(el.querySelectorAll('[data-test="source-bar"]').length).toBe(1);
    expect(section.textContent).toContain('20 applicants');
    expect(section.textContent).toContain('25%');
  });

  it('renders the time-to-hire trend line (FR-4)', () => {
    setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-test="trend-chart"] polyline')).toBeTruthy();
    expect(component.trendLine().length).toBeGreaterThan(0);
  });

  it('renders the vacancy-status donut + total (FR-5)', () => {
    setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('[data-test="vacancy-donut"] path').length).toBe(2);
    expect(component.totalVacancies()).toBe(5);
  });

  it('renders the recent-activity feed with relative timestamps (FR-9)', () => {
    setup();
    const el: HTMLElement = fixture.nativeElement;
    const items = el.querySelectorAll('[data-test="activity-item"]');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('Ada was hired');
    expect(items[0].textContent).toContain('ago');
  });

  it('shows empty states when sections have no data', () => {
    svc.getFilterOptions.and.returnValue(of({ departments: [], vacancies: [] }));
    svc.getDashboard.and.returnValue(
      of(data({ funnel: [], sources: [], timeToHireTrend: [], vacancyStatus: [], recentActivity: [] })),
    );
    fixture = TestBed.createComponent(RecruitmentDashboardComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-test="funnel-empty"]')).toBeTruthy();
    expect(el.querySelector('[data-test="source-empty"]')).toBeTruthy();
    expect(el.querySelector('[data-test="trend-empty"]')).toBeTruthy();
    expect(el.querySelector('[data-test="vacancy-status-empty"]')).toBeTruthy();
    expect(el.querySelector('[data-test="activity-empty"]')).toBeTruthy();
  });

  it('reloads with a new range when a preset pill is selected (FR-6)', () => {
    setup();
    svc.getDashboard.calls.reset();
    component.selectPreset('7d');
    expect(component.preset()).toBe('7d');
    expect(svc.getDashboard).toHaveBeenCalledTimes(1);
  });

  it('does NOT reload while the custom range is invalid (from > to)', () => {
    setup();
    svc.getDashboard.calls.reset();
    component.selectPreset('custom');
    expect(svc.getDashboard).not.toHaveBeenCalled();
    // Pin `to` to a fixed value first, then reset: the custom range inherits the preset's `to` = *today*,
    // so comparing it against a hard-coded `from` flips valid/invalid as the calendar advances (this test
    // broke once today passed 2026-06-20). With `to` fixed at 2026-06-10 the out-of-order check is stable.
    component.setCustomTo('2026-06-10');
    svc.getDashboard.calls.reset();
    // An out-of-order range (from after to) must not trigger a reload.
    component.setCustomFrom('2026-06-20');
    expect(svc.getDashboard).not.toHaveBeenCalled();
    // Correcting the start makes the range valid and reloads.
    component.setCustomFrom('2026-06-01');
    expect(svc.getDashboard).toHaveBeenCalledTimes(1);
    const arg = svc.getDashboard.calls.mostRecent().args[0];
    expect(arg.from).toBe('2026-06-01');
    expect(arg.to).toBe('2026-06-10');
  });

  it('reloads with a departmentId drill-down param (FR-7)', () => {
    setup();
    svc.getDashboard.calls.reset();
    component.setDepartment('d1');
    const arg = svc.getDashboard.calls.mostRecent().args[0];
    expect(arg.departmentId).toBe('d1');
  });

  it('downloads a CSV export (FR-8)', () => {
    setup();
    const blob = new Blob(['a,b'], { type: 'text/csv' });
    const resp = new HttpResponse<Blob>({
      body: blob,
      headers: new HttpHeaders({ 'content-disposition': 'attachment; filename="r.csv"' }),
    });
    svc.exportDashboard.and.returnValue(of(resp));
    const dl = spyOn(RecruitmentDashboardService, 'downloadBlob');
    component.exportAs('csv');
    expect(svc.exportDashboard).toHaveBeenCalledWith(component['currentQuery'](), 'csv');
    expect(dl).toHaveBeenCalledWith(blob, 'r.csv');
    expect(toastr.success).toHaveBeenCalled();
    expect(component.isExporting()).toBeFalse();
  });

  it('toasts on export failure', () => {
    setup();
    svc.exportDashboard.and.returnValue(throwError(() => new Error('boom')));
    component.exportAs('pdf');
    expect(toastr.error).toHaveBeenCalled();
    expect(component.isExporting()).toBeFalse();
  });

  it('shows the empty dashboard state + toasts on load error', () => {
    svc.getFilterOptions.and.returnValue(of({ departments: [], vacancies: [] }));
    svc.getDashboard.and.returnValue(throwError(() => new Error('fail')));
    fixture = TestBed.createComponent(RecruitmentDashboardComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-test="dashboard-empty"]')).toBeTruthy();
    expect(toastr.error).toHaveBeenCalled();
  });

  it('tears down the in-flight subscription on destroy', () => {
    const stream = new Subject<IRecruitmentDashboard>();
    svc.getFilterOptions.and.returnValue(of({ departments: [], vacancies: [] }));
    svc.getDashboard.and.returnValue(stream.asObservable());
    fixture = TestBed.createComponent(RecruitmentDashboardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    fixture.destroy();
    stream.next(data());
    // No assertion error / no set after destroy — dashboard stays null.
    expect(component.dashboard()).toBeNull();
  });
});
