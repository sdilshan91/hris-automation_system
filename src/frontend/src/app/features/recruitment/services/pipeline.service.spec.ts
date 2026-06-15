import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
  HttpHeaders,
  HttpResponse,
} from '@angular/common/http';

import { PipelineService } from './pipeline.service';
import { environment } from '../../../../environments/environment';
import {
  IApplicantCard,
  IApplicantDetail,
  IPipelineBoard,
  IStageMoveResult,
  PIPELINE_STAGES,
} from '../models/pipeline.models';

describe('PipelineService', () => {
  let service: PipelineService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/recruitment`;

  const card = (over: Partial<IApplicantCard> = {}): IApplicantCard => ({
    id: 'app-1',
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    source: 'Public',
    isInternal: false,
    appliedAt: '2026-06-10T00:00:00Z',
    stage: 'Applied',
    ...over,
  });

  const board: IPipelineBoard = {
    vacancyId: 'vac-1',
    vacancyTitle: 'Senior Engineer',
    stages: [{ stage: 'Applied', count: 1, applicants: [card()] }],
    total: 1,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PipelineService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PipelineService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── getPipeline ───────────────────────────────────────────

  describe('getPipeline', () => {
    it('GETs the pipeline endpoint with credentials and returns all canonical stages', () => {
      let result: IPipelineBoard | undefined;
      service.getPipeline('vac-1').subscribe((b) => (result = b));

      const req = httpMock.expectOne(`${base}/vacancies/vac-1/pipeline`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(board);

      // Backend sent only "Applied"; service fills the rest so every stage has a column.
      expect(result!.stages.length).toBe(PIPELINE_STAGES.length);
      expect(result!.stages.map((s) => s.stage)).toEqual([...PIPELINE_STAGES]);
      const applied = result!.stages.find((s) => s.stage === 'Applied')!;
      expect(applied.applicants.length).toBe(1);
      const screening = result!.stages.find((s) => s.stage === 'Screening')!;
      expect(screening.applicants.length).toBe(0);
      expect(result!.total).toBe(1);
    });

    it('passes filter query params', () => {
      service
        .getPipeline('vac-1', {
          stage: 'Screening',
          source: 'Internal',
          from: '2026-01-01',
          to: '2026-12-31',
          search: 'ada',
        })
        .subscribe();

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${base}/vacancies/vac-1/pipeline` &&
          r.params.get('stage') === 'Screening',
      );
      expect(req.request.params.get('source')).toBe('Internal');
      expect(req.request.params.get('from')).toBe('2026-01-01');
      expect(req.request.params.get('to')).toBe('2026-12-31');
      expect(req.request.params.get('search')).toBe('ada');
      req.flush(board);
    });

    it('appends non-canonical custom stages after the canonical set', () => {
      let result: IPipelineBoard | undefined;
      service.getPipeline('vac-1').subscribe((b) => (result = b));
      const req = httpMock.expectOne(`${base}/vacancies/vac-1/pipeline`);
      req.flush({
        vacancyId: 'vac-1',
        stages: [
          { stage: 'Assessment' as never, count: 2, applicants: [] },
        ],
        total: 0,
      });
      const last = result!.stages[result!.stages.length - 1];
      expect(last.stage).toBe('Assessment' as never);
    });
  });

  // ─── getApplicant ──────────────────────────────────────────

  it('getApplicant GETs detail with credentials and defaults stageHistory', () => {
    let detail: IApplicantDetail | undefined;
    service.getApplicant('app-1').subscribe((d) => (detail = d));
    const req = httpMock.expectOne(`${base}/applicants/app-1/detail`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    // Backend nests the applicant under `profile`; flush without stageHistory to prove the default.
    req.flush({
      profile: {
        id: 'app-1',
        applicationReferenceNumber: 'APP-1',
        vacancyId: 'vac-1',
        firstName: 'Ada',
        lastName: 'Lovelace',
        email: 'ada@example.com',
        phone: null,
        coverLetter: null,
        resumeFileName: 'cv.pdf',
        stage: 'Applied',
        source: 'Public',
        isInternal: false,
        appliedAt: '2026-06-10T00:00:00Z',
      },
    });
    expect(detail!.firstName).toBe('Ada');
    expect(detail!.stageHistory).toEqual([]);
  });

  // ─── changeStage ───────────────────────────────────────────

  it('changeStage POSTs the body with credentials and maps the move result', () => {
    let updated: IStageMoveResult | undefined;
    service
      .changeStage('app-1', {
        toStage: 'Rejected',
        reason: 'Not qualified',
        rejectionReason: 'NotQualified',
      })
      .subscribe((c) => (updated = c));
    const req = httpMock.expectOne(`${base}/applicants/app-1/move-stage`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual({
      toStage: 'Rejected',
      reason: 'Not qualified',
      rejectionReason: 'NotQualified',
    });
    // Backend returns MoveApplicantStageResultDto; the service maps it to IStageMoveResult.
    req.flush({
      applicantId: 'app-1',
      toStage: 'Rejected',
      enteredStageAt: '2026-06-15T00:00:00Z',
      stageHistoryId: 'h-1',
    });
    expect(updated!.id).toBe('app-1');
    expect(updated!.stage).toBe('Rejected');
    expect(updated!.enteredStageAt).toBe('2026-06-15T00:00:00Z');
    // Absent warnings default to an empty array (US-REC-004 soft gate).
    expect(updated!.warnings).toEqual([]);
  });

  it('changeStage surfaces soft-gate warnings from the response (BR-4/FR-1)', () => {
    let updated: IStageMoveResult | undefined;
    service
      .changeStage('app-1', { toStage: 'Offer' })
      .subscribe((c) => (updated = c));
    const req = httpMock.expectOne(`${base}/applicants/app-1/move-stage`);
    req.flush({
      applicantId: 'app-1',
      toStage: 'Offer',
      warnings: ['Headcount already filled', 'No interview scorecard on file'],
    });
    expect(updated!.warnings).toEqual([
      'Headcount already filled',
      'No interview scorecard on file',
    ]);
  });

  // ─── downloadResume ────────────────────────────────────────

  it('downloadResume GETs a blob response with credentials', () => {
    let res: HttpResponse<Blob> | undefined;
    service.downloadResume('app-1').subscribe((r) => (res = r));
    const req = httpMock.expectOne(`${base}/applicants/app-1/resume`);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(new Blob(['pdf']), {
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="ada-cv.pdf"',
      }),
    });
    expect(res!.body).toBeTruthy();
    expect(PipelineService.filenameFromResponse(res!, 'fallback')).toBe(
      'ada-cv.pdf',
    );
  });

  // ─── static helpers ────────────────────────────────────────

  describe('filenameFromResponse', () => {
    it('parses a UTF-8 encoded filename', () => {
      const res = new HttpResponse<Blob>({
        headers: new HttpHeaders({
          'content-disposition': "attachment; filename*=UTF-8''r%C3%A9sum%C3%A9.pdf",
        }),
      });
      expect(PipelineService.filenameFromResponse(res, 'x')).toBe('résumé.pdf');
    });

    it('falls back when no header is present', () => {
      const res = new HttpResponse<Blob>({ headers: new HttpHeaders() });
      expect(PipelineService.filenameFromResponse(res, 'fallback.pdf')).toBe(
        'fallback.pdf',
      );
    });
  });

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({
      error: { message: 'Boom' },
      status: 400,
    });
    expect(PipelineService.parseErrorMessage(withMsg)).toBe('Boom');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(PipelineService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
