import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { LeaveReportsComponent } from './leave-reports.component';
import { LeaveReportsService } from '../../services/leave-reports.service';
import { ILeaveSummaryMetrics } from '../../models/leave-reports.models';

describe('LeaveReportsComponent (US-LV-012)', () => {
  let component: LeaveReportsComponent;
  let fixture: ComponentFixture<LeaveReportsComponent>;
  let reportsSpy: jasmine.SpyObj<LeaveReportsService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const metrics: ILeaveSummaryMetrics = {
    totalUtilizationPct: 42,
    topLeaveType: 'Annual Leave',
    absenteeismRatePct: 5,
  };

  function setup(metricsValue = metrics, fail = false): void {
    reportsSpy = jasmine.createSpyObj('LeaveReportsService', ['getSummaryMetrics']);
    reportsSpy.getSummaryMetrics.and.returnValue(
      fail ? throwError(() => new Error('x')) : of(metricsValue),
    );
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'info', 'warning']);

    TestBed.configureTestingModule({
      imports: [LeaveReportsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideRouter([]),
        provideToastr(),
        { provide: LeaveReportsService, useValue: reportsSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(LeaveReportsComponent);
    component = fixture.componentInstance;
  }

  it('renders a card per pre-built report (FR-1)', () => {
    setup();
    fixture.detectChanges();
    const cards = fixture.nativeElement.querySelectorAll('[data-testid^="report-card-"]');
    expect(cards.length).toBe(6);
  });

  it('loads + renders the summary widgets on init (AC cards)', () => {
    setup();
    fixture.detectChanges();
    expect(reportsSpy.getSummaryMetrics).toHaveBeenCalled();
    expect(component.metrics()).toEqual(metrics);
    const el = fixture.nativeElement;
    expect(el.querySelector('[data-testid="metric-utilization"]').textContent).toContain('42%');
    expect(el.querySelector('[data-testid="metric-top-type"]').textContent).toContain('Annual Leave');
    expect(el.querySelector('[data-testid="metric-absenteeism"]').textContent).toContain('5%');
  });

  it('toasts and renders no widgets when metrics fail', () => {
    setup(metrics, true);
    fixture.detectChanges();
    expect(component.metrics()).toBeNull();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[data-testid="summary-widgets"]')).toBeNull();
  });

  it('links each card to its detail route', () => {
    setup();
    fixture.detectChanges();
    const link: HTMLAnchorElement = fixture.nativeElement.querySelector(
      '[data-testid="report-card-utilization"]',
    );
    expect(link.getAttribute('href')).toContain('/leave/reports/utilization');
  });

  it('lastViewed returns "Not yet viewed" when no stamp exists', () => {
    setup();
    spyOn(localStorage, 'getItem').and.returnValue(null);
    expect(component.lastViewed('utilization')).toBe('Not yet viewed');
  });

  it('lastViewed reflects a stored stamp', () => {
    setup();
    spyOn(localStorage, 'getItem').and.returnValue('2026-06-01T00:00:00.000Z');
    expect(component.lastViewed('utilization')).toContain('Last viewed');
  });
});
