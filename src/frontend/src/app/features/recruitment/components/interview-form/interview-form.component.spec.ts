import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { InterviewFormComponent } from './interview-form.component';
import { InterviewService } from '../../services/interview.service';
import { IInterview, IScheduleResult } from '../../models/interview.models';

describe('InterviewFormComponent', () => {
  let component: InterviewFormComponent;
  let fixture: ComponentFixture<InterviewFormComponent>;
  let serviceSpy: jasmine.SpyObj<InterviewService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const futureDate = (() => {
    const d = new Date();
    d.setDate(d.getDate() + 7);
    return `${d.getFullYear()}-${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;
  })();

  const interview = (over: Partial<IInterview> = {}): IInterview => ({
    id: 'iv-1',
    applicantId: 'app-1',
    vacancyId: 'vac-1',
    roundNumber: 2,
    interviewType: 'InPerson',
    scheduledDate: futureDate,
    startTime: '10:00',
    durationMinutes: 90,
    location: 'Room 1',
    videoLink: null,
    notes: 'Bring laptop',
    status: 'Scheduled',
    interviewers: [
      { employeeId: 'e1', name: 'Ada Lovelace', department: 'Eng' },
    ],
    ...over,
  });

  const result = (over: Partial<IScheduleResult> = {}): IScheduleResult => ({
    interview: interview(),
    warnings: [],
    ...over,
  });

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('InterviewService', [
      'schedule',
      'reschedule',
      'searchEmployees',
    ]);
    serviceSpy.schedule.and.returnValue(of(result()));
    serviceSpy.reschedule.and.returnValue(of(result()));
    serviceSpy.searchEmployees.and.returnValue(
      of([{ id: 'e1', label: 'Ada Lovelace', sublabel: 'Eng' }]),
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [InterviewFormComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: InterviewService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(InterviewFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('applicantId', 'app-1');
    fixture.componentRef.setInput('vacancyId', 'vac-1');
    fixture.componentRef.setInput('applicantName', 'Ada Lovelace');
    fixture.detectChanges();
  });

  it('creates and defaults to a 60-minute in-person interview (FR-1)', () => {
    expect(component).toBeTruthy();
    expect(component.form.controls.durationMinutes.value).toBe(60);
    expect(component.form.controls.interviewType.value).toBe('InPerson');
    expect(component.isEdit()).toBeFalse();
  });

  it('does not submit when no interviewer is selected (≥1 required)', () => {
    component.form.patchValue({
      scheduledDate: futureDate,
      startTime: '09:00',
      location: 'Room 2',
    });
    component.submit();
    expect(serviceSpy.schedule).not.toHaveBeenCalled();
    expect(component.form.controls.interviewerIds.invalid).toBeTrue();
  });

  it('requires a location for in-person interviews (conditional validator)', () => {
    component.addInterviewer({ id: 'e1', label: 'Ada Lovelace' });
    component.form.patchValue({
      scheduledDate: futureDate,
      startTime: '09:00',
      interviewType: 'InPerson',
      location: '',
    });
    component.submit();
    expect(serviceSpy.schedule).not.toHaveBeenCalled();
    expect(component.form.controls.location.invalid).toBeTrue();
  });

  it('requires a video link for video interviews (conditional validator)', () => {
    component.addInterviewer({ id: 'e1', label: 'Ada Lovelace' });
    component.form.controls.interviewType.setValue('Video');
    component.form.patchValue({
      scheduledDate: futureDate,
      startTime: '09:00',
      videoLink: '',
    });
    component.submit();
    expect(serviceSpy.schedule).not.toHaveBeenCalled();
    expect(component.form.controls.videoLink.invalid).toBeTrue();
  });

  it('blocks a past date/time (BR-3 / NFR-6)', () => {
    component.addInterviewer({ id: 'e1', label: 'Ada Lovelace' });
    component.form.patchValue({
      scheduledDate: '2020-01-01',
      startTime: '09:00',
      location: 'Room 2',
    });
    component.submit();
    expect(component.pastError()).toBeTrue();
    expect(serviceSpy.schedule).not.toHaveBeenCalled();
  });

  it('schedules a valid in-person interview and emits saved (AC-1)', () => {
    let saved: IInterview | undefined;
    component.saved.subscribe((iv) => (saved = iv));

    component.addInterviewer({ id: 'e1', label: 'Ada Lovelace' });
    component.form.patchValue({
      scheduledDate: futureDate,
      startTime: '09:00',
      interviewType: 'InPerson',
      location: 'Room 2',
      notes: 'hello',
    });
    component.submit();

    expect(serviceSpy.schedule).toHaveBeenCalled();
    const [appId, req] = serviceSpy.schedule.calls.mostRecent().args;
    expect(appId).toBe('app-1');
    expect(req.interviewerIds).toEqual(['e1']);
    expect(req.location).toBe('Room 2');
    expect(req.videoLink).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(saved).toBeTruthy();
  });

  it('surfaces soft conflict warnings as toasts on success (FR-7)', () => {
    serviceSpy.schedule.and.returnValue(
      of(result({ warnings: ['Ada is double-booked'] })),
    );
    component.addInterviewer({ id: 'e1', label: 'Ada Lovelace' });
    component.form.patchValue({
      scheduledDate: futureDate,
      startTime: '09:00',
      location: 'Room 2',
    });
    component.submit();
    expect(toastrSpy.warning).toHaveBeenCalledWith(
      'Ada is double-booked',
      'Scheduling conflict',
    );
  });

  it('shows an error toast when scheduling fails', () => {
    serviceSpy.schedule.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { message: 'Bad slot' },
          }),
      ),
    );
    component.addInterviewer({ id: 'e1', label: 'Ada Lovelace' });
    component.form.patchValue({
      scheduledDate: futureDate,
      startTime: '09:00',
      location: 'Room 2',
    });
    component.submit();
    expect(toastrSpy.error).toHaveBeenCalledWith('Bad slot');
    expect(component.saving()).toBeFalse();
  });

  it('manages the interviewer multi-select (add / dedupe / remove)', () => {
    component.addInterviewer({ id: 'e1', label: 'Ada' });
    component.addInterviewer({ id: 'e1', label: 'Ada' }); // dedupe
    expect(component.selected().length).toBe(1);
    component.addInterviewer({ id: 'e2', label: 'Bob' });
    expect(component.selected().length).toBe(2);
    component.removeInterviewer('e1');
    expect(component.selected().map((s) => s.id)).toEqual(['e2']);
    expect(component.form.controls.interviewerIds.value).toEqual(['e2']);
  });

  it('pre-fills and reschedules in edit mode (AC-3)', () => {
    fixture.componentRef.setInput('interview', interview({ id: 'iv-7' }));
    fixture.detectChanges();
    expect(component.isEdit()).toBeTrue();
    expect(component.selected().length).toBe(1);
    expect(component.form.controls.location.value).toBe('Room 1');

    component.submit();
    expect(serviceSpy.reschedule).toHaveBeenCalled();
    expect(serviceSpy.reschedule.calls.mostRecent().args[0]).toBe('iv-7');
    expect(serviceSpy.schedule).not.toHaveBeenCalled();
  });

  it('emits cancelled on close()', () => {
    let cancelled = false;
    component.cancelled.subscribe(() => (cancelled = true));
    component.close();
    expect(cancelled).toBeTrue();
  });
});
