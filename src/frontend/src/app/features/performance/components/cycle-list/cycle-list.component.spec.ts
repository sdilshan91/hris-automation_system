import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { provideNoopAnimations } from '@angular/platform-browser/animations';

import { CycleListComponent } from './cycle-list.component';
import { CycleService } from '../../services/cycle.service';
import { ICycleSummary } from '../../models/cycle.models';

function makeCycles(): ICycleSummary[] {
  return [
    {
      id: 'cyc-1',
      name: '2026 Annual',
      type: 'Annual',
      status: 'Active',
      startDate: '2026-01-01',
      endDate: '2026-12-31',
      participantCount: 120,
    },
    {
      id: 'cyc-2',
      name: 'Q1 KPI',
      type: 'Quarterly',
      status: 'Draft',
      startDate: '2026-01-01',
      endDate: '2026-03-31',
      participantCount: 40,
    },
    {
      id: 'cyc-3',
      name: '2025 Annual',
      type: 'Annual',
      status: 'Completed',
      startDate: '2025-01-01',
      endDate: '2025-12-31',
      participantCount: 110,
    },
  ];
}

describe('CycleListComponent (FR-7 / §8)', () => {
  let fixture: ComponentFixture<CycleListComponent>;
  let component: CycleListComponent;
  let serviceSpy: jasmine.SpyObj<CycleService>;

  async function setup(
    result = of(makeCycles()) as ReturnType<CycleService['list']>,
  ): Promise<void> {
    serviceSpy = jasmine.createSpyObj<CycleService>('CycleService', ['list']);
    serviceSpy.list.and.returnValue(result);

    await TestBed.configureTestingModule({
      imports: [CycleListComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: CycleService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CycleListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function badges(): HTMLElement[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="status-badge"]'),
    ) as HTMLElement[];
  }

  it('renders a card per cycle with a status badge', async () => {
    await setup();
    const cards = fixture.nativeElement.querySelectorAll(
      '[data-testid="cycle-card"]',
    );
    expect(cards.length).toBe(3);
    expect(badges().length).toBe(3);
  });

  it('color-codes status badges per §8 (Active green, Draft gray, Completed blue)', async () => {
    await setup();
    const classMap = badges().map((b) => b.className);
    // first cycle is Active → emerald, second Draft → neutral, third Completed → sky
    expect(classMap[0]).toContain('emerald');
    expect(classMap[1]).toContain('neutral');
    expect(classMap[2]).toContain('sky');
  });

  it('filters cycles by status', async () => {
    await setup();
    component.statusFilter.set('Draft');
    fixture.detectChanges();
    const cards = fixture.nativeElement.querySelectorAll(
      '[data-testid="cycle-card"]',
    );
    expect(cards.length).toBe(1);
    expect(component.visibleCycles()[0].id).toBe('cyc-2');
  });

  it('shows an empty state when there are no cycles', async () => {
    await setup(of([]) as ReturnType<CycleService['list']>);
    expect(fixture.nativeElement.textContent).toContain('No cycles yet');
  });

  it('shows an error state with retry when the load fails', async () => {
    await setup(
      throwError(() => new Error('boom')) as ReturnType<CycleService['list']>,
    );
    expect(component.error()).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Retry');
  });
});
