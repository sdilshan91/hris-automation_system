import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';

import { ExitInterviewService } from './exit-interview.service';
import { environment } from '../../../../environments/environment';
import {
  IExitInterview,
  IExitInterviewAnalytics,
  IExitInterviewTemplate,
  IRecordExitInterviewRequest,
} from '../models/exit-interview.models';

describe('ExitInterviewService', () => {
  let service: ExitInterviewService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/exit-interviews`;

  const template = (): IExitInterviewTemplate => ({
    templateId: 'tmpl-1',
    categories: [
      {
        category: 'Reason for Leaving',
        questions: [
          { questionId: 'q1', text: 'Why are you leaving?', type: 'multiple_choice', options: ['Pay', 'Growth'], required: true },
        ],
      },
    ],
  });

  const interview = (): IExitInterview => ({
    id: 'ei-1',
    offboardingInstanceId: 'off-1',
    interviewMode: 'hr_conducted',
    interviewDate: '2026-06-17',
    responses: [{ questionId: 'q1', selectedOption: 'Pay' }],
    overallExperienceRating: 4,
    wouldRecommendEmployer: true,
    additionalComments: 'Good place.',
  });

  const analytics = (): IExitInterviewAnalytics => ({
    reasonDistribution: [{ reason: 'Pay', count: 3 }],
    averageRatingsPerCategory: [{ category: 'Management', averageRating: 3.5 }],
    trend: [{ period: '2026-Q1', count: 2, averageRating: 3.8 }],
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ExitInterviewService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ExitInterviewService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── getTemplate (AC-1 / FR-1) ─────────────────────────────

  it('getTemplate GETs /template with credentials', () => {
    let result: IExitInterviewTemplate | undefined;
    service.getTemplate().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/template`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(template());

    expect(result!.categories.length).toBe(1);
    expect(result!.categories[0].questions[0].type).toBe('multiple_choice');
  });

  // ─── getByOffboarding (BR-1) ───────────────────────────────

  it('getByOffboarding GETs the collection filtered by offboardingId', () => {
    let result: IExitInterview | undefined;
    service.getByOffboarding('off-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === base && r.params.get('offboardingId') === 'off-1',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(interview());

    expect(result!.id).toBe('ei-1');
  });

  it('getByOffboarding surfaces a 404 (no interview yet) to the subscriber', () => {
    let errored: HttpErrorResponse | undefined;
    service.getByOffboarding('off-2').subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(
      (r) => r.url === base && r.params.get('offboardingId') === 'off-2',
    );
    req.flush({ message: 'Not found' }, { status: 404, statusText: 'Not Found' });

    expect(errored!.status).toBe(404);
  });

  // ─── record (AC-2 / AC-3 / FR-6) ───────────────────────────

  it('record POSTs the request to the base with credentials', () => {
    const request: IRecordExitInterviewRequest = {
      offboardingId: 'off-1',
      interviewMode: 'hr_conducted',
      interviewDate: '2026-06-17',
      responses: [{ questionId: 'q1', selectedOption: 'Pay' }],
      overallExperienceRating: 4,
      wouldRecommendEmployer: true,
      additionalComments: 'Good place.',
    };
    let result: IExitInterview | undefined;
    service.record(request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual(request);
    req.flush(interview());

    expect(result!.offboardingInstanceId).toBe('off-1');
  });

  it('record surfaces a BR-1 duplicate 409 to the subscriber', () => {
    let errored: HttpErrorResponse | undefined;
    service
      .record({
        offboardingId: 'off-1',
        interviewMode: 'self_service',
        interviewDate: '2026-06-17',
        responses: [],
      })
      .subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(base);
    req.flush(
      { message: 'An exit interview already exists for this offboarding.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(errored).toBeTruthy();
    expect(ExitInterviewService.parseErrorMessage(errored!)).toContain('already exists');
  });

  // ─── getAnalytics (AC-4 / FR-4) ────────────────────────────

  it('getAnalytics GETs /analytics with no params when no range given', () => {
    let result: IExitInterviewAnalytics | undefined;
    service.getAnalytics().subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${base}/analytics` &&
        !r.params.has('fromDate') &&
        !r.params.has('toDate'),
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(analytics());

    expect(result!.reasonDistribution[0].reason).toBe('Pay');
  });

  it('getAnalytics passes the fromDate/toDate range as query params', () => {
    service.getAnalytics('2026-01-01', '2026-06-30').subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${base}/analytics` &&
        r.params.get('fromDate') === '2026-01-01' &&
        r.params.get('toDate') === '2026-06-30',
    );
    expect(req.request.method).toBe('GET');
    req.flush(analytics());
  });

  it('getAnalytics passes only fromDate when toDate is omitted', () => {
    service.getAnalytics('2026-01-01').subscribe();

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${base}/analytics` &&
        r.params.get('fromDate') === '2026-01-01' &&
        !r.params.has('toDate'),
    );
    expect(req.request.method).toBe('GET');
    req.flush(analytics());
  });

  // ─── helper ────────────────────────────────────────────────

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({ error: { message: 'Boom' }, status: 400 });
    expect(ExitInterviewService.parseErrorMessage(withMsg)).toBe('Boom');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(ExitInterviewService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
