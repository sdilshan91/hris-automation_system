import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { ReviewSignoffComponent } from './review-signoff.component';
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
    status: 'NotesDraft',
    ratingScaleMax: 5,
    finalScore: 87,
    meetingNotesHtml: '',
    managerSignature: null,
    employeeSignature: null,
    disputeComments: null,
    disputedOn: null,
    employeeViewed: false,
    goals: [{ goalId: 'g-1', title: 'Improve NPS', weight: 100, managerRating: 4 }],
    exportAvailable: true,
    ...overrides,
  };
}

describe('ReviewSignoffComponent (manager side)', () => {
  let fixture: ComponentFixture<ReviewSignoffComponent>;
  let component: ReviewSignoffComponent;
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
      imports: [ReviewSignoffComponent],
      providers: [
        provideRouter([]),
        { provide: ReviewSignoffService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => 'e-1' },
              queryParamMap: { get: () => 'rv-1' },
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ReviewSignoffComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('seeds the editor with the pre-populated template when no notes exist (AC-1)', async () => {
    await setup(makeRecord({ meetingNotesHtml: '' }));
    // hydrateNotes seeds the live notes signal with the template synchronously.
    expect(component.notesHtml()).toContain('Key strengths');
    expect(component.notesHtml()).toContain('Improve NPS');
    expect(component.editable()).toBeTrue();
    expect(component.notesEmpty()).toBeFalse();
  });

  it('saveNotes() PUTs the current notes and shows a toast', async () => {
    await setup(makeRecord());
    serviceSpy.saveNotes.and.returnValue(of(makeRecord()));

    component.notesHtml.set('<p>Edited notes</p>');
    component.saveNotes();

    expect(serviceSpy.saveNotes).toHaveBeenCalledWith('e-1', {
      meetingNotesHtml: '<p>Edited notes</p>',
    });
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('requestSignoff() transitions the record to PendingEmployeeSignOff (AC-2)', async () => {
    await setup(makeRecord());
    serviceSpy.requestSignoff.and.returnValue(
      of(makeRecord({ status: 'PendingEmployeeSignOff' })),
    );

    component.notesHtml.set('<p>Final notes</p>');
    component.requestSignoff();

    expect(serviceSpy.requestSignoff).toHaveBeenCalledWith('e-1', {
      meetingNotesHtml: '<p>Final notes</p>',
    });
    expect(component.record()?.status).toBe('PendingEmployeeSignOff');
    expect(component.editable()).toBeFalse();
    fixture.detectChanges();
    // Once locked, the completed record component is shown instead of the editor.
    expect(
      fixture.nativeElement.querySelector('[data-testid="review-record"]'),
    ).toBeTruthy();
  });

  it('blocks requestSignoff() when notes are empty', async () => {
    await setup(makeRecord());
    component.notesHtml.set('<p></p>');
    component.requestSignoff();

    expect(serviceSpy.requestSignoff).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('shows the dispute escalation banner and lets HR resolve (BR-4)', async () => {
    await setup(
      makeRecord({ status: 'Disputed', disputeComments: 'I disagree' }),
    );
    serviceSpy.resolveDispute.and.returnValue(
      of(makeRecord({ status: 'NotesDraft' })),
    );

    expect(
      fixture.nativeElement.querySelector('[data-testid="dispute-banner"]'),
    ).toBeTruthy();

    component.resolve('Amend');
    expect(serviceSpy.resolveDispute).toHaveBeenCalledWith('e-1', {
      resolution: 'Amend',
    });
    expect(component.record()?.status).toBe('NotesDraft');
  });

  it('surfaces a load error with retry', async () => {
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
    serviceSpy.getSignoff.and.returnValue(throwError(() => new Error('boom')));

    await TestBed.configureTestingModule({
      imports: [ReviewSignoffComponent],
      providers: [
        provideRouter([]),
        { provide: ReviewSignoffService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => 'e-1' },
              queryParamMap: { get: () => 'rv-1' },
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ReviewSignoffComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.loadError()).toBeTruthy();
  });
});
