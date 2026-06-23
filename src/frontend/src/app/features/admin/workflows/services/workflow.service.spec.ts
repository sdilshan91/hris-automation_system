import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { WorkflowService } from './workflow.service';
import {
  ICreateWorkflowRequest,
  IUpdateWorkflowRequest,
  IWorkflowDefinition,
  IWorkflowStep,
  IWorkflowSummary,
} from '../models/workflow.models';
import { environment } from '../../../../../environments/environment';

describe('WorkflowService', () => {
  let service: WorkflowService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/workflows`;
  const rolesUrl = `${environment.apiBaseUrl}/tenant/roles`;
  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  const sampleStep: IWorkflowStep = {
    stepOrder: 1,
    approverType: 'LineManager',
    approverIdentifier: null,
    isParallel: false,
    parallelApproverIdentifiers: [],
    slaHours: 24,
    escalationApproverType: null,
    escalationApproverIdentifier: null,
    conditionJson: null,
    delegationEnabled: false,
    delegationBackupUserId: null,
  };

  const sampleDefinition: IWorkflowDefinition = {
    id: 'wf-1',
    name: 'Standard Leave',
    entityType: 'Leave',
    version: 1,
    status: 'Active',
    isDefault: true,
    steps: [sampleStep],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        WorkflowService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(WorkflowService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getWorkflows GETs the tenant workflows list', () => {
    const list: IWorkflowSummary[] = [
      {
        id: 'wf-1',
        name: 'Standard Leave',
        entityType: 'Leave',
        version: 1,
        status: 'Active',
        isDefault: true,
        stepCount: 3,
        updatedAt: '2026-06-01T00:00:00Z',
      },
    ];
    let result: IWorkflowSummary[] | undefined;
    service.getWorkflows().subscribe((r) => (result = r));

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(list);

    expect(result).toEqual(list);
  });

  it('getWorkflow GETs a single definition by id', () => {
    let result: IWorkflowDefinition | undefined;
    service.getWorkflow('wf-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${baseUrl}/wf-1`);
    expect(req.request.method).toBe('GET');
    req.flush(sampleDefinition);

    expect(result).toEqual(sampleDefinition);
  });

  it('createWorkflow POSTs the create body', () => {
    const body: ICreateWorkflowRequest = {
      name: 'New WF',
      entityType: 'Leave',
      steps: [sampleStep],
    };
    service.createWorkflow(body).subscribe();

    const req = httpMock.expectOne(baseUrl);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(sampleDefinition);
  });

  it('createWorkflow surfaces the plan-limit rejection (AC-4)', () => {
    const limitMessage =
      'You have reached the maximum number of workflows (2) for your plan. Please upgrade or archive an existing workflow.';
    let errorBody: { message?: string } | undefined;
    service.createWorkflow({
      name: 'Over limit',
      entityType: 'Leave',
      steps: [sampleStep],
    }).subscribe({
      next: () => fail('expected error'),
      error: (err) => (errorBody = err.error),
    });

    const req = httpMock.expectOne(baseUrl);
    req.flush({ message: limitMessage }, { status: 409, statusText: 'Conflict' });

    expect(errorBody?.message).toBe(limitMessage);
  });

  it('updateWorkflow PUTs to the id and creates a new version', () => {
    const body: IUpdateWorkflowRequest = {
      name: 'Updated',
      steps: [sampleStep],
    };
    service.updateWorkflow('wf-1', body).subscribe();

    const req = httpMock.expectOne(`${baseUrl}/wf-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush({ ...sampleDefinition, version: 2 });
  });

  it('archiveWorkflow POSTs to /:id/archive', () => {
    service.archiveWorkflow('wf-1').subscribe();
    const req = httpMock.expectOne(`${baseUrl}/wf-1/archive`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('restoreWorkflow POSTs to /:id/restore', () => {
    service.restoreWorkflow('wf-1').subscribe();
    const req = httpMock.expectOne(`${baseUrl}/wf-1/restore`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('deleteWorkflow DELETEs /:id', () => {
    service.deleteWorkflow('wf-1').subscribe();
    const req = httpMock.expectOne(`${baseUrl}/wf-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('getApproverRoles reuses the tenant roles endpoint', () => {
    let result: { id: string; name: string }[] | undefined;
    service.getApproverRoles().subscribe((r) => (result = r));

    const req = httpMock.expectOne(rolesUrl);
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'r-1', name: 'HR Officer', description: 'ignored' }]);

    expect(result).toEqual([{ id: 'r-1', name: 'HR Officer' }]);
  });

  it('getApproverUsers reuses the US-ADM-005 user list and projects options', () => {
    let result: { id: string; displayName: string; email: string }[] | undefined;
    service.getApproverUsers().subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === usersUrl && r.method === 'GET'
    );
    expect(req.request.params.get('pageSize')).toBe('200');
    expect(req.request.params.get('status')).toBe('active');
    req.flush({
      items: [
        { userTenantId: 'ut-1', displayName: 'Jane Doe', email: 'jane@acme.com' },
      ],
    });

    expect(result).toEqual([
      { id: 'ut-1', displayName: 'Jane Doe', email: 'jane@acme.com' },
    ]);
  });
});
