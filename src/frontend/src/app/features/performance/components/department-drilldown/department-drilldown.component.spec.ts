import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { DepartmentDrilldownComponent } from './department-drilldown.component';
import { PerformanceDashboardService } from '../../services/performance-dashboard.service';
import { IDepartmentDrilldown } from '../../models/dashboard.models';

function makeDrilldown(
  overrides: Partial<IDepartmentDrilldown> = {},
): IDepartmentDrilldown {
  return {
    departmentId: 'd1',
    departmentName: 'Engineering',
    cycleLabel: '2026 Annual',
    averageScore: 83,
    scoreScaleMax: 100,
    employees: [
      { employeeId: 'e1', employeeName: 'Ada Lovelace', jobTitle: 'Lead', grade: 'B5', score: 92, trend: 'Up' },
      { employeeId: 'e2', employeeName: 'Alan Turing', jobTitle: 'Engineer', grade: 'B3', score: null, trend: 'New' },
    ],
    ...overrides,
  };
}

describe('DepartmentDrilldownComponent (US-PRF-007 FR-5)', () => {
  let fixture: ComponentFixture<DepartmentDrilldownComponent>;
  let component: DepartmentDrilldownComponent;
  let serviceSpy: jasmine.SpyObj<PerformanceDashboardService>;

  function setup(
    options: {
      drilldown?: IDepartmentDrilldown;
      cycleId?: string | null;
      fail?: boolean;
    } = {},
  ): void {
    const { drilldown = makeDrilldown(), cycleId = 'c1', fail = false } = options;

    serviceSpy = jasmine.createSpyObj<PerformanceDashboardService>(
      'PerformanceDashboardService',
      ['getDepartmentDrilldown'],
    );
    serviceSpy.getDepartmentDrilldown.and.returnValue(
      fail ? throwError(() => new Error('boom')) : of(drilldown),
    );

    TestBed.configureTestingModule({
      imports: [DepartmentDrilldownComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PerformanceDashboardService, useValue: serviceSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (k: string) => (k === 'departmentId' ? 'd1' : null) },
              queryParamMap: { get: (k: string) => (k === 'cycleId' ? cycleId : null) },
            },
          },
        },
      ],
    });

    fixture = TestBed.createComponent(DepartmentDrilldownComponent);
    component = fixture.componentInstance;
  }

  it('loads the drill-down for the route department + cycle (FR-5)', () => {
    setup();
    fixture.detectChanges();

    expect(serviceSpy.getDepartmentDrilldown).toHaveBeenCalledWith('d1', 'c1');
    expect(component.drilldown()?.departmentName).toBe('Engineering');
  });

  it('omits cycleId when none is in the query string', () => {
    setup({ cycleId: null });
    fixture.detectChanges();
    expect(serviceSpy.getDepartmentDrilldown).toHaveBeenCalledWith('d1', undefined);
  });

  it('renders the breadcrumb (Dashboard > Department) and employee rows', () => {
    setup();
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="crumb-dashboard"]')?.textContent).toContain('Dashboard');
    expect(el.querySelector('[data-testid="crumb-department"]')?.textContent).toContain('Engineering');
    const rows = el.querySelectorAll('[data-testid="employee-row"]');
    expect(rows.length).toBe(2);
  });

  it('maps an employee score to a bar width percent', () => {
    setup();
    fixture.detectChanges();
    expect(component.percent(92, 100)).toBe(92);
    expect(component.percent(null, 100)).toBe(0);
  });

  it('shows an error state on failure', () => {
    setup({ fail: true });
    fixture.detectChanges();
    expect(component.error()).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Unable to load');
  });
});
