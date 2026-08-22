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
    id: 't-it',
    title: 'Return laptop',
    responsibleRole: 'IT',
    dueDate: '2026-07-30',
    status: 'Pending',
    isMandatory: true,
    // null = undecided. `'pending'` was never a task-level value; the old fixture agreed with the
    // production code's wrong union instead of with the API.
    clearanceStatus: null,
    remarks: null,
    linkedAssetId: 'a-1',
    ...over,
  });

  const finTask = (over: Partial<IOffboardingTask> = {}): IOffboardingTask => ({
    id: 't-fin',
    title: 'Clear advances',
    responsibleRole: 'Finance',
    dueDate: '2026-07-29',
    status: 'Pending',
    isMandatory: true,
    clearanceStatus: null,
    remarks: null,
    linkedAssetId: null,
    ...over,
  });

  const instance = (over: Partial<IOffboardingInstance> = {}): IOffboardingInstance => ({
    id: 'off-1',
    employeeId: 'emp-1',
    employeeName: 'Jane Doe',
    lastWorkingDay: '2026-07-31',
    reason: 'Resignation',
    status: 'InProgress',
    overallClearance: 'pending',
    progressPercent: 0,
    departments: [
      { department: 'IT', clearanceStatus: 'pending', tasks: [itTask()] },
      { department: 'Finance', clearanceStatus: 'pending', tasks: [finTask()] },
    ],
    // AC-5: the SERVER's verdict. The component renders it; it no longer derives it from the tasks above.
    pendingMandatory: [
      { taskId: 't-it', title: 'Return laptop', department: 'IT', reason: 'not_completed' },
      { taskId: 't-fin', title: 'Clear advances', department: 'Finance', reason: 'not_completed' },
    ],
    canComplete: false,
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
          tasks: [itTask({ clearanceStatus: 'approved', status: 'Completed' })],
        },
        {
          department: 'Finance',
          clearanceStatus: 'cleared',
          tasks: [finTask({ clearanceStatus: 'approved', status: 'Completed' })],
        },
      ],
      pendingMandatory: [],
      canComplete: true,
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
    expect(component.instance()?.id).toBe('off-1');
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
    serviceSpy.complete.and.returnValue(of({ ...cleared(), status: 'Completed' }));
    component.openConfirm();
    expect(component.confirmOpen()).toBeTrue();
    component.confirmComplete();

    expect(serviceSpy.complete).toHaveBeenCalledWith('off-1');
    expect(component.confirmOpen()).toBeFalse();
    expect(component.instance()?.status).toBe('Completed');
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  // ── DOM arms: the migrated template tokens (AC-4/AC-5) ────────────────────────────────────────────
  //
  // Three template comparisons moved from lowercase to the wire's PascalCase during this change
  // (`line.status === 'Completed'`, `inst.status === 'Completed'`, `task.clearanceStatus !== 'approved'`).
  // Every other arm asserts on component methods, so leaving any one of them lowercase shipped green — and
  // a template token that never matches is precisely the bug B4 exists to fix. These read the DOM.
  //
  // One state per arm: TestBed cannot be reconfigured once instantiated.

  const completeButton = (): HTMLButtonElement =>
    (fixture.nativeElement as HTMLElement).querySelector(
      'button.btn-primary',
    ) as HTMLButtonElement;

  it('disables the Complete button while the server reports blockers', async () => {
    await setup(instance());
    expect(completeButton().disabled)
      .withContext('the server sent blocking items, so the button must be unusable')
      .toBeTrue();
  });

  it('enables the Complete button when the server reports nothing blocking', async () => {
    await setup(cleared());
    expect(completeButton().disabled)
      .withContext('a button that never enables is the original defect')
      .toBeFalse();
    expect(completeButton().textContent?.trim()).toBe('Complete Offboarding');
  });

  it('labels the button from the wire status token once completed', async () => {
    await setup(instance({ status: 'Completed', pendingMandatory: [], canComplete: false }));
    expect(completeButton().textContent?.trim())
      .withContext("the template compares against 'Completed'; a lowercase token silently never matches")
      .toBe('Offboarding Completed');
  });

  it('renders an undecided task in the TASK vocabulary, not the department one', async () => {
    await setup(instance());
    expect((fixture.nativeElement as HTMLElement).textContent)
      .withContext('an undecided task must not read as a department-style "Pending"')
      .toContain('Awaiting clearance');
  });

  it('renders an approved task as Approved', async () => {
    await setup(cleared());
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Approved');
  });

  it('surfaces the 409 pending-mandatory list inline (AC-5)', async () => {
    await setup(cleared());
    // The REAL 409 body: the failure envelope carrying the result DTO. The old fixture used a flat
    // `{ pending: [titles] }` the API has never sent, so this arm passed against a parser that returned
    // null on every genuine block.
    serviceSpy.complete.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            error: {
              success: false,
              code: 'pending_mandatory_items',
              message: 'Cannot complete offboarding. The following mandatory items are pending.',
              data: {
                completed: false,
                pendingItems: [
                  {
                    taskId: 't-it',
                    title: 'Return laptop',
                    clearanceCategoryName: 'IT',
                    reason: 'not_completed',
                  },
                  {
                    taskId: 't-fin',
                    title: 'Finance clearance',
                    clearanceCategoryName: 'Finance',
                    reason: 'clearance_not_approved',
                  },
                ],
              },
            },
            status: 409,
          }),
      ),
    );
    component.openConfirm();
    component.confirmComplete();

    expect(component.completeError()).toContain('Return laptop');
    expect(component.completeError()).toContain('Finance clearance');
    // AC-5 asks for WHY. "not completed" and "clearance not approved" need different action from HR, and
    // the list used to collapse both into a bare title.
    expect(component.completeError())
      .withContext('a refused clearance must not read the same as an untouched task')
      .toContain('clearance not approved');
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
