import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
} from '@angular/common/http';

import { ScorecardService } from './scorecard.service';
import { environment } from '../../../../environments/environment';
import {
  IScorecard,
  IScorecardBoard,
  IScorecardCriterion,
  IScorecardRequest,
} from '../models/scorecard.models';

describe('ScorecardService (US-REC-006)', () => {
  let service: ScorecardService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/recruitment`;

  const cardDto = (over: Partial<IScorecard> = {}): IScorecard => ({
    id: 'sc-1',
    interviewId: 'iv-1',
    interviewerEmployeeId: 'e1',
    interviewerName: 'Ada Lovelace',
    overallRecommendation: 'Hire',
    ratings: [
      { criterionKey: 'tech', score: 4, comment: 'solid' },
      { criterionKey: 'comm', score: 5, comment: null },
    ],
    generalNotes: 'Strong candidate',
    averageScore: 4.5,
    submittedAt: '2026-06-15T10:00:00Z',
    ...over,
  });

  const request: IScorecardRequest = {
    ratings: [
      { criterionKey: 'tech', score: 4, comment: 'solid' },
      { criterionKey: 'comm', score: 5, comment: null },
    ],
    overallRecommendation: 'Hire',
    generalNotes: 'Strong candidate',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ScorecardService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ScorecardService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── listCriteria (FR-1 / BR-2) ───────────────────────────

  it('listCriteria GETs the criteria endpoint with credentials, preserving order', () => {
    let rows: IScorecardCriterion[] | undefined;
    service.listCriteria().subscribe((r) => (rows = r));
    const req = httpMock.expectOne(`${base}/scorecard-criteria`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([
      { key: 'comm', label: 'Communication' },
      { key: 'tech', label: 'Technical Skills' },
    ]);
    expect(rows!.map((c) => c.key)).toEqual(['comm', 'tech']);
  });

  it('listCriteria maps key + label via the mapper', () => {
    let rows: IScorecardCriterion[] | undefined;
    service.listCriteria().subscribe((r) => (rows = r));
    httpMock
      .expectOne(`${base}/scorecard-criteria`)
      .flush([{ key: 'tech', label: 'Technical Skills' }]);
    expect(rows![0].key).toBe('tech');
    expect(rows![0].label).toBe('Technical Skills');
  });

  // ─── submit (AC-1 / FR-1) ─────────────────────────────────

  it('submit POSTs the request to the interview endpoint with credentials', () => {
    let result: IScorecard | undefined;
    service.submit('iv-1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/interviews/iv-1/scorecard`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual(request);
    req.flush(cardDto());
    expect(result!.id).toBe('sc-1');
    expect(result!.averageScore).toBe(4.5);
  });

  it('submit maps a joined fullName into interviewerName when present', () => {
    let result: IScorecard | undefined;
    service.submit('iv-1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/interviews/iv-1/scorecard`);
    req.flush({ id: 'sc-2', interviewId: 'iv-1', fullName: 'Alan Turing' });
    expect(result!.interviewerName).toBe('Alan Turing');
    expect(result!.ratings).toEqual([]);
    expect(result!.overallRecommendation).toBe('NoHire');
  });

  // ─── update (FR-2 / BR-4) — routes through the same POST upsert ─────

  it('update POSTs to the interview scorecard endpoint and maps the result', () => {
    let result: IScorecard | undefined;
    service.update('iv-1', request).subscribe((r) => (result = r));
    const req = httpMock.expectOne(`${base}/interviews/iv-1/scorecard`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual(request);
    req.flush(cardDto({ overallRecommendation: 'StrongHire' }));
    expect(result!.overallRecommendation).toBe('StrongHire');
  });

  // ─── listForInterview consolidated board (AC-2 / AC-3 / FR-6) ──

  it('listForInterview GETs the board and maps cards + aggregate', () => {
    let board: IScorecardBoard | undefined;
    service.listForInterview('iv-1').subscribe((b) => (board = b));
    const req = httpMock.expectOne(`${base}/interviews/iv-1/scorecards`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush({
      scorecards: [cardDto({ id: 'a' }), cardDto({ id: 'b', averageScore: 3.5 })],
      aggregateAverageScore: 4,
      hiddenCount: 0,
      antiBiasApplies: false,
    });
    expect(board!.scorecards.length).toBe(2);
    expect(board!.aggregateAverageScore).toBe(4);
    expect(board!.antiBiasApplies).toBeFalse();
  });

  it('listForInterview reflects the anti-bias gate flags from the backend (FR-6/BR-5)', () => {
    let board: IScorecardBoard | undefined;
    service.listForInterview('iv-1').subscribe((b) => (board = b));
    httpMock.expectOne(`${base}/interviews/iv-1/scorecards`).flush({
      scorecards: [],
      aggregateAverageScore: null,
      hiddenCount: 2,
      antiBiasApplies: true,
    });
    expect(board!.antiBiasApplies).toBeTrue();
    expect(board!.hiddenCount).toBe(2);
  });

  it('listForInterview tolerates a bare array and computes a fallback aggregate', () => {
    let board: IScorecardBoard | undefined;
    service.listForInterview('iv-1').subscribe((b) => (board = b));
    httpMock
      .expectOne(`${base}/interviews/iv-1/scorecards`)
      .flush([cardDto({ averageScore: 4 }), cardDto({ averageScore: 2 })]);
    expect(board!.scorecards.length).toBe(2);
    expect(board!.aggregateAverageScore).toBe(3);
    // Bare array → no anti-bias gate signalled → defaults to viewable.
    expect(board!.antiBiasApplies).toBeFalse();
  });

  it('listForInterview defaults antiBiasApplies to false when the backend omits it', () => {
    let board: IScorecardBoard | undefined;
    service.listForInterview('iv-1').subscribe((b) => (board = b));
    httpMock
      .expectOne(`${base}/interviews/iv-1/scorecards`)
      .flush({ scorecards: [], aggregateAverageScore: null });
    expect(board!.antiBiasApplies).toBeFalse();
  });

  // ─── static helper ────────────────────────────────────────

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({
      error: { message: 'Already submitted' },
      status: 409,
    });
    expect(ScorecardService.parseErrorMessage(withMsg)).toBe('Already submitted');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(ScorecardService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
