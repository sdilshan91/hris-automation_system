import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, Router, ActivatedRoute } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { ChecklistAssignmentComponent } from './checklist-assignment.component';
import { OnboardingChecklistService } from '../../services/onboarding-checklist.service';
import {
  IApplicableTemplate,
  IAssignedChecklist,
  IChecklistPreview,
} from '../../models/onboarding-checklist.models';

describe('ChecklistAssignmentComponent', () => {
  let component: ChecklistAssignmentComponent;
  let fixture: ComponentFixture<ChecklistAssignmentComponent>;
  let serviceSpy: jasmine.SpyObj<OnboardingChecklistService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const templates: IApplicableTemplate[] = [
    {
      id: 'tpl-1',
      templateName: 'Engineering New Hire',
      description: 'For engineers',
      taskCount: 2,
      matchReason: 'department',
    },
    {
      id: 'tpl-2',
      templateName: 'Universal Welcome',
      description: null,
      taskCount: 1,
      matchReason: 'universal',
    },
  ];

  const preview: IChecklistPreview = {
    employeeId: 'emp-1',
    employeeName: 'Grace Hopper',
    templateId: 'tpl-1',
    templateName: 'Engineering New Hire',
    startDate: '2026-07-01',
    tasks: [
      {
        templateTaskId: 'tt-2',
        title: 'IT laptop setup',
        responsibleRole: 'IT',
        dueOffsetDays: 3,
        dueDate: '2026-07-04',
        status: 'pending',
        isMandatory: false,
        sortOrder: 1,
      },
      {
        templateTaskId: 'tt-1',
        title: 'Sign contract',
        responsibleRole: 'HR',
        dueOffsetDays: 0,
        dueDate: '2026-07-01',
        status: 'pending',
        isMandatory: true,
        sortOrder: 0,
      },
    ],
  };

  const assigned: IAssignedChecklist = {
    checklistInstanceId: 'ci-1',
    employeeId: 'emp-1',
    templateId: 'tpl-1',
    templateName: 'Engineering New Hire',
    startDate: '2026-07-01',
    isActive: true,
    tasks: [],
    notifiedCount: 3,
  };

  /** Configure TestBed; `existing` controls the AC-3 lookup result. */
  async function setup(
    existing: IAssignedChecklist | null = null,
  ): Promise<void> {
    serviceSpy = jasmine.createSpyObj('OnboardingChecklistService', [
      'getApplicableTemplates',
      'preview',
      'getByEmployee',
      'assign',
      'modify',
    ]);
    serviceSpy.getApplicableTemplates.and.returnValue(of(templates));
    serviceSpy.preview.and.returnValue(of(preview));
    serviceSpy.getByEmployee.and.returnValue(of(existing));
    serviceSpy.assign.and.returnValue(of(assigned));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [ChecklistAssignmentComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OnboardingChecklistService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'emp-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ChecklistAssignmentComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('employeeId', 'emp-1');
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
  }

  /** Select template tpl-1 and let the preview build the task array. */
  function selectAndPreview(): void {
    component.selectTemplate(templates[0]);
    fixture.detectChanges();
  }

  // ─── Init (AC-1) ───────────────────────────────────────────

  it('loads applicable templates on init via resolved employeeId', async () => {
    await setup();
    expect(serviceSpy.getApplicableTemplates).toHaveBeenCalledWith('emp-1');
    expect(component.templates().length).toBe(2);
    expect(component.loadingTemplates()).toBeFalse();
  });

  it('falls back to the route snapshot when the input is empty', async () => {
    serviceSpy = jasmine.createSpyObj('OnboardingChecklistService', [
      'getApplicableTemplates',
      'preview',
      'getByEmployee',
      'assign',
      'modify',
    ]);
    serviceSpy.getApplicableTemplates.and.returnValue(of(templates));
    serviceSpy.preview.and.returnValue(of(preview));
    serviceSpy.getByEmployee.and.returnValue(of(null));
    serviceSpy.assign.and.returnValue(of(assigned));
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [ChecklistAssignmentComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OnboardingChecklistService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'emp-snap' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ChecklistAssignmentComponent);
    component = fixture.componentInstance;
    // No setInput → input() is the '' default → snapshot fallback applies.
    fixture.detectChanges();

    expect(serviceSpy.getApplicableTemplates).toHaveBeenCalledWith('emp-snap');
  });

  // ─── Search filter (AC-1) ──────────────────────────────────

  it('filters templates by the search term (name or description)', async () => {
    await setup();
    expect(component.filteredTemplates().length).toBe(2);
    component.search.set('universal');
    expect(component.filteredTemplates().length).toBe(1);
    expect(component.filteredTemplates()[0].id).toBe('tpl-2');
  });

  // ─── Preview + due-date ordering (FR-2 / UI/UX §8) ─────────

  it('builds the task array from the preview, sorted by due date', async () => {
    await setup();
    selectAndPreview();

    expect(serviceSpy.preview).toHaveBeenCalledWith('emp-1', 'tpl-1');
    expect(component.preview()!.startDate).toBe('2026-07-01');
    // Sorted ascending by dueDate: Sign contract (07-01) before IT setup (07-04).
    const titles = component.taskGroups().map((g) => g.controls.title.value);
    expect(titles).toEqual(['Sign contract', 'IT laptop setup']);
  });

  it('adds an ad-hoc task seeded with the preview start date', async () => {
    await setup();
    selectAndPreview();
    const before = component.taskGroups().length;
    component.addTask();
    fixture.detectChanges();
    expect(component.taskGroups().length).toBe(before + 1);
    const added = component.taskGroups()[before];
    expect(added.controls.dueDate.value).toBe('2026-07-01');
    expect(added.controls.isMandatory.value).toBeFalse();
  });

  it('removes a non-mandatory task', async () => {
    await setup();
    selectAndPreview();
    const itTask = component
      .taskGroups()
      .find((g) => g.controls.title.value === 'IT laptop setup')!;
    component.removeTaskById(itTask);
    fixture.detectChanges();
    const titles = component.taskGroups().map((g) => g.controls.title.value);
    expect(titles).toEqual(['Sign contract']);
  });

  // ─── Confirm flow guards (validation) ──────────────────────

  it('blocks confirm and errors when a task is invalid', async () => {
    await setup();
    selectAndPreview();
    component.taskGroups()[0].controls.title.setValue('ab'); // < 3 chars
    component.openConfirm();
    expect(component.showConfirmDialog()).toBeFalse();
    expect(component.showModeDialog()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('opens the confirm dialog directly when no existing checklist', async () => {
    await setup(null);
    selectAndPreview();
    component.openConfirm();
    expect(component.showModeDialog()).toBeFalse();
    expect(component.showConfirmDialog()).toBeTrue();
    expect(component.chosenMode()).toBeNull();
  });

  // ─── AC-3 replace / merge ──────────────────────────────────

  it('shows the replace/merge dialog when an active checklist exists', async () => {
    await setup({ ...assigned, templateName: 'Old checklist' });
    selectAndPreview();
    component.openConfirm();
    expect(component.alreadyAssigned()).toBeTruthy();
    expect(component.showModeDialog()).toBeTrue();
    expect(component.showConfirmDialog()).toBeFalse();
  });

  it('confirmMode records the mode and advances to the confirm dialog', async () => {
    await setup({ ...assigned });
    selectAndPreview();
    component.openConfirm();
    component.confirmMode('replace');
    expect(component.chosenMode()).toBe('replace');
    expect(component.showModeDialog()).toBeFalse();
    expect(component.showConfirmDialog()).toBeTrue();
  });

  // ─── AC-2 / AC-5 assign + toast ────────────────────────────

  it('assigns with the chosen mode and shows a pluralized toast', async () => {
    await setup({ ...assigned });
    selectAndPreview();
    component.openConfirm();
    component.confirmMode('merge');
    component.assign();

    const arg = serviceSpy.assign.calls.mostRecent().args[0];
    expect(arg.employeeId).toBe('emp-1');
    expect(arg.templateId).toBe('tpl-1');
    expect(arg.mode).toBe('merge');
    expect(arg.tasks.length).toBe(2);
    expect(arg.tasks[0].sortOrder).toBe(0);

    expect(toastrSpy.success).toHaveBeenCalledWith(
      'Onboarding checklist assigned. Notifications sent to 3 people.',
    );
    expect(router.navigate).toHaveBeenCalled();
    expect(component.showConfirmDialog()).toBeFalse();
  });

  it('uses the singular "person" when exactly one is notified', async () => {
    await setup(null);
    serviceSpy.assign.and.returnValue(of({ ...assigned, notifiedCount: 1 }));
    selectAndPreview();
    component.openConfirm();
    component.assign();
    expect(toastrSpy.success).toHaveBeenCalledWith(
      'Onboarding checklist assigned. Notifications sent to 1 person.',
    );
  });

  it('surfaces an assign error and keeps the dialog open', async () => {
    await setup(null);
    serviceSpy.assign.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            error: { message: 'Mandatory tasks cannot be removed.' },
            status: 400,
          }),
      ),
    );
    selectAndPreview();
    component.openConfirm();
    component.assign();
    expect(toastrSpy.error).toHaveBeenCalledWith(
      'Mandatory tasks cannot be removed.',
    );
    expect(component.assigning()).toBeFalse();
  });

  // ─── Confirmation summary count ────────────────────────────

  it('counts distinct responsible parties for the summary', async () => {
    await setup();
    selectAndPreview();
    // preview has HR + IT → 2 distinct parties.
    expect(component.responsiblePartyCount()).toBe(2);
  });
});
