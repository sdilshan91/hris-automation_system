import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, HttpErrorResponse } from '@angular/common/http';

import { OnboardingChecklistService } from './onboarding-checklist.service';
import { environment } from '../../../../environments/environment';
import {
  IApplicableTemplate,
  IAssignChecklistRequest,
  IAssignedChecklist,
  IChecklistPreview,
} from '../models/onboarding-checklist.models';
import { HttpEventType } from '@angular/common/http';
import {
  ICompleteTaskResponse,
  IMyChecklist,
  IOnboardingProgress,
} from '../models/my-onboarding.models';

describe('OnboardingChecklistService', () => {
  let service: OnboardingChecklistService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/onboarding/checklists`;

  const applicable = (
    over: Partial<IApplicableTemplate> = {},
  ): IApplicableTemplate => ({
    id: 'tpl-1',
    templateName: 'Engineering New Hire',
    description: null,
    taskCount: 3,
    matchReason: 'department',
    ...over,
  });

  const preview = (over: Partial<IChecklistPreview> = {}): IChecklistPreview => ({
    employeeId: 'emp-1',
    employeeName: 'Grace Hopper',
    templateId: 'tpl-1',
    templateName: 'Engineering New Hire',
    startDate: '2026-07-01',
    tasks: [
      {
        templateTaskId: 'tt-1',
        title: 'Sign contract',
        dueOffsetDays: 0,
        dueDate: '2026-07-01',
        status: 'pending',
        isMandatory: true,
        sortOrder: 0,
      },
    ],
    ...over,
  });

  const assigned = (
    over: Partial<IAssignedChecklist> = {},
  ): IAssignedChecklist => ({
    checklistInstanceId: 'ci-1',
    employeeId: 'emp-1',
    templateId: 'tpl-1',
    templateName: 'Engineering New Hire',
    startDate: '2026-07-01',
    isActive: true,
    tasks: [],
    notifiedCount: 3,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        OnboardingChecklistService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(OnboardingChecklistService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── getApplicableTemplates (AC-1 / FR-1) ──────────────────

  it('getApplicableTemplates GETs /applicable-templates with the employeeId param', () => {
    let result: IApplicableTemplate[] | undefined;
    service.getApplicableTemplates('emp-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === `${base}/applicable-templates` && r.params.get('employeeId') === 'emp-1',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([applicable(), applicable({ id: 'tpl-2', matchReason: 'universal' })]);

    expect(result!.length).toBe(2);
    expect(result![1].matchReason).toBe('universal');
  });

  // ─── preview (FR-2 / BR-4) ─────────────────────────────────

  it('preview GETs /preview with employeeId + templateId params', () => {
    let result: IChecklistPreview | undefined;
    service.preview('emp-1', 'tpl-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${base}/preview` &&
        r.params.get('employeeId') === 'emp-1' &&
        r.params.get('templateId') === 'tpl-1',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(preview());

    expect(result!.startDate).toBe('2026-07-01');
    expect(result!.tasks[0].dueDate).toBe('2026-07-01');
  });

  // ─── getByEmployee (AC-3 lookup) ───────────────────────────

  it('getByEmployee GETs /employee/:id and returns the active checklist', () => {
    let result: IAssignedChecklist | null | undefined;
    service.getByEmployee('emp-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employee/emp-1`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(assigned());

    expect(result!.checklistInstanceId).toBe('ci-1');
  });

  it('getByEmployee returns null when no checklist exists', () => {
    let result: IAssignedChecklist | null | undefined = undefined;
    service.getByEmployee('emp-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/employee/emp-1`);
    req.flush(null);

    expect(result).toBeNull();
  });

  // ─── assign (AC-2 / AC-3 / AC-5) ───────────────────────────

  it('assign POSTs the payload with credentials and returns notifiedCount', () => {
    const request: IAssignChecklistRequest = {
      employeeId: 'emp-1',
      templateId: 'tpl-1',
      overrideStartDate: '2026-07-01',
      mode: null,
      // BUG-441: the assignment screen posts its authoritative task set as `resolvedTasks`.
      // `additionalTasks` (extras on top of the template) is no longer sent by this app.
      resolvedTasks: [
        {
          templateTaskId: 'tt-1',
          title: 'Sign contract',
          dueDate: '2026-07-01',
          isMandatory: true,
          sortOrder: 0,
        },
      ],
    };

    let result: IAssignedChecklist | undefined;
    service.assign(request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual(request);
    req.flush(assigned({ notifiedCount: 5 }));

    expect(result!.notifiedCount).toBe(5);
  });

  it('assign sends the chosen mode for the replace/merge path (AC-3)', () => {
    service
      .assign({
        employeeId: 'emp-1',
        templateId: 'tpl-1',
        mode: 'replace',
        resolvedTasks: [],
      })
      .subscribe();

    const req = httpMock.expectOne(base);
    expect(req.request.body.mode).toBe('replace');
    req.flush(assigned());
  });

  it('assign surfaces a server error to the subscriber', () => {
    let errored: HttpErrorResponse | undefined;
    service
      .assign({ employeeId: 'emp-1', templateId: 'tpl-1', resolvedTasks: [] })
      .subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(base);
    req.flush(
      { message: 'Mandatory tasks cannot be removed.' },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(errored).toBeTruthy();
    expect(OnboardingChecklistService.parseErrorMessage(errored!)).toBe(
      'Mandatory tasks cannot be removed.',
    );
  });

  // ─── modify (AC-4 / FR-5 / FR-6) ───────────────────────────

  it('modify PUTs the instance endpoint with the task set', () => {
    let result: IAssignedChecklist | undefined;
    service
      .modify('ci-1', {
        tasks: [
          {
            title: 'Ad-hoc task',
            dueDate: '2026-07-05',
            isMandatory: false,
            sortOrder: 0,
          },
        ],
      })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/ci-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(assigned());

    expect(result!.checklistInstanceId).toBe('ci-1');
  });

  // ─── parseErrorMessage ─────────────────────────────────────

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({
      error: { message: 'Boom' },
      status: 400,
    });
    expect(OnboardingChecklistService.parseErrorMessage(withMsg)).toBe('Boom');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(OnboardingChecklistService.parseErrorMessage(plain)).toContain(
      'unexpected',
    );
  });

  // ─── US-ONB-003: "me" checklist + completion ───────────────

  const myChecklist = (over: Partial<IMyChecklist> = {}): IMyChecklist => ({
    checklistInstanceId: 'ci-9',
    status: 'in_progress',
    progressPercent: 50,
    totalTasks: 4,
    completedTasks: 2,
    pendingTasks: 2,
    overdueTasks: 0,
    categories: [
      {
        category: 'Documentation',
        tasks: [
          {
            taskInstanceId: 'ti-1',
            title: 'Submit ID proof',
            description: null,
            category: 'Documentation',
            responsibleRole: 'Employee',
            dueDate: '2026-07-01',
            status: 'pending',
            isMandatory: true,
            requiresDocument: true,
            completedAt: null,
            completedBy: null,
            comment: null,
            attachmentUrl: null,
          },
        ],
      },
    ],
    ...over,
  });

  const progressBody = (over: Partial<IOnboardingProgress> = {}): IOnboardingProgress => ({
    progressPercent: 50,
    totalTasks: 4,
    completedTasks: 2,
    pendingTasks: 2,
    overdueTasks: 0,
    ...over,
  });

  it('getMyChecklist GETs /me with credentials and returns grouped categories (FR-1)', () => {
    let result: IMyChecklist | undefined;
    service.getMyChecklist().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/me`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(myChecklist());

    expect(result!.categories[0].category).toBe('Documentation');
    expect(result!.progressPercent).toBe(50);
  });

  it('getMyProgress GETs /me/progress (AC-1 widget)', () => {
    let result: IOnboardingProgress | undefined;
    service.getMyProgress().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/me/progress`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(progressBody({ overdueTasks: 1 }));

    expect(result!.overdueTasks).toBe(1);
  });

  it('completeTask without a file POSTs a JSON comment body (FR-2)', () => {
    let result: ICompleteTaskResponse | undefined;
    service
      .completeTask('ti-1', { comment: 'Done it' })
      .subscribe((event) => {
        if (event.type === HttpEventType.Response) result = event.body!;
      });

    const req = httpMock.expectOne(`${base}/tasks/ti-1/complete`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual({ comment: 'Done it' });
    expect(req.request.body instanceof FormData).toBeFalse();

    req.flush({
      task: myChecklist().categories[0].tasks[0],
      progress: progressBody({ progressPercent: 75, completedTasks: 3, pendingTasks: 1 }),
    });
    expect(result!.progress.progressPercent).toBe(75);
  });

  it('completeTask with a file POSTs multipart with the "attachment" field + reports progress (AC-4)', () => {
    const file = new File(['x'], 'id.pdf', { type: 'application/pdf' });
    Object.defineProperty(file, 'size', { value: 2048 });

    let pct: number = -1;
    let result: ICompleteTaskResponse | undefined;
    service
      .completeTask('ti-1', { comment: 'attached', file })
      .subscribe((event) => {
        if (event.type === HttpEventType.UploadProgress) {
          pct = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
        } else if (event.type === HttpEventType.Response) {
          result = event.body!;
        }
      });

    const req = httpMock.expectOne(`${base}/tasks/ti-1/complete`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBeTrue();
    const fd = req.request.body as FormData;
    const sent = fd.get('attachment') as File;
    expect(sent.name).toBe('id.pdf');
    expect(sent.type).toBe('application/pdf');
    expect(fd.get('comment')).toBe('attached');
    expect(req.request.reportProgress).toBeTrue();

    req.event({ type: HttpEventType.UploadProgress, loaded: 1024, total: 2048 });
    expect(pct).toBe(50);

    req.flush({
      task: myChecklist().categories[0].tasks[0],
      progress: progressBody({ progressPercent: 75 }),
    });
    expect(result!.progress.progressPercent).toBe(75);
  });
});
