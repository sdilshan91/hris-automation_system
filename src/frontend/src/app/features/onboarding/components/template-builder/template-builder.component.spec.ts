import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, Router, ActivatedRoute } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { CdkDragDrop } from '@angular/cdk/drag-drop';

import { TemplateBuilderComponent } from './template-builder.component';
import { OnboardingTemplateService } from '../../services/onboarding-template.service';
import {
  IOnboardingLookups,
  IOnboardingTemplate,
} from '../../models/onboarding-template.models';

describe('TemplateBuilderComponent', () => {
  let component: TemplateBuilderComponent;
  let fixture: ComponentFixture<TemplateBuilderComponent>;
  let serviceSpy: jasmine.SpyObj<OnboardingTemplateService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const lookups: IOnboardingLookups = {
    departments: [{ id: 'd1', name: 'Engineering' }],
    jobTitles: [{ id: 'j1', name: 'Engineer' }],
    users: [{ id: 'u1', name: 'Ada Lovelace' }],
  };

  const clonedTemplate: IOnboardingTemplate = {
    id: 'tpl-copy',
    templateName: 'Engineering New Hire (Copy)',
    description: 'Welcome',
    applicableDepartments: ['d1'],
    applicableJobTitles: [],
    isActive: true,
    tasks: [
      {
        title: 'Second task',
        category: 'IT Setup',
        dueOffsetDays: 2,
        isMandatory: false,
        sortOrder: 1,
      },
      {
        title: 'First task',
        category: 'Documentation',
        dueOffsetDays: 0,
        isMandatory: true,
        sortOrder: 0,
      },
    ],
  };

  /** Configure TestBed with a controllable cloneFrom query param. */
  async function setup(cloneFrom: string | null = null): Promise<void> {
    serviceSpy = jasmine.createSpyObj('OnboardingTemplateService', [
      'getLookups',
      'create',
      'clone',
    ]);
    serviceSpy.getLookups.and.returnValue(of(lookups));
    serviceSpy.create.and.returnValue(of(clonedTemplate));
    serviceSpy.clone.and.returnValue(of(clonedTemplate));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [TemplateBuilderComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OnboardingTemplateService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { queryParamMap: { get: () => cloneFrom } },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TemplateBuilderComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();
  }

  // ─── Init (blank) ──────────────────────────────────────────

  it('loads lookups and seeds one blank task on a fresh form', async () => {
    await setup();
    expect(serviceSpy.getLookups).toHaveBeenCalled();
    expect(component.lookups().departments.length).toBe(1);
    expect(component.tasks.length).toBe(1);
    expect(component.isClone()).toBeFalse();
  });

  it('warns but stays usable when lookups fail to load', async () => {
    serviceSpy = jasmine.createSpyObj('OnboardingTemplateService', [
      'getLookups',
      'create',
      'clone',
    ]);
    serviceSpy.getLookups.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    serviceSpy.create.and.returnValue(of(clonedTemplate));
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [TemplateBuilderComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OnboardingTemplateService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => null } } },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TemplateBuilderComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(toastrSpy.warning).toHaveBeenCalled();
    expect(component.tasks.length).toBe(1);
  });

  // ─── Init (clone — FR-6) ───────────────────────────────────

  it('pre-fills the form from a cloned template, sorted by sortOrder', async () => {
    await setup('tpl-1');
    expect(serviceSpy.clone).toHaveBeenCalledWith('tpl-1');
    expect(component.isClone()).toBeTrue();
    expect(component.form.controls.templateName.value).toContain('(Copy)');
    // Tasks reordered by sortOrder: "First task" (0) then "Second task" (1).
    expect(component.tasks.length).toBe(2);
    expect(component.tasks.at(0).controls.title.value).toBe('First task');
    expect(component.tasks.at(1).controls.title.value).toBe('Second task');
  });

  it('navigates away when the clone request fails', async () => {
    serviceSpy = jasmine.createSpyObj('OnboardingTemplateService', [
      'getLookups',
      'create',
      'clone',
    ]);
    serviceSpy.getLookups.and.returnValue(of(lookups));
    serviceSpy.clone.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);
    await TestBed.configureTestingModule({
      imports: [TemplateBuilderComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OnboardingTemplateService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: { get: () => 'tpl-x' } } },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(TemplateBuilderComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigate').and.resolveTo(true);
    fixture.detectChanges();

    expect(toastrSpy.error).toHaveBeenCalled();
    expect(navSpy).toHaveBeenCalledWith(['/onboarding/templates']);
  });

  // ─── Task mutations ────────────────────────────────────────

  it('addTask and removeTask grow/shrink the FormArray', async () => {
    await setup();
    expect(component.tasks.length).toBe(1);
    component.addTask();
    expect(component.tasks.length).toBe(2);
    component.removeTask(0);
    expect(component.tasks.length).toBe(1);
  });

  it('moveTask reorders tasks via the arrow buttons (NFR-4)', async () => {
    await setup();
    component.addTask({ title: 'Alpha' });
    component.addTask({ title: 'Beta' });
    // tasks: ['', 'Alpha', 'Beta'] -> move index 2 up to index 1
    component.moveTask(2, 1);
    expect(component.tasks.at(1).controls.title.value).toBe('Beta');
    expect(component.tasks.at(2).controls.title.value).toBe('Alpha');
  });

  it('moveTask ignores out-of-range targets', async () => {
    await setup();
    component.moveTask(0, -1);
    expect(component.tasks.length).toBe(1);
  });

  it('onDrop reorders tasks via CDK drag-and-drop (FR-1)', async () => {
    await setup();
    component.addTask({ title: 'Alpha' });
    component.addTask({ title: 'Beta' });
    const evt = {
      previousIndex: 2,
      currentIndex: 0,
    } as unknown as CdkDragDrop<unknown>;
    component.onDrop(evt);
    expect(component.tasks.at(0).controls.title.value).toBe('Beta');
  });

  // ─── Validation (BR-2 / BR-3 / name) ───────────────────────

  it('blocks save with no tasks and shows an error (BR-2)', async () => {
    await setup();
    component.removeTask(0);
    component.save();
    expect(serviceSpy.create).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('blocks save when the form is invalid (name too short)', async () => {
    await setup();
    component.form.controls.templateName.setValue('ab');
    component.tasks.at(0).controls.title.setValue('Valid title');
    component.save();
    expect(serviceSpy.create).not.toHaveBeenCalled();
    expect(component.showError('templateName')).toBeTrue();
  });

  it('flags a negative due offset as invalid (BR-3)', async () => {
    await setup();
    const due = component.tasks.at(0).controls.dueOffsetDays;
    due.setValue(-1);
    due.markAsTouched();
    expect(component.taskError(component.tasks.at(0), 'dueOffsetDays')).toBeTrue();
  });

  // ─── Save (AC-2 / FR-3 / FR-5) ─────────────────────────────

  it('saves a valid template and posts a tenant-free, dense-sortOrder payload', async () => {
    await setup();
    component.form.controls.templateName.setValue('Engineering New Hire');
    component.form.controls.applicableDepartments.setValue(['d1']);
    const t0 = component.tasks.at(0).controls;
    t0.title.setValue('Sign contract');
    t0.category.setValue('Documentation');
    t0.dueOffsetDays.setValue(0);
    t0.isMandatory.setValue(true);

    component.addTask({ title: 'Setup laptop' });
    const t1 = component.tasks.at(1).controls;
    t1.title.setValue('Setup laptop');
    t1.dueOffsetDays.setValue(1);

    component.save();

    expect(serviceSpy.create).toHaveBeenCalledTimes(1);
    const payload = serviceSpy.create.calls.mostRecent().args[0];
    expect(payload.templateName).toBe('Engineering New Hire');
    expect(payload.applicableDepartments).toEqual(['d1']);
    expect(payload.tasks.length).toBe(2);
    // sort_order is derived from on-screen order (FR-3).
    expect(payload.tasks.map((t) => t.sortOrder)).toEqual([0, 1]);
    // tenant_id is never present in the payload (FR-5).
    expect('tenantId' in (payload as object)).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/onboarding/templates']);
  });

  it('only sends responsibleUserId when the role is Custom', async () => {
    await setup();
    component.form.controls.templateName.setValue('Valid name');
    const t0 = component.tasks.at(0).controls;
    t0.title.setValue('Pick a buddy');
    t0.responsibleRole.setValue('HR');
    t0.responsibleUserId.setValue('u1');
    component.save();

    const payload = serviceSpy.create.calls.mostRecent().args[0];
    // Role is HR (not Custom) -> the stray user id is dropped.
    expect(payload.tasks[0].responsibleUserId).toBeNull();
  });

  it('surfaces a duplicate-name server error verbatim (AC-3)', async () => {
    await setup();
    serviceSpy.create.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            error: { message: 'A checklist template with this name already exists.' },
            status: 409,
          }),
      ),
    );
    component.form.controls.templateName.setValue('Engineering New Hire');
    component.tasks.at(0).controls.title.setValue('Sign contract');
    component.save();

    expect(toastrSpy.error).toHaveBeenCalledWith(
      'A checklist template with this name already exists.',
    );
    expect(component.saving()).toBeFalse();
  });
});
