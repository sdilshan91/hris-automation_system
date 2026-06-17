import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError, Subject } from 'rxjs';
import { DashboardComponent } from './dashboard.component';
import { DashboardService } from './dashboard.service';
import { IDashboardResponse, IDashboardWidget } from './dashboard.models';

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;
  let component: DashboardComponent;
  let serviceSpy: jasmine.SpyObj<DashboardService>;
  let router: Router;

  function widget(overrides: Partial<IDashboardWidget>): IDashboardWidget {
    return {
      widgetKey: 'w',
      label: 'Widget',
      value: null,
      unit: null,
      previousValue: null,
      trendDirection: null,
      trendPercentage: null,
      trendIsPositive: null,
      miniChart: null,
      items: null,
      linkUrl: null,
      linkFilters: null,
      ...overrides,
    };
  }

  const response: IDashboardResponse = {
    role: 'hr',
    greetingName: 'Sam',
    generatedAt: '2026-06-17T08:00:00Z',
    widgets: [
      widget({
        widgetKey: 'headcount',
        label: 'Total Headcount',
        value: 142,
        trendDirection: 'up',
        trendPercentage: 3,
        trendIsPositive: true,
        miniChart: {
          type: 'sparkline',
          series: [
            { label: 'Apr', value: 130 },
            { label: 'May', value: 138 },
            { label: 'Jun', value: 142 },
          ],
          max: null,
        },
      }),
      widget({
        widgetKey: 'turnover',
        label: 'Turnover Rate',
        value: 8,
        unit: '%',
        trendDirection: 'up',
        trendPercentage: 2,
        // up but BAD → must paint red, not green (colour by trendIsPositive).
        trendIsPositive: false,
      }),
      widget({
        widgetKey: 'pending-leave',
        label: 'Pending Leave Requests',
        value: 12,
        linkUrl: '/leave/requests',
        linkFilters: { status: 'pending' },
      }),
      widget({
        widgetKey: 'leave-balance',
        label: 'Leave Balance',
        miniChart: {
          type: 'donut',
          series: [
            { label: 'Used', value: 8 },
            { label: 'Remaining', value: 14 },
          ],
          max: 22,
        },
      }),
      widget({
        widgetKey: 'attendance',
        label: 'Attendance This Month',
        miniChart: {
          type: 'progress',
          series: [{ label: 'Present', value: 18 }],
          max: 22,
        },
      }),
      widget({
        widgetKey: 'quick-actions',
        label: 'Quick Actions',
        items: [
          {
            label: 'Approve leave',
            value: '3',
            linkUrl: '/leave/approvals',
            linkFilters: { status: 'pending' },
          },
          { label: 'Clock in', value: null, linkUrl: '/attendance', linkFilters: null },
        ],
      }),
    ],
  };

  function setup(): void {
    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    fixture.detectChanges();
  }

  beforeEach(() => {
    serviceSpy = jasmine.createSpyObj<DashboardService>('DashboardService', [
      'getWidgets',
      'refresh',
    ]);
    serviceSpy.getWidgets.and.returnValue(of(response));

    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        provideTranslateService(),
        { provide: DashboardService, useValue: serviceSpy },
      ],
    });
  });

  it('creates and loads widgets on init via getWidgets(false)', () => {
    setup();
    expect(component).toBeTruthy();
    expect(serviceSpy.getWidgets).toHaveBeenCalledWith(false);
    expect(component.widgets().length).toBe(6);
    expect(component.loading()).toBeFalse();
  });

  it('renders a grid of cards from the mocked widget array', () => {
    setup();
    const cards = fixture.nativeElement.querySelectorAll('section.dashboard-card');
    expect(cards.length).toBe(6);
    // KPI value rendered.
    expect(fixture.nativeElement.textContent).toContain('142');
    expect(fixture.nativeElement.textContent).toContain('Total Headcount');
  });

  it('shows skeleton loaders while the first load is in flight', () => {
    // Open subject keeps the stream pending so loading stays true.
    const pending = new Subject<IDashboardResponse>();
    serviceSpy.getWidgets.and.returnValue(pending.asObservable());
    setup();
    expect(component.loading()).toBeTrue();
    const skeletons = fixture.nativeElement.querySelectorAll('.animate-pulse');
    expect(skeletons.length).toBeGreaterThan(0);
    // Settle the stream so the fixture isn't left pending.
    pending.next(response);
    pending.complete();
  });

  it('colours the trend by trendIsPositive, NOT by direction', () => {
    setup();
    const headcount = component.widgets()[0]; // up + positive
    const turnover = component.widgets()[1]; // up + NOT positive
    expect(component.trendClass(headcount)).toBe('text-green-600');
    expect(component.trendClass(turnover)).toBe('text-red-600');
    // Both arrows point up despite opposite colours.
    expect(component.trendArrow(headcount)).toBe('↑');
    expect(component.trendArrow(turnover)).toBe('↑');
  });

  it('uses a neutral trend colour when trendIsPositive is null', () => {
    setup();
    const flat = widget({ trendDirection: 'flat', trendIsPositive: null });
    expect(component.trendClass(flat)).toBe('text-neutral-400');
    expect(component.trendArrow(flat)).toBe('→');
  });

  it('maps sparkline miniChart to a line chart block', () => {
    setup();
    const sparkline = component.widgets()[0];
    const chart = component.chartFor(sparkline);
    expect(chart.kind).toBe('line');
    expect(chart.series.length).toBe(1);
    expect(chart.series[0].points.length).toBe(3);
  });

  it('maps donut miniChart to a pie chart block', () => {
    setup();
    const donut = component.widgets()[3];
    const chart = component.chartFor(donut);
    expect(chart.kind).toBe('pie');
    expect(chart.series[0].points.map((p) => p.label)).toEqual([
      'Used',
      'Remaining',
    ]);
  });

  it('computes the progress percentage from value/max (clamped)', () => {
    setup();
    const progress = component.widgets()[4]; // 18 of 22
    expect(component.progressValue(progress)).toBe(18);
    expect(Math.round(component.progressPercent(progress))).toBe(82);
  });

  it('renders sparkline and donut charts and a progress bar by miniChart.type', () => {
    setup();
    const charts = fixture.nativeElement.querySelectorAll('app-report-chart');
    // headcount sparkline + leave-balance donut = 2 chart instances.
    expect(charts.length).toBe(2);
    const progressBar = fixture.nativeElement.querySelector('[role="progressbar"]');
    expect(progressBar).toBeTruthy();
  });

  it('navigates with linkFilters as query params on card click-through', () => {
    setup();
    const navSpy = spyOn(router, 'navigate').and.stub();
    const pendingLeave = component.widgets()[2];
    component.onCardClick(pendingLeave);
    expect(navSpy).toHaveBeenCalledWith(['/leave/requests'], {
      queryParams: { status: 'pending' },
    });
  });

  it('does not navigate when a card has no linkUrl', () => {
    setup();
    const navSpy = spyOn(router, 'navigate').and.stub();
    component.onCardClick(component.widgets()[0]); // headcount, no linkUrl
    expect(navSpy).not.toHaveBeenCalled();
  });

  it('renders quick-action items as router links carrying their filters', () => {
    setup();
    const links = fixture.nativeElement.querySelectorAll('a[href]');
    const hrefs = Array.from(links).map((a: any) => a.getAttribute('href'));
    expect(hrefs.some((h: string) => h.includes('/leave/approvals'))).toBeTrue();
    expect(hrefs.some((h: string) => h.includes('/attendance'))).toBeTrue();
  });

  it('re-fetches with refresh=true when the Refresh button is used', () => {
    setup();
    serviceSpy.getWidgets.calls.reset();
    component.refresh();
    expect(serviceSpy.getWidgets).toHaveBeenCalledWith(true);
  });

  it('builds a time-of-day greeting with the server-provided name', () => {
    setup();
    expect(component.greeting()).toMatch(/^Good (morning|afternoon|evening), Sam$/);
  });

  it('shows the error state when the load fails', () => {
    serviceSpy.getWidgets.and.returnValue(
      throwError(() => new Error('boom'))
    );
    setup();
    expect(component.error()).toBeTrue();
    expect(component.loading()).toBeFalse();
    expect(fixture.nativeElement.textContent.length).toBeGreaterThan(0);
  });
});
