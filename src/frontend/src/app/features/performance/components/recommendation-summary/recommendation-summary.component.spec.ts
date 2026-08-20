import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { RecommendationSummaryComponent } from './recommendation-summary.component';
import { RecommendationService } from '../../services/recommendation.service';
import { IRecommendationSummary } from '../../models/recommendation.models';

function summary(
  overrides: Partial<IRecommendationSummary> = {},
): IRecommendationSummary {
  return {
    cycleId: 'c-1',
    cycleName: '2026 Annual',
    currency: 'USD',
    totalPromotions: 6,
    totalIncrements: 9,
    totalTrainingNominations: 3,
    bonusPoolAllocated: 250000,
    byDepartment: [
      {
        departmentId: 'd-1',
        departmentName: 'Engineering',
        promotionCount: 3,
        bonusCount: 5,
        incrementCount: 4,
        allocatedAmount: 100000,
      },
      {
        departmentId: 'd-2',
        departmentName: 'Sales',
        promotionCount: 1,
        bonusCount: 2,
        incrementCount: 1,
        allocatedAmount: 40000,
      },
    ],
    comparison: [
      { label: 'Promotions', current: 6, previous: 4 },
      { label: 'Bonus pool', current: 250000, previous: 300000 },
    ],
    ...overrides,
  };
}

describe('RecommendationSummaryComponent (US-PRF-010 AC-4)', () => {
  let fixture: ComponentFixture<RecommendationSummaryComponent>;
  let component: RecommendationSummaryComponent;
  let serviceSpy: jasmine.SpyObj<RecommendationService>;

  async function setup(
    data: IRecommendationSummary | 'error',
    cycleId = 'c-1',
  ): Promise<void> {
    serviceSpy = jasmine.createSpyObj<RecommendationService>(
      'RecommendationService',
      ['getSummary'],
    );
    serviceSpy.getSummary.and.returnValue(
      data === 'error' ? throwError(() => new Error('x')) : of(data),
    );

    await TestBed.configureTestingModule({
      imports: [RecommendationSummaryComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: RecommendationService, useValue: serviceSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: convertToParamMap({ cycleId }) },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RecommendationSummaryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads the summary for the cycleId query param', async () => {
    await setup(summary());
    expect(serviceSpy.getSummary).toHaveBeenCalledWith('c-1');
    expect(component.summary()?.totalPromotions).toBe(6);
  });

  it('maps aggregate stats into the stat cards (AC-4)', async () => {
    await setup(summary());
    const el = fixture.nativeElement as HTMLElement;
    expect(
      el.querySelector('[data-testid="stat-promotions"]')?.textContent,
    ).toContain('6');
    expect(
      el.querySelector('[data-testid="stat-bonus-pool"]')?.textContent,
    ).toContain('250,000');
    expect(
      el.querySelector('[data-testid="stat-increments"]')?.textContent,
    ).toContain('9');
  });

  it('renders a department allocation bar per department (CSS bars, no chart lib)', async () => {
    await setup(summary());
    const bars = (fixture.nativeElement as HTMLElement).querySelectorAll(
      '[data-testid="dept-bar"]',
    );
    expect(bars.length).toBe(2);
  });

  it('scales the busiest department bar to 100% (AC-4 distribution)', async () => {
    await setup(summary());
    // Engineering (100k) is the peak; Sales (40k) → 40%
    expect(component.barPercent(component.summary()!.byDepartment[0])).toBe(100);
    expect(component.barPercent(component.summary()!.byDepartment[1])).toBe(40);
  });

  it('computes the comparison delta vs the previous cycle (AC-4)', async () => {
    await setup(summary());
    const s = component.summary()!;
    expect(component.delta(s.comparison[0])).toBe(2); // 6 - 4
    expect(component.delta(s.comparison[1])).toBe(-50000); // 250k - 300k
    expect(component.absDelta(s.comparison[1])).toBe(50000);
  });

  it('shows an error state with retry on failure', async () => {
    await setup('error');
    expect(component.error()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });
});
