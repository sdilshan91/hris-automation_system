import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { ExitInterviewAnalyticsComponent } from './exit-interview-analytics.component';
import { ExitInterviewService } from '../../services/exit-interview.service';
import { IExitInterviewAnalytics } from '../../models/exit-interview.models';

describe('ExitInterviewAnalyticsComponent', () => {
  let component: ExitInterviewAnalyticsComponent;
  let fixture: ComponentFixture<ExitInterviewAnalyticsComponent>;
  let serviceSpy: jasmine.SpyObj<ExitInterviewService>;

  const analytics: IExitInterviewAnalytics = {
    reasonDistribution: [
      { reason: 'Pay', count: 6 },
      { reason: 'Growth', count: 2 },
    ],
    averageRatingsPerCategory: [
      { category: 'Management', averageRating: 3.5 },
      { category: 'Work Environment', averageRating: 4.2 },
    ],
    trend: [
      { period: '2026-Q1', count: 3, averageRating: 3.0 },
      { period: '2026-Q2', count: 5, averageRating: 4.0 },
    ],
  };

  async function setup(
    data: IExitInterviewAnalytics | 'error' = analytics,
  ): Promise<void> {
    serviceSpy = jasmine.createSpyObj('ExitInterviewService', ['getAnalytics']);
    serviceSpy.getAnalytics.and.returnValue(
      data === 'error'
        ? throwError(() => new HttpErrorResponse({ status: 500 }))
        : of(data),
    );

    await TestBed.configureTestingModule({
      imports: [ExitInterviewAnalyticsComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: ExitInterviewService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ExitInterviewAnalyticsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads analytics on init with no date params', async () => {
    await setup();
    expect(component).toBeTruthy();
    expect(serviceSpy.getAnalytics).toHaveBeenCalledWith(undefined, undefined);
    expect(component.analytics()?.reasonDistribution.length).toBe(2);
  });

  it('derives pie slices and trend points from the loaded analytics', async () => {
    await setup();
    const slices = component.slices();
    expect(slices.length).toBe(2);
    expect(slices[0].percent).toBe(75);
    expect(component.trendPoints().split(' ').length).toBe(2);
  });

  it('renders one pie-slice path and one trend polyline in the DOM', async () => {
    await setup();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('[data-testid="pie-slice"]').length).toBe(2);
    expect(el.querySelectorAll('[data-testid="rating-bar"]').length).toBe(2);
    expect(el.querySelector('[data-testid="trend-line"]')).toBeTruthy();
  });

  it('passes the date range to the service when filters are applied', async () => {
    await setup();
    component.filterForm.setValue({ fromDate: '2026-01-01', toDate: '2026-06-30' });
    component.apply();
    expect(serviceSpy.getAnalytics).toHaveBeenCalledWith('2026-01-01', '2026-06-30');
  });

  it('clears the filter and reloads with no range', async () => {
    await setup();
    component.filterForm.setValue({ fromDate: '2026-01-01', toDate: '2026-06-30' });
    component.apply();
    component.clear();
    expect(component.filterForm.controls.fromDate.value).toBe('');
    expect(serviceSpy.getAnalytics).toHaveBeenCalledWith(undefined, undefined);
  });

  it('shows empty-state messages when there is no data', async () => {
    await setup({
      reasonDistribution: [],
      averageRatingsPerCategory: [],
      trend: [],
    });
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="reasons-empty"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="ratings-empty"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="trend-empty"]')).toBeTruthy();
    expect(component.slices()).toEqual([]);
  });

  it('surfaces a load error and recovers on retry', async () => {
    await setup('error');
    expect(component.error()).toContain('Unable to load');

    serviceSpy.getAnalytics.and.returnValue(of(analytics));
    component.load();
    expect(component.error()).toBeNull();
    expect(component.analytics()).toEqual(analytics);
  });
});
