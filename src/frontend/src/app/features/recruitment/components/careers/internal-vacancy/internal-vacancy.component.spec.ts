import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, ActivatedRoute, convertToParamMap } from '@angular/router';
import { signal } from '@angular/core';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { InternalVacancyComponent } from './internal-vacancy.component';
import { CareersService } from '../../../services/careers.service';
import { AuthService } from '../../../../../core/auth/auth.service';
import { IPublicVacancy } from '../../../models/applicant.models';
import { IUser } from '../../../../../core/auth/auth.models';

describe('InternalVacancyComponent', () => {
  let component: InternalVacancyComponent;
  let fixture: ComponentFixture<InternalVacancyComponent>;
  let serviceSpy: jasmine.SpyObj<CareersService>;

  const mockVacancy: IPublicVacancy = {
    id: 'vac-1',
    referenceNumber: 'VAC-1',
    title: 'Internal Role',
    departmentName: 'Engineering',
    jobTitleName: null,
    employmentType: 'FullTime',
    locationName: 'HQ',
    description: '<p>Role</p>',
    qualifications: null,
    salaryMin: null,
    salaryMax: null,
    currency: null,
    applicationDeadline: null,
    postedAt: '2026-06-01',
  };

  const setup = async (id: string | null, fail = false) => {
    serviceSpy = jasmine.createSpyObj('CareersService', ['getPublicVacancy']);
    serviceSpy.getPublicVacancy.and.returnValue(
      fail ? throwError(() => new HttpErrorResponse({ status: 404 })) : of(mockVacancy),
    );

    const user = signal<IUser | null>({
      userId: 'u1',
      email: 'a@b.com',
      displayName: 'A B',
      mfaEnabled: false,
    });

    await TestBed.configureTestingModule({
      imports: [InternalVacancyComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: CareersService, useValue: serviceSpy },
        { provide: AuthService, useValue: { currentUser: user } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(id ? { id } : {}) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(InternalVacancyComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  it('loads the vacancy and offers Apply', async () => {
    await setup('vac-1');
    expect(component.vacancy()?.id).toBe('vac-1');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Apply');
  });

  it('opens the internal slide-over on Apply', async () => {
    await setup('vac-1');
    expect(component.showApply()).toBeFalse();
    component.showApply.set(true);
    fixture.detectChanges();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('app-internal-apply'),
    ).toBeTruthy();
  });

  it('shows not-found when id missing', async () => {
    await setup(null);
    expect(component.notFound()).toBeTrue();
  });
});
