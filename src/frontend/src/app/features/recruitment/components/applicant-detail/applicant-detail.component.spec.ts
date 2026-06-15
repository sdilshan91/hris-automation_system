import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import {
  HttpErrorResponse,
  HttpHeaders,
  HttpResponse,
} from '@angular/common/http';

import { ApplicantDetailComponent } from './applicant-detail.component';
import { PipelineService } from '../../services/pipeline.service';
import { IApplicantDetail } from '../../models/pipeline.models';

describe('ApplicantDetailComponent', () => {
  let component: ApplicantDetailComponent;
  let fixture: ComponentFixture<ApplicantDetailComponent>;
  let serviceSpy: jasmine.SpyObj<PipelineService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const detail = (over: Partial<IApplicantDetail> = {}): IApplicantDetail => ({
    id: 'app-1',
    applicationReferenceNumber: 'APP-2026-0001',
    vacancyId: 'vac-1',
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    phone: '123',
    coverLetter: null,
    resumeFileName: 'ada-cv.pdf',
    stage: 'Screening',
    source: 'public',
    isInternal: false,
    appliedAt: '2026-06-10T00:00:00Z',
    stageHistory: [
      {
        fromStage: 'Applied',
        toStage: 'Screening',
        changedByUserName: 'Recruiter Rita',
        reason: null,
        notes: null,
        changedAt: '2026-06-11T00:00:00Z',
      },
    ],
    ...over,
  });

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('PipelineService', [
      'getApplicant',
      'downloadResume',
    ]);
    serviceSpy.getApplicant.and.returnValue(of(detail()));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [ApplicantDetailComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: PipelineService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ApplicantDetailComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('applicantId', 'app-1');
    fixture.detectChanges();
  });

  it('loads the applicant on init', () => {
    expect(serviceSpy.getApplicant).toHaveBeenCalledWith('app-1');
    expect(component.detail()!.id).toBe('app-1');
    expect(component.fullName()).toBe('Ada Lovelace');
    expect(component.initials()).toBe('AL');
    expect(component.loading()).toBeFalse();
  });

  it('renders the stage-transition timeline', () => {
    component.tab.set('timeline');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Applied → Screening');
    expect(el.textContent).toContain('Recruiter Rita');
  });

  it('shows an error toast when load fails', () => {
    serviceSpy.getApplicant.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    fixture.componentRef.setInput('applicantId', 'missing');
    fixture.detectChanges();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.loading()).toBeFalse();
  });

  it('downloads the resume blob', () => {
    const res = new HttpResponse<Blob>({
      body: new Blob(['pdf']),
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="ada-cv.pdf"',
      }),
    });
    serviceSpy.downloadResume.and.returnValue(of(res));
    const createSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:x');
    const revokeSpy = spyOn(URL, 'revokeObjectURL');
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');

    component.downloadResume();

    expect(serviceSpy.downloadResume).toHaveBeenCalledWith('app-1');
    expect(createSpy).toHaveBeenCalled();
    expect(clickSpy).toHaveBeenCalled();
    expect(revokeSpy).toHaveBeenCalled();
    expect(component.downloading()).toBeFalse();
  });

  it('shows an error toast when resume download fails', () => {
    serviceSpy.downloadResume.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    component.downloadResume();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.downloading()).toBeFalse();
  });

  it('emits closed on close()', () => {
    let closed = false;
    component.closed.subscribe(() => (closed = true));
    component.close();
    expect(closed).toBeTrue();
  });
});
