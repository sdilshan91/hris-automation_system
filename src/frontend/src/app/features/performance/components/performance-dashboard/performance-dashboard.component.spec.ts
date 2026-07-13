import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { HttpResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';

import { PerformanceDashboardComponent } from './performance-dashboard.component';
import { PerformanceDashboardService } from '../../services/performance-dashboard.service';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  IDashboardOverview,
  ITrendResponse,
} from '../../models/dashboard.models';

function makeOverview(
  overrides: Partial<IDashboardOverview> = {},
): IDashboardOverview {
  return {
    scope: 'Organization',
    scopeLabel: 'Acme Corp',
    filterOptions: {
      cycles: [{ id: 'c1', label: '2026 Annual' }],
      departments: [{ id: 'd1', label: 'Engineering' }],
      grades: [],
      employmentTypes: [],
      locations: [],
    },
    completionRate: 72,
    averageScore: 81,
    scoreScaleMax: 100,
    ratedCount: 40,
    histogram: [
      { rangeStart: 0, rangeEnd: 50, label: '0–50', count: 2 },
      { rangeStart: 50, rangeEnd: 100, label: '50–100', count: 8 },
    ],
    departmentAverages: [
      { departmentId: 'd1', departmentName: 'Engineering', averageScore: 85, headcount: 12 },
    ],
    topPerformers: [
      { employeeId: 'e1', employeeName: 'Top Person', departmentName: 'Eng', jobTitle: 'Dev', score: 95, trend: 'Up' },
    ],
    bottomPerformers: [
      { employeeId: 'e2', employeeName: 'Low Person', departmentName: 'Eng', jobTitle: 'Dev', score: 45, trend: 'Down' },
    ],
    teamRanking: [],
    cycleProgress: {
      totalParticipants: 50,
      goalSettingComplete: 50,
      selfAssessmentComplete: 45,
      managerReviewComplete: 40,
      signedOff: 36,
    },
    availableExportFormats: ['Csv', 'Excel'],
    ...overrides,
  };
}

describe('PerformanceDashboardComponent (US-PRF-007)', () => {
  let fixture: ComponentFixture<PerformanceDashboardComponent>;
  let component: PerformanceDashboardComponent;
  let serviceSpy: jasmine.SpyObj<PerformanceDashboardService>;
  let authSpy: jasmine.SpyObj<AuthService>;
  let router: Router;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  function setup(
    options: {
      overview?: IDashboardOverview;
      hasScope?: boolean;
    } = {},
  ): void {
    const { overview = makeOverview(), hasScope = true } = options;

    serviceSpy = jasmine.createSpyObj<PerformanceDashboardService>(
      'PerformanceDashboardService',
      ['getOverview', 'getTrend', 'getDepartmentDrilldown', 'export'],
    );
    serviceSpy.getOverview.and.returnValue(of(overview));
    serviceSpy.getTrend.and.returnValue(
      of({ scoreScaleMax: 100, series: [] } as ITrendResponse),
    );

    authSpy = jasmine.createSpyObj<AuthService>('AuthService', ['hasAnyPermission']);
    authSpy.hasAnyPermission.and.returnValue(hasScope);

    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', ['error']);

    TestBed.configureTestingModule({
      imports: [PerformanceDashboardComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PerformanceDashboardService, useValue: serviceSpy },
        { provide: AuthService, useValue: authSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(PerformanceDashboardComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
  }

  it('loads the overview on init and renders the org scope label', () => {
    setup();
    fixture.detectChanges();

    expect(serviceSpy.getOverview).toHaveBeenCalledTimes(1);
    expect(component.overview()?.scope).toBe('Organization');
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Organization view');
  });

  it('redirects a user with no read scope to /my-review (BR-1/AC-5)', () => {
    setup({ hasScope: false });
    const navSpy = spyOn(router, 'navigate');
    fixture.detectChanges();

    expect(navSpy).toHaveBeenCalledWith(['/my-review']);
    expect(serviceSpy.getOverview).not.toHaveBeenCalled();
  });

  it('HR scope shows top + bottom performers, NOT team ranking (BR-3)', () => {
    setup({ overview: makeOverview({ scope: 'Organization' }) });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="widget-top"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="widget-bottom"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="widget-team-ranking"]')).toBeFalsy();
  });

  it('manager scope shows team ranking, NOT org top/bottom (BR-3/AC-5)', () => {
    setup({
      overview: makeOverview({
        scope: 'Team',
        scopeLabel: "Jane's team",
        topPerformers: [],
        bottomPerformers: [],
        teamRanking: [
          { employeeId: 'e3', employeeName: 'Member A', departmentName: 'Eng', jobTitle: null, score: 80, trend: 'Flat' },
        ],
      }),
    });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="widget-team-ranking"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="widget-top"]')).toBeFalsy();
    expect(el.querySelector('[data-testid="widget-bottom"]')).toBeFalsy();
    expect(el.textContent).toContain('Team view');
  });

  it('maps histogram buckets to bars scaled against the peak', () => {
    setup();
    fixture.detectChanges();

    const bars = fixture.nativeElement.querySelectorAll('[data-testid="histogram-bar"]');
    expect(bars.length).toBe(2);
    // tallest bucket (count 8) = 100%, shorter (count 2) = 25%
    expect(component.barHeight(component.overview()!.histogram[1])).toBe(100);
    expect(component.barHeight(component.overview()!.histogram[0])).toBe(25);
  });

  it('maps a department average to a bar width percent (FR-2)', () => {
    setup();
    fixture.detectChanges();
    // 85 on a 100 scale → 85%
    expect(component.deptPercent(85, 100)).toBe(85);
    const deptBar = fixture.nativeElement.querySelector('[data-testid="dept-bar-d1"]');
    expect(deptBar).toBeTruthy();
  });

  it('toggling a filter re-fetches the overview with the selection (AC-2)', () => {
    setup();
    fixture.detectChanges();
    expect(serviceSpy.getOverview).toHaveBeenCalledTimes(1);

    component.toggleFilter('departmentIds', 'd1');

    expect(component.filters().departmentIds).toEqual(['d1']);
    expect(serviceSpy.getOverview).toHaveBeenCalledTimes(2);
    expect(serviceSpy.getOverview.calls.mostRecent().args[0].departmentIds).toEqual(['d1']);
    expect(component.filterBadge()).toBe(1);
  });

  it('clearFilters resets the selection and refetches', () => {
    setup();
    fixture.detectChanges();
    component.toggleFilter('cycleIds', 'c1');
    expect(component.filters().cycleIds).toEqual(['c1']);

    component.clearFilters();
    expect(component.filters().cycleIds).toEqual([]);
    expect(component.filterBadge()).toBe(0);
    expect(serviceSpy.getOverview).toHaveBeenCalledTimes(3); // init + toggle + clear
  });

  it('opening the trend view loads and maps the series (AC-3/FR-7)', () => {
    const trend: ITrendResponse = {
      scoreScaleMax: 100,
      series: [
        {
          key: null,
          label: 'Organization',
          points: [
            { cycleId: 'c1', cycleLabel: '2025', averageScore: 70 },
            { cycleId: 'c2', cycleLabel: '2026', averageScore: 80 },
          ],
        },
      ],
    };
    setup();
    serviceSpy.getTrend.and.returnValue(of(trend));
    fixture.detectChanges();

    component.toggleTrend();
    fixture.detectChanges();

    expect(serviceSpy.getTrend).toHaveBeenCalled();
    expect(component.trendHasData()).toBeTrue();
    const points = component.polyline(trend.series[0], 100);
    expect(points.split(' ').length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-testid="trend-line"]')).toBeTruthy();
  });

  it('export() streams a blob and triggers a download (AC-4/FR-8)', () => {
    setup();
    fixture.detectChanges();

    const blob = new Blob(['a,b,c'], { type: 'text/csv' });
    const response = new HttpResponse({
      body: blob,
      headers: undefined,
    });
    serviceSpy.export.and.returnValue(of(response));

    const link = document.createElement('a');
    const clickSpy = spyOn(link, 'click');
    spyOn(document, 'createElement').and.returnValue(link);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:url');
    spyOn(URL, 'revokeObjectURL');

    component.export('Csv');

    expect(serviceSpy.export).toHaveBeenCalledWith('Csv', component.filters());
    expect(clickSpy).toHaveBeenCalled();
    expect(component.exporting()).toBeNull();
  });

  it('export() surfaces a toast on failure', () => {
    setup();
    fixture.detectChanges();
    serviceSpy.export.and.returnValue(throwError(() => new Error('boom')));

    component.export('Excel');

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.exporting()).toBeNull();
  });

  it('drillDown navigates to the department route with the active cycle', () => {
    setup();
    fixture.detectChanges();
    const navSpy = spyOn(router, 'navigate');
    component.toggleFilter('cycleIds', 'c1');

    component.drillDown('d1');

    expect(navSpy).toHaveBeenCalledWith(
      ['/performance', 'dashboard', 'departments', 'd1'],
      { queryParams: { cycleId: 'c1' } },
    );
  });

  it('shows an error state when the overview request fails', () => {
    setup();
    serviceSpy.getOverview.and.returnValue(throwError(() => new Error('500')));
    fixture.detectChanges();

    expect(component.error()).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Unable to load');
  });

  // ── ISSUE-290: the scope gate must check the REAL backend permission strings ──
  // These arms deliberately delegate hasAnyPermission to the REAL some()/includes()
  // logic against a controllable permission set (NOT a constant true/false — that is
  // exactly what hid the bug). Arm 1 FAILS against the old 'Performance.Read.*'
  // strings (no user holds them) and PASSES once the gate checks 'Performance.View.*'.
  describe('scope gate permission strings (ISSUE-290)', () => {
    it('admits a user holding the real Performance.View.All perm (does NOT redirect)', () => {
      setup();
      // A user with only the concrete backend perm the controller gates on.
      const userPerms = ['Performance.View.All'];
      authSpy.hasAnyPermission.and.callFake((perms: string[]) =>
        perms.some((p) => userPerms.includes(p)),
      );
      const navSpy = spyOn(router, 'navigate');

      fixture.detectChanges();

      expect(navSpy).not.toHaveBeenCalledWith(['/my-review']);
      expect(serviceSpy.getOverview).toHaveBeenCalledTimes(1);
    });

    it('admits a manager holding the real Performance.View.Team perm (does NOT redirect)', () => {
      setup();
      const userPerms = ['Performance.View.Team'];
      authSpy.hasAnyPermission.and.callFake((perms: string[]) =>
        perms.some((p) => userPerms.includes(p)),
      );
      const navSpy = spyOn(router, 'navigate');

      fixture.detectChanges();

      expect(navSpy).not.toHaveBeenCalledWith(['/my-review']);
      expect(serviceSpy.getOverview).toHaveBeenCalledTimes(1);
    });

    it('redirects an employee with no performance scope to /my-review (defensive net still works)', () => {
      setup();
      const userPerms: string[] = [];
      authSpy.hasAnyPermission.and.callFake((perms: string[]) =>
        perms.some((p) => userPerms.includes(p)),
      );
      const navSpy = spyOn(router, 'navigate');

      fixture.detectChanges();

      expect(navSpy).toHaveBeenCalledWith(['/my-review']);
      expect(serviceSpy.getOverview).not.toHaveBeenCalled();
    });
  });
});
