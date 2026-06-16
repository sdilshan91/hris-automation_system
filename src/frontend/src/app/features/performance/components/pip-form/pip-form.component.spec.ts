import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { PipFormComponent } from './pip-form.component';
import { PipService } from '../../services/pip.service';
import { IPip, IPipDraft } from '../../models/pip.models';

function makeDraft(overrides: Partial<IPipDraft> = {}): IPipDraft {
  return {
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    suggestedReason: 'Below Q1 threshold',
    hasActivePip: false,
    escalationOptions: ['Demotion', 'TerminationRecommendation'],
    ...overrides,
  };
}

function makePip(): IPip {
  return {
    pipId: 'pip-9',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    status: 'Active',
    reason: 'r',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    mentorName: null,
    objectives: [],
    escalationAction: 'Demotion',
    escalation: null,
    acknowledgement: 'Pending',
    acknowledgedSignature: null,
    outcome: null,
  };
}

describe('PipFormComponent (AC-1/AC-2 create form)', () => {
  let fixture: ComponentFixture<PipFormComponent>;
  let component: PipFormComponent;
  let serviceSpy: jasmine.SpyObj<PipService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(
    draft: IPipDraft = makeDraft(),
    queryParams: Record<string, string> = { employeeId: 'e-1' },
  ): Promise<void> {
    serviceSpy = jasmine.createSpyObj<PipService>('PipService', [
      'getDraft',
      'createPip',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getDraft.and.returnValue(of(draft));

    await TestBed.configureTestingModule({
      imports: [PipFormComponent],
      providers: [
        provideRouter([]),
        { provide: PipService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: { get: (k: string) => queryParams[k] ?? null },
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PipFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  function fillValidObjective(): void {
    const obj = component.objectives.at(0);
    obj.patchValue({
      title: 'Improve code review turnaround',
      description: 'Review PRs within 24h',
      successCriteria: '90% within 24h over 4 weeks',
      dueDate: '2026-07-15',
    });
  }

  it('pre-fills the employee + suggested reason from the draft (AC-1)', async () => {
    await setup();
    expect(component.prefilled()).toBeTrue();
    expect(component.form.get('employeeName')!.value).toBe('Alex Doe');
    expect(component.form.get('employeeName')!.disabled).toBeTrue();
    expect(component.form.get('reason')!.value).toBe('Below Q1 threshold');
  });

  it('seeds escalation options from the tenant draft', async () => {
    await setup();
    expect(component.escalationOptions()).toEqual([
      'Demotion',
      'TerminationRecommendation',
    ]);
    expect(component.form.get('escalationAction')!.value).toBe('Demotion');
  });

  it('BR-3: blocks submit when duration is under 30 days', async () => {
    await setup();
    fillValidObjective();
    component.form.patchValue({
      reason: 'Performance below expectations',
      startDate: '2026-06-01',
      endDate: '2026-06-20', // 19 days
    });
    expect(component.durationTooShort()).toBeTrue();
    expect(component.canSubmit()).toBeFalse();

    component.submit();
    expect(serviceSpy.createPip).not.toHaveBeenCalled();
  });

  it('BR-3: allows submit at exactly the 30-day minimum', async () => {
    await setup();
    fillValidObjective();
    component.form.patchValue({
      reason: 'Performance below expectations',
      startDate: '2026-06-01',
      endDate: '2026-07-01', // 30 days
    });
    expect(component.durationValid()).toBeTrue();
    expect(component.canSubmit()).toBeTrue();
  });

  it('FR-1: requires at least one objective', async () => {
    await setup();
    component.form.patchValue({
      reason: 'Performance below expectations',
      startDate: '2026-06-01',
      endDate: '2026-08-30',
    });
    fillValidObjective();
    expect(component.canSubmit()).toBeTrue();

    component.removeObjective(0);
    expect(component.objectives.length).toBe(0);
    expect(component.canSubmit()).toBeFalse();
  });

  it('addObjective / addCheckpoint grow the form arrays', async () => {
    await setup();
    expect(component.objectives.length).toBe(1);
    component.addObjective();
    expect(component.objectives.length).toBe(2);

    expect(component.checkpointDates.length).toBe(0);
    component.addCheckpoint();
    expect(component.checkpointDates.length).toBe(1);
  });

  it('applyPreset sets end = start + preset days', async () => {
    await setup();
    component.form.patchValue({ startDate: '2026-06-01' });
    component.applyPreset(60);
    expect(component.form.get('endDate')!.value).toBe('2026-07-31');
  });

  it('BR-2: blocks submit when the employee already has an active PIP', async () => {
    await setup(makeDraft({ hasActivePip: true }));
    fillValidObjective();
    component.form.patchValue({
      reason: 'r',
      startDate: '2026-06-01',
      endDate: '2026-08-30',
    });
    expect(component.hasActivePip()).toBeTrue();
    expect(component.canSubmit()).toBeFalse();
  });

  it('AC-2: submits the create request and navigates to the detail on success', async () => {
    await setup();
    const router = TestBed.inject(Router);
    const navSpy = spyOn(router, 'navigate');
    serviceSpy.createPip.and.returnValue(of(makePip()));

    fillValidObjective();
    component.form.patchValue({
      reason: 'Performance below expectations',
      startDate: '2026-06-01',
      endDate: '2026-08-30',
      mentorId: 'm-1',
    });
    component.submit();

    expect(serviceSpy.createPip).toHaveBeenCalled();
    const req = serviceSpy.createPip.calls.mostRecent().args[0];
    expect(req.employeeId).toBe('e-1');
    expect(req.objectives.length).toBe(1);
    expect(req.mentorId).toBe('m-1');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(navSpy).toHaveBeenCalledWith(['/performance/pips', 'pip-9']);
  });

  it('shows an error toast when create fails', async () => {
    await setup();
    serviceSpy.createPip.and.returnValue(
      throwError(() => new Error('boom')),
    );
    fillValidObjective();
    component.form.patchValue({
      reason: 'Performance below expectations',
      startDate: '2026-06-01',
      endDate: '2026-08-30',
    });
    component.submit();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });
});
