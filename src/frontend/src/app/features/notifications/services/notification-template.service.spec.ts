import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { NotificationTemplateService } from './notification-template.service';
import { environment } from '../../../../environments/environment';
import {
  IRenderedPreview,
  ITemplateDetail,
  ITemplateListItem,
  ITestEmailResult,
} from '../models/notification-template.models';

/**
 * US-NTF-002: NotificationTemplateService — REST + signal state. Asserts URLs,
 * query params, request bodies, and the cache-sync side effects on the
 * templates()/currentTemplate() signals. Tenant isolation is server-side; here we
 * just confirm the contract the backend was built against.
 */
describe('NotificationTemplateService', () => {
  let service: NotificationTemplateService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/notification-templates`;

  const listRows: ITemplateListItem[] = [
    {
      eventKey: 'leave_approved',
      eventName: 'Leave Approved',
      language: 'en',
      isCustom: false,
      lastModified: null,
    },
    {
      eventKey: 'onboarding_welcome',
      eventName: 'Onboarding Welcome',
      language: 'en',
      isCustom: true,
      lastModified: '2026-05-01T10:00:00Z',
    },
  ];

  const detail: ITemplateDetail = {
    eventKey: 'leave_approved',
    eventName: 'Leave Approved',
    language: 'en',
    subject: 'Your leave was approved',
    bodyHtml: '<p>Hi {{employee.firstName}}</p>',
    bodyText: 'Hi {{employee.firstName}}',
    isCustom: false,
    version: 1,
    availablePlaceholders: ['employee.firstName', 'leave.startDate'],
    sampleData: { employee: { firstName: 'Ada' } },
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        NotificationTemplateService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(NotificationTemplateService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('loadTemplates GETs the list with the language param and populates the signal', () => {
    service.loadTemplates('en');
    expect(service.loadingList()).toBeTrue();

    const req = httpMock.expectOne(`${base}?language=en`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(listRows);

    expect(service.loadingList()).toBeFalse();
    expect(service.templates().length).toBe(2);
    expect(service.customCount()).toBe(1);
  });

  it('loadTemplates clears the loading flag on error', () => {
    service.loadTemplates();
    const req = httpMock.expectOne(`${base}?language=en`);
    req.flush('boom', { status: 500, statusText: 'Server Error' });
    expect(service.loadingList()).toBeFalse();
  });

  it('loadTemplate GETs one event and sets currentTemplate', () => {
    service.loadTemplate('leave_approved', 'en');
    const req = httpMock.expectOne(`${base}/leave_approved?language=en`);
    expect(req.request.method).toBe('GET');
    req.flush(detail);

    expect(service.currentTemplate()?.eventKey).toBe('leave_approved');
    expect(service.loadingTemplate()).toBeFalse();
  });

  it('saveTemplate PUTs the editable fields and updates state', () => {
    // seed a list row so we can assert the cache-sync flip to "Custom"
    service.loadTemplates();
    httpMock.expectOne(`${base}?language=en`).flush(listRows);

    const body = {
      subject: 'New subject',
      bodyHtml: '<p>New</p>',
      bodyText: 'New',
    };
    const saved: ITemplateDetail = { ...detail, ...body, isCustom: true, version: 2 };

    let result: ITemplateDetail | undefined;
    service.saveTemplate('leave_approved', body, 'en').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/leave_approved?language=en`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(saved);

    expect(result?.version).toBe(2);
    expect(service.currentTemplate()?.isCustom).toBeTrue();
    expect(service.saving()).toBeFalse();
    const row = service.templates().find((t) => t.eventKey === 'leave_approved');
    expect(row?.isCustom).toBeTrue();
  });

  it('resetToDefault DELETEs the override and returns the effective default', () => {
    const reverted: ITemplateDetail = { ...detail, isCustom: false };
    let result: ITemplateDetail | undefined;
    service
      .resetToDefault('onboarding_welcome', 'en')
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/onboarding_welcome?language=en`);
    expect(req.request.method).toBe('DELETE');
    req.flush(reverted);

    expect(result?.isCustom).toBeFalse();
    expect(service.currentTemplate()?.isCustom).toBeFalse();
  });

  it('preview POSTs the draft and returns the rendered output', () => {
    const rendered: IRenderedPreview = {
      subject: 'Your leave was approved',
      bodyHtml: '<p>Hi Ada</p>',
      bodyText: 'Hi Ada',
    };
    let result: IRenderedPreview | undefined;
    service
      .preview('leave_approved', {
        subject: 'Your leave was approved',
        bodyHtml: '<p>Hi {{employee.firstName}}</p>',
        bodyText: 'Hi {{employee.firstName}}',
        language: 'en',
      })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/leave_approved/preview`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.language).toBe('en');
    req.flush(rendered);

    expect(result?.bodyHtml).toContain('Ada');
  });

  it('sendTestEmail POSTs the recipient and returns the result', () => {
    const ok: ITestEmailResult = { sent: true };
    let result: ITestEmailResult | undefined;
    service
      .sendTestEmail('leave_approved', {
        recipientEmail: 'hr@acme.com',
        language: 'en',
      })
      .subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/leave_approved/test-email`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.recipientEmail).toBe('hr@acme.com');
    req.flush(ok);

    expect(result?.sent).toBeTrue();
  });
});
