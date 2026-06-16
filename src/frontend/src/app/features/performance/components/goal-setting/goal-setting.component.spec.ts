import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { GoalSettingComponent } from './goal-setting.component';
import { PerformanceGoalService } from '../../services/performance-goal.service';
import { IAppraisalCycle, IGoal } from '../../models/goal.models';

describe('GoalSettingComponent', () => {
  let fixture: ComponentFixture<GoalSettingComponent>;
  let component: GoalSettingComponent;
  let goalService: jasmine.SpyObj<PerformanceGoalService>;
  let toastr: jasmine.SpyObj<ToastrService>;

  const openCycle: IAppraisalCycle = {
    id: 'cyc-1',
    name: '2026 Annual',
    goalSettingOpensOn: '2026-01-01',
    goalSettingClosesOn: '2026-01-31',
    goalSettingOpen: true,
  };
  const closedCycle: IAppraisalCycle = { ...openCycle, goalSettingOpen: false };

  const existingGoals: IGoal[] = [
    {
      id: 'g-1',
      title: 'Improve NPS',
      description: 'Lift NPS',
      category: 'KPI',
      weight: 60,
      targetValue: '85',
      measurementUnit: 'score',
      dueDate: '2026-06-30',
      parentGoalId: null,
    },
    {
      id: 'g-2',
      title: 'Ship feature X',
      description: 'Deliver X',
      category: 'Project',
      weight: 40,
      targetValue: '1',
      measurementUnit: 'release',
      dueDate: '2026-09-30',
      parentGoalId: null,
    },
  ];

  async function setup(
    cycle: IAppraisalCycle = openCycle,
    goals: IGoal[] = [],
  ): Promise<void> {
    goalService = jasmine.createSpyObj<PerformanceGoalService>(
      'PerformanceGoalService',
      ['getActiveCycle', 'getEmployeeGoals', 'saveGoals'],
    );
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    goalService.getActiveCycle.and.returnValue(of(cycle));
    goalService.getEmployeeGoals.and.returnValue(of(goals));
    goalService.saveGoals.and.returnValue(of(goals));

    await TestBed.configureTestingModule({
      imports: [GoalSettingComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PerformanceGoalService, useValue: goalService },
        { provide: ToastrService, useValue: toastr },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'e-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(GoalSettingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads cycle + goals and seeds one empty row when none exist (AC-1)', async () => {
    await setup(openCycle, []);
    expect(goalService.getActiveCycle).toHaveBeenCalled();
    expect(goalService.getEmployeeGoals).toHaveBeenCalledWith('cyc-1', 'e-1');
    expect(component.goals.length).toBe(1);
    expect(component.loading()).toBeFalse();
  });

  it('hydrates existing goals into the form', async () => {
    await setup(openCycle, existingGoals);
    expect(component.goals.length).toBe(2);
    expect(component.totalWeight()).toBe(100);
    expect(component.totalIs100()).toBeTrue();
  });

  // ─── AC-3: weight-sum gate ───────────────────────────────────

  it('disables Save when weights do not total 100% (95%)', async () => {
    await setup(openCycle, [{ ...existingGoals[0], weight: 60 }]);
    // single goal at 60 -> 60% total
    expect(component.totalWeight()).toBe(60);
    expect(component.totalIs100()).toBeFalse();
    expect(component.canSave()).toBeFalse();
  });

  it('disables Save when weights exceed 100% (105%)', async () => {
    await setup(openCycle, [
      { ...existingGoals[0], weight: 60 },
      { ...existingGoals[1], weight: 45 },
    ]);
    expect(component.totalWeight()).toBe(105);
    expect(component.totalIs100()).toBeFalse();
    expect(component.canSave()).toBeFalse();
  });

  it('enables Save when weights total exactly 100% and the form is valid', async () => {
    await setup(openCycle, existingGoals);
    expect(component.canSave()).toBeTrue();
  });

  it('shows the "Goal weights must total 100%" error when off-total', async () => {
    await setup(openCycle, [{ ...existingGoals[0], weight: 60 }]);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Goal weights must total 100%');
  });

  it('save() shows a toast error and aborts when not at 100%', async () => {
    await setup(openCycle, [{ ...existingGoals[0], weight: 60 }]);
    component.save();
    expect(toastr.error).toHaveBeenCalledWith('Goal weights must total 100%');
    expect(goalService.saveGoals).not.toHaveBeenCalled();
  });

  // ─── AC-2: save ──────────────────────────────────────────────

  it('save() PUTs the goal set and notifies success when valid (AC-2)', async () => {
    await setup(openCycle, existingGoals);
    component.save();
    expect(goalService.saveGoals).toHaveBeenCalledWith(
      'cyc-1',
      'e-1',
      jasmine.objectContaining({ goals: jasmine.any(Array) }),
    );
    expect(toastr.success).toHaveBeenCalled();
    expect(component.saving()).toBeFalse();
  });

  // ─── AC-5: closed window ─────────────────────────────────────

  it('renders read-only with the closed message when window is closed (AC-5)', async () => {
    await setup(closedCycle, existingGoals);
    expect(component.windowOpen()).toBeFalse();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain(
      'The goal-setting window for this cycle has closed',
    );
    expect(component.form.disabled).toBeTrue();
    expect(component.canSave()).toBeFalse();
  });

  it('addGoal is a no-op when the window is closed', async () => {
    await setup(closedCycle, existingGoals);
    const before = component.goals.length;
    component.addGoal();
    expect(component.goals.length).toBe(before);
  });

  // ─── Goal count limits (BR-2) ────────────────────────────────

  it('addGoal stops at the max of 10 goals', async () => {
    await setup(openCycle, []);
    for (let i = 0; i < 20; i++) {
      component.addGoal();
    }
    expect(component.goals.length).toBe(component.maxGoals);
  });

  it('removeGoal drops a row when the window is open', async () => {
    await setup(openCycle, existingGoals);
    component.removeGoal(0);
    expect(component.goals.length).toBe(1);
  });

  // ─── Error path ──────────────────────────────────────────────

  it('surfaces a load error when the cycle fails', async () => {
    goalService = jasmine.createSpyObj<PerformanceGoalService>(
      'PerformanceGoalService',
      ['getActiveCycle', 'getEmployeeGoals', 'saveGoals'],
    );
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    goalService.getActiveCycle.and.returnValue(
      throwError(() => new Error('boom')),
    );
    goalService.getEmployeeGoals.and.returnValue(of([]));
    goalService.saveGoals.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [GoalSettingComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: PerformanceGoalService, useValue: goalService },
        { provide: ToastrService, useValue: toastr },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'e-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(GoalSettingComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.loadError()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });
});
