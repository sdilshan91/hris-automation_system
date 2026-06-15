import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { ApplicationFormComponent } from './application-form.component';
import { CareersService, IUploadEvent } from '../../../services/careers.service';
import { IApplicant } from '../../../models/applicant.models';

describe('ApplicationFormComponent', () => {
  let component: ApplicationFormComponent;
  let fixture: ComponentFixture<ApplicationFormComponent>;
  let serviceSpy: jasmine.SpyObj<CareersService>;

  const mockApplicant: IApplicant = {
    id: 'app-1',
    applicationReferenceNumber: 'APP-2026-0001',
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
    appliedAt: '2026-06-15T00:00:00Z',
  };

  const makePdf = () => {
    const f = new File(['x'], 'cv.pdf', { type: 'application/pdf' });
    Object.defineProperty(f, 'size', { value: 1024 });
    return f;
  };

  const fillValid = () => {
    component.form.patchValue({
      firstName: 'Ada',
      lastName: 'Lovelace',
      email: 'ada@example.com',
    });
    component.form.controls.resume.setValue(makePdf());
  };

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('CareersService', [
      'applyPublic',
      'applyInternal',
    ]);

    await TestBed.configureTestingModule({
      imports: [ApplicationFormComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: CareersService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ApplicationFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('vacancyId', 'vac-1');
    fixture.detectChanges();
  });

  it('creates with a required-field form', () => {
    expect(component).toBeTruthy();
    expect(component.form.invalid).toBeTrue();
  });

  it('does not submit when invalid and shows the resume-missing error', () => {
    component.form.patchValue({ firstName: 'Ada', lastName: 'Lovelace', email: 'ada@example.com' });
    component.submit();
    fixture.detectChanges();
    expect(serviceSpy.applyPublic).not.toHaveBeenCalled();
    expect(component.resumeMissing()).toBeTrue();
  });

  it('flags an invalid email', () => {
    component.form.patchValue({ firstName: 'A', lastName: 'B', email: 'not-an-email' });
    component.form.controls.email.markAsTouched();
    expect(component.form.controls.email.hasError('email')).toBeTrue();
  });

  it('tracks the cover-letter length for the counter', () => {
    component.form.controls.coverLetter.setValue('hello');
    expect(component.coverLen()).toBe(5);
  });

  it('submits the public path and emits the applicant on success (AC-1)', () => {
    const events: IUploadEvent[] = [
      { type: 'progress', progress: 40 },
      { type: 'done', applicant: mockApplicant },
    ];
    serviceSpy.applyPublic.and.returnValue(of(...events));

    let emitted: IApplicant | undefined;
    component.submitted.subscribe((a) => (emitted = a));

    fillValid();
    component.submit();

    expect(serviceSpy.applyPublic).toHaveBeenCalledWith(
      'vac-1',
      jasmine.objectContaining({ firstName: 'Ada', email: 'ada@example.com' }),
      jasmine.any(File),
    );
    expect(emitted).toBe(mockApplicant);
    expect(component.submitting()).toBeFalse();
  });

  it('uses the internal path when internal=true (AC-4)', () => {
    fixture.componentRef.setInput('internal', true);
    fixture.detectChanges();
    serviceSpy.applyInternal.and.returnValue(of({ type: 'done', applicant: mockApplicant }));

    fillValid();
    component.submit();

    expect(serviceSpy.applyInternal).toHaveBeenCalled();
    expect(serviceSpy.applyPublic).not.toHaveBeenCalled();
  });

  it('shows the duplicate message on a 409 (AC-3)', () => {
    const err = new HttpErrorResponse({ status: 409, statusText: 'Conflict' });
    serviceSpy.applyPublic.and.returnValue(throwError(() => err));

    fillValid();
    component.submit();
    fixture.detectChanges();

    expect(component.serverError()).toContain('already applied');
    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('already applied');
  });

  it('shows a generic server message on other errors', () => {
    const err = new HttpErrorResponse({ error: { message: 'File rejected' }, status: 400 });
    serviceSpy.applyPublic.and.returnValue(throwError(() => err));

    fillValid();
    component.submit();

    expect(component.serverError()).toBe('File rejected');
  });

  it('pre-fills name/email from prefill input (AC-4)', () => {
    const f = TestBed.createComponent(ApplicationFormComponent);
    f.componentRef.setInput('vacancyId', 'vac-1');
    f.componentRef.setInput('prefill', {
      firstName: 'Grace',
      lastName: 'Hopper',
      email: 'grace@example.com',
    });
    f.detectChanges();
    expect(f.componentInstance.form.controls.firstName.value).toBe('Grace');
    expect(f.componentInstance.form.controls.email.value).toBe('grace@example.com');
  });
});
