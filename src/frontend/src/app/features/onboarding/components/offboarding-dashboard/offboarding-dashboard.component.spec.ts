import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { OffboardingDashboardComponent } from './offboarding-dashboard.component';
import { OffboardingService } from '../../services/offboarding.service';
import {
  IOffboardingInstance,
  IOffboardingTask,
} from '../../models/offboarding.models';

describe('OffboardingDashboardComponent', () => {
  let component: OffboardingDashboardComponent;
  let fixture: ComponentFixture<OffboardingDashboardComponent>;
  let serviceSpy: jasmine.SpyObj<OffboardingService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const itTask = (over: Partial<IOffboardingTask> = {}): IOffboardingTask => ({
    taskId: 't-it',
    title: 'Return laptop',
    responsibleRole: 'IT',
    dueDate: '2026-07-30',
    status: 'pending',
    isMandatory: true,
    clearanceStatus: 'pending',
    remarks: null,
    linkedAssetId: 'a-1',
    ...over,
  });

  const finTask = (over: Partial<IOffboardingTask> = {}): IOffboardingTask => ({
    taskId: 't-fin',
    title: 'Clear advances',
    responsibleRole: 'Finance',
    dueDate: '2026-07-29',
    status: 'pending',
    isMandatory: true,
    clearanceStatus: 'pending',
    remarks: null,
    linkedAssetId: null,
    ...over,
  });

  const instance = (over: Partial<IOffboardingInstance> = {}): IOffboardingInstance => ({
    offboardingId: 'off-1',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    lastWorkingDay: '2026-07-31',
    reason: 'Resignation',
    status: 'in_progress',
    overallClearance: 'pending',
    progressPercent: 0,
    departments: [
      { department: 'IT', clearanceStatus: 'pending', tasks: [itTask()] },
      { department: 'Finance', clearanceStatus: 'pending', tasks: [finTask()] },
    ],
    ...over,
  });

  const cleared = (): IOffboardingInstance =>
    instance({
      overallClearance: 'cleared',
      progressPercent: 100,
      departments: [
        {
          department: 'IT',
          clearanceStatus: 'cleared',
          tasks: [itTask({ clearanceStatus: 'cleared', status: 'completed' })],
        },
        {
          department: 'Finance',
          clearanceStatus: 'cleared',
          tasks: [finTask({ clearanceStatus: 'cleared', status: 'completed' })],
        },
      ],
    });

  async function setup(initial: IOffboardingInstance = instance()): Promise<void> {
    serviceSpy = jasmine.createSpyObj('OffboardingService', [
      'getById',
      'recordClearance',
      'returnAsset',
      'complete',
    ]);
    serviceSpy.getById.and.returnValue(of(initial));
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [OffboardingDashboardComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OffboardingService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => 'off-1' } },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(OffboardingDashboardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('offboardingId', 'off-1');
    fixture.detectChanges();
  }

  it('loads the instance on init (AC-3)', async () => {
    await setup();
    expect(serviceSpy.getById).toHaveBeenCalledWith('off-1');
    expect(component.loading()).toBeFalse();
    expect(component.instance()?.offboardingId).toBe('off-1');
  });

  it('surfaces a load error', async () => {
    await setup();
    serviceSpy.getById.and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { message: 'Not found' }, status: 404 })),
    );
    component.ngOnInit();
    expect(component.loadError()).toBe('Not found');
  });

  it('derives the asset-return lines from linkedAssetId tasks (AC-2)', async () => {
    await setup();
    const lines = component.assetLines();
    expect(lines.length).toBe(1);
    expect(lines[0].assetId).toBe('a-1');
  });

  it('computes pending mandatory titles and blocks completion (BR-2/AC-5)', async () => {
    await setup();
    expect(component.pendingTitles()).toEqual(['Return laptop', 'Clear advances']);
    expect(component.canFinish()).toBeFalse();
  });

  it('allows completion once every mandatory task is cleared', async () => {
    await setup(cleared());
    expect(component.pendingTitles()).toEqual([]);
    expect(component.canFinish()).toBeTrue();
  });

  it('records a clearance approval and refreshes from the response (AC-3)', async () => {
    await setup();
    serviceSpy.recordClearance.and.returnValue(of(cleared()));
    component.startEdit(itTask());
    component.remarksDraft.set('looks good');
    component.recordClearance(itTask(), 'approved');

    expect(serviceSpy.recordClearance).toHaveBeenCalledWith('t-it', {
      status: 'approved',
      remarks: 'looks good',
    });
    expect(component.instance()?.overallClearance).toBe('cleared');
    expect(component.editingTask()).toBeNull();
    expect(component.busyTask()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('sends null remarks when the draft is blank', async () => {
    await setup();
    serviceSpy.recordClearance.and.returnValue(of(instance()));
    component.recordClearance(finTask(), 'pending_issues');
    expect(serviceSpy.recordClearance).toHaveBeenCalledWith('t-fin', {
      status: 'pending_issues',
      remarks: null,
    });
  });

  it('marks an asset returned with the chosen condition + dispose flag (AC-2)', async () => {
    await setup();
    serviceSpy.returnAsset.and.returnValue(of(instance()));
    const line = component.assetLines()[0];
    component.setReturnCondition(line.taskId, 'Poor');
    component.setDispose(line.taskId, true);
    component.markReturned(line);

    expect(serviceSpy.returnAsset).toHaveBeenCalledWith('t-it', {
      assetId: 'a-1',
      condition: 'Poor',
      disposed: true,
    });
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('defaults asset return to Good condition, not disposed', async () => {
    await setup();
    serviceSpy.returnAsset.and.returnValue(of(instance()));
    const line = component.assetLines()[0];
    component.markReturned(line);
    expect(serviceSpy.returnAsset).toHaveBeenCalledWith('t-it', {
      assetId: 'a-1',
      condition: 'Good',
      disposed: false,
    });
  });

  it('opens the confirm modal and completes offboarding (AC-4)', async () => {
    await setup(cleared());
    serviceSpy.complete.and.returnValue(of({ ...cleared(), status: 'completed' }));
    component.openConfirm();
    expect(component.confirmOpen()).toBeTrue();
    component.confirmComplete();

    expect(serviceSpy.complete).toHaveBeenCalledWith('off-1');
    expect(component.confirmOpen()).toBeFalse();
    expect(component.instance()?.status).toBe('completed');
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('surfaces the 409 pending-mandatory list inline (AC-5)', async () => {
    await setup(cleared());
    serviceSpy.complete.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            error: { pending: ['Return laptop', 'Finance clearance'] },
            status: 409,
          }),
      ),
    );
    component.openConfirm();
    component.confirmComplete();

    expect(component.completeError()).toContain('Return laptop, Finance clearance');
    expect(component.confirmOpen()).toBeFalse();
    expect(component.finishing()).toBeFalse();
  });

  it('toggles mobile accordion expansion', async () => {
    await setup();
    expect(component.isExpanded('IT')).toBeFalse();
    component.toggleDept('IT');
    expect(component.isExpanded('IT')).toBeTrue();
    component.toggleDept('IT');
    expect(component.isExpanded('IT')).toBeFalse();
  });

  it('maps clearance status to chip + traffic-light classes (FR-4)', async () => {
    await setup();
    expect(component.chipClass('cleared')).toBe('chip-cleared');
    expect(component.lightClass('issues')).toBe('light-yellow');
    expect(component.lightClass('pending')).toBe('light-red');
    expect(component.statusLabel('cleared')).toBe('Cleared');
  });
});
