import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PipService } from './pip.service';
import { environment } from '../../../../environments/environment';
import {
  IPip,
  IPipSummary,
  PipWire,
} from '../models/pip.models';

describe('PipService', () => {
  let service: PipService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/pips`;

  // Real wire (PerformancePipDto): id (not pipId), statusName, objectives whose id is
  // `id` (not objectiveId), checkpoints keyed by checkpointDate/progressStatus/
  // evidenceNotes/reviewerName, and NO jobTitle / outcome enum / signature DTO.
  const mockPipWire: PipWire = {
    id: 'pip-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    employeeNo: 'E-001',
    managerName: 'Sam Lead',
    mentorName: 'Pat Coach',
    status: 'Active',
    statusName: 'Active',
    reason: 'Below threshold',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    escalationAction: 'TerminationRecommendation',
    escalationActionName: 'TerminationRecommendation',
    escalationConfirmed: false,
    acknowledgementStatus: 'Pending',
    acknowledgementStatusName: 'Pending',
    acknowledgedAt: null,
    outcomeSetAt: null,
    objectives: [
      {
        id: 'obj-1',
        title: 'Ship the release',
        description: 'Deliver v2',
        successCriteria: 'GA by Aug',
        dueDate: '2026-08-01',
        checkpoints: [
          {
            id: 'cp-1',
            checkpointDate: '2026-07-01',
            recordedAt: '2026-07-02T09:00:00Z',
            progressStatus: 'OnTrack',
            progressStatusName: 'OnTrack',
            evidenceNotes: 'good progress',
            reviewerName: 'Sam Lead',
            attachmentFileName: 'evidence.pdf',
          },
        ],
      },
    ],
    events: [],
  };

  // Expected mapped view-model.
  const mockPip: IPip = {
    pipId: 'pip-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: null,
    managerName: 'Sam Lead',
    status: 'Active',
    reason: 'Below threshold',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    mentorName: 'Pat Coach',
    objectives: [
      {
        objectiveId: 'obj-1',
        title: 'Ship the release',
        description: 'Deliver v2',
        successCriteria: 'GA by Aug',
        dueDate: '2026-08-01',
        checkpoints: [
          {
            checkpointId: 'cp-1',
            dueDate: '2026-07-01',
            recordedOn: '2026-07-02T09:00:00Z',
            status: 'OnTrack',
            notes: 'good progress',
            recordedBy: 'Sam Lead',
            attachmentName: 'evidence.pdf',
            overdue: false,
          },
        ],
      },
    ],
    escalationAction: 'TerminationRecommendation',
    escalation: null,
    acknowledgement: 'Pending',
    acknowledgedSignature: null,
    outcome: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PipService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PipService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listPips() GETs the list and maps the summary rows (checkpointCount → total)', () => {
    let result: IPipSummary[] | undefined;
    service.listPips().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([
      {
        id: 'pip-1',
        employeeId: 'e-1',
        employeeName: 'Alex Doe',
        statusName: 'Active',
        startDate: '2026-06-01',
        endDate: '2026-08-30',
        objectiveCount: 2,
        checkpointCount: 3,
        acknowledgementStatusName: 'Acknowledged',
      },
    ]);

    expect(result).toEqual([
      {
        pipId: 'pip-1',
        employeeId: 'e-1',
        employeeName: 'Alex Doe',
        jobTitle: null,
        status: 'Active',
        startDate: '2026-06-01',
        endDate: '2026-08-30',
        objectiveCount: 2,
        checkpointsRecorded: 0,
        checkpointsTotal: 3,
        acknowledgement: 'Acknowledged',
      },
    ]);
  });

  it('listPips() unwraps a { data } page', () => {
    let result: IPipSummary[] | undefined;
    service.listPips().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    req.flush({ data: [] });

    expect(result).toEqual([]);
  });

  it('getPip() GETs the record and maps id/objectives/checkpoints (fails on the un-migrated pass-through)', () => {
    // The un-migrated code returned the raw payload: `pipId` was undefined (wire `id`),
    // each `objectiveId` was undefined (wire `id`) so `@for … track obj.objectiveId`
    // collided keys, and checkpoint `dueDate`/`status`/`notes` were undefined (wire
    // `checkpointDate`/`progressStatus`/`evidenceNotes`).
    let result: IPip | undefined;
    service.getPip('pip-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/pip-1`);
    expect(req.request.method).toBe('GET');
    req.flush(mockPipWire);

    expect(result).toEqual(mockPip);
    expect(result?.objectives[0].objectiveId).toBe('obj-1');
    expect(result?.objectives[0].checkpoints[0].dueDate).toBe('2026-07-01');
    expect(result?.objectives[0].checkpoints[0].status).toBe('OnTrack');
  });

  it('getPip() derives the escalation record, acknowledgement signature, and final outcome', () => {
    let result: IPip | undefined;
    service.getPip('pip-1').subscribe((r) => (result = r));

    httpMock.expectOne(`${baseUrl}/pip-1`).flush({
      ...mockPipWire,
      status: 'NotMet',
      statusName: 'NotMet',
      outcomeSetAt: '2026-08-31T12:00:00Z',
      acknowledgedAt: '2026-06-05T08:00:00Z',
      acknowledgementStatus: 'Acknowledged',
      acknowledgementStatusName: 'Acknowledged',
      escalationConfirmed: true,
      escalationConfirmedAt: '2026-09-01T09:00:00Z',
      escalationNotes: 'Proceeding with termination recommendation',
      events: [
        {
          eventType: 'EscalationConfirmed',
          eventTypeName: 'EscalationConfirmed',
          actorName: 'Dana HR',
          occurredAt: '2026-09-01T09:00:00Z',
        },
      ],
    });

    // Outcome derived from the terminal status once outcomeSetAt is present.
    expect(result?.outcome).toBe('NotMet');
    // Signature synthesised from acknowledgedAt + the employee; IP has no wire source.
    expect(result?.acknowledgedSignature).toEqual({
      name: 'Alex Doe',
      signedOn: '2026-06-05T08:00:00Z',
      ipAddress: null,
    });
    // Escalation confirmer read from the EscalationConfirmed event.
    expect(result?.escalation).toEqual({
      action: 'TerminationRecommendation',
      note: 'Proceeding with termination recommendation',
      confirmedBy: 'Dana HR',
      confirmedOn: '2026-09-01T09:00:00Z',
    });
  });

  it('getDraft() sends employeeId + reviewId params and maps the draft', () => {
    let result: unknown;
    service.getDraft('e-1', 'rv-9').subscribe((r) => (result = r));

    const req = httpMock.expectOne((r) => r.url === `${baseUrl}/draft`);
    expect(req.request.params.get('employeeId')).toBe('e-1');
    expect(req.request.params.get('reviewId')).toBe('rv-9');
    req.flush({
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
      suggestedReason: null,
      hasActivePip: false,
      escalationOptions: ['TerminationRecommendation'],
    });

    expect(result).toEqual({
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
      jobTitle: null,
      managerName: null,
      suggestedReason: null,
      hasActivePip: false,
      escalationOptions: ['TerminationRecommendation'],
    });
  });

  it('getDraft() omits params for a blank form', () => {
    service.getDraft().subscribe();

    const req = httpMock.expectOne(`${baseUrl}/draft`);
    expect(req.request.params.keys().length).toBe(0);
    req.flush({
      employeeId: null,
      employeeName: null,
      suggestedReason: null,
      hasActivePip: false,
      escalationOptions: [],
    });
  });

  it('createPip() POSTs the create request', () => {
    const body = {
      employeeId: 'e-1',
      reason: 'r',
      startDate: '2026-06-01',
      endDate: '2026-08-30',
      mentorEmployeeId: null,
      escalationAction: 'Demotion' as const,
      objectives: [
        {
          title: 't',
          description: 'd',
          successCriteria: 's',
          dueDate: '2026-07-01',
        },
      ],
      checkpointDates: [],
    };
    let result: IPip | undefined;
    service.createPip(body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(mockPipWire);

    expect(result).toEqual(mockPip);
  });

  it('recordCheckpoint() POSTs JSON when no file is attached', () => {
    const body = { progressStatus: 'OnTrack' as const, evidenceNotes: 'good progress' };
    let result: IPip | undefined;
    service.recordCheckpoint('pip-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/pip-1/checkpoints`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(mockPipWire);

    expect(result?.pipId).toBe('pip-1');
  });

  it('recordCheckpoint() POSTs multipart FormData when a file is attached', () => {
    const file = new File(['x'], 'evidence.pdf', { type: 'application/pdf' });
    service
      .recordCheckpoint('pip-1', { progressStatus: 'AtRisk', evidenceNotes: 'note' }, file)
      .subscribe();

    const req = httpMock.expectOne(`${baseUrl}/pip-1/checkpoints`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    const form = req.request.body as FormData;
    expect(form.get('progressStatus')).toBe('AtRisk');
    expect(form.get('evidenceNotes')).toBe('note');
    expect(form.get('file')).toBeTruthy();
    req.flush(mockPipWire);
  });

  it('setOutcome() POSTs the outcome request', () => {
    const body = { outcome: 'Extended' as const, newEndDate: '2026-10-30' };
    let result: IPip | undefined;
    service.setOutcome('pip-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/pip-1/outcome`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({
      ...mockPipWire,
      status: 'Extended',
      statusName: 'Extended',
      outcomeSetAt: '2026-08-30T00:00:00Z',
    });

    expect(result?.status).toBe('Extended');
    expect(result?.outcome).toBe('Extended');
  });

  it('escalate() POSTs the escalation action', () => {
    const body = { action: 'TerminationRecommendation' as const, note: 'n' };
    let result: IPip | undefined;
    service.escalate('pip-1', body).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/pip-1/escalation`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ ...mockPipWire, status: 'NotMet', statusName: 'NotMet' });

    expect(result?.status).toBe('NotMet');
  });

  it('acknowledge() POSTs an empty body (BR-4)', () => {
    let result: IPip | undefined;
    service.acknowledge('pip-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/pip-1/acknowledge`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({
      ...mockPipWire,
      acknowledgementStatus: 'Acknowledged',
      acknowledgementStatusName: 'Acknowledged',
    });

    expect(result?.acknowledgement).toBe('Acknowledged');
  });
});
