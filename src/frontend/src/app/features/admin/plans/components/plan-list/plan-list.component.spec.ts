import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { PlanListComponent } from './plan-list.component';
import { environment } from '../../../../../../environments/environment';
import { IPlanSummary } from '../../models/plan.models';

describe('PlanListComponent', () => {
  let fixture: ComponentFixture<PlanListComponent>;
  let component: PlanListComponent;
  let httpMock: HttpTestingController;

  const listUrl = `${environment.apiBaseUrl}/system/plans`;

  const plans: IPlanSummary[] = [
    {
      id: 'p-1',
      code: 'starter',
      name: 'Starter',
      priceMonthly: 19,
      priceYearly: 190,
      currency: 'USD',
      isPublic: true,
      isActive: true,
      activeTenantCount: 3,
    },
    {
      id: 'p-2',
      code: 'growth',
      name: 'Growth',
      priceMonthly: 49,
      priceYearly: 490,
      currency: 'USD',
      isPublic: true,
      isActive: false,
      activeTenantCount: 25,
    },
    {
      id: 'p-3',
      code: 'enterprise',
      name: 'Enterprise',
      priceMonthly: 0,
      priceYearly: 0,
      currency: 'USD',
      isPublic: false,
      isActive: true,
      activeTenantCount: 10,
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PlanListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideTranslateService(),
      ],
    });
    fixture = TestBed.createComponent(PlanListComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flush(rows: IPlanSummary[] = plans): void {
    fixture.detectChanges();
    httpMock.expectOne(listUrl).flush(rows);
    fixture.detectChanges();
  }

  it('renders a card per plan (AC-1)', () => {
    flush();
    const cards = fixture.nativeElement.querySelectorAll('[data-testid="plan-card"]');
    expect(cards.length).toBe(3);
  });

  it('shows active/archived and public/private badges (AC-1)', () => {
    flush();
    const statuses = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="plan-status-badge"]'),
    ).map((el) => (el as HTMLElement).textContent?.trim());
    // Default sort is by name asc: Enterprise, Growth, Starter.
    // Growth is archived; the others active.
    expect(statuses.some((s) => s?.includes('archived'))).toBeTrue();
    expect(statuses.some((s) => s?.includes('active'))).toBeTrue();

    const visibility = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="plan-visibility-badge"]'),
    ).map((el) => (el as HTMLElement).textContent?.trim());
    expect(visibility.some((v) => v?.includes('private'))).toBeTrue();
    expect(visibility.some((v) => v?.includes('public'))).toBeTrue();
  });

  it('defaults to name ascending', () => {
    flush();
    expect(component.sortedPlans().map((p) => p.name)).toEqual([
      'Enterprise',
      'Growth',
      'Starter',
    ]);
  });

  it('sorts by tenant count descending when selected (AC-1)', () => {
    flush();
    component.setSort('tenants');
    fixture.detectChanges();
    expect(component.sortedPlans().map((p) => p.activeTenantCount)).toEqual([
      25, 10, 3,
    ]);
  });

  it('flips direction when the same sort key is re-selected', () => {
    flush();
    component.setSort('price'); // desc first
    expect(component.sortedPlans()[0].priceMonthly).toBe(49);
    component.setSort('price'); // asc
    expect(component.sortedPlans()[0].priceMonthly).toBe(0);
  });

  it('shows an empty state when there are no plans', () => {
    flush([]);
    const grid = fixture.nativeElement.querySelector('[data-testid="plan-grid"]');
    expect(grid).toBeNull();
    expect(component.plans().length).toBe(0);
  });

  it('shows an error message on load failure', () => {
    fixture.detectChanges();
    httpMock.expectOne(listUrl).flush('boom', {
      status: 500,
      statusText: 'Server Error',
    });
    fixture.detectChanges();
    expect(component.errorMessage()).toBe('Failed to load plans.');
  });
});
