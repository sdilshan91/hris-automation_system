import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { TeamGoalProgressComponent } from './team-goal-progress.component';
import { GoalProgressService } from '../../services/goal-progress.service';
import {
  IEmployeeGoalProgress,
  IGoalUpdate,
  ITeamGoalProgressRow,
} from '../../models/goal-progress.models';

function makeRow(overrides: Partial<ITeamGoalProgressRow> = {}): ITeamGoalProgressRow {
  return {
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    overallCompletionPercent: 55,
    goalCount: 3,
    goalsAtRisk: 1,
    lastUpdatedOn: '2026-06-10',
    ...overrides,
  };
}

function makeEmployee(
  overrides: Partial<IEmployeeGoalProgress> = {},
): IEmployeeGoalProgress {
  return {
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    overallCompletionPercent: 55,
    goals: [
      {
        goalId: 'g-1',
        title: 'Ship onboarding',
        target: 'Q3',
        weight: 100,
        progressPercent: 55,
        status: 'AtRisk',
        lastUpdatedOn: '2026-06-10',
        needsAttention: true,
      },
    ],
    ...overrides,
  };
}

function makeUpdate(overrides: Partial<IGoalUpdate> = {}): IGoalUpdate {
  return {
    updateId: 'u-1',
    progressPercent: 55,
    previousProgressPercent: 30,
    status: 'AtRisk',
    notes: 'blocked on infra',
    authorName: 'Alex Doe',
    createdOn: '2026-06-10T10:00:00Z',
    attachments: [],
    comments: [],
    ...overrides,
  };
}

describe('TeamGoalProgressComponent (US-PRF-009 manager)', () => {
  let fixture: ComponentFixture<TeamGoalProgressComponent>;
  let component: TeamGoalProgressComponent;
  let serviceSpy: jasmine.SpyObj<GoalProgressService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(rows: ITeamGoalProgressRow[]): Promise<void> {
    serviceSpy = jasmine.createSpyObj<GoalProgressService>('GoalProgressService', [
      'getTeamProgress',
      'getEmployeeProgress',
      'getGoalUpdates',
      'addComment',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getTeamProgress.and.returnValue(of(rows));
    serviceSpy.getEmployeeProgress.and.returnValue(of(makeEmployee()));
    serviceSpy.getGoalUpdates.and.returnValue(of([makeUpdate()]));

    await TestBed.configureTestingModule({
      imports: [TeamGoalProgressComponent],
      providers: [
        { provide: GoalProgressService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        provideNoopAnimations(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TeamGoalProgressComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders the team summary table with completion, at-risk and last update (AC-4)', async () => {
    await setup([makeRow()]);
    const el = fixture.nativeElement as HTMLElement;
    const rows = el.querySelectorAll('[data-testid="team-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].textContent).toContain('Alex Doe');
    expect(el.querySelector('[data-testid="at-risk-badge"]')?.textContent).toContain(
      '1',
    );
  });

  it('shows no-at-risk dash when goalsAtRisk is 0', async () => {
    await setup([makeRow({ goalsAtRisk: 0 })]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="at-risk-badge"]')).toBeFalsy();
  });

  it('renders empty state when there are no direct reports', async () => {
    await setup([]);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="team-row"]')).toBeFalsy();
    expect(el.textContent).toContain('no direct reports');
  });

  it('drills down into an employee’s goals on row click (AC-4)', async () => {
    await setup([makeRow()]);
    component.openEmployee(makeRow());
    fixture.detectChanges();
    expect(serviceSpy.getEmployeeProgress).toHaveBeenCalledWith('e-1');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="emp-goal-card"]').length).toBe(1);
    expect(el.querySelector('[data-testid="back-btn"]')).toBeTruthy();
  });

  it('loads + renders an employee update timeline on expand', async () => {
    await setup([makeRow()]);
    component.openEmployee(makeRow());
    fixture.detectChanges();
    component.toggleHistory('g-1');
    fixture.detectChanges();
    expect(serviceSpy.getGoalUpdates).toHaveBeenCalledWith('g-1');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="emp-timeline-item"]').length).toBe(1);
  });

  it('posts a manager comment on an update and appends it to the thread (FR-8)', async () => {
    await setup([makeRow()]);
    serviceSpy.addComment.and.returnValue(
      of({
        commentId: 'c-1',
        authorName: 'Sam Lead',
        comment: 'Let’s sync on the blocker',
        createdOn: '2026-06-11T09:00:00Z',
      }),
    );
    component.openEmployee(makeRow());
    component.toggleHistory('g-1');
    const update = makeUpdate();
    component.setCommentDraft(update.updateId, 'Let’s sync on the blocker');
    component.postComment(update);

    expect(serviceSpy.addComment).toHaveBeenCalledWith(
      'u-1',
      'Let’s sync on the blocker',
    );
    expect(component.history()[0].comments.length).toBe(1);
    expect(toastrSpy.success).toHaveBeenCalled();
    // draft cleared after posting
    expect(component.commentDraft()['u-1']).toBe('');
  });

  it('does not post an empty comment', async () => {
    await setup([makeRow()]);
    component.openEmployee(makeRow());
    component.toggleHistory('g-1');
    component.setCommentDraft('u-1', '   ');
    component.postComment(makeUpdate());
    expect(serviceSpy.addComment).not.toHaveBeenCalled();
  });

  it('returns to the team table via back', async () => {
    await setup([makeRow()]);
    component.openEmployee(makeRow());
    expect(component.selected()).not.toBeNull();
    component.backToTeam();
    expect(component.selected()).toBeNull();
  });
});
