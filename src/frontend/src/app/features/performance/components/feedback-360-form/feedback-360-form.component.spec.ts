import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { Feedback360FormComponent } from './feedback-360-form.component';
import { Feedback360Service } from '../../services/feedback-360.service';
import { IFeedbackForm } from '../../models/feedback-360.models';

function makeForm(overrides: Partial<IFeedbackForm> = {}): IFeedbackForm {
  return {
    assignmentId: 'a-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    revieweeId: 'e-1',
    revieweeName: 'Alex Doe',
    revieweeJobTitle: 'Engineer',
    category: 'Peer',
    ratingScaleMax: 5,
    submitted: false,
    submittedOn: null,
    anonymous: true,
    questions: [
      {
        questionId: 'q-1',
        kind: 'Competency',
        title: 'Communication',
        rating: null,
        comment: '',
      },
      {
        questionId: 'q-2',
        kind: 'Competency',
        title: 'Teamwork',
        rating: null,
        comment: '',
      },
    ],
    ...overrides,
  };
}

describe('Feedback360FormComponent', () => {
  let fixture: ComponentFixture<Feedback360FormComponent>;
  let component: Feedback360FormComponent;
  let serviceSpy: jasmine.SpyObj<Feedback360Service>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(form: IFeedbackForm = makeForm()): Promise<void> {
    serviceSpy = jasmine.createSpyObj<Feedback360Service>(
      'Feedback360Service',
      ['getFeedbackForm', 'submitFeedback'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getFeedbackForm.and.returnValue(of(form));

    await TestBed.configureTestingModule({
      imports: [Feedback360FormComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        { provide: Feedback360Service, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'a-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Feedback360FormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('renders one card per question (FR-4)', async () => {
    await setup();
    const cards = fixture.nativeElement.querySelectorAll(
      '[data-testid="question-card"]',
    );
    expect(cards.length).toBe(2);
  });

  it('renders stars equal to the tenant scale max', async () => {
    await setup(makeForm({ ratingScaleMax: 5 }));
    expect(component.stars()).toEqual([1, 2, 3, 4, 5]);
  });

  it('cannot submit until every question is rated (AC-3)', async () => {
    await setup();
    expect(component.canSubmit()).toBeFalse();
    expect(component.unrated()).toEqual(['Communication', 'Teamwork']);

    component.setRating(component.form()!.questions[0], 4);
    component.setRating(component.form()!.questions[1], 3);
    fixture.detectChanges();

    expect(component.canSubmit()).toBeTrue();
    expect(component.unrated()).toEqual([]);
  });

  it('submit() POSTs all answers and locks on success (AC-3)', async () => {
    await setup();
    component.setRating(component.form()!.questions[0], 4);
    component.setRating(component.form()!.questions[1], 5);
    component.setComment(component.form()!.questions[0], 'Clear comms');

    const submitted = makeForm({
      submitted: true,
      submittedOn: '2026-06-10T10:00:00Z',
    });
    serviceSpy.submitFeedback.and.returnValue(of(submitted));

    component.submit();
    fixture.detectChanges();

    expect(serviceSpy.submitFeedback).toHaveBeenCalledWith('a-1', {
      items: [
        { questionId: 'q-1', rating: 4, comment: 'Clear comms' },
        { questionId: 'q-2', rating: 5, comment: '' },
      ],
    });
    expect(component.form()?.submitted).toBeTrue();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('does not submit when unrated (guard)', async () => {
    await setup();
    component.submit();
    expect(serviceSpy.submitFeedback).not.toHaveBeenCalled();
  });

  it('shows the submitted banner and hides the submit button when locked', async () => {
    await setup(
      makeForm({
        submitted: true,
        submittedOn: '2026-06-10T10:00:00Z',
        questions: [
          {
            questionId: 'q-1',
            kind: 'Competency',
            title: 'Communication',
            rating: 4,
            comment: 'Done',
          },
        ],
      }),
    );
    const banner = fixture.nativeElement.querySelector(
      '[data-testid="submitted-banner"]',
    );
    const submitBtn = fixture.nativeElement.querySelector(
      '[data-testid="submit-feedback"]',
    );
    expect(banner).toBeTruthy();
    expect(submitBtn).toBeNull();
  });

  it('toasts an error when submit fails', async () => {
    await setup();
    component.setRating(component.form()!.questions[0], 4);
    component.setRating(component.form()!.questions[1], 4);
    serviceSpy.submitFeedback.and.returnValue(
      throwError(() => new Error('boom')),
    );
    component.submit();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });
});
