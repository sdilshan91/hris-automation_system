import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { PipDetailComponent } from './pip-detail.component';
import { PipService } from '../../services/pip.service';
import { AuthService } from '../../../../core/auth/auth.service';
import { IPip } from '../../models/pip.models';

function makePip(overrides: Partial<IPip> = {}): IPip {
  return {
    pipId: 'pip-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'Active',
    reason: 'Below threshold on Q1 review',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    mentorName: 'Pat Coach',
    objectives: [
      {
        objectiveId: 'obj-1',
        title: 'Improve review turnaround',
        description: 'Review PRs within 24h',
        successCriteria: '90% within 24h',
        dueDate: '2026-07-15',
        checkpoints: [
          {
            checkpointId: 'cp-1',
            dueDate: '2026-07-01',
            recordedOn: null,
            status: null,
            notes: null,
            recordedBy: null,
            attachmentName: null,
            overdue: false,
          },
        ],
      },
    ],
    escalationAction: 'TerminationRecommendation',
    escalation: null,
    acknowledgement: 'Acknowledged',
    acknowledgedSignature: null,
    outcome: null,
    ...overrides,
  };
}

describe('PipDetailComponent', () => {
  let fixture: ComponentFixture<PipDetailComponent>;
  let component: PipDetailComponent;
  let serviceSpy: jasmine.SpyObj<PipService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  const rolesSig = signal<string[]>(['HR Officer']);

  async function setup(pip: IPip, roles: string[]): Promise<void> {
    rolesSig.set(roles);
    serviceSpy = jasmine.createSpyObj<PipService>('PipService', [
      'getPip',
      'recordCheckpoint',
      'setOutcome',
      'escalate',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getPip.and.returnValue(of(pip));

    await TestBed.configureTestingModule({
      imports: [PipDetailComponent],
      providers: [
        provideRouter([]),
        { provide: PipService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        { provide: AuthService, useValue: { roles: rolesSig } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'pip-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PipDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders the objective accordion and the timeline (§8)', async () => {
    await setup(makePip(), ['HR Officer']);
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="timeline-bar"]')).toBeTruthy();
    expect(
      el.querySelectorAll('[data-testid="objective-accordion"]').length,
    ).toBe(1);
    // first objective expanded by default → checkpoint marker present
    expect(el.querySelector('[data-testid="checkpoint-marker"]')).toBeTruthy();
  });

  it('renders the confidentiality banner (§8)', async () => {
    await setup(makePip(), ['Manager']);
    const banner = fixture.nativeElement.querySelector(
      '[data-testid="confidential-banner"]',
    );
    expect(banner?.textContent).toContain('confidential');
  });

  it('BR-1: HR sees the Set-outcome section', async () => {
    await setup(makePip(), ['HR Officer']);
    expect(component.isHr()).toBeTrue();
    expect(component.showOutcome()).toBeTrue();
    expect(
      fixture.nativeElement.querySelector('[data-testid="outcome-section"]'),
    ).toBeTruthy();
  });

  it('BR-1: a manager does NOT get close/extend (outcome) controls', async () => {
    await setup(makePip(), ['Manager']);
    expect(component.isHr()).toBeFalse();
    expect(component.showOutcome()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('[data-testid="outcome-section"]'),
    ).toBeFalsy();
  });

  it('AC-3: a manager CAN open the checkpoint modal and pick a traffic-light status', async () => {
    const pip = makePip();
    await setup(pip, ['Manager']);
    component.openCheckpoint(pip.objectives[0], pip.objectives[0].checkpoints[0]);
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="checkpoint-modal"]'),
    ).toBeTruthy();

    // status selector starts empty → invalid; notes alone not enough
    component.checkpointForm.patchValue({ notes: 'Good early progress noted.' });
    expect(component.checkpointValid()).toBeFalse();

    component.selectCheckpointStatus('AtRisk');
    expect(component.checkpointStatus()).toBe('AtRisk');
    expect(component.checkpointValid()).toBeTrue();
  });

  it('AC-3: records the checkpoint and updates the PIP', async () => {
    const pip = makePip();
    await setup(pip, ['Manager']);
    const updated = makePip({ status: 'Active' });
    serviceSpy.recordCheckpoint.and.returnValue(of(updated));

    component.openCheckpoint(pip.objectives[0], pip.objectives[0].checkpoints[0]);
    component.selectCheckpointStatus('OnTrack');
    component.checkpointForm.patchValue({ notes: 'Met all PRs in 24h.' });
    component.submitCheckpoint();

    expect(serviceSpy.recordCheckpoint).toHaveBeenCalledWith(
      'pip-1',
      { status: 'OnTrack', notes: 'Met all PRs in 24h.' },
      null,
    );
    expect(component.checkpointOpen()).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('AC-4: HR setting outcome Extended requires a new end date', async () => {
    await setup(makePip(), ['HR Officer']);
    component.outcomeForm.patchValue({ outcome: 'Extended', newEndDate: '' });
    expect(component.outcomeValid()).toBeFalse();
    component.submitOutcome();
    expect(serviceSpy.setOutcome).not.toHaveBeenCalled();

    component.outcomeForm.patchValue({ newEndDate: '2026-10-30' });
    serviceSpy.setOutcome.and.returnValue(
      of(makePip({ status: 'Extended', outcome: 'Extended' })),
    );
    component.submitOutcome();
    expect(serviceSpy.setOutcome).toHaveBeenCalledWith('pip-1', {
      outcome: 'Extended',
      newEndDate: '2026-10-30',
    });
  });

  it('AC-5: escalation section shows only for HR on a NotMet PIP, and confirms', async () => {
    await setup(makePip({ status: 'NotMet' }), ['HR Officer']);
    expect(component.showEscalation()).toBeTrue();
    expect(
      fixture.nativeElement.querySelector('[data-testid="escalation-section"]'),
    ).toBeTruthy();

    serviceSpy.escalate.and.returnValue(of(makePip({ status: 'NotMet' })));
    component.escalationForm.patchValue({
      action: 'TerminationRecommendation',
      note: 'After review board.',
    });
    component.submitEscalation();
    expect(serviceSpy.escalate).toHaveBeenCalledWith('pip-1', {
      action: 'TerminationRecommendation',
      note: 'After review board.',
    });
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('AC-5: a manager never sees the escalation section on a NotMet PIP (BR-1)', async () => {
    await setup(makePip({ status: 'NotMet' }), ['Manager']);
    expect(component.showEscalation()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('[data-testid="escalation-section"]'),
    ).toBeFalsy();
  });

  it('toggles the objective accordion', async () => {
    await setup(makePip(), ['HR Officer']);
    expect(component.isExpanded('obj-1')).toBeTrue(); // expanded by default
    component.toggleObjective('obj-1');
    expect(component.isExpanded('obj-1')).toBeFalse();
  });
});
