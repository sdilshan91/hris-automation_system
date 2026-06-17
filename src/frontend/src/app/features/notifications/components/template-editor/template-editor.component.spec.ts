import {
  ComponentFixture,
  TestBed,
  fakeAsync,
  tick,
  flush,
} from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { provideAnimations } from '@angular/platform-browser/animations';
import { TemplateEditorComponent } from './template-editor.component';
import { environment } from '../../../../../environments/environment';
import { ITemplateDetail } from '../../models/notification-template.models';

/**
 * US-NTF-002 AC-2..AC-4 / FR-3/FR-4/FR-8: the split-pane template editor.
 */
describe('TemplateEditorComponent', () => {
  let fixture: ComponentFixture<TemplateEditorComponent>;
  let component: TemplateEditorComponent;
  let httpMock: HttpTestingController;
  let toastr: ToastrService;
  const base = `${environment.apiBaseUrl}/notification-templates`;

  const detail: ITemplateDetail = {
    eventKey: 'leave_approved',
    eventName: 'Leave Approved',
    language: 'en',
    subject: 'Your leave was approved',
    bodyHtml: '<p>Hi {{employee.firstName}}</p>',
    bodyText: 'Hi {{employee.firstName}}',
    isCustom: true,
    version: 2,
    availablePlaceholders: ['employee.firstName', 'leave.startDate'],
    sampleData: { employee: { firstName: 'Ada' } },
  };

  /** Drive ngOnInit's async hydrate: flush the detail GET, then any preview POST. */
  function bootstrap(): void {
    fixture.detectChanges(); // ngOnInit -> loadTemplate GET
    httpMock.expectOne(`${base}/leave_approved?language=en`).flush(detail);
    tick(60); // hydrateWhenLoaded setTimeout(50) settles -> form populated, runPreview()
    flushPreview();
    fixture.detectChanges();
  }

  /** Answer any pending preview POST with a canned render. */
  function flushPreview(): void {
    const reqs = httpMock.match(`${base}/leave_approved/preview`);
    reqs.forEach((r) =>
      r.flush({
        subject: 'Your leave was approved',
        bodyHtml: '<p>Hi Ada</p>',
        bodyText: 'Hi Ada',
      }),
    );
  }

  beforeEach(async () => {
    // The reused RTE uses execCommand; stub it so jsdom/headless doesn't throw.
    spyOn(document, 'execCommand').and.returnValue(true);
    spyOn(document, 'queryCommandState').and.returnValue(false);

    await TestBed.configureTestingModule({
      imports: [TemplateEditorComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideTranslateService(),
        provideToastr(),
        provideAnimations(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: (k: string) => (k === 'eventKey' ? 'leave_approved' : null) },
              queryParamMap: { get: () => 'en' },
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TemplateEditorComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    toastr = TestBed.inject(ToastrService);
  });

  afterEach(() => httpMock.verify());

  it('loads the template and hydrates the form', fakeAsync(() => {
    bootstrap();
    expect(component['form'].controls.subject.value).toBe(
      'Your leave was approved',
    );
    expect(component['form'].controls.bodyText.value).toContain('firstName');
  }));

  it('exposes the available placeholders for the variable panel (FR-3)', fakeAsync(() => {
    bootstrap();
    expect(component['placeholders']).toEqual([
      'employee.firstName',
      'leave.startDate',
    ]);
  }));

  it('inserts a {{placeholder}} token into the last-focused text field (FR-3)', fakeAsync(() => {
    bootstrap();
    component['trackFocus']('subject');
    component['insertPlaceholder']('tenant.companyName');
    expect(component['form'].controls.subject.value).toContain(
      '{{tenant.companyName}}',
    );
    // insertPlaceholder schedules a debounced preview; let it fire and answer it.
    tick(500);
    flushPreview();
    flush();
  }));

  it('saves the override and toasts success (AC-3)', fakeAsync(() => {
    bootstrap();
    const ok = spyOn(toastr, 'success');
    component['form'].patchValue({ subject: 'Edited subject' });
    component['save']();

    const req = httpMock.expectOne(`${base}/leave_approved?language=en`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.subject).toBe('Edited subject');
    req.flush({ ...detail, subject: 'Edited subject' });

    expect(ok).toHaveBeenCalled();
    flush();
  }));

  it('does not save an invalid form', fakeAsync(() => {
    bootstrap();
    component['form'].controls.subject.setValue('');
    component['save']();
    httpMock.expectNone(`${base}/leave_approved?language=en`);
    expect(component['form'].controls.subject.touched).toBeTrue();
    flush();
  }));

  it('opens then confirms reset-to-default, repopulating from the default (AC-4)', fakeAsync(() => {
    bootstrap();
    component['openResetConfirm']();
    expect(component['showResetConfirm']()).toBeTrue();

    component['confirmReset']();
    const req = httpMock.expectOne(`${base}/leave_approved?language=en`);
    expect(req.request.method).toBe('DELETE');
    req.flush({ ...detail, isCustom: false, subject: 'System default subject' });
    flushPreview();

    expect(component['showResetConfirm']()).toBeFalse();
    expect(component['form'].controls.subject.value).toBe('System default subject');
    flush();
  }));

  it('sends a test email to a valid recipient (FR-8)', fakeAsync(() => {
    bootstrap();
    const ok = spyOn(toastr, 'success');
    component['openTestModal']();
    component['testForm'].controls.recipientEmail.setValue('hr@acme.com');
    component['sendTest']();

    const req = httpMock.expectOne(`${base}/leave_approved/test-email`);
    expect(req.request.body.recipientEmail).toBe('hr@acme.com');
    req.flush({ sent: true });

    expect(ok).toHaveBeenCalled();
    expect(component['showTestModal']()).toBeFalse();
    flush();
  }));

  it('blocks a test send with an invalid email (FR-8)', fakeAsync(() => {
    bootstrap();
    component['openTestModal']();
    component['testForm'].controls.recipientEmail.setValue('not-an-email');
    component['sendTest']();
    httpMock.expectNone(`${base}/leave_approved/test-email`);
    expect(component['showTestModal']()).toBeTrue();
    flush();
  }));

  it('switches the mobile tab', fakeAsync(() => {
    bootstrap();
    expect(component['mobileTab']()).toBe('editor');
    component['mobileTab'].set('preview');
    expect(component['mobileTab']()).toBe('preview');
    flush();
  }));
});
