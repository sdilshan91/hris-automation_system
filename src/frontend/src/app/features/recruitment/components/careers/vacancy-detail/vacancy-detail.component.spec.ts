import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { CareersVacancyDetailComponent } from './vacancy-detail.component';
import { CareersService } from '../../../services/careers.service';
import { TenantService } from '../../../../../core/tenant/tenant.service';
import { IApplicant, IPublicVacancy } from '../../../models/applicant.models';

describe('CareersVacancyDetailComponent', () => {
  let component: CareersVacancyDetailComponent;
  let fixture: ComponentFixture<CareersVacancyDetailComponent>;
  let serviceSpy: jasmine.SpyObj<CareersService>;

  const mockVacancy: IPublicVacancy = {
    id: 'vac-1',
    referenceNumber: 'VAC-1',
    title: 'Senior Engineer',
    departmentName: 'Engineering',
    jobTitleName: 'Engineer',
    employmentType: 'Full-Time',
    locationName: 'HQ',
    description: '<p>Role</p>',
    qualifications: '<p>Skills</p>',
    salaryMin: null,
    salaryMax: null,
    currency: null,
    applicationDeadline: null,
    postedAt: '2026-06-01',
  };

  const mockApplicant: IApplicant = {
    id: 'app-1',
    applicationReferenceNumber: 'APP-2026-0009',
    vacancyId: 'vac-1',
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    phone: null,
    coverLetter: null,
    resumeFileName: 'cv.pdf',
    stage: 'Applied',
    source: 'public',
    isInternal: false,
    appliedAt: '2026-06-15',
  };

  const setup = async (id: string | null, vacancy = mockVacancy, fail = false) => {
    serviceSpy = jasmine.createSpyObj('CareersService', [
      'getPublicVacancy',
      'applyPublic',
    ]);
    serviceSpy.getPublicVacancy.and.returnValue(
      fail
        ? throwError(() => new HttpErrorResponse({ status: 404 }))
        : of(vacancy),
    );

    await TestBed.configureTestingModule({
      imports: [CareersVacancyDetailComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: CareersService, useValue: serviceSpy },
        {
          provide: TenantService,
          useValue: {
            displayName: () => 'Acme',
            tenantContext: () => ({ logoUrl: null }),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CareersVacancyDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  it('loads the vacancy by id', async () => {
    await setup('vac-1');
    expect(serviceSpy.getPublicVacancy).toHaveBeenCalledWith('vac-1');
    expect(component.vacancy()?.id).toBe('vac-1');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Senior Engineer');
  });

  it('shows not-found when id is missing', async () => {
    await setup(null);
    expect(component.notFound()).toBeTrue();
    expect(serviceSpy.getPublicVacancy).not.toHaveBeenCalled();
  });

  it('shows not-found when the load fails', async () => {
    await setup('vac-1', mockVacancy, true);
    expect(component.notFound()).toBeTrue();
  });

  it('swaps to the confirmation screen with the reference number (AC-1)', async () => {
    await setup('vac-1');
    component.onSubmitted(mockApplicant);
    fixture.detectChanges();
    expect(component.confirmation()).toBe(mockApplicant);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Application submitted');
    expect(text).toContain('APP-2026-0009');
  });
});
