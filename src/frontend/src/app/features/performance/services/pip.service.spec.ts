import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { PipService } from './pip.service';
import { environment } from '../../../../environments/environment';
import { IPip, IPipSummary } from '../models/pip.models';

describe('PipService', () => {
  let service: PipService;
  let httpMock: HttpTestingController;
  const baseUrl = `${environment.apiBaseUrl}/tenant/performance/pips`;

  const mockPip: IPip = {
    pipId: 'pip-1',
    employeeId: 'e-1',
    employeeName: 'Alex Doe',
    jobTitle: 'Engineer',
    managerName: 'Sam Lead',
    status: 'Active',
    reason: 'Below threshold',
    startDate: '2026-06-01',
    endDate: '2026-08-30',
    mentorName: 'Pat Coach',
    objectives: [],
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

  it('listPips() GETs the list with credentials (bare array)', () => {
    const rows: IPipSummary[] = [
      {
        pipId: 'pip-1',
        employeeId: 'e-1',
        employeeName: 'Alex Doe',
        status: 'Active',
        startDate: '2026-06-01',
        endDate: '2026-08-30',
        objectiveCount: 2,
        checkpointsRecorded: 1,
        checkpointsTotal: 3,
        acknowledgement: 'Acknowledged',
      },
    ];
    let result: IPipSummary[] | undefined;
    service.listPips().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(rows);

    expect(result).toEqual(rows);
  });

  it('listPips() unwraps a { data } page', () => {
    let result: IPipSummary[] | undefined;
    service.listPips().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    req.flush({ data: [] });

    expect(result).toEqual([]);
  });

  it('getPip() GETs the record by id', () => {
    let result: IPip | undefined;
    service.getPip('pip-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/pip-1`);
    expect(req.request.method).toBe('GET');
    req.flush(mockPip);

    expect(result).toEqual(mockPip);
  });

  it('getDraft() sends employeeId + reviewId params when provided', () => {
    service.getDraft('e-1', 'rv-9').subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === `${baseUrl}/draft`,
    );
    expect(req.request.params.get('employeeId')).toBe('e-1');
    expect(req.request.params.get('reviewId')).toBe('rv-9');
    req.flush({
      employeeId: 'e-1',
      employeeName: 'Alex Doe',
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
      mentorId: null,
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
    service.createPip(body).subscribe();

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(mockPip);
  });

  it('recordCheckpoint() POSTs JSON when no file is attached', () => {
    const body = { status: 'OnTrack' as const, notes: 'good progress' };
    service.recordCheckpoint('pip-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/pip-1/checkpoints`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(mockPip);
  });

  it('recordCheckpoint() POSTs multipart FormData when a file is attached', () => {
    const file = new File(['x'], 'evidence.pdf', { type: 'application/pdf' });
    service
      .recordCheckpoint('pip-1', { status: 'AtRisk', notes: 'note' }, file)
      .subscribe();

    const req = httpMock.expectOne(`${baseUrl}/pip-1/checkpoints`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    const form = req.request.body as FormData;
    expect(form.get('status')).toBe('AtRisk');
    expect(form.get('notes')).toBe('note');
    expect(form.get('file')).toBeTruthy();
    req.flush(mockPip);
  });

  it('setOutcome() POSTs the outcome request', () => {
    const body = { outcome: 'Extended' as const, newEndDate: '2026-10-30' };
    service.setOutcome('pip-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/pip-1/outcome`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ ...mockPip, status: 'Extended', outcome: 'Extended' });
  });

  it('escalate() POSTs the escalation action', () => {
    const body = { action: 'TerminationRecommendation' as const, note: 'n' };
    service.escalate('pip-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/pip-1/escalation`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({ ...mockPip, status: 'NotMet' });
  });

  it('acknowledge() POSTs an empty body (BR-4)', () => {
    service.acknowledge('pip-1').subscribe();

    const req = httpMock.expectOne(`${baseUrl}/pip-1/acknowledge`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({ ...mockPip, acknowledgement: 'Acknowledged' });
  });
});
