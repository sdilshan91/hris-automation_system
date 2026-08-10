import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ToastrService } from 'ngx-toastr';

import { ManagerReviewComponent } from './manager-review.component';
import { ManagerReviewService } from '../../services/manager-review.service';
import { IManagerReview } from '../../models/manager-review.models';

const LONG_COMMENT = 'This is a sufficiently long manager comment for the goal.';

function makeReview(overrides: Partial<IManagerReview> = {}): IManagerReview {
  return {
    id: 'rv-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    status: 'SelfAssessmentSubmitted',
    windowOpen: true,
    ratingScaleMax: 5,
    selfScore: 80,
    managerScore: null,
    finalScore: null,
    summaryComment: '',
    flag: 'None',
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
        selfRating: 4,
        selfComment: 'I drove the NPS programme.',
        managerRating: null,
        managerComment: '',
      },
      {
        goalId: 'g-2',
        title: 'Ship feature X',
        description: 'Deliver X',
        weight: 40,
        targetValue: 'GA',
        measurementUnit: 'release',
        dueDate: '2026-09-30',
        selfRating: 3,
        selfComment: 'Shipped on time.',
        managerRating: null,
        managerComment: '',
      },
    ],
    ...overrides,
  };
}

describe('ManagerReviewComponent', () => {
  let fixture: ComponentFixture<ManagerReviewComponent>;
  let component: ManagerReviewComponent;
  let serviceSpy: jasmine.SpyObj<ManagerReviewService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  async function setup(review: IManagerReview): Promise<void> {
    serviceSpy = jasmine.createSpyObj<ManagerReviewService>(
      'ManagerReviewService',
      ['getTeam', 'getEmployeeReview', 'saveDraft', 'submit'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getEmployeeReview.and.returnValue(of(review));

    await TestBed.configureTestingModule({
      imports: [ManagerReviewComponent],
      providers: [
        provideRouter([]),
        { provide: ManagerReviewService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => 'e-1' } },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ManagerReviewComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  /** Fully rate every goal so the submit gate is satisfied. */
  function completeAllGoals(): void {
    component.goals.controls.forEach((ctrl) => {
      ctrl.get('managerRating')?.setValue(5);
      ctrl.get('managerComment')?.setValue(LONG_COMMENT);
    });
    fixture.detectChanges();
  }

  // ─── AC-1: side-by-side rendering of self vs manager ──────────

  it('renders the employee self-rating and self-comment alongside manager inputs (AC-1)', async () => {
    await setup(makeReview());

    const selfPanels = fixture.nativeElement.querySelectorAll(
      '[data-testid="self-panel"]',
    );
    const managerPanels = fixture.nativeElement.querySelectorAll(
      '[data-testid="manager-panel"]',
    );
    expect(selfPanels.length).toBe(2);
    expect(managerPanels.length).toBe(2);

    const selfComments = fixture.nativeElement.querySelectorAll(
      '[data-testid="self-comment"]',
    );
    expect((selfComments[0] as HTMLElement).textContent).toContain(
      'I drove the NPS programme.',
    );

    const selfRatings = fixture.nativeElement.querySelectorAll(
      '[data-testid="self-rating"]',
    );
    expect((selfRatings[0] as HTMLElement).textContent).toContain('4/5');
  });

  it('uses the tenant rating scale for the manager rating radiogroup (FR-2)', async () => {
    await setup(makeReview({ ratingScaleMax: 10 }));
    // 10 stars per goal × 2 goals = 20 rating radios (within the rating radiogroups
    // only — the flag control is a separate radiogroup).
    const ratingGroups = fixture.nativeElement.querySelectorAll(
      '[role="radiogroup"][aria-label^="Manager rating"]',
    );
    const ratingRadios = Array.from(ratingGroups).flatMap((g) =>
      Array.from((g as HTMLElement).querySelectorAll('[role="radio"]')),
    );
    expect(ratingRadios.length).toBe(20);
  });

  // ─── AC-2 / AC-3: submit gating + unrated list ───────────────

  it('keeps canSubmit false until every goal is rated with a ≥20 char comment', async () => {
    await setup(makeReview());
    expect(component.canSubmit()).toBeFalse();

    // Rate only the first goal.
    component.goals.at(0).get('managerRating')?.setValue(4);
    component.goals.at(0).get('managerComment')?.setValue(LONG_COMMENT);
    fixture.detectChanges();
    expect(component.canSubmit()).toBeFalse();

    completeAllGoals();
    expect(component.canSubmit()).toBeTrue();
  });

  it('shows a validation error LISTING unrated goals on a blocked submit (AC-3)', async () => {
    await setup(makeReview());

    // Rate only goal 1; leave goal 2 unrated.
    component.goals.at(0).get('managerRating')?.setValue(4);
    component.goals.at(0).get('managerComment')?.setValue(LONG_COMMENT);
    fixture.detectChanges();

    component.submit();
    fixture.detectChanges();

    expect(serviceSpy.submit).not.toHaveBeenCalled();
    const err = fixture.nativeElement.querySelector(
      '[data-testid="unrated-error"]',
    );
    expect(err).toBeTruthy();
    expect((err as HTMLElement).textContent).toContain('Ship feature X');
    expect((err as HTMLElement).textContent).not.toContain('Improve NPS');
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('submits when all goals are rated and locks the view (AC-2/AC-5)', async () => {
    const submitted = makeReview({
      status: 'ManagerReviewSubmitted',
      managerScore: 95,
      finalScore: 90,
      submittedOn: '2026-06-10T10:00:00Z',
    });
    await setup(makeReview());
    serviceSpy.submit.and.returnValue(of(submitted));

    completeAllGoals();
    expect(component.canSubmit()).toBeTrue();

    component.submit();
    fixture.detectChanges();

    expect(serviceSpy.submit).toHaveBeenCalledWith(
      jasmine.objectContaining({
        flag: 'None',
        cycleId: 'cyc-1',
        employeeId: 'e-1',
      }),
    );
    expect(component.isLocked()).toBeTrue();
    expect(component.canEdit()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('[data-testid="locked-banner"]'),
    ).toBeTruthy();
  });

  // ─── AC-5: read-only when already submitted ──────────────────

  it('renders read-only (no submit button) when the review is already submitted', async () => {
    await setup(
      makeReview({
        status: 'ManagerReviewSubmitted',
        managerScore: 88,
        finalScore: 85,
        submittedOn: '2026-06-09T10:00:00Z',
      }),
    );

    expect(component.canEdit()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('[data-testid="submit-btn"]'),
    ).toBeNull();
    expect(
      fixture.nativeElement.querySelector('[data-testid="locked-banner"]'),
    ).toBeTruthy();
  });

  // ─── BR-4: final score is display-only when the server returns it ──

  it('displays the server-computed final score when present (BR-4)', async () => {
    await setup(
      makeReview({
        status: 'Completed',
        managerScore: 90,
        finalScore: 87,
      }),
    );
    const finalEl = fixture.nativeElement.querySelector(
      '[data-testid="final-score"]',
    );
    expect((finalEl as HTMLElement).textContent).toContain('87%');
  });

  // ─── FR-6: flag control ──────────────────────────────────────

  it('lets the manager set a follow-up flag (FR-6)', async () => {
    await setup(makeReview());
    component.setFlag('Pip');
    fixture.detectChanges();
    expect(component.flagValue()).toBe('Pip');
  });

  // ─── Window closed ───────────────────────────────────────────

  it('is read-only when the manager-review window is closed', async () => {
    await setup(makeReview({ windowOpen: false }));
    expect(component.canEdit()).toBeFalse();
    expect(
      fixture.nativeElement.querySelector('[data-testid="submit-btn"]'),
    ).toBeNull();
  });

  // ─── Load error ──────────────────────────────────────────────

  it('shows a load error with retry on failure', async () => {
    serviceSpy = jasmine.createSpyObj<ManagerReviewService>(
      'ManagerReviewService',
      ['getTeam', 'getEmployeeReview', 'saveDraft', 'submit'],
    );
    toastrSpy = jasmine.createSpyObj<ToastrService>('ToastrService', [
      'success',
      'error',
    ]);
    serviceSpy.getEmployeeReview.and.returnValue(
      throwError(() => new Error('boom')),
    );

    await TestBed.configureTestingModule({
      imports: [ManagerReviewComponent],
      providers: [
        provideRouter([]),
        { provide: ManagerReviewService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'e-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ManagerReviewComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Unable to load this employee review.',
    );
  });
});
