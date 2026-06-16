import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { TeamGoalsComponent } from './team-goals.component';
import { PerformanceGoalService } from '../../services/performance-goal.service';
import { IAppraisalCycle, ITeamGoalStatus } from '../../models/goal.models';

describe('TeamGoalsComponent', () => {
  let fixture: ComponentFixture<TeamGoalsComponent>;
  let component: TeamGoalsComponent;
  let goalService: jasmine.SpyObj<PerformanceGoalService>;

  const openCycle: IAppraisalCycle = {
    id: 'cyc-1',
    name: '2026 Annual',
    goalSettingOpensOn: '2026-01-01',
    goalSettingClosesOn: '2026-01-31',
    goalSettingOpen: true,
  };
  const closedCycle: IAppraisalCycle = { ...openCycle, goalSettingOpen: false };

  const team: ITeamGoalStatus[] = [
    {
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
      jobTitle: 'Engineer',
      status: 'Draft',
      goalCount: 2,
      totalWeight: 60,
    },
    {
      employeeId: 'e-2',
      employeeName: 'Sam Roe',
      jobTitle: 'Designer',
      status: 'Acknowledged',
      goalCount: 3,
      totalWeight: 100,
    },
  ];

  async function setup(
    cycle: IAppraisalCycle = openCycle,
    rows: ITeamGoalStatus[] = team,
  ): Promise<void> {
    goalService = jasmine.createSpyObj<PerformanceGoalService>(
      'PerformanceGoalService',
      ['getActiveCycle', 'getTeamStatus'],
    );
    goalService.getActiveCycle.and.returnValue(of(cycle));
    goalService.getTeamStatus.and.returnValue(of(rows));

    await TestBed.configureTestingModule({
      imports: [TeamGoalsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PerformanceGoalService, useValue: goalService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TeamGoalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads the active cycle then the team list (AC-4)', async () => {
    await setup();
    expect(goalService.getActiveCycle).toHaveBeenCalled();
    expect(goalService.getTeamStatus).toHaveBeenCalledWith('cyc-1');
    expect(component.members().length).toBe(2);
    expect(component.loading()).toBeFalse();
  });

  it('renders a card per team member with status badges', async () => {
    await setup();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Alex Doe');
    expect(text).toContain('Sam Roe');
    expect(text).toContain('Draft');
    expect(text).toContain('Acknowledged');
  });

  it('shows the closed-window banner when the window is closed (AC-5)', async () => {
    await setup(closedCycle);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain(
      'The goal-setting window for this cycle has closed',
    );
    expect(component.windowOpen()).toBeFalse();
  });

  it('does NOT show the closed-window banner when the window is open', async () => {
    await setup(openCycle);
    const text = fixture.nativeElement.textContent as string;
    expect(text).not.toContain(
      'The goal-setting window for this cycle has closed',
    );
  });

  it('shows an empty state when the manager has no direct reports', async () => {
    await setup(openCycle, []);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No team members yet');
  });

  it('surfaces an error when the cycle fails to load', async () => {
    goalService = jasmine.createSpyObj<PerformanceGoalService>(
      'PerformanceGoalService',
      ['getActiveCycle', 'getTeamStatus'],
    );
    goalService.getActiveCycle.and.returnValue(
      throwError(() => new Error('boom')),
    );
    goalService.getTeamStatus.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [TeamGoalsComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PerformanceGoalService, useValue: goalService },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TeamGoalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.error()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });

  it('builds a per-member link into the goal-setting view', async () => {
    await setup();
    expect(component.memberLink('e-1')).toEqual([
      '/performance',
      'goals',
      'e-1',
    ]);
  });
});
