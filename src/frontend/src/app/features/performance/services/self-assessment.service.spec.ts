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
  SelfAssessmentAttachmentWire,
  SelfAssessmentWire,
} from '../models/self-assessment.models';

describe('SelfAssessmentService', () => {
  let service: SelfAssessmentService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/self-assessments`;
  const activeCycleUrl = `${environment.apiBaseUrl}/tenant/performance/cycles/active`;

  // The REAL wire payload (PerformanceSelfAssessmentDto): the window/score/submitted
  // fields are `isSelfAssessmentOpen`/`weightedSelfScore`/`submittedAt`, the goals are
  // `items` with `goal*`-prefixed fields. This is the shape the un-migrated service
  // cast straight to `ISelfAssessment`, leaving `goals` (and the rest) `undefined`.
  const wireAssessment: SelfAssessmentWire = {
    id: 'sa-1',
    cycleId: 'cyc-1',
    cycleName: '2026 Annual',
    statusName: 'Draft',
    isSelfAssessmentOpen: true,
    ratingScaleMax: 5,
    windowClosesOn: '2026-12-31',
    weightedSelfScore: null,
    submittedAt: null,
    items: [
      {
        goalId: 'g-1',
        goalTitle: 'Improve NPS',
        goalDescription: 'Lift NPS',
        goalWeight: 100,
        goalTargetValue: '85',
        goalMeasurementUnit: 'score',
        goalDueDate: '2026-06-30',
        selfRating: null,
        achievementPercentage: null,
        comment: '',
        attachments: [],
      },
    ],
  };

  // The view-model the mapper is expected to PRODUCE from `wireAssessment`.
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

  const wireAttachment: SelfAssessmentAttachmentWire = {
    id: 'att-1',
    fileName: 'evidence.pdf',
    sizeBytes: 2048,
    uploadedAt: '2026-06-16',
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

  // ── ARM (Break 2): the wire sends `items`/`isSelfAssessmentOpen`/`weightedSelfScore`/
  //    `submittedAt`; the mapper must expose them as `goals`/`windowOpen`/`weightedScore`/
  //    `submittedOn`. Against the un-migrated cast, `result.goals` is `undefined` and the
  //    `.length`/field assertions below fail — the same access that threw at runtime.
  it('getActive resolves the active cycle then maps the wire record to the view-model', () => {
    let result: ISelfAssessment | undefined;
    service.getActive().subscribe((a) => (result = a));

    httpMock.expectOne(activeCycleUrl).flush({ id: 'cyc-1' });
    const req = httpMock.expectOne(`${baseUrl}/cycles/cyc-1/me`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(wireAssessment);

    expect(result).toEqual(mockAssessment);
    // Explicit renamed-field assertions (the ones that shipped broken):
    expect(result?.goals.length).toBe(1);
    expect(result?.goals[0].title).toBe('Improve NPS');
    expect(result?.goals[0].achievementPercent).toBeNull();
    expect(result?.windowOpen).toBeTrue();
    expect(result?.weightedScore).toBeNull();
    expect(result?.submittedOn).toBeNull();
  });

  it('saveDraft PUTs the cycleId + items to the draft endpoint', () => {
    const request: ISaveSelfAssessmentRequest = {
      cycleId: 'cyc-1',
      items: [
        {
          goalId: 'g-1',
          selfRating: 4,
          achievementPercentage: 80,
          comment: 'A sufficiently long self-assessment comment.',
        },
      ],
    };
    let result: ISelfAssessment | undefined;
    service.saveDraft(request).subscribe((a) => (result = a));

    const req = httpMock.expectOne(`${baseUrl}/draft`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(request);
    req.flush(wireAssessment);

    expect(result).toEqual(mockAssessment);
  });

  it('submit POSTs the cycleId + items to the submit endpoint', () => {
    const request: ISaveSelfAssessmentRequest = { cycleId: 'cyc-1', items: [] };
    const wireSubmitted: SelfAssessmentWire = {
      ...wireAssessment,
      statusName: 'Submitted',
      submittedAt: '2026-06-16T10:00:00Z',
      weightedSelfScore: 80,
    };
    let result: ISelfAssessment | undefined;
    service.submit(request).subscribe((a) => (result = a));

    const req = httpMock.expectOne(`${baseUrl}/submit`);
    expect(req.request.method).toBe('POST');
    req.flush(wireSubmitted);

    expect(result?.status).toBe('Submitted');
    expect(result?.submittedOn).toBe('2026-06-16T10:00:00Z');
    expect(result?.weightedScore).toBe(80);
  });

  it('uploadAttachment POSTs multipart to the cycle/goal route and emits progress then done', () => {
    const file = new File(['x'], 'evidence.pdf', { type: 'application/pdf' });
    const events: string[] = [];
    let received: IAssessmentAttachment | undefined;

    service.uploadAttachment('cyc-1', 'g-1', file).subscribe((e) => {
      events.push(e.type);
      if (e.type === 'done') {
        received = e.attachment;
      }
    });

    const req = httpMock.expectOne(
      `${baseUrl}/cycles/cyc-1/goals/g-1/attachments`,
    );
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    expect((req.request.body as FormData).get('file')).toBeTruthy();

    req.event({ type: 1, loaded: 50, total: 100 }); // UploadProgress
    req.flush(wireAttachment);

    expect(events).toContain('progress');
    expect(events).toContain('done');
    // Mapped from the wire attachment: `uploadedAt` → `uploadedOn`.
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
