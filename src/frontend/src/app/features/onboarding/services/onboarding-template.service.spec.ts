import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
} from '@angular/common/http';

import { OnboardingTemplateService } from './onboarding-template.service';
import { environment } from '../../../../environments/environment';
import {
  ICreateOnboardingTemplateRequest,
  IOnboardingLookups,
  IOnboardingTemplate,
  IOnboardingTemplateSummary,
} from '../models/onboarding-template.models';

describe('OnboardingTemplateService', () => {
  let service: OnboardingTemplateService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/onboarding/templates`;

  const summary = (
    over: Partial<IOnboardingTemplateSummary> = {},
  ): IOnboardingTemplateSummary => ({
    id: 'tpl-1',
    templateName: 'Engineering New Hire',
    description: null,
    isActive: true,
    taskCount: 3,
    applicableDepartments: [],
    applicableJobTitles: [],
    ...over,
  });

  const template = (over: Partial<IOnboardingTemplate> = {}): IOnboardingTemplate => ({
    id: 'tpl-1',
    templateName: 'Engineering New Hire',
    description: 'Welcome aboard',
    applicableDepartments: ['dept-1'],
    applicableJobTitles: [],
    isActive: true,
    tasks: [
      {
        id: 't-1',
        title: 'Sign contract',
        category: 'Documentation',
        dueOffsetDays: 0,
        isMandatory: true,
        sortOrder: 0,
      },
    ],
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        OnboardingTemplateService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(OnboardingTemplateService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── list ──────────────────────────────────────────────────

  it('list GETs the templates endpoint with credentials', () => {
    let result: IOnboardingTemplateSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([summary(), summary({ id: 'tpl-2', isActive: false })]);

    expect(result!.length).toBe(2);
    expect(result![1].isActive).toBeFalse();
  });

  it('list reads the new { items, totalCount } page envelope', () => {
    let result: IOnboardingTemplateSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    httpMock.expectOne(base).flush({
      items: [summary(), summary({ id: 'tpl-2', isActive: false })],
      totalCount: 2,
      page: 1,
      pageSize: 20,
    });

    expect(result!.length).toBe(2);
    expect(result![1].isActive).toBeFalse();
  });

  it('list still tolerates the legacy { data } envelope', () => {
    let result: IOnboardingTemplateSummary[] | undefined;
    service.list().subscribe((r) => (result = r));

    httpMock.expectOne(base).flush({ data: [summary()] });

    expect(result!.length).toBe(1);
  });

  // ─── get ───────────────────────────────────────────────────

  it('get GETs a single template by id', () => {
    let result: IOnboardingTemplate | undefined;
    service.get('tpl-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/tpl-1`);
    expect(req.request.method).toBe('GET');
    req.flush(template());

    expect(result!.tasks.length).toBe(1);
    expect(result!.tasks[0].title).toBe('Sign contract');
  });

  // ─── getLookups ────────────────────────────────────────────

  it('getLookups GETs the lookups endpoint', () => {
    let result: IOnboardingLookups | undefined;
    service.getLookups().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/lookups`);
    expect(req.request.method).toBe('GET');
    req.flush({
      departments: [{ id: 'd1', name: 'Engineering' }],
      jobTitles: [{ id: 'j1', name: 'Engineer' }],
      users: [{ id: 'u1', name: 'Ada Lovelace' }],
    });

    expect(result!.departments[0].name).toBe('Engineering');
    expect(result!.users[0].id).toBe('u1');
  });

  // ─── create ────────────────────────────────────────────────

  it('create POSTs the payload with credentials and returns the template', () => {
    const request: ICreateOnboardingTemplateRequest = {
      templateName: 'New Hire',
      description: null,
      applicableDepartments: [],
      applicableJobTitles: [],
      isActive: true,
      tasks: [
        {
          title: 'Sign contract',
          dueOffsetDays: 0,
          isMandatory: true,
          sortOrder: 0,
        },
      ],
    };

    let result: IOnboardingTemplate | undefined;
    service.create(request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(base);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.body).toEqual(request);
    req.flush(template());

    expect(result!.id).toBe('tpl-1');
  });

  it('create surfaces a duplicate-name (AC-3) error to the subscriber', () => {
    let errored: HttpErrorResponse | undefined;
    service
      .create({
        templateName: 'Dup',
        description: null,
        applicableDepartments: [],
        applicableJobTitles: [],
        isActive: true,
        tasks: [
          { title: 'X', dueOffsetDays: 0, isMandatory: false, sortOrder: 0 },
        ],
      })
      .subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(base);
    req.flush(
      { message: 'A checklist template with this name already exists.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(errored).toBeTruthy();
    expect(OnboardingTemplateService.parseErrorMessage(errored!)).toBe(
      'A checklist template with this name already exists.',
    );
  });

  // ─── clone (FR-6) ──────────────────────────────────────────

  it('clone POSTs to the clone endpoint and returns the copy', () => {
    let result: IOnboardingTemplate | undefined;
    service.clone('tpl-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/tpl-1/clone`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(template({ id: 'tpl-9', templateName: 'Engineering New Hire (Copy)' }));

    expect(result!.id).toBe('tpl-9');
    expect(result!.templateName).toContain('(Copy)');
  });

  // ─── activate / deactivate (FR-7) ──────────────────────────

  // The controller exposes POST {id}/activate and POST {id}/deactivate — a PATCH
  // here 405s for a real user, with both list buttons dead (verified against
  // OnboardingTemplatesController).
  it('activate POSTs to the activate endpoint', () => {
    let result: IOnboardingTemplateSummary | undefined;
    service.activate('tpl-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/tpl-1/activate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(summary({ isActive: true }));

    expect(result!.isActive).toBeTrue();
  });

  it('deactivate POSTs to the deactivate endpoint', () => {
    let result: IOnboardingTemplateSummary | undefined;
    service.deactivate('tpl-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/tpl-1/deactivate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(summary({ isActive: false }));

    expect(result!.isActive).toBeFalse();
  });

  // ─── parseErrorMessage ─────────────────────────────────────

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({
      error: { message: 'Boom' },
      status: 400,
    });
    expect(OnboardingTemplateService.parseErrorMessage(withMsg)).toBe('Boom');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(OnboardingTemplateService.parseErrorMessage(plain)).toContain(
      'unexpected',
    );
  });
});
