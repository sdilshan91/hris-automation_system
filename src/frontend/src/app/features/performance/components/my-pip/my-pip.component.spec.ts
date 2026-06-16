import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { MyPipComponent } from './my-pip.component';
import { PipService } from '../../services/pip.service';
import { IPip, PIP_ACKNOWLEDGE_MESSAGE, PipAcknowledgement } from '../../models/pip.models';

const ACK_PENDING: PipAcknowledgement = 'Pending';
const ACK_DONE: PipAcknowledgement = 'Acknowledged';

function makePip(overrides: Partial<IPip> = {}): IPip {
  return {
    pipId: 'pip-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'Active',
    reason: 'Below threshold',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    mentorName: 'Pat Coach',
    objectives: [
      {
        objectiveId: 'obj-1',
        title: 'Improve turnaround',
        description: 'desc',
        successCriteria: 'crit',
        dueDate: '2026-07-15',
        checkpoints: [],
      },
    ],
    escalationAction: 'TerminationRecommendation',
    escalation: null,
    acknowledgement: ACK_PENDING,
    acknowledgedSignature: null,
    outcome: null,
    ...overrides,
  };
}

describe('MyPipComponent (employee BR-4)', () => {
  let fixture: ComponentFixture<MyPipComponent>;
  let component: MyPipComponent;
  let serviceSpy: jasmine.SpyObj<PipService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(pip: IPip): Promise<void> {
    serviceSpy = jasmine.createSpyObj<PipService>('PipService', [
      'getPip',
      'acknowledge',
    ]);
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getPip.and.returnValue(of(pip));

    await TestBed.configureTestingModule({
      imports: [MyPipComponent],
      providers: [
        { provide: PipService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'pip-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyPipComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders the read-only objectives + confidentiality banner', async () => {
    await setup(makePip());
    const el = fixture.nativeElement as HTMLElement;
    expect(
      el.querySelector('[data-testid="confidential-banner"]')?.textContent,
    ).toContain('confidential');
    expect(el.querySelectorAll('[data-testid="objective"]').length).toBe(1);
  });

  it('shows the acknowledge button while acknowledgement is awaiting (BR-4)', async () => {
    await setup(makePip());
    expect(component.canAcknowledge()).toBeTrue();
    expect(
      fixture.nativeElement.querySelector('[data-testid="acknowledge-btn"]'),
    ).toBeTruthy();
  });

  it('hides the acknowledge button once acknowledged', async () => {
    await setup(makePip({ acknowledgement: ACK_DONE }));
    expect(component.canAcknowledge()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('[data-testid="acknowledge-btn"]'),
    ).toBeFalsy();
    expect(
      fixture.nativeElement.querySelector('[data-testid="acknowledged-banner"]'),
    ).toBeTruthy();
  });

  it('opens the confirmation modal with the verbatim copy then records the acknowledgement', async () => {
    await setup(makePip());
    serviceSpy.acknowledge.and.returnValue(
      of(makePip({ acknowledgement: ACK_DONE })),
    );

    component.openConfirm();
    fixture.detectChanges();
    const modal = fixture.nativeElement.querySelector(
      '[data-testid="confirm-modal"]',
    );
    expect(modal).toBeTruthy();
    expect(modal.textContent).toContain(PIP_ACKNOWLEDGE_MESSAGE);

    component.acknowledge();
    expect(serviceSpy.acknowledge).toHaveBeenCalledWith('pip-1');
    expect(component.pip()?.acknowledgement).toBe(ACK_DONE);
    expect(component.confirmOpen()).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalled();
  });
});
