import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import {
  VacancyFormComponent,
  salaryRangeValidator,
} from './vacancy-form.component';
import { VacancyService } from '../../services/vacancy.service';
import { IVacancy } from '../../models/vacancy.models';
import { FormControl, FormGroup } from '@angular/forms';

describe('VacancyFormComponent', () => {
  let component: VacancyFormComponent;
  let fixture: ComponentFixture<VacancyFormComponent>;
  let serviceSpy: jasmine.SpyObj<VacancyService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockVacancy: IVacancy = {
    id: 'vac-1',
    referenceNumber: 'VAC-2026-0001',
    title: 'Senior Engineer',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: 'jt-1',
    jobTitleName: 'Engineer',
    employmentType: 'FullTime',
    locationId: 'loc-1',
    locationName: 'HQ',
    hiringManagerId: 'emp-1',
    hiringManagerName: 'Jane Doe',
    headcount: 2,
    filledCount: 0,
    salaryMin: 100,
    salaryMax: 200,
    currency: 'USD',
    description: '<p>Role</p>',
    qualifications: '<p>Skills</p>',
    applicationDeadline: '2026-12-31',
    status: 'Draft',
    slug: 'senior-engineer',
    createdAt: '2026-06-01T00:00:00Z',
  };

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('VacancyService', [
      'createVacancy',
      'updateVacancy',
      'publishVacancy',
      'getDepartments',
      'getJobTitles',
      'getLocations',
      'searchEmployees',
    ]);
    serviceSpy.getDepartments.and.returnValue(of([{ id: 'dept-1', label: 'Eng' }]));
    serviceSpy.getJobTitles.and.returnValue(of([{ id: 'jt-1', label: 'Engineer' }]));
    serviceSpy.getLocations.and.returnValue(of([{ id: 'loc-1', label: 'HQ' }]));
    serviceSpy.searchEmployees.and.returnValue(
      of([{ id: 'emp-1', label: 'Jane Doe', sublabel: 'Engineer' }]),
    );
    serviceSpy.createVacancy.and.returnValue(of(mockVacancy));
    serviceSpy.updateVacancy.and.returnValue(of(mockVacancy));
    serviceSpy.publishVacancy.and.returnValue(of({ ...mockVacancy, status: 'Open' }));

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [VacancyFormComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: VacancyService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VacancyFormComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create and load lookups', () => {
    expect(component).toBeTruthy();
    expect(serviceSpy.getDepartments).toHaveBeenCalled();
    expect(component.departments().length).toBe(1);
    expect(component.employees().length).toBe(1);
  });

  it('marks the form invalid when title is empty and blocks save', () => {
    component.save(false);
    expect(serviceSpy.createVacancy).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('saves as draft with only a title', () => {
    component.form.get('title')!.setValue('Quick draft');
    component.save(false);
    expect(serviceSpy.createVacancy).toHaveBeenCalled();
    expect(serviceSpy.publishVacancy).not.toHaveBeenCalled();
    expect(toastrSpy.success).toHaveBeenCalledWith('Vacancy saved as draft.');
  });

  it('blocks publish when BR-2 required fields are missing', () => {
    component.form.get('title')!.setValue('Only a title');
    component.save(true);
    expect(serviceSpy.createVacancy).not.toHaveBeenCalled();
    expect(serviceSpy.publishVacancy).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('publishes after save when all BR-2 fields are present', () => {
    component.form.patchValue({
      title: 'Full role',
      departmentId: 'dept-1',
      jobTitleId: 'jt-1',
      hiringManagerId: 'emp-1',
      headcount: 3,
      description: '<p>desc</p>',
    });
    component.save(true);
    expect(serviceSpy.createVacancy).toHaveBeenCalled();
    expect(serviceSpy.publishVacancy).toHaveBeenCalledWith('vac-1');
    expect(toastrSpy.success).toHaveBeenCalledWith('Vacancy published.');
  });

  it('emits saved after a successful draft save', () => {
    const savedSpy = jasmine.createSpy('saved');
    component.saved.subscribe(savedSpy);
    component.form.get('title')!.setValue('Draft');
    component.save(false);
    expect(savedSpy).toHaveBeenCalledWith(mockVacancy);
  });

  it('shows the backend error message on save failure', () => {
    serviceSpy.createVacancy.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ error: { message: 'Boom' }, status: 400 }),
      ),
    );
    component.form.get('title')!.setValue('Draft');
    component.save(false);
    expect(toastrSpy.error).toHaveBeenCalledWith('Boom');
    expect(component.saving()).toBeFalse();
  });

  it('uses updateVacancy when editing an existing vacancy', () => {
    fixture.componentRef.setInput('vacancy', mockVacancy);
    fixture.detectChanges();
    expect(component.isEdit()).toBeTrue();
    expect(component.form.get('title')!.value).toBe('Senior Engineer');
    component.save(false);
    expect(serviceSpy.updateVacancy).toHaveBeenCalledWith('vac-1', jasmine.any(Object));
  });

  it('blocks save when salary range is invalid', () => {
    component.form.patchValue({
      title: 'Role',
      salaryMin: 200,
      salaryMax: 100,
    });
    component.save(false);
    expect(serviceSpy.createVacancy).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  // DF-26: the backend now rejects an omitted headcount with 400. The form must
  // default headcount to 1 and never submit it empty.
  it('defaults headcount to 1 and sends it on a draft save (DF-26)', () => {
    expect(component.form.get('headcount')!.value).toBe(1);
    component.form.get('title')!.setValue('Draft role');
    component.save(false);
    expect(serviceSpy.createVacancy).toHaveBeenCalled();
    const request = serviceSpy.createVacancy.calls.mostRecent().args[0];
    expect(request.headcount).toBe(1);
  });

  it('blocks save when headcount is cleared (DF-26)', () => {
    component.form.get('title')!.setValue('Role');
    component.form.get('headcount')!.setValue(null);
    component.save(false);
    expect(serviceSpy.createVacancy).not.toHaveBeenCalled();
    expect(toastrSpy.error).toHaveBeenCalled();
  });

  it('attemptClose emits closed', () => {
    const closedSpy = jasmine.createSpy('closed');
    component.closed.subscribe(closedSpy);
    component.attemptClose();
    expect(closedSpy).toHaveBeenCalled();
  });

  it('onManagerSearch re-queries employees', () => {
    serviceSpy.searchEmployees.calls.reset();
    serviceSpy.searchEmployees.and.returnValue(
      of([{ id: 'emp-2', label: 'Bob Smith', sublabel: null }]),
    );
    component.onManagerSearch({
      target: { value: 'bob' },
    } as unknown as Event);
    expect(serviceSpy.searchEmployees).toHaveBeenCalledWith('bob');
    expect(component.employees()[0].label).toBe('Bob Smith');
  });

  describe('salaryRangeValidator', () => {
    const make = (min: number | null, max: number | null) =>
      new FormGroup({
        salaryMin: new FormControl(min),
        salaryMax: new FormControl(max),
      });

    it('returns null when either bound is missing', () => {
      expect(salaryRangeValidator(make(null, 100))).toBeNull();
      expect(salaryRangeValidator(make(100, null))).toBeNull();
    });

    it('returns an error when max < min', () => {
      expect(salaryRangeValidator(make(200, 100))).toEqual({ salaryRange: true });
    });

    it('returns null when max >= min', () => {
      expect(salaryRangeValidator(make(100, 200))).toBeNull();
    });
  });
});
