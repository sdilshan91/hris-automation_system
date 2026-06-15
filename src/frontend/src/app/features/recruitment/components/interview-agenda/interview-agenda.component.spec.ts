import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { InterviewAgendaComponent } from './interview-agenda.component';
import { InterviewService } from '../../services/interview.service';
import { VacancyService } from '../../services/vacancy.service';
import { IInterview } from '../../models/interview.models';

describe('InterviewAgendaComponent', () => {
  let component: InterviewAgendaComponent;
  let fixture: ComponentFixture<InterviewAgendaComponent>;
  let interviewSpy: jasmine.SpyObj<InterviewService>;
  let vacancySpy: jasmine.SpyObj<VacancyService>;
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
    applicantName: 'Jane Doe',
    vacancyTitle: 'Senior Eng',
    ...over,
  });

  beforeEach(async () => {
    interviewSpy = jasmine.createSpyObj('InterviewService', [
      'listInterviews',
      'searchEmployees',
    ]);
    interviewSpy.listInterviews.and.returnValue(
      of([
        interview({ id: 'a', scheduledDate: '2026-07-02', startTime: '11:00' }),
        interview({ id: 'b', scheduledDate: '2026-07-01', startTime: '09:00' }),
      ]),
    );
    interviewSpy.searchEmployees.and.returnValue(
      of([{ id: 'e1', label: 'Ada Lovelace', sublabel: 'Eng' }]),
    );

    vacancySpy = jasmine.createSpyObj('VacancyService', ['listVacancies']);
    vacancySpy.listVacancies.and.returnValue(
      of({
        data: [
          {
            id: 'vac-1',
            title: 'Senior Eng',
          } as never,
        ],
        total: 1,
        page: 1,
        pageSize: 200,
      }),
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', ['error']);

    await TestBed.configureTestingModule({
      imports: [InterviewAgendaComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: InterviewService, useValue: interviewSpy },
        { provide: VacancyService, useValue: vacancySpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(InterviewAgendaComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads interviews + vacancies on init (FR-5)', () => {
    expect(interviewSpy.listInterviews).toHaveBeenCalled();
    expect(component.interviews().length).toBe(2);
    expect(component.vacancies().length).toBe(1);
  });

  it('groups interviews by date, ascending (FR-5)', () => {
    const groups = component.groups();
    expect(groups.map((g) => g.date)).toEqual(['2026-07-01', '2026-07-02']);
  });

  it('re-queries with the selected filters (FR-6)', () => {
    interviewSpy.listInterviews.calls.reset();
    component.vacancyId = 'vac-1';
    component.status = 'Scheduled';
    component.from = '2026-07-01';
    component.to = '2026-07-31';
    component.applyFilters();
    const filters = interviewSpy.listInterviews.calls.mostRecent().args[0]!;
    expect(filters.vacancyId).toBe('vac-1');
    expect(filters.status).toBe('Scheduled');
    expect(filters.from).toBe('2026-07-01');
    expect(filters.to).toBe('2026-07-31');
  });

  it('filters by a picked interviewer and clears it', () => {
    interviewSpy.listInterviews.calls.reset();
    component.pickInterviewer({ id: 'e1', label: 'Ada Lovelace' });
    expect(component.selectedInterviewer()?.id).toBe('e1');
    expect(
      interviewSpy.listInterviews.calls.mostRecent().args[0]!.interviewerId,
    ).toBe('e1');

    component.clearInterviewer();
    expect(component.selectedInterviewer()).toBeNull();
    expect(
      interviewSpy.listInterviews.calls.mostRecent().args[0]!.interviewerId,
    ).toBeNull();
  });

  it('shows an error toast when the agenda fails to load', () => {
    interviewSpy.listInterviews.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    component.applyFilters();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.loading()).toBeFalse();
  });
});
