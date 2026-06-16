import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
} from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { MyReviewComponent } from './my-review.component';
import { SelfAssessmentService } from '../../services/self-assessment.service';
import {
  AUTO_SAVE_INTERVAL_MS,
  ISelfAssessment,
  WINDOW_CLOSED_MESSAGE,
} from '../../models/self-assessment.models';

function makeAssessment(
  overrides: Partial<ISelfAssessment> = {},
): ISelfAssessment {
  return {
    id: 'sa-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    status: 'Draft',
    windowOpen: true,
    ratingScaleMax: 5,
    windowClosesOn: '2026-12-31',
    weightedScore: null,
    submittedOn: null,
    goals: [
      {
        goalId: 'g-1',
        title: 'Improve NPS',
        description: 'Lift NPS',
        weight: 60,
        targetValue: '85',
        measurementUnit: 'score',
        dueDate: '2026-06-30',
        selfRating: null,
        achievementPercent: null,
        comment: '',
        attachments: [],
      },
      {
        goalId: 'g-2',
        title: 'Ship feature X',
        description: 'Deliver X',
        weight: 40,
        targetValue: 'GA',
        measurementUnit: 'release',
        dueDate: '2026-09-30',
        selfRating: null,
        achievementPercent: null,
        comment: '',
        attachments: [],
      },
    ],
    ...overrides,
  };
}

const LONG_COMMENT = 'This is a sufficiently long self-assessment comment.';

describe('MyReviewComponent', () => {
  let fixture: ComponentFixture<MyReviewComponent>;
  let component: MyReviewComponent;
  let serviceSpy: jasmine.SpyObj<SelfAssessmentService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(assessment: ISelfAssessment): Promise<void> {
    serviceSpy = jasmine.createSpyObj<SelfAssessmentService>(
      'SelfAssessmentService',
      ['getActive', 'saveDraft', 'submit', 'uploadAttachment', 'deleteAttachment'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getActive.and.returnValue(of(assessment));
    serviceSpy.saveDraft.and.returnValue(of(assessment));
    serviceSpy.submit.and.returnValue(
      of({ ...assessment, status: 'Submitted' }),
    );

    await TestBed.configureTestingModule({
      imports: [MyReviewComponent],
      providers: [
        { provide: SelfAssessmentService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyReviewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  /** Fill goal i's editable controls. */
  function fillGoal(i: number, rating: number | null, comment: string): void {
    const group = component.goals.at(i);
    group.get('selfRating')!.setValue(rating);
    group.get('comment')!.setValue(comment);
    fixture.detectChanges();
  }

  it('loads and lists the assigned goals', async () => {
    await setup(makeAssessment());
    expect(serviceSpy.getActive).toHaveBeenCalledTimes(1);
    expect(component.totalGoals()).toBe(2);
    expect(component.canEdit()).toBeTrue();
  });

  describe('submit gating (AC-2 / BR-2 / FR-3)', () => {
    it('blocks submit when a goal is unrated', async () => {
      await setup(makeAssessment());
      fillGoal(0, 4, LONG_COMMENT);
      // goal 1 left unrated
      expect(component.canSubmit()).toBeFalse();
      component.submit();
      expect(serviceSpy.submit).not.toHaveBeenCalled();
    });

    it('blocks submit when a comment is shorter than 20 chars', async () => {
      await setup(makeAssessment());
      fillGoal(0, 4, LONG_COMMENT);
      fillGoal(1, 5, 'too short');
      expect(component.canSubmit()).toBeFalse();
      component.submit();
      expect(serviceSpy.submit).not.toHaveBeenCalled();
    });

    it('allows submit when every goal is rated with a long comment', async () => {
      await setup(makeAssessment());
      fillGoal(0, 4, LONG_COMMENT);
      fillGoal(1, 5, LONG_COMMENT);
      expect(component.canSubmit()).toBeTrue();
      component.submit();
      expect(serviceSpy.submit).toHaveBeenCalledTimes(1);
    });

    it('locks the view (read-only) after a successful submit', async () => {
      await setup(makeAssessment());
      fillGoal(0, 4, LONG_COMMENT);
      fillGoal(1, 5, LONG_COMMENT);
      component.submit();
      fixture.detectChanges();
      expect(component.isSubmitted()).toBeTrue();
      expect(component.canEdit()).toBeFalse();
      expect(component.form.disabled).toBeTrue();
    });
  });

  describe('progress indicator (§8)', () => {
    it('counts only fully-completed goals as rated', async () => {
      await setup(makeAssessment());
      expect(component.ratedCount()).toBe(0);
      fillGoal(0, 4, LONG_COMMENT);
      expect(component.ratedCount()).toBe(1);
      fillGoal(1, 5, 'short'); // incomplete — short comment
      expect(component.ratedCount()).toBe(1);
    });

    it('computes the live weighted score from ratings and weights', async () => {
      await setup(makeAssessment());
      // goal0 weight 60 rated 5/5; goal1 weight 40 unrated → 60% earned of 100
      fillGoal(0, 5, LONG_COMMENT);
      expect(component.liveScore()).toBe(60);
    });
  });

  describe('save as draft (AC-3 / FR-6)', () => {
    it('persists a partial draft without the submit gate', async () => {
      await setup(makeAssessment());
      fillGoal(0, 3, ''); // partial — would never pass submit
      component.saveDraft();
      expect(serviceSpy.saveDraft).toHaveBeenCalledTimes(1);
      const [, request] = serviceSpy.saveDraft.calls.mostRecent().args;
      expect(request.goals[0].goalId).toBe('g-1');
      expect(request.goals[0].selfRating).toBe(3);
    });
  });

  describe('closed-window read-only (AC-4)', () => {
    it('shows the closed message and disables editing', async () => {
      await setup(makeAssessment({ windowOpen: false }));
      expect(component.canEdit()).toBeFalse();
      expect(component.form.disabled).toBeTrue();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain(WINDOW_CLOSED_MESSAGE);
    });

    it('does not call submit when the window is closed', async () => {
      await setup(makeAssessment({ windowOpen: false }));
      component.submit();
      expect(serviceSpy.submit).not.toHaveBeenCalled();
    });
  });

  describe('auto-save (NFR-3)', () => {
    // TestBed is configured async (compileComponents); fakeAsync can't await it.
    // Configure in beforeEach, then create the component synchronously per test.
    beforeEach(async () => {
      const assessment = makeAssessment();
      serviceSpy = jasmine.createSpyObj<SelfAssessmentService>(
        'SelfAssessmentService',
        [
          'getActive',
          'saveDraft',
          'submit',
          'uploadAttachment',
          'deleteAttachment',
        ],
      );
      toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
        'success',
        'error',
      ]);
      serviceSpy.getActive.and.returnValue(of(assessment));
      serviceSpy.saveDraft.and.returnValue(of(assessment));

      await TestBed.configureTestingModule({
        imports: [MyReviewComponent],
        providers: [
          { provide: SelfAssessmentService, useValue: serviceSpy },
          { provide: ToastrService, useValue: toastrSpy },
        ],
      }).compileComponents();
    });

    it('auto-saves a dirty draft after the 60s interval', fakeAsync(() => {
      fixture = TestBed.createComponent(MyReviewComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      fillGoal(0, 3, LONG_COMMENT); // makes the form dirty (pendingChanges)
      tick(AUTO_SAVE_INTERVAL_MS);
      expect(serviceSpy.saveDraft).toHaveBeenCalledTimes(1);
      component.ngOnDestroy(); // tear down the interval so fakeAsync settles
    }));

    it('does not auto-save when there are no pending changes', fakeAsync(() => {
      fixture = TestBed.createComponent(MyReviewComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      tick(AUTO_SAVE_INTERVAL_MS);
      expect(serviceSpy.saveDraft).not.toHaveBeenCalled();
      component.ngOnDestroy();
    }));
  });

  describe('load error', () => {
    it('shows a retry on load failure', async () => {
      serviceSpy = jasmine.createSpyObj<SelfAssessmentService>(
        'SelfAssessmentService',
        ['getActive', 'saveDraft', 'submit', 'uploadAttachment', 'deleteAttachment'],
      );
      toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
        'success',
        'error',
      ]);
      serviceSpy.getActive.and.returnValue(
        throwError(() => new Error('boom')),
      );

      await TestBed.configureTestingModule({
        imports: [MyReviewComponent],
        providers: [
          { provide: SelfAssessmentService, useValue: serviceSpy },
          { provide: ToastrService, useValue: toastrSpy },
        ],
      }).compileComponents();

      fixture = TestBed.createComponent(MyReviewComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      expect(component.loadError()).toBeTruthy();
    });
  });
});
