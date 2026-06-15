import { ComponentFixture, TestBed } from '@angular/core/testing';

import { InterviewCardComponent } from './interview-card.component';
import { IInterview } from '../../models/interview.models';

describe('InterviewCardComponent', () => {
  let component: InterviewCardComponent;
  let fixture: ComponentFixture<InterviewCardComponent>;

  const interview = (over: Partial<IInterview> = {}): IInterview => ({
    id: 'iv-1',
    applicantId: 'app-1',
    vacancyId: 'vac-1',
    roundNumber: 2,
    interviewType: 'in-person',
    scheduledDate: '2026-07-01',
    startTime: '09:00',
    durationMinutes: 60,
    location: 'Room 3B',
    videoLink: null,
    notes: 'Focus on system design',
    status: 'Scheduled',
    interviewers: [
      { employeeId: 'e1', name: 'Ada Lovelace', department: 'Eng' },
      { employeeId: 'e2', name: 'Bob Newman', department: 'Eng' },
    ],
    ...over,
  });

  function setup(iv: IInterview): void {
    fixture = TestBed.createComponent(InterviewCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('interview', iv);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [InterviewCardComponent],
    }).compileComponents();
  });

  it('renders round, type, status and location (AC-4)', () => {
    setup(interview());
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Round 2');
    expect(text).toContain('In-person');
    expect(text).toContain('Scheduled');
    expect(text).toContain('Room 3B');
  });

  it('computes the end time from start + duration', () => {
    setup(interview({ startTime: '09:00', durationMinutes: 90 }));
    expect(component.endLabel()).toBe('10:30');
  });

  it('color-codes the status badge per status (§8)', () => {
    setup(interview({ status: 'NoShow' }));
    expect(component.statusBadge('NoShow')).toContain('amber');
    expect(component.statusBadge('Completed')).toContain('green');
    expect(component.statusBadge('Cancelled')).toContain('neutral');
  });

  it('shows a join link for video interviews', () => {
    setup(
      interview({
        interviewType: 'video',
        location: null,
        videoLink: 'https://meet/x',
      }),
    );
    const link = (fixture.nativeElement as HTMLElement).querySelector('a');
    expect(link?.getAttribute('href')).toBe('https://meet/x');
  });

  it('emits reschedule and cancel for a scheduled interview', () => {
    setup(interview());
    let rescheduled: IInterview | undefined;
    let cancelled: IInterview | undefined;
    component.reschedule.subscribe((iv) => (rescheduled = iv));
    component.cancel.subscribe((iv) => (cancelled = iv));

    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll(
      'button',
    );
    (buttons[0] as HTMLButtonElement).click(); // Reschedule
    (buttons[1] as HTMLButtonElement).click(); // Cancel
    expect(rescheduled?.id).toBe('iv-1');
    expect(cancelled?.id).toBe('iv-1');
  });

  it('hides action buttons when showActions is false (agenda read-only)', () => {
    fixture = TestBed.createComponent(InterviewCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('interview', interview());
    fixture.componentRef.setInput('showActions', false);
    fixture.detectChanges();
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll(
      'button',
    );
    expect(buttons.length).toBe(0);
  });

  it('shows applicant/vacancy context when enabled (agenda view)', () => {
    fixture = TestBed.createComponent(InterviewCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput(
      'interview',
      interview({ applicantName: 'Jane Doe', vacancyTitle: 'Senior Eng' }),
    );
    fixture.componentRef.setInput('showContext', true);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Jane Doe');
    expect(text).toContain('Senior Eng');
  });

  it('renders interviewer initials and names', () => {
    setup(interview());
    expect(component.initials('Ada Lovelace')).toBe('AL');
    expect(component.interviewerNames()).toBe('Ada Lovelace, Bob Newman');
  });
});
