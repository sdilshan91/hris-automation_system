import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of } from 'rxjs';

import { CycleFormComponent } from './cycle-form.component';
import { CycleService } from '../../services/cycle.service';
import {
  ICycle,
  IRatingScaleOption,
  ISaveCycleRequest,
} from '../../models/cycle.models';

describe('CycleFormComponent (AC-1 / AC-5)', () => {
  let fixture: ComponentFixture<CycleFormComponent>;
  let component: CycleFormComponent;
  let serviceSpy: jasmine.SpyObj<CycleService>;
  let toastr: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const scales: IRatingScaleOption[] = [
    { id: 'scale-5', name: '1-5 scale', max: 5 },
  ];

  const createdCycle: ICycle = {
    id: 'cyc-new',
    name: '2026 Annual',
    type: 'Annual',
    status: 'Draft',
    startDate: '2026-01-01',
    endDate: '2026-12-31',
    phases: [],
    scope: {
      type: 'AllEmployees',
      departmentIds: [],
      gradeIds: [],
      employeeIds: [],
    },
    ratingScaleId: 'scale-5',
    selfWeight: 40,
    enable360: false,
    enableCalibration: false,
    participantCount: 0,
    cancelledReason: null,
  };

  async function setup(cycleId: string | null = null): Promise<void> {
    serviceSpy = jasmine.createSpyObj<CycleService>('CycleService', [
      'list',
      'get',
      'create',
      'update',
      'dashboard',
      'transition',
      'clone',
      'ratingScales',
    ]);
    toastr = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.ratingScales.and.returnValue(of(scales));
    serviceSpy.create.and.returnValue(of(createdCycle));
    serviceSpy.update.and.returnValue(of(createdCycle));
    serviceSpy.get.and.returnValue(of(createdCycle));

    await TestBed.configureTestingModule({
      imports: [CycleFormComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: CycleService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastr },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => cycleId } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CycleFormComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
  }

  /** Fill the form with a valid, sequential, in-window phase set. */
  function fillValid(): void {
    component.form.patchValue({
      name: '2026 Annual',
      type: 'Annual',
      startDate: '2026-01-01',
      endDate: '2026-12-31',
      ratingScaleId: 'scale-5',
      selfWeight: 40,
      scopeType: 'AllEmployees',
    });
    const phaseDates: Record<string, [string, string]> = {
      GoalSetting: ['2026-01-05', '2026-01-20'],
      SelfAssessment: ['2026-01-25', '2026-02-10'],
      ManagerReview: ['2026-02-15', '2026-03-01'],
      Calibration: ['2026-03-05', '2026-03-10'],
      Publish: ['2026-03-15', '2026-03-20'],
    };
    for (const ctrl of component.phases.controls) {
      const [s, e] = phaseDates[ctrl.value.kind];
      ctrl.patchValue({ startDate: s, endDate: e });
    }
    fixture.detectChanges();
  }

  it('loads rating scales and is in create mode without a route id', async () => {
    await setup(null);
    expect(serviceSpy.ratingScales).toHaveBeenCalled();
    expect(component.isEdit()).toBeFalse();
    expect(component.ratingScales().length).toBe(1);
    expect(component.loading()).toBeFalse();
  });

  it('happy path: a valid form can be saved and calls create() (AC-1/AC-2)', async () => {
    await setup(null);
    fillValid();

    expect(component.phaseErrors()).toEqual([]);
    expect(component.canSave()).toBeTrue();

    component.save();

    expect(serviceSpy.create).toHaveBeenCalled();
    const req = serviceSpy.create.calls.mostRecent()
      .args[0] as ISaveCycleRequest;
    expect(req.name).toBe('2026 Annual');
    expect(req.phases.length).toBe(4); // calibration excluded (toggle off)
    expect(req.scope.type).toBe('AllEmployees');
    expect(toastr.success).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith([
      '/performance/cycles',
      'cyc-new',
    ]);
  });

  it('rejects overlapping phases — canSave is false and errors are shown (FR-2)', async () => {
    await setup(null);
    fillValid();
    // Make self-assessment start before goal-setting ends → overlap.
    const self = component.phases.controls.find(
      (c) => c.value.kind === 'SelfAssessment',
    )!;
    self.patchValue({ startDate: '2026-01-10', endDate: '2026-02-10' });
    fixture.detectChanges();

    expect(component.phaseErrors().length).toBeGreaterThan(0);
    expect(component.canSave()).toBeFalse();

    component.save();
    expect(serviceSpy.create).not.toHaveBeenCalled();
  });

  it('rejects an out-of-window phase (BR-3)', async () => {
    await setup(null);
    fillValid();
    const gs = component.phases.controls.find(
      (c) => c.value.kind === 'GoalSetting',
    )!;
    gs.patchValue({ startDate: '2025-12-01', endDate: '2026-01-20' });
    fixture.detectChanges();

    expect(
      component.phaseErrors().some((e) => e.includes('within the cycle window')),
    ).toBeTrue();
    expect(component.canSave()).toBeFalse();
  });

  it('includes the calibration phase when the toggle is on (FR-6)', async () => {
    await setup(null);
    fillValid();
    component.form.patchValue({ enableCalibration: true });
    fixture.detectChanges();

    expect(component.isPhaseVisible('Calibration')).toBeTrue();
    expect(component.canSave()).toBeTrue();

    component.save();
    const req = serviceSpy.create.calls.mostRecent()
      .args[0] as ISaveCycleRequest;
    expect(req.phases.length).toBe(5);
    expect(req.enableCalibration).toBeTrue();
  });

  it('in edit mode loads the cycle and calls update() on save (AC-5)', async () => {
    await setup('cyc-new');
    expect(serviceSpy.get).toHaveBeenCalledWith('cyc-new');
    expect(component.isEdit()).toBeTrue();
    fillValid();

    component.save();
    expect(serviceSpy.update).toHaveBeenCalled();
    expect(serviceSpy.create).not.toHaveBeenCalled();
  });

  it('maps custom-list scope ids into the request employeeIds (FR-3)', async () => {
    await setup(null);
    fillValid();
    component.form.patchValue({
      scopeType: 'CustomList',
      scopeIds: 'emp-1, emp-2 , emp-3',
    });
    fixture.detectChanges();

    component.save();
    const req = serviceSpy.create.calls.mostRecent()
      .args[0] as ISaveCycleRequest;
    expect(req.scope.type).toBe('CustomList');
    expect(req.scope.employeeIds).toEqual(['emp-1', 'emp-2', 'emp-3']);
    expect(req.scope.departmentIds).toEqual([]);
  });
});
