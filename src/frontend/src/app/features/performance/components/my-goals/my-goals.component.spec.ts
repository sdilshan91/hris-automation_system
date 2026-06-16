import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { MyGoalsComponent } from './my-goals.component';
import { GoalProgressService } from '../../services/goal-progress.service';
import {
  IGoalProgress,
  IGoalUpdate,
  IMyGoals,
} from '../../models/goal-progress.models';

function makeGoal(overrides: Partial<IGoalProgress> = {}): IGoalProgress {
  return {
    goalId: 'g-1',
    title: 'Ship the onboarding flow',
    target: 'Launch by Q3',
    weight: 60,
    progressPercent: 40,
    status: 'InProgress',
    lastUpdatedOn: '2026-06-01',
    needsAttention: false,
    ...overrides,
  };
}

function makeMyGoals(overrides: Partial<IMyGoals> = {}): IMyGoals {
  return {
    cycleId: 'c-1',
    cycleName: '2026 Annual',
    windowOpen: true,
    overallCompletionPercent: 60,
    goals: [makeGoal()],
    ...overrides,
  };
}

function makeUpdate(overrides: Partial<IGoalUpdate> = {}): IGoalUpdate {
  return {
    updateId: 'u-1',
    progressPercent: 40,
    previousProgressPercent: 20,
    status: 'InProgress',
    notes: 'Made good progress',
    authorName: 'Alex Doe',
    createdOn: '2026-06-01T10:00:00Z',
    attachments: [],
    comments: [],
    ...overrides,
  };
}

describe('MyGoalsComponent (US-PRF-009 employee)', () => {
  let fixture: ComponentFixture<MyGoalsComponent>;
  let component: MyGoalsComponent;
  let serviceSpy: jasmine.SpyObj<GoalProgressService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(data: IMyGoals): Promise<void> {
    serviceSpy = jasmine.createSpyObj<GoalProgressService>('GoalProgressService', [
      'getMyGoals',
      'getGoalUpdates',
      'addGoalUpdate',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getMyGoals.and.returnValue(of(data));
    serviceSpy.getGoalUpdates.and.returnValue(of([makeUpdate()]));

    await TestBed.configureTestingModule({
      imports: [MyGoalsComponent],
      providers: [
        { provide: GoalProgressService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        provideNoopAnimations(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyGoalsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders goal cards with title, target, status chip and progress bar (AC-1)', async () => {
    await setup(makeMyGoals());
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('[data-testid="goal-card"]').length).toBe(1);
    expect(el.querySelector('[data-testid="status-chip"]')?.textContent).toContain(
      'In progress',
    );
    const bar = el.querySelector('[data-testid="progress-bar"]') as HTMLElement;
    expect(bar.style.width).toBe('40%');
  });

  it('displays the weighted overall completion in the donut widget (FR-4)', async () => {
    await setup(makeMyGoals({ overallCompletionPercent: 72 }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="overall-pct"]')?.textContent).toContain(
      '72%',
    );
  });

  it('shows the closed-window message off the authoritative flag (BR-1)', async () => {
    await setup(makeMyGoals({ windowOpen: false }));
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="window-closed"]')).toBeTruthy();
    // Add-Update is hidden when the window is closed.
    expect(el.querySelector('[data-testid="add-update-btn"]')).toBeFalsy();
  });

  it('maps each status to its §8 color chip', async () => {
    await setup(makeMyGoals({ goals: [makeGoal({ status: 'Blocked' })] }));
    const chip = fixture.nativeElement.querySelector(
      '[data-testid="status-chip"]',
    ) as HTMLElement;
    expect(chip.className).toContain('rose');
    expect(chip.textContent).toContain('Blocked');
  });

  describe('Add Update form (AC-2)', () => {
    it('auto-selects Completed when the slider hits 100% (BR-2)', async () => {
      await setup(makeMyGoals());
      component.openUpdate(makeGoal({ status: 'InProgress' }));
      component.onProgressChange(100);
      expect(component.progress()).toBe(100);
      expect(component.status()).toBe('Completed');
    });

    it('keeps a manually-chosen AtRisk below 100% (BR-2 overridable)', async () => {
      await setup(makeMyGoals());
      component.openUpdate(makeGoal());
      component.status.set('AtRisk');
      component.onProgressChange(50);
      expect(component.status()).toBe('AtRisk');
    });

    it('opens the bottom-sheet seeded from the goal', async () => {
      await setup(makeMyGoals());
      component.openUpdate(makeGoal({ progressPercent: 40, status: 'InProgress' }));
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-testid="update-sheet"]')).toBeTruthy();
      expect(component.progress()).toBe(40);
    });

    it('caps attachments at the max and warns', async () => {
      await setup(makeMyGoals());
      component.openUpdate(makeGoal());
      const files = [0, 1, 2, 3].map(
        (i) => new File(['x'], `f${i}.pdf`, { type: 'application/pdf' }),
      );
      component.onFiles({
        target: { files, value: '' },
      } as unknown as Event);
      expect(component.files().length).toBe(3);
      expect(toastrSpy.error).toHaveBeenCalled();
    });

    it('saves the update and reflects it on the card (AC-2)', async () => {
      await setup(makeMyGoals());
      serviceSpy.addGoalUpdate.and.returnValue(
        of(makeUpdate({ progressPercent: 100, status: 'Completed' })),
      );
      component.openUpdate(makeGoal());
      component.onProgressChange(100);
      component.save();
      expect(serviceSpy.addGoalUpdate).toHaveBeenCalled();
      expect(toastrSpy.success).toHaveBeenCalled();
      // card reflects the new progress
      expect(component.data()?.goals[0].progressPercent).toBe(100);
      expect(component.data()?.goals[0].status).toBe('Completed');
    });
  });

  describe('Update history timeline (AC-3)', () => {
    it('lazily loads + renders the timeline on expand', async () => {
      await setup(makeMyGoals());
      component.toggleHistory('g-1');
      fixture.detectChanges();
      expect(serviceSpy.getGoalUpdates).toHaveBeenCalledWith('g-1');
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelectorAll('[data-testid="timeline-item"]').length).toBe(1);
    });

    it('renders the manager comment thread under an update (FR-8)', async () => {
      serviceSpy = jasmine.createSpyObj<GoalProgressService>(
        'GoalProgressService',
        ['getMyGoals', 'getGoalUpdates', 'addGoalUpdate'],
      );
      toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
        'success',
        'error',
      ]);
      serviceSpy.getMyGoals.and.returnValue(of(makeMyGoals()));
      serviceSpy.getGoalUpdates.and.returnValue(
        of([
          makeUpdate({
            comments: [
              {
                commentId: 'c-1',
                authorName: 'Sam Lead',
                comment: 'Great progress!',
                createdOn: '2026-06-02T09:00:00Z',
              },
            ],
          }),
        ]),
      );
      await TestBed.configureTestingModule({
        imports: [MyGoalsComponent],
        providers: [
          { provide: GoalProgressService, useValue: serviceSpy },
          { provide: ToastrService, useValue: toastrSpy },
          provideNoopAnimations(),
        ],
      }).compileComponents();
      fixture = TestBed.createComponent(MyGoalsComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleHistory('g-1');
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('[data-testid="comment-thread"]')?.textContent).toContain(
        'Great progress!',
      );
    });

    it('collapses the timeline on a second toggle', async () => {
      await setup(makeMyGoals());
      component.toggleHistory('g-1');
      expect(component.isExpanded('g-1')).toBeTrue();
      component.toggleHistory('g-1');
      expect(component.isExpanded('g-1')).toBeFalse();
    });
  });
});
