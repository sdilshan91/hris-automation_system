import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of } from 'rxjs';

import { CycleDashboardComponent } from './cycle-dashboard.component';
import { CycleService } from '../../services/cycle.service';
import {
  CycleStatus,
  ICycle,
  ICycleDashboard,
} from '../../models/cycle.models';

describe('CycleDashboardComponent (AC-3 / FR-7 / FR-8)', () => {
  let fixture: ComponentFixture<CycleDashboardComponent>;
  let component: CycleDashboardComponent;
  let serviceSpy: jasmine.SpyObj<CycleService>;
  let toastr: jasmine.SpyObj<ToastrService>;
  let router: Router;

  function makeCycle(status: CycleStatus = 'Active'): ICycle {
    return {
      id: 'cyc-1',
      name: '2026 Annual',
      type: 'Annual',
      status,
      startDate: '2026-01-01',
      endDate: '2026-12-31',
      phases: [],
      scope: { scopeType: 'AllEmployees', departmentIds: [], employeeIds: [] },
      ratingScaleMax: 5,
      selfWeightPercent: 40,
      is360Enabled: false,
      isCalibrationEnabled: false,
      participantCount: 120,
      cancelledReason: null,
    };
  }

  const dashboard: ICycleDashboard = {
    cycleId: 'cyc-1',
    name: '2026 Annual',
    status: 'Active',
    participantCount: 120,
    phases: [
      {
        phaseType: 'GoalSetting',
        startDate: '2026-01-05',
        endDate: '2026-01-20',
        completedCount: 120,
        totalParticipants: 120,
        overdueCount: 0,
      },
      {
        phaseType: 'SelfAssessment',
        startDate: '2026-01-25',
        endDate: '2026-02-10',
        completedCount: 60,
        totalParticipants: 120,
        overdueCount: 8,
      },
      {
        phaseType: 'ManagerReview',
        startDate: '2026-02-15',
        endDate: '2026-03-01',
        completedCount: 0,
        totalParticipants: 120,
        overdueCount: 0,
      },
    ],
  };

  async function setup(status: CycleStatus = 'Active'): Promise<void> {
    serviceSpy = jasmine.createSpyObj<CycleService>('CycleService', [
      'get',
      'dashboard',
      'transition',
      'clone',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.get.and.returnValue(of(makeCycle(status)));
    serviceSpy.dashboard.and.returnValue(of(dashboard));
    serviceSpy.transition.and.returnValue(
      of({ ...makeCycle('Paused') }),
    );
    serviceSpy.clone.and.returnValue(of({ ...makeCycle('Draft'), id: 'cyc-2' }));

    await TestBed.configureTestingModule({
      imports: [CycleDashboardComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: CycleService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastr },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'cyc-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CycleDashboardComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
  }

  it('loads cycle + dashboard and renders a step per phase (AC-3)', async () => {
    await setup();
    expect(serviceSpy.get).toHaveBeenCalledWith('cyc-1');
    expect(serviceSpy.dashboard).toHaveBeenCalledWith('cyc-1');
    const steps = fixture.nativeElement.querySelectorAll(
      '[data-testid="phase-step"]',
    );
    expect(steps.length).toBe(3);
  });

  it('renders completion percentages and overdue counts (AC-3)', async () => {
    await setup();
    // Goal-setting fill = 100%, self-assessment = 50%.
    const gsFill = fixture.nativeElement.querySelector(
      '[data-testid="fill-GoalSetting"]',
    ) as HTMLElement;
    const saFill = fixture.nativeElement.querySelector(
      '[data-testid="fill-SelfAssessment"]',
    ) as HTMLElement;
    expect(gsFill.style.width).toBe('100%');
    expect(saFill.style.width).toBe('50%');

    // Self-assessment overdue badge shows 8; goal-setting has none.
    const saOverdue = fixture.nativeElement.querySelector(
      '[data-testid="overdue-SelfAssessment"]',
    );
    expect(saOverdue.textContent).toContain('8 overdue');
    expect(
      fixture.nativeElement.querySelector('[data-testid="overdue-GoalSetting"]'),
    ).toBeNull();

    // BUG-258: phase label (phaseType) + totals (totalParticipants) render, not blank/zero.
    const cards = fixture.nativeElement.querySelectorAll(
      '[data-testid="phase-card"]',
    ) as NodeListOf<HTMLElement>;
    expect(cards[0].textContent).toContain('Goal setting');
    expect(cards[0].textContent).toContain('120 / 120 completed');
    expect(cards[1].textContent).toContain('Self-assessment');
    expect(cards[1].textContent).toContain('60 / 120 completed');
  });

  it('shows only the legal transition buttons for an Active cycle (FR-7)', async () => {
    await setup('Active');
    expect(
      fixture.nativeElement.querySelector('[data-testid="transition-Pause"]'),
    ).not.toBeNull();
    expect(
      fixture.nativeElement.querySelector('[data-testid="transition-Complete"]'),
    ).not.toBeNull();
    expect(
      fixture.nativeElement.querySelector('[data-testid="transition-Cancel"]'),
    ).not.toBeNull();
    // No Activate/Resume for an already-active cycle.
    expect(
      fixture.nativeElement.querySelector('[data-testid="transition-Activate"]'),
    ).toBeNull();
  });

  it('shows no transition buttons for a Completed (terminal) cycle', async () => {
    await setup('Completed');
    expect(component.transitions()).toEqual([]);
    expect(
      fixture.nativeElement.querySelector('[data-testid="actions"] button[data-testid^="transition-"]'),
    ).toBeNull();
  });

  it('Pause calls transition() immediately (no reason needed)', async () => {
    await setup('Active');
    component.onTransition('Pause');
    expect(serviceSpy.transition).toHaveBeenCalledWith('cyc-1', {
      action: 'Pause',
      reason: undefined,
    });
  });

  it('Cancel opens the reason modal and blocks until a reason is given (BR-6)', async () => {
    await setup('Active');
    component.onTransition('Cancel');
    expect(component.cancelOpen()).toBeTrue();
    expect(serviceSpy.transition).not.toHaveBeenCalled();

    // Too-short reason is invalid.
    component.cancelReason.set('no');
    expect(component.cancelReasonValid()).toBeFalse();
    component.confirmCancel();
    expect(serviceSpy.transition).not.toHaveBeenCalled();

    // Valid reason submits.
    component.cancelReason.set('Reorganisation');
    expect(component.cancelReasonValid()).toBeTrue();
    component.confirmCancel();
    expect(serviceSpy.transition).toHaveBeenCalledWith('cyc-1', {
      action: 'Cancel',
      reason: 'Reorganisation',
    });
  });

  it('clone modal validates and calls clone(), then navigates (FR-8)', async () => {
    await setup('Completed');
    component.startClone();
    expect(component.cloneOpen()).toBeTrue();
    expect(component.cloneName()).toContain('copy');

    // Missing dates → invalid.
    expect(component.cloneValid()).toBeFalse();

    component.cloneStart.set('2027-01-01');
    component.cloneEnd.set('2027-12-31');
    expect(component.cloneValid()).toBeTrue();

    component.confirmClone();
    expect(serviceSpy.clone).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith([
      '/performance/cycles',
      'cyc-2',
    ]);
  });
});
