import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { ConversionFormComponent } from './conversion-form.component';
import { ConversionService } from '../../services/conversion.service';
import { VacancyService } from '../../services/vacancy.service';
import {
  IConversionPrefill,
  IConvertResult,
  ALREADY_CONVERTED_CODE,
  PLAN_LIMIT_CODE,
} from '../../models/conversion.models';
import { ILookupOption } from '../../models/vacancy.models';

describe('ConversionFormComponent (US-REC-010)', () => {
  let fixture: ComponentFixture<ConversionFormComponent>;
  let component: ConversionFormComponent;
  let conversionSpy: jasmine.SpyObj<ConversionService>;
  let vacancySpy: jasmine.SpyObj<VacancyService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const dept: ILookupOption = { id: 'd1', label: 'Engineering' };
  const jt: ILookupOption = { id: 'jt1', label: 'Engineer' };
  const loc: ILookupOption = { id: 'loc1', label: 'HQ' };
  const mgr: ILookupOption = { id: 'm1', label: 'Boss' };

  const prefill = (over: Partial<IConversionPrefill> = {}): IConversionPrefill => ({
    applicantId: 'a1',
    alreadyConverted: false,
    convertedToEmployeeId: null,
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    phone: '123',
    jobTitleId: 'jt1',
    jobTitleName: 'Engineer',
    departmentId: 'd1',
    departmentName: 'Engineering',
    reportingManagerId: 'm1',
    reportingManagerName: 'Boss',
    dateOfJoining: '2026-08-01',
    probationMonths: 3,
    salaryAmount: 120000,
    currency: 'USD',
    suggestedEmployeeNumber: 'EMP-2026-0042',
    vacancyId: 'v1',
    vacancyTitle: 'Senior Engineer',
    vacancyFilledCount: 0,
    vacancyHeadcount: 1,
    ...over,
  });

  const result = (over: Partial<IConvertResult> = {}): IConvertResult => ({
    employeeId: 'emp-9',
    employeeNumber: 'EMP-2026-0042',
    firstName: 'Ada',
    lastName: 'Lovelace',
    userAccountCreated: true,
    vacancyFilledCount: 1,
    vacancyHeadcount: 1,
    vacancyClosed: true,
    ...over,
  });

  function create(pre: IConversionPrefill = prefill()): void {
    conversionSpy.getPrefill.and.returnValue(of(pre));
    fixture = TestBed.createComponent(ConversionFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('applicantId', 'a1');
    fixture.componentRef.setInput('applicantName', 'Ada Lovelace');
    fixture.detectChanges();
  }

  beforeEach(async () => {
    conversionSpy = jasmine.createSpyObj('ConversionService', [
      'getPrefill',
      'convert',
    ]);
    vacancySpy = jasmine.createSpyObj('VacancyService', [
      'getDepartments',
      'getJobTitles',
      'getLocations',
      'searchEmployees',
    ]);
    vacancySpy.getDepartments.and.returnValue(of([dept]));
    vacancySpy.getJobTitles.and.returnValue(of([jt]));
    vacancySpy.getLocations.and.returnValue(of([loc]));
    vacancySpy.searchEmployees.and.returnValue(of([mgr]));

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [ConversionFormComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: ConversionService, useValue: conversionSpy },
        { provide: VacancyService, useValue: vacancySpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  // ─── Prefill (FR-2 / FR-3 / AC-1) ─────────────────────────

  it('loads the prefill + lookups and shows the form', () => {
    create();
    expect(conversionSpy.getPrefill).toHaveBeenCalledWith('a1');
    expect(component.step()).toBe('form');
    expect(component.form.get('firstName')?.value).toBe('Ada');
    expect(component.form.get('departmentId')?.value).toBe('d1');
    expect(component.departments().length).toBe(1);
  });

  it('marks pre-filled fields as auto-filled but leaves required ones empty (FR-2)', () => {
    create(prefill({ jobTitleId: null, jobTitleName: null }));
    expect(component.isAutoFilled('firstName')).toBeTrue();
    expect(component.isAutoFilled('salaryAmount')).toBeTrue();
    expect(component.isAutoFilled('jobTitleId')).toBeFalse();
  });

  it('seeds the suggested employee number, locked until override (FR-4)', () => {
    create();
    expect(component.form.getRawValue().employeeNumber).toBe('EMP-2026-0042');
    expect(component.overrideNumber()).toBeFalse();
    expect(component.form.get('employeeNumber')?.disabled).toBeTrue();

    component.toggleOverride();
    expect(component.overrideNumber()).toBeTrue();
    expect(component.form.get('employeeNumber')?.disabled).toBeFalse();
  });

  // ─── Blocked: already converted (BR-2) ────────────────────

  it('shows the blocked banner + employee link when already converted (BR-2)', () => {
    create(prefill({ alreadyConverted: true, convertedToEmployeeId: 'emp-7' }));
    expect(component.blockedMessage()).toContain('already been converted');
    expect(component.alreadyConvertedEmployeeId()).toBe('emp-7');
  });

  // ─── Review / validation ──────────────────────────────────

  it('does not advance to confirm while required fields are missing', () => {
    create();
    // workLocation + employmentType are not pre-filled (required, FR-2).
    component.review();
    expect(component.step()).toBe('form');
    expect(component.form.touched).toBeTrue();
  });

  it('advances to the confirmation summary when valid', () => {
    create();
    component.form.patchValue({
      employmentType: 'Full-Time',
      workLocationId: 'loc1',
    });
    component.review();
    expect(component.step()).toBe('confirm');
    expect(component.summaryName()).toBe('Ada Lovelace');
    expect(component.summaryPosition()).toBe('Engineer');
    expect(component.summaryDepartment()).toBe('Engineering');
    expect(component.summarySalary()).toBe('USD 120,000');
  });

  // ─── Create (AC-2 / AC-4) ─────────────────────────────────

  function fillValid(): void {
    component.form.patchValue({
      employmentType: 'Full-Time',
      workLocationId: 'loc1',
    });
  }

  it('create POSTs the conversion and emits converted on success (AC-2/AC-4)', () => {
    create();
    fillValid();
    component.review();
    conversionSpy.convert.and.returnValue(of(result()));
    const convertedSpy = jasmine.createSpy('converted');
    component.converted.subscribe(convertedSpy);

    component.create();

    expect(conversionSpy.convert).toHaveBeenCalledWith(
      'a1',
      jasmine.objectContaining({
        firstName: 'Ada',
        employmentType: 'Full-Time',
        workLocationId: 'loc1',
        departmentId: 'd1',
        jobTitleId: 'jt1',
        employeeNumber: null,
      }),
    );
    expect(component.step()).toBe('success');
    expect(component.result()?.employeeId).toBe('emp-9');
    expect(component.vacancyRatio()).toBe('1 / 1');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(convertedSpy).toHaveBeenCalled();
  });

  it('sends the overridden employee number when override is on (FR-4)', () => {
    create();
    fillValid();
    component.toggleOverride();
    component.form.patchValue({ employeeNumber: 'CUSTOM-1' });
    component.review();
    conversionSpy.convert.and.returnValue(of(result()));

    component.create();

    expect(conversionSpy.convert).toHaveBeenCalledWith(
      'a1',
      jasmine.objectContaining({ employeeNumber: 'CUSTOM-1' }),
    );
  });

  // ─── Blocked on submit (BR-2 / BR-3) ──────────────────────

  it('surfaces an already-converted error inline on create (BR-2)', () => {
    create();
    fillValid();
    component.review();
    conversionSpy.convert.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 409,
            error: { message: 'Already converted', code: ALREADY_CONVERTED_CODE },
          }),
      ),
    );
    component.create();
    expect(component.blockedMessage()).toBe('Already converted');
    expect(component.step()).toBe('form');
    expect(component.creating()).toBeFalse();
  });

  it('surfaces a plan-limit error inline on create (BR-3)', () => {
    create();
    fillValid();
    component.review();
    conversionSpy.convert.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 402,
            error: { message: 'Upgrade your plan', code: PLAN_LIMIT_CODE },
          }),
      ),
    );
    component.create();
    expect(component.blockedMessage()).toBe('Upgrade your plan');
    expect(component.step()).toBe('form');
  });

  it('falls back to a toast for an untyped create error', () => {
    create();
    fillValid();
    component.review();
    conversionSpy.convert.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500, error: 'boom' })),
    );
    component.create();
    expect(toastrSpy.error).toHaveBeenCalled();
    expect(component.step()).toBe('confirm');
  });

  // ─── Cancel ───────────────────────────────────────────────

  it('cancel emits cancelled', () => {
    create();
    const cancelSpy = jasmine.createSpy('cancelled');
    component.cancelled.subscribe(cancelSpy);
    component.cancel();
    expect(cancelSpy).toHaveBeenCalled();
  });
});
