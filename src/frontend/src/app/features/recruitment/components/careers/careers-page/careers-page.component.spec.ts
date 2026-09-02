import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { CareersPageComponent } from './careers-page.component';
import { CareersService } from '../../../services/careers.service';
import { TenantService } from '../../../../../core/tenant/tenant.service';
import { IPublicVacancy } from '../../../models/applicant.models';

describe('CareersPageComponent', () => {
  let component: CareersPageComponent;
  let fixture: ComponentFixture<CareersPageComponent>;
  let serviceSpy: jasmine.SpyObj<CareersService>;

  const vac = (over: Partial<IPublicVacancy>): IPublicVacancy => ({
    id: 'v',
    slug: 'vac-slug',
    referenceNumber: 'VAC-1',
    title: 'Role',
    departmentName: null,
    employmentType: null,
    locationName: null,
    description: null,
    qualifications: null,
    salaryMin: null,
    salaryMax: null,
    salaryCurrency: null,
    applicationDeadline: null,
    publishedAt: '2026-06-01',
    ...over,
  });

  // Ids are GUID-ish and slugs are SEO strings on purpose: the detail link must be built
  // from the slug, so the two must never be interchangeable in this fixture.
  const list: IPublicVacancy[] = [
    vac({ id: 'a', slug: 'backend-engineer', title: 'Backend Engineer', departmentName: 'Engineering', locationName: 'HQ', employmentType: 'FullTime' }),
    vac({ id: 'b', slug: 'recruiter', title: 'Recruiter', departmentName: 'People', locationName: 'Remote', employmentType: 'Contract' }),
    vac({ id: 'c', slug: 'frontend-engineer', title: 'Frontend Engineer', departmentName: 'Engineering', locationName: 'Remote', employmentType: 'FullTime' }),
  ];

  const setup = async () => {
    serviceSpy = jasmine.createSpyObj('CareersService', ['listOpenVacancies']);
    serviceSpy.listOpenVacancies.and.returnValue(of(list));

    await TestBed.configureTestingModule({
      imports: [CareersPageComponent],
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
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CareersPageComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  beforeEach(setup);

  it('loads and shows all vacancies', () => {
    expect(component.loading()).toBeFalse();
    expect(component.filtered().length).toBe(3);
  });

  it('links each vacancy card to /careers/{slug}, never the id (detail API is slug-keyed)', () => {
    const hrefs = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        'a[href^="/careers/"]',
      ),
    ).map((a) => a.getAttribute('href'));

    expect(hrefs).toEqual([
      '/careers/backend-engineer',
      '/careers/recruiter',
      '/careers/frontend-engineer',
    ]);
  });

  it('derives distinct sorted filter options', () => {
    expect(component.departments()).toEqual(['Engineering', 'People']);
    expect(component.locations()).toEqual(['HQ', 'Remote']);
    expect(component.employmentTypes()).toEqual(['Contract', 'FullTime']);
  });

  it('filters by search title', () => {
    component.search.set('engineer');
    expect(component.filtered().map((v) => v.id)).toEqual(['a', 'c']);
  });

  it('filters by department + location', () => {
    component.dept.set('Engineering');
    component.location.set('Remote');
    expect(component.filtered().map((v) => v.id)).toEqual(['c']);
  });

  it('filters by employment type', () => {
    component.empType.set('Contract');
    expect(component.filtered().map((v) => v.id)).toEqual(['b']);
  });

  it('shows the error state when the load fails', async () => {
    serviceSpy.listOpenVacancies.and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { message: 'Down' }, status: 500 })),
    );
    component.load();
    fixture.detectChanges();
    expect(component.error()).toBe('Down');
  });
});
