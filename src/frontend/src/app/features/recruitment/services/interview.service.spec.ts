import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
} from '@angular/common/http';

import { InterviewService } from './interview.service';
import { environment } from '../../../../environments/environment';
import {
  IInterview,
  IInterviewRequest,
  IScheduleResult,
} from '../models/interview.models';
import { ILookupOption } from '../models/vacancy.models';

describe('InterviewService', () => {
  let service: InterviewService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/recruitment`;

  const dto = (over: Partial<IInterview> = {}): IInterview => ({
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

  const request: IInterviewRequest = {
    applicantId: 'app-1',
    vacancyId: 'vac-1',
    interviewerIds: ['e1'],
    interviewType: 'Video',
    scheduledDate: '2026-07-01',
    startTime: '09:00',
    durationMinutes: 60,
    location: null,
    videoLink: 'https://meet/x',
    notes: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        InterviewService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(InterviewService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── listForApplicant (AC-4) ───────────────────────────────

  it('listForApplicant GETs the rounds with credentials and maps them', () => {
    let rows: IInterview[] | undefined;
    service.listForApplicant('app-1').subscribe((r) => (rows = r));
    const req = httpMock.expectOne(`${base}/applicants/app-1/interviews`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([dto({ id: 'iv-1' }), dto({ id: 'iv-2', roundNumber: 2 })]);
    expect(rows!.length).toBe(2);
    expect(rows![1].roundNumber).toBe(2);
  });

  it('listForApplicant defaults missing fields via the mapper', () => {
    let rows: IInterview[] | undefined;
    service.listForApplicant('app-1').subscribe((r) => (rows = r));
    const req = httpMock.expectOne(`${base}/applicants/app-1/interviews`);
    // Backend omits interviewers + roundNumber on this row.
    req.flush([{ id: 'iv-9', applicantId: 'app-1', vacancyId: 'vac-1' }]);
    expect(rows![0].roundNumber).toBe(1);
    expect(rows![0].interviewers).toEqual([]);
    expect(rows![0].status).toBe('Scheduled');
  });

  // ─── schedule (AC-1 / FR-7) ────────────────────────────────

  it('schedule POSTs the body with credentials and returns the interview', () => {
    let result: IScheduleResult | undefined;
    service.schedule('app-1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/interviews`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual(request);
    req.flush({ interview: dto(), warnings: [] });
    expect(result!.interview.id).toBe('iv-1');
    expect(result!.warnings).toEqual([]);
  });

  it('schedule surfaces soft conflict warnings on a 2xx (FR-7)', () => {
    let result: IScheduleResult | undefined;
    service.schedule('app-1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/interviews`);
    req.flush({
      interview: dto(),
      warnings: ['Ada Lovelace is double-booked at 09:00'],
    });
    expect(result!.warnings).toEqual([
      'Ada Lovelace is double-booked at 09:00',
    ]);
  });

  it('schedule tolerates a bare interview DTO (no envelope) and defaults warnings', () => {
    let result: IScheduleResult | undefined;
    service.schedule('app-1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/interviews`);
    // Backend returns the interview directly, not wrapped in { interview, warnings }.
    req.flush(dto({ id: 'iv-bare' }));
    expect(result!.interview.id).toBe('iv-bare');
    expect(result!.warnings).toEqual([]);
  });

  // ─── reschedule / cancel (AC-3) ────────────────────────────

  it('reschedule PUTs to the interview id and maps the result', () => {
    let result: IScheduleResult | undefined;
    service.reschedule('iv-1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/interviews/iv-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({ interview: dto({ startTime: '10:00' }), warnings: [] });
    expect(result!.interview.startTime).toBe('10:00');
  });

  it('cancel POSTs to the cancel endpoint with the reason', () => {
    let res: IInterview | undefined;
    service.cancel('iv-1', 'Candidate withdrew').subscribe((r) => (res = r));
    const req = httpMock.expectOne(`${base}/interviews/iv-1/cancel`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reason: 'Candidate withdrew' });
    expect(req.request.withCredentials).toBeTrue();
    req.flush(dto({ status: 'Cancelled' }));
    expect(res!.status).toBe('Cancelled');
  });

  it('cancel sends a null reason when omitted', () => {
    service.cancel('iv-1').subscribe();
    const req = httpMock.expectOne(`${base}/interviews/iv-1/cancel`);
    expect(req.request.body).toEqual({ reason: null });
    req.flush(dto({ status: 'Cancelled' }));
  });

  // ─── listInterviews (FR-5 / FR-6) ──────────────────────────

  it('listInterviews GETs the agenda endpoint and maps rows', () => {
    let rows: IInterview[] | undefined;
    service.listInterviews().subscribe((r) => (rows = r));
    const req = httpMock.expectOne(`${base}/interviews`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([dto()]);
    expect(rows!.length).toBe(1);
  });

  it('listInterviews passes all filter query params (FR-6)', () => {
    service
      .listInterviews({
        interviewerId: 'e1',
        vacancyId: 'vac-1',
        from: '2026-07-01',
        to: '2026-07-31',
        status: 'Scheduled',
      })
      .subscribe();
    const req = httpMock.expectOne(
      (r) =>
        r.url === `${base}/interviews` && r.params.get('interviewerId') === 'e1',
    );
    expect(req.request.params.get('vacancyId')).toBe('vac-1');
    expect(req.request.params.get('from')).toBe('2026-07-01');
    expect(req.request.params.get('to')).toBe('2026-07-31');
    expect(req.request.params.get('status')).toBe('Scheduled');
    req.flush([]);
  });

  // ─── searchEmployees (interviewer lookup) ──────────────────

  it('searchEmployees hits the Core HR tenant endpoint and maps to ILookupOption', () => {
    let rows: ILookupOption[] | undefined;
    service.searchEmployees('ada').subscribe((r) => (rows = r));
    const req = httpMock.expectOne(
      (r) =>
        r.url === `${environment.apiBaseUrl}/tenant/employees` &&
        r.params.get('search') === 'ada',
    );
    expect(req.request.method).toBe('GET');
    req.flush([
      {
        employeeId: 'e1',
        firstName: 'Ada',
        lastName: 'Lovelace',
        departmentName: 'Engineering',
      },
    ]);
    expect(rows![0]).toEqual({
      id: 'e1',
      label: 'Ada Lovelace',
      sublabel: 'Engineering',
    });
  });

  // ─── static helper ─────────────────────────────────────────

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({
      error: { message: 'Conflict' },
      status: 409,
    });
    expect(InterviewService.parseErrorMessage(withMsg)).toBe('Conflict');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(InterviewService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
