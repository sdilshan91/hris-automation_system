import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { PayrollAnalyticsComponent } from './payroll-analytics.component';
import { PayrollReportService } from '../../services/payroll-report.service';
import { IDashboardAnalytics } from '../../models/payroll-report.models';

/**
 * US-PAY-009 (FR-5): analytics dashboard spec. PayrollReportService mocked (no
 * HttpClient). Covers: trend lines build (one per series), department bars sort desc +
 * scale, statutory stacked bars build from series/categories, and the error path.
 */
describe('PayrollAnalyticsComponent', () => {
  let fixture: ComponentFixture<PayrollAnalyticsComponent>;
  let component: PayrollAnalyticsComponent;
  let reportSpy: jasmine.SpyObj<PayrollReportService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const analytics: IDashboardAnalytics = {
    trend: {
      chartType: 'MonthlyTrend',
      points: [],
      categories: ['Apr 2026', 'May 2026'],
      series: [
        { name: 'Gross', points: [{ label: 'Apr 2026', value: 1000 }, { label: 'May 2026', value: 1200 }] },
        { name: 'Deductions', points: [{ label: 'Apr 2026', value: 300 }, { label: 'May 2026', value: 350 }] },
        { name: 'Net', points: [{ label: 'Apr 2026', value: 700 }, { label: 'May 2026', value: 850 }] },
      ],
    },
    departmentCosts: {
      chartType: 'DepartmentCostDistribution',
      points: [
        { label: 'Engineering', value: 400 },
        { label: 'People', value: 800 },
      ],
      categories: [],
      series: [],
    },
    statutory: {
      chartType: 'StatutoryBreakdown',
      points: [],
      categories: ['Apr 2026', 'May 2026'],
      series: [
        { name: 'Income Tax', points: [{ label: 'Apr 2026', value: 100 }, { label: 'May 2026', value: 120 }] },
        { name: 'EPF', points: [{ label: 'Apr 2026', value: 50 }, { label: 'May 2026', value: 60 }] },
        { name: 'ETF', points: [{ label: 'Apr 2026', value: 0 }, { label: 'May 2026', value: 20 }] },
      ],
    },
  };

  function setup(value: IDashboardAnalytics | 'error' = analytics): void {
    reportSpy.getDashboardAnalytics.and.returnValue(
      value === 'error' ? throwError(() => new Error('fail')) : of(value),
    );
    fixture = TestBed.createComponent(PayrollAnalyticsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => {
    reportSpy = jasmine.createSpyObj<PayrollReportService>('PayrollReportService', [
      'getDashboardAnalytics',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', ['error', 'success']);

    TestBed.configureTestingModule({
      imports: [PayrollAnalyticsComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: PayrollReportService, useValue: reportSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });
  });

  it('loads analytics on init and clears the loading flag', () => {
    setup();
    expect(reportSpy.getDashboardAnalytics).toHaveBeenCalled();
    expect(component.isLoading()).toBeFalse();
    expect(component.analytics()).toEqual(analytics);
  });

  it('builds one trend line per series with polyline points', () => {
    setup();
    const lines = component.trendLines();
    expect(lines.map((l) => l.key)).toEqual(['Gross', 'Deductions', 'Net']);
    expect(lines[0].points).toContain(' '); // 2 points → space-separated
    expect(lines[0].last).toBe(1200); // last gross
  });

  it('renders the trend chart in the DOM', () => {
    setup();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="trend-chart"]')).toBeTruthy();
    expect(el.querySelectorAll('[data-test="trend-chart"] polyline').length).toBe(3);
  });

  it('sorts department costs descending and scales the widest bar to 100%', () => {
    setup();
    const sorted = component.sortedDepartments();
    expect(sorted.map((d) => d.departmentName)).toEqual(['People', 'Engineering']);
    expect(component.deptBarWidth(800)).toBe(100); // max
    expect(component.deptBarWidth(400)).toBe(50);
  });

  it('builds statutory stacked bars (one per category) with a distinct legend', () => {
    setup();
    const legend = component.statutoryLegend();
    expect(legend.map((l) => l.name)).toEqual(['Income Tax', 'EPF', 'ETF']);

    const bars = component.statutoryBars();
    expect(bars.length).toBe(2);
    // First month (Apr) ETF is 0 → filtered out → 2 segments.
    expect(bars[0].segments.length).toBe(2);
    // Segment fractions within a month sum to ~1.
    const sum = bars[1].segments.reduce((s, seg) => s + seg.frac, 0);
    expect(sum).toBeCloseTo(1, 5);
  });

  it('shows the error empty-state and toasts on failure', () => {
    setup('error');
    expect(component.analytics()).toBeNull();
    expect(toastrSpy.error).toHaveBeenCalled();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-test="analytics-empty"]')).toBeTruthy();
  });
});
