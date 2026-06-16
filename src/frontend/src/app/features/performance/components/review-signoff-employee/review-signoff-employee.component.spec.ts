import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { ReviewSignoffEmployeeComponent } from './review-signoff-employee.component';
import { ReviewSignoffService } from '../../services/review-signoff.service';
import { IReviewSignoff } from '../../models/review-signoff.models';

function makeRecord(overrides: Partial<IReviewSignoff> = {}): IReviewSignoff {
  return {
    reviewId: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'PendingEmployeeSignOff',
    ratingScaleMax: 5,
    finalScore: 87,
    meetingNotesHtml: '<p>We discussed your goals.</p>',
    managerSignature: { name: 'Sam Lead', signedOn: '2026-06-10T09:00:00Z' },
    employeeSignature: null,
    disputeComments: null,
    disputedOn: null,
    employeeViewed: true,
    goals: [{ goalId: 'g-1', title: 'Improve NPS', weight: 100, managerRating: 4 }],
    exportAvailable: false,
    ...overrides,
  };
}

describe('ReviewSignoffEmployeeComponent (employee side)', () => {
  let fixture: ComponentFixture<ReviewSignoffEmployeeComponent>;
  let component: ReviewSignoffEmployeeComponent;
  let serviceSpy: jasmine.SpyObj<ReviewSignoffService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(record: IReviewSignoff): Promise<void> {
    serviceSpy = jasmine.createSpyObj<ReviewSignoffService>(
      'ReviewSignoffService',
      [
        'getSignoff',
        'saveNotes',
        'requestSignoff',
        'acknowledge',
        'dispute',
        'resolveDispute',
        'exportPdf',
      ],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getSignoff.and.returnValue(of(record));

    await TestBed.configureTestingModule({
      imports: [ReviewSignoffEmployeeComponent],
      providers: [
        provideRouter([]),
        { provide: ReviewSignoffService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => 'rv-1' } },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ReviewSignoffEmployeeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders the meeting notes and the sign-off timeline (AC-4)', async () => {
    await setup(makeRecord());
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="record-notes"]')?.innerHTML).toContain(
      'We discussed your goals.',
    );
    expect(el.querySelector('[data-testid="signoff-timeline"]')).toBeTruthy();
    const steps = el.querySelectorAll('[data-testid="timeline-step"]');
    expect(steps.length).toBeGreaterThanOrEqual(3);
  });

  it('shows respond actions only while pending the employee sign-off', async () => {
    await setup(makeRecord({ status: 'SignedOff' }));
    expect(component.canRespond()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('[data-testid="acknowledge-btn"]'),
    ).toBeFalsy();
  });

  it('acknowledge flow opens the confirmation modal then records the signature (AC-3)', async () => {
    await setup(makeRecord());
    serviceSpy.acknowledge.and.returnValue(
      of(
        makeRecord({
          status: 'SignedOff',
          employeeSignature: {
            name: 'Alex Doe',
            signedOn: '2026-06-12T10:00:00Z',
          },
        }),
      ),
    );

    component.openConfirm();
    fixture.detectChanges();
    const modal = fixture.nativeElement.querySelector(
      '[data-testid="confirm-modal"]',
    );
    expect(modal).toBeTruthy();
    expect(modal.textContent).toContain(
      'By signing, you acknowledge this review has been discussed.',
    );

    component.acknowledge();
    expect(serviceSpy.acknowledge).toHaveBeenCalledWith('rv-1');
    expect(component.record()?.status).toBe('SignedOff');
    expect(component.record()?.employeeSignature?.name).toBe('Alex Doe');
    expect(component.confirmOpen()).toBeFalse();
  });

  it('dispute submit is gated on mandatory comments ≥ minimum (FR-4)', async () => {
    await setup(makeRecord());
    serviceSpy.dispute.and.returnValue(
      of(makeRecord({ status: 'Disputed', disputeComments: 'A'.repeat(20) })),
    );

    component.startDispute();
    // Too short → gated.
    component.disputeControl.setValue('nope');
    expect(component.disputeValid()).toBeFalse();
    component.submitDispute();
    expect(serviceSpy.dispute).not.toHaveBeenCalled();

    // Long enough → submits with the trimmed comments.
    const comments = 'I disagree with the rating on goal 1, here is why.';
    component.disputeControl.setValue(comments);
    expect(component.disputeValid()).toBeTrue();
    component.submitDispute();

    expect(serviceSpy.dispute).toHaveBeenCalledWith('rv-1', { comments });
    expect(component.record()?.status).toBe('Disputed');
    expect(component.disputing()).toBeFalse();
  });
});
