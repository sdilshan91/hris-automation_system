import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import {
  HttpErrorResponse,
  HttpHeaders,
  HttpResponse,
} from '@angular/common/http';

import { ApplicantDetailComponent } from './applicant-detail.component';
import { PipelineService } from '../../services/pipeline.service';
import { InterviewService } from '../../services/interview.service';
import { OfferService } from '../../services/offer.service';
import { IApplicantDetail } from '../../models/pipeline.models';
import { IInterview } from '../../models/interview.models';

describe('ApplicantDetailComponent', () => {
  let component: ApplicantDetailComponent;
  let fixture: ComponentFixture<ApplicantDetailComponent>;
  let serviceSpy: jasmine.SpyObj<PipelineService>;
  let interviewSpy: jasmine.SpyObj<InterviewService>;
  let offerSpy: jasmine.SpyObj<OfferService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const interview = (over: Partial<IInterview> = {}): IInterview => ({
    id: 'iv-1',
    applicantId: 'app-1',
    vacancyId: 'vac-1',
    roundNumber: 1,
    interviewType: 'Video',
    scheduledDate: '2026-07-01',
    startTime: '09:00',
    durationMinutes: 60,
    location: null,
    videoLink: 'https://meet/x',
    notes: null,
    status: 'Scheduled',
    interviewers: [{ employeeId: 'e1', name: 'Ada Lovelace' }],
    ...over,
  });

  const detail = (over: Partial<IApplicantDetail> = {}): IApplicantDetail => ({
    id: 'app-1',
    applicationReferenceNumber: 'APP-2026-0001',
    vacancyId: 'vac-1',
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    phone: '123',
    coverLetter: null,
    resumeFileName: 'ada-cv.pdf',
    stage: 'Screening',
    source: 'Public',
    isInternal: false,
    appliedAt: '2026-06-10T00:00:00Z',
    stageHistory: [
      {
        fromStage: 'Applied',
        toStage: 'Screening',
        changedByUserName: 'Recruiter Rita',
        reason: null,
        notes: null,
        changedAt: '2026-06-11T00:00:00Z',
      },
    ],
    ...over,
  });

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('PipelineService', [
      'getApplicant',
      'downloadResume',
      'changeStage',
    ]);
    serviceSpy.getApplicant.and.returnValue(of(detail()));
    serviceSpy.changeStage.and.returnValue(
      of({ id: 'app-1', stage: 'Interview', enteredStageAt: null, warnings: [] }),
    );

    interviewSpy = jasmine.createSpyObj('InterviewService', [
      'listForApplicant',
      'schedule',
      'reschedule',
      'cancel',
      'searchEmployees',
    ]);
    interviewSpy.listForApplicant.and.returnValue(of([interview()]));
    interviewSpy.cancel.and.returnValue(of(interview({ status: 'Cancelled' })));

    offerSpy = jasmine.createSpyObj('OfferService', [
      'listForApplicant',
      'generate',
      'send',
      'respond',
      'withdraw',
      'downloadPdf',
      'getOffer',
    ]);
    offerSpy.listForApplicant.and.returnValue(of([]));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [ApplicantDetailComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: PipelineService, useValue: serviceSpy },
        { provide: InterviewService, useValue: interviewSpy },
        { provide: OfferService, useValue: offerSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ApplicantDetailComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('applicantId', 'app-1');
    fixture.detectChanges();
  });

  it('loads the applicant on init', () => {
    expect(serviceSpy.getApplicant).toHaveBeenCalledWith('app-1');
    expect(component.detail()!.id).toBe('app-1');
    expect(component.fullName()).toBe('Ada Lovelace');
    expect(component.initials()).toBe('AL');
    expect(component.loading()).toBeFalse();
  });

  it('renders the stage-transition timeline', () => {
    component.tab.set('timeline');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Applied → Screening');
    expect(el.textContent).toContain('Recruiter Rita');
  });

  it('shows an error toast when load fails', () => {
    serviceSpy.getApplicant.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    fixture.componentRef.setInput('applicantId', 'missing');
    fixture.detectChanges();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.loading()).toBeFalse();
  });

  it('downloads the resume blob', () => {
    const res = new HttpResponse<Blob>({
      body: new Blob(['pdf']),
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="ada-cv.pdf"',
      }),
    });
    serviceSpy.downloadResume.and.returnValue(of(res));
    const createSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    const revokeSpy = spyOn(URL, 'revokeObjectURL');
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    component.downloadResume();

    expect(serviceSpy.downloadResume).toHaveBeenCalledWith('app-1');
    expect(createSpy).toHaveBeenCalled();
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeSpy).toHaveBeenCalled();
    expect(component.downloading()).toBeFalse();
  });

  it('shows an error toast when resume download fails', () => {
    serviceSpy.downloadResume.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    component.downloadResume();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.downloading()).toBeFalse();
  });

  it('emits closed on close()', () => {
    let closed = false;
    component.closed.subscribe(() => (closed = true));
    component.close();
    expect(closed).toBeTrue();
  });

  // ─── Stage transition action buttons (US-REC-004) ──────────

  it('computes the next/previous stage from the current stage (AC-1/AC-2/FR-5)', () => {
    // detail() defaults to Screening.
    expect(component.nextStageName()).toBe('Interview');
    expect(component.prevStageName()).toBe('Applied');
    expect(component.isTerminal()).toBeFalse();
  });

  it('moves forward without a reason and emits moved (AC-1/AC-2)', () => {
    let moved: string | undefined;
    component.moved.subscribe((s) => (moved = s));

    component.moveForward();

    expect(serviceSpy.changeStage).toHaveBeenCalledWith('app-1', {
      toStage: 'Interview',
      reason: undefined,
      rejectionReason: undefined,
      notes: undefined,
    });
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(moved).toBe('Interview');
    expect(component.moving()).toBeFalse();
  });

  it('opens the reason dialog when rejecting (AC-4) and does not call the API yet', () => {
    component.reject();
    expect(component.pendingTo()).toBe('Rejected');
    expect(serviceSpy.changeStage).not.toHaveBeenCalled();
  });

  it('persists the rejection with the structured reason on confirm (AC-4)', () => {
    serviceSpy.changeStage.and.returnValue(
      of({ id: 'app-1', stage: 'Rejected', enteredStageAt: null, warnings: [] }),
    );
    component.reject();
    component.confirmReasonMove({
      reason: 'Not qualified',
      rejectionReason: 'NotQualified',
      notes: 'weak fit',
    });

    expect(serviceSpy.changeStage).toHaveBeenCalledWith('app-1', {
      toStage: 'Rejected',
      reason: 'Not qualified',
      rejectionReason: 'NotQualified',
      notes: 'weak fit',
    });
    expect(component.pendingTo()).toBeNull();
  });

  it('opens the reason dialog for a backward move (FR-5)', () => {
    component.moveBack();
    expect(component.pendingTo()).toBe('Applied');
    expect(serviceSpy.changeStage).not.toHaveBeenCalled();
  });

  it('surfaces soft-gate warnings as toasts after a move (BR-4/FR-1)', () => {
    serviceSpy.changeStage.and.returnValue(
      of({
        id: 'app-1',
        stage: 'Interview',
        enteredStageAt: null,
        warnings: ['No interview scheduled yet'],
      }),
    );
    component.moveForward();
    expect(toastrSpy.warning).toHaveBeenCalledWith(
      'No interview scheduled yet',
      'Heads up',
    );
  });

  it('shows the error message when the move is rejected (e.g. vacancy closed, FR-8)', () => {
    serviceSpy.changeStage.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { message: 'Vacancy is closed.' },
          }),
      ),
    );
    component.moveForward();
    expect(toastrSpy.error).toHaveBeenCalledWith('Vacancy is closed.');
    expect(component.moving()).toBeFalse();
  });

  it('renders the structured rejection reason in the timeline (AC-4)', () => {
    serviceSpy.getApplicant.and.returnValue(
      of(
        detail({
          stage: 'Rejected',
          stageHistory: [
            {
              fromStage: 'Screening',
              toStage: 'Rejected',
              changedByUserName: 'Recruiter Rita',
              reason: null,
              rejectionReason: 'PositionFilled',
              notes: null,
              changedAt: '2026-06-12T00:00:00Z',
            },
          ],
        }),
      ),
    );
    fixture.componentRef.setInput('applicantId', 'app-2');
    fixture.detectChanges();
    component.tab.set('timeline');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Position filled');
  });

  it('shows no action buttons for a terminal stage (Hired/Rejected)', () => {
    serviceSpy.getApplicant.and.returnValue(of(detail({ stage: 'Hired' })));
    fixture.componentRef.setInput('applicantId', 'app-3');
    fixture.detectChanges();
    expect(component.isTerminal()).toBeTrue();
    expect(component.nextStageName()).toBeNull();
  });

  // ─── Interviews tab (US-REC-005) ───────────────────────────

  it('lazy-loads interview rounds when the Interviews tab opens (AC-4)', () => {
    expect(interviewSpy.listForApplicant).not.toHaveBeenCalled();
    component.tab.set('interviews');
    fixture.detectChanges();
    expect(interviewSpy.listForApplicant).toHaveBeenCalledWith('app-1');
    expect(component.interviews().length).toBe(1);
  });

  it('opens the schedule slide-over for a new round (AC-1)', () => {
    component.openSchedule();
    expect(component.showInterviewForm()).toBeTrue();
    expect(component.editingInterview()).toBeNull();
  });

  it('opens the slide-over pre-filled to reschedule (AC-3)', () => {
    const iv = interview({ id: 'iv-9' });
    component.openReschedule(iv);
    expect(component.showInterviewForm()).toBeTrue();
    expect(component.editingInterview()).toBe(iv);
  });

  it('reloads rounds after a successful save', () => {
    component.tab.set('interviews');
    fixture.detectChanges();
    interviewSpy.listForApplicant.calls.reset();
    component.onInterviewSaved();
    expect(component.showInterviewForm()).toBeFalse();
    expect(interviewSpy.listForApplicant).toHaveBeenCalledWith('app-1');
  });

  it('cancels an interview after confirmation and reloads (AC-3)', () => {
    component.tab.set('interviews');
    fixture.detectChanges();
    component.askCancel(interview({ id: 'iv-1' }));
    expect(component.cancellingInterview()).toBeTruthy();
    interviewSpy.listForApplicant.calls.reset();

    component.confirmCancel('Candidate withdrew');

    expect(interviewSpy.cancel).toHaveBeenCalledWith('iv-1', 'Candidate withdrew');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(interviewSpy.listForApplicant).toHaveBeenCalledWith('app-1');
    expect(component.cancellingInterview()).toBeNull();
  });

  it('shows an error toast when interview load fails', () => {
    interviewSpy.listForApplicant.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    component.tab.set('interviews');
    fixture.detectChanges();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.interviewsLoading()).toBeFalse();
  });
});
