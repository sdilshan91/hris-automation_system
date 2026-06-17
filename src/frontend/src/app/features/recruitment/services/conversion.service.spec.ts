import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
} from '@angular/common/http';

import { ConversionService } from './conversion.service';
import { environment } from '../../../../environments/environment';
import {
  IConversionPrefill,
  IConvertEmployeeRequest,
  IConvertResult,
  ALREADY_CONVERTED_CODE,
} from '../models/conversion.models';

describe('ConversionService (US-REC-010)', () => {
  let service: ConversionService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/recruitment/applicants`;

  const prefillDto = (over: Partial<IConversionPrefill> = {}): IConversionPrefill => ({
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

  const request: IConvertEmployeeRequest = {
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    phone: '123',
    employeeNumber: null,
    jobTitleId: 'jt1',
    departmentId: 'd1',
    reportingManagerId: 'm1',
    employmentType: 'FullTime',
    workLocationId: 'loc1',
    dateOfJoining: '2026-08-01',
    salaryAmount: 120000,
    currency: 'USD',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ConversionService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ConversionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('GETs the conversion prefill and maps the BARE DTO', () => {
    let result: IConversionPrefill | undefined;
    service.getPrefill('a1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/a1/conversion-prefill`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush(prefillDto());

    expect(result?.firstName).toBe('Ada');
    expect(result?.suggestedEmployeeNumber).toBe('EMP-2026-0042');
    expect(result?.alreadyConverted).toBeFalse();
  });

  it('tolerates a sparse prefill DTO (defaults applied)', () => {
    let result: IConversionPrefill | undefined;
    service.getPrefill('a1').subscribe((r) => (result = r));

    httpMock.expectOne(`${base}/a1/conversion-prefill`).flush({ applicantId: 'a1' });

    expect(result?.alreadyConverted).toBeFalse();
    expect(result?.phone).toBeNull();
    expect(result?.salaryAmount).toBeNull();
  });

  it('POSTs the convert request and maps the result (AC-2/AC-4)', () => {
    let result: IConvertResult | undefined;
    service.convert('a1', request).subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/a1/convert`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    // The service maps the FE request to the backend ConvertApplicantRequest shape
    // (reportsToEmployeeId/locationId/employeeNo) and normalizes the employment type.
    expect(req.request.body).toEqual({
      jobTitleId: 'jt1',
      departmentId: 'd1',
      employmentType: 'FullTime',
      dateOfJoining: '2026-08-01',
      reportsToEmployeeId: 'm1',
      locationId: 'loc1',
      employeeNo: null,
    });
    req.flush({
      employeeId: 'emp-9',
      employeeNumber: 'EMP-2026-0042',
      firstName: 'Ada',
      lastName: 'Lovelace',
      userAccountCreated: true,
      vacancyFilledCount: 1,
      vacancyHeadcount: 1,
      vacancyClosed: true,
    } as IConvertResult);

    expect(result?.employeeId).toBe('emp-9');
    expect(result?.userAccountCreated).toBeTrue();
    expect(result?.vacancyClosed).toBeTrue();
  });

  it('parseError extracts the typed code (BR-2)', () => {
    const err = new HttpErrorResponse({
      status: 409,
      error: { message: 'Already converted', code: ALREADY_CONVERTED_CODE },
    });
    expect(ConversionService.parseError(err)?.code).toBe(ALREADY_CONVERTED_CODE);
    expect(ConversionService.parseErrorMessage(err)).toBe('Already converted');
  });

  it('parseErrorMessage falls back for an untyped error', () => {
    const err = new HttpErrorResponse({ status: 500, error: 'boom' });
    expect(ConversionService.parseErrorMessage(err)).toBe(
      'An unexpected error occurred.',
    );
  });
});
