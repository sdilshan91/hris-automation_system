import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { ExitInterviewFormComponent } from './exit-interview-form.component';
import { ExitInterviewService } from '../../services/exit-interview.service';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  IExitInterview,
  IExitInterviewTemplate,
} from '../../models/exit-interview.models';

describe('ExitInterviewFormComponent', () => {
  let component: ExitInterviewFormComponent;
  let fixture: ComponentFixture<ExitInterviewFormComponent>;
  let serviceSpy: jasmine.SpyObj<ExitInterviewService>;
  let authSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;
  let router: Router;

  const template: IExitInterviewTemplate = {
    templateId: 'tmpl-1',
    categories: [
      {
        category: 'Reason for Leaving',
        questions: [
          { questionId: 'q1', text: 'Primary reason?', type: 'multiple_choice', options: ['Pay', 'Growth'], required: true },
          { questionId: 'q2', text: 'Rate your experience', type: 'rating', required: true },
        ],
      },
      {
        category: 'Recommendations',
        questions: [
          { questionId: 'q3', text: 'Any comments?', type: 'free_text', required: false },
          { questionId: 'q4', text: 'Would you return?', type: 'yes_no', required: true },
        ],
      },
    ],
  };

  const recorded: IExitInterview = {
    id: 'ei-1',
    offboardingInstanceId: 'off-1',
    interviewMode: 'hr_conducted',
    interviewDate: '2026-06-17',
    responses: [],
  };

  async function setup(opts: {
    isHr?: boolean;
    existing?: boolean;
  } = {}): Promise<void> {
    serviceSpy = jasmine.createSpyObj('ExitInterviewService', [
      'getTemplate',
      'getByOffboarding',
      'record',
    ]);
    serviceSpy.getTemplate.and.returnValue(of(template));
    serviceSpy.record.and.returnValue(of(recorded));
    // BR-1 probe: 404 → no existing interview (load template); else return one.
    serviceSpy.getByOffboarding.and.returnValue(
      opts.existing
        ? of(recorded)
        : throwError(() => new HttpErrorResponse({ status: 404 })),
    );

    authSpy = jasmine.createSpyObj('AuthService', ['hasRole', 'hasAnyPermission']);
    authSpy.hasAnyPermission.and.returnValue(false);
    authSpy.hasRole.and.callFake((r: string) =>
      !!opts.isHr && ['HR Officer', 'HR Manager', 'Tenant Admin'].includes(r),
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [ExitInterviewFormComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: ExitInterviewService, useValue: serviceSpy },
        { provide: AuthService, useValue: authSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'off-1' } } },
        },
      ],
    }).compileComponents();

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    fixture = TestBed.createComponent(ExitInterviewFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('offboardingId', 'off-1');
    fixture.detectChanges();
  }

  it('loads the template and builds a control per question', async () => {
    await setup();
    expect(component).toBeTruthy();
    expect(component.template()?.categories.length).toBe(2);
    expect(component.form.controls.responses.length).toBe(4);
  });

  it('derives self_service mode for a plain employee', async () => {
    await setup({ isHr: false });
    expect(component.mode()).toBe('self_service');
  });

  it('derives hr_conducted mode for an HR user', async () => {
    await setup({ isHr: true });
    expect(component.mode()).toBe('hr_conducted');
  });

  it('shows the immutable "already recorded" state when one exists (BR-1)', async () => {
    await setup({ existing: true });
    expect(component.alreadyRecorded()).toBeTrue();
    expect(serviceSpy.getTemplate).not.toHaveBeenCalled();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="already-recorded"]')).toBeTruthy();
  });

  it('starts at 0% progress and advances as required questions are answered', async () => {
    await setup();
    expect(component.progress()).toBe(0); // 3 required questions, none answered

    component.setChoice('q1', 'Pay');
    component.setRating('q2', 4);
    fixture.detectChanges();
    // 2 of 3 required answered
    expect(component.progress()).toBe(67);

    component.setYesNo('q4', true);
    fixture.detectChanges();
    expect(component.progress()).toBe(100);
  });

  it('setRating toggles the value off when the same star is re-clicked', async () => {
    await setup();
    component.setRating('q2', 3);
    expect(component.controlFor('q2').value).toBe(3);
    component.setRating('q2', 3);
    expect(component.controlFor('q2').value).toBeNull();
  });

  it('blocks submit and shows an error when required questions are unanswered', async () => {
    await setup();
    component.submit();
    expect(serviceSpy.record).not.toHaveBeenCalled();
    expect(component.submitError()).toContain('required');
  });

  it('POSTs typed responses and shows the success state on a full submit', async () => {
    await setup({ isHr: true });
    component.setChoice('q1', 'Growth');
    component.setRating('q2', 5);
    component.controlFor('q3').setValue('  great team  ');
    component.setYesNo('q4', false);
    component.setOverall(4);
    component.setRecommend(true);
    component.form.controls.additionalComments.setValue('thanks');

    component.submit();

    expect(serviceSpy.record).toHaveBeenCalledTimes(1);
    const req = serviceSpy.record.calls.mostRecent().args[0];
    expect(req.offboardingId).toBe('off-1');
    expect(req.interviewMode).toBe('hr_conducted');
    expect(req.overallExperienceRating).toBe(4);
    expect(req.wouldRecommendEmployer).toBeTrue();
    expect(req.additionalComments).toBe('thanks');
    // free_text trimmed, yes_no mapped to selectedOption, choice + rating present
    const byId = (id: string) => req.responses.find((r) => r.questionId === id);
    expect(byId('q1')!.selectedOption).toBe('Growth');
    expect(byId('q2')!.rating).toBe(5);
    expect(byId('q3')!.freeText).toBe('great team');
    expect(byId('q4')!.selectedOption).toBe('No');

    expect(component.submitted()).toBeTrue();
    expect(toastrSpy.success).toHaveBeenCalled();
  });

  it('surfaces a server error on submit failure without entering the success state', async () => {
    await setup();
    serviceSpy.record.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ error: { message: 'Duplicate' }, status: 409 }),
      ),
    );
    component.setChoice('q1', 'Pay');
    component.setRating('q2', 2);
    component.setYesNo('q4', true);

    component.submit();

    expect(component.submitted()).toBeFalse();
    expect(component.submitError()).toBe('Duplicate');
  });

  it('shows a load error when the template fails to load', async () => {
    serviceSpy = jasmine.createSpyObj('ExitInterviewService', [
      'getTemplate',
      'getByOffboarding',
      'record',
    ]);
    serviceSpy.getByOffboarding.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    serviceSpy.getTemplate.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    authSpy = jasmine.createSpyObj('AuthService', ['hasRole', 'hasAnyPermission']);
    authSpy.hasAnyPermission.and.returnValue(false);
    authSpy.hasRole.and.returnValue(false);
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [ExitInterviewFormComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: ExitInterviewService, useValue: serviceSpy },
        { provide: AuthService, useValue: authSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: { get: () => 'off-1' } } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ExitInterviewFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('offboardingId', 'off-1');
    fixture.detectChanges();

    expect(component.loadError()).toContain('Unable to load');
  });
});
