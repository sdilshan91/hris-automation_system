import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { SelfAssessmentService } from './self-assessment.service';
import { environment } from '../../../../environments/environment';
import {
  IAssessmentAttachment,
  ISaveSelfAssessmentRequest,
  ISelfAssessment,
} from '../models/self-assessment.models';

describe('SelfAssessmentService', () => {
  let service: SelfAssessmentService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/performance/self-assessment`;

  const mockAssessment: ISelfAssessment = {
    id: 'sa-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    status: 'Draft',
    windowOpen: true,
    ratingScaleMax: 5,
    windowClosesOn: '2026-12-31',
    weightedScore: null,
    submittedOn: null,
    goals: [
      {
        goalId: 'g-1',
        title: 'Improve NPS',
        description: 'Lift NPS',
        weight: 100,
        targetValue: '85',
        measurementUnit: 'score',
        dueDate: '2026-06-30',
        selfRating: null,
        achievementPercent: null,
        comment: '',
        attachments: [],
      },
    ],
  };

  const mockAttachment: IAssessmentAttachment = {
    id: 'att-1',
    fileName: 'evidence.pdf',
    sizeBytes: 2048,
    uploadedOn: '2026-06-16',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        SelfAssessmentService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(SelfAssessmentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('getActive GETs the active self-assessment (bare payload, withCredentials)', () => {
    let result: ISelfAssessment | undefined;
    service.getActive().subscribe((a) => (result = a));

    const req = httpMock.expectOne(`${baseUrl}/active`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(mockAssessment);

    expect(result).toEqual(mockAssessment);
  });

  it('saveDraft PUTs the goal inputs to the draft endpoint', () => {
    const request: ISaveSelfAssessmentRequest = {
      goals: [
        {
          goalId: 'g-1',
          selfRating: 4,
          achievementPercent: 80,
          comment: 'A sufficiently long self-assessment comment.',
        },
      ],
    };
    let result: ISelfAssessment | undefined;
    service.saveDraft('sa-1', request).subscribe((a) => (result = a));

    const req = httpMock.expectOne(`${baseUrl}/sa-1/draft`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush(mockAssessment);

    expect(result).toEqual(mockAssessment);
  });

  it('submit POSTs the goal inputs to the submit endpoint', () => {
    const request: ISaveSelfAssessmentRequest = { goals: [] };
    const submitted: ISelfAssessment = {
      ...mockAssessment,
      status: 'Submitted',
      submittedOn: '2026-06-16T10:00:00Z',
      weightedScore: 80,
    };
    let result: ISelfAssessment | undefined;
    service.submit('sa-1', request).subscribe((a) => (result = a));

    const req = httpMock.expectOne(`${baseUrl}/sa-1/submit`);
    expect(req.request.method).toBe('POST');
    req.flush(submitted);

    expect(result?.status).toBe('Submitted');
  });

  it('uploadAttachment POSTs multipart and emits progress then done', () => {
    const file = new File(['x'], 'evidence.pdf', { type: 'application/pdf' });
    const events: string[] = [];
    let received: IAssessmentAttachment | undefined;

    service.uploadAttachment('sa-1', 'g-1', file).subscribe((e) => {
      events.push(e.type);
      if (e.type === 'done') {
        received = e.attachment;
      }
    });

    const req = httpMock.expectOne(
      `${baseUrl}/sa-1/goals/g-1/attachments`,
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    expect((req.request.body as FormData).get('file')).toBeTruthy();

    req.event({ type: 1, loaded: 50, total: 100 }); // UploadProgress
    req.flush(mockAttachment);

    expect(events).toContain('progress');
    expect(events).toContain('done');
    expect(received).toEqual(mockAttachment);
  });

  it('deleteAttachment DELETEs the attachment', () => {
    let done = false;
    service.deleteAttachment('sa-1', 'att-1').subscribe(() => (done = true));

    const req = httpMock.expectOne(`${baseUrl}/sa-1/attachments/att-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    expect(done).toBeTrue();
  });
});
