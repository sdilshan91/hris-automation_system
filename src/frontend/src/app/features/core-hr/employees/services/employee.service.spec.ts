import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpErrorResponse } from '@angular/common/http';
import { EmployeeService } from './employee.service';
import {
  IEmployee,
  IEmployeeProfile,
  ICreateEmployeeRequest,
  IUpdateSectionRequest,
} from '../models/employee.models';
import { environment } from '../../../../../environments/environment';

describe('EmployeeService', () => {
  let service: EmployeeService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/employees`;

  const mockEmployee: IEmployee = {
    employeeId: 'emp-1',
    tenantId: 'tenant-1',
    employeeNo: 'EMP-0001',
    firstName: 'John',
    lastName: 'Doe',
    email: 'john.doe@company.com',
    phone: '+94771234567',
    dateOfBirth: '1990-01-15',
    gender: 'Male',
    dateOfJoining: '2026-06-01',
    departmentId: 'dept-1',
    departmentName: 'Engineering',
    jobTitleId: 'jt-1',
    jobTitleName: 'Software Engineer',
    employmentType: 'Full-Time',
    status: 'active',
    profilePhotoUrl: null,
    customFields: null,
    isActive: true,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        EmployeeService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(EmployeeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getEmployees', () => {
    it('should return all employees for the tenant', () => {
      service.getEmployees().subscribe((employees) => {
        expect(employees.length).toBe(1);
        expect(employees[0].firstName).toBe('John');
        expect(employees[0].employeeNo).toBe('EMP-0001');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockEmployee]);
    });

    it('should return an empty array when no employees exist', () => {
      service.getEmployees().subscribe((employees) => {
        expect(employees.length).toBe(0);
      });

      const req = httpMock.expectOne(baseUrl);
      req.flush([]);
    });
  });

  describe('getEmployee', () => {
    it('should return a single employee by ID', () => {
      service.getEmployee('emp-1').subscribe((employee) => {
        expect(employee.employeeId).toBe('emp-1');
        expect(employee.email).toBe('john.doe@company.com');
      });

      const req = httpMock.expectOne(`${baseUrl}/emp-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockEmployee);
    });
  });

  describe('createEmployee', () => {
    const createRequest: ICreateEmployeeRequest = {
      firstName: 'Jane',
      lastName: 'Smith',
      email: 'jane.smith@company.com',
      dateOfJoining: '2026-07-01',
      departmentId: 'dept-1',
      jobTitleId: 'jt-1',
      employmentType: 'Full-Time',
    };

    it('should create an employee with JSON when no photo is provided', () => {
      service.createEmployee(createRequest).subscribe((employee) => {
        expect(employee.firstName).toBe('Jane');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(createRequest);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockEmployee, firstName: 'Jane', lastName: 'Smith' });
    });

    it('should create an employee with multipart FormData when a photo is provided', () => {
      const mockFile = new File(['photo-data'], 'avatar.jpg', {
        type: 'image/jpeg',
      });

      service.createEmployee(createRequest, mockFile).subscribe((employee) => {
        expect(employee.firstName).toBe('Jane');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body instanceof FormData).toBeTrue();
      expect(req.request.withCredentials).toBeTrue();

      // Verify FormData contains expected fields
      const formData = req.request.body as FormData;
      expect(formData.get('firstName')).toBe('Jane');
      expect(formData.get('lastName')).toBe('Smith');
      expect(formData.get('email')).toBe('jane.smith@company.com');
      expect(formData.get('profilePhoto')).toBeTruthy();

      req.flush({ ...mockEmployee, firstName: 'Jane', lastName: 'Smith' });
    });

    it('should not send null/undefined fields in FormData', () => {
      const requestWithNulls: ICreateEmployeeRequest = {
        ...createRequest,
        phone: null,
        dateOfBirth: null,
      };

      const mockFile = new File(['data'], 'pic.png', { type: 'image/png' });

      service
        .createEmployee(requestWithNulls, mockFile)
        .subscribe();

      const req = httpMock.expectOne(baseUrl);
      const formData = req.request.body as FormData;
      expect(formData.has('phone')).toBeFalse();
      expect(formData.has('dateOfBirth')).toBeFalse();
      req.flush(mockEmployee);
    });
  });

  describe('parseError', () => {
    it('should parse a duplicate_email error response', () => {
      const httpErr = new HttpErrorResponse({
        error: {
          message: 'An employee with this email already exists.',
          code: 'duplicate_email',
        },
        status: 409,
      });

      const parsed = EmployeeService.parseError(httpErr);
      expect(parsed).toBeTruthy();
      expect(parsed!.code).toBe('duplicate_email');
      expect(parsed!.message).toBe(
        'An employee with this email already exists.'
      );
    });

    it('should parse a plan_limit_reached error response', () => {
      const httpErr = new HttpErrorResponse({
        error: {
          message: 'Employee limit reached for your current plan.',
          code: 'plan_limit_reached',
        },
        status: 403,
      });

      const parsed = EmployeeService.parseError(httpErr);
      expect(parsed).toBeTruthy();
      expect(parsed!.code).toBe('plan_limit_reached');
    });

    it('should return null for non-standard error shapes', () => {
      const httpErr = new HttpErrorResponse({
        error: 'Server error string',
        status: 500,
      });

      const parsed = EmployeeService.parseError(httpErr);
      expect(parsed).toBeNull();
    });
  });

  // ─── US-CHR-002: Profile methods ──────────────────────────

  describe('getEmployeeProfile (US-CHR-002)', () => {
    const mockProfile: IEmployeeProfile = {
      ...mockEmployee,
      xmin: '12345',
      personalEmail: 'john@personal.com',
      address: '123 Main St',
      city: 'Colombo',
      state: 'Western',
      postalCode: '10100',
      country: 'Sri Lanka',
      reportingManagerId: null,
      reportingManagerName: null,
      emergencyContacts: [],
      education: [],
      workHistory: [],
      dependents: [],
      employmentHistory: [],
    };

    it('should GET the full employee profile', () => {
      service.getEmployeeProfile('emp-1').subscribe((profile) => {
        expect(profile.employeeId).toBe('emp-1');
        expect(profile.xmin).toBe('12345');
        expect(profile.emergencyContacts).toEqual([]);
      });

      const req = httpMock.expectOne(`${baseUrl}/emp-1/profile`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockProfile);
    });
  });

  describe('updateProfileSection (US-CHR-002)', () => {
    it('should PATCH a section with xmin concurrency token', () => {
      const request: IUpdateSectionRequest = {
        xmin: '12345',
        data: { phone: '+94779999999' },
      };

      service
        .updateProfileSection('emp-1', 'contact', request)
        .subscribe((response) => {
          expect(response.xmin).toBe('12346');
        });

      const req = httpMock.expectOne(`${baseUrl}/emp-1/sections/contact`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body.xmin).toBe('12345');
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ xmin: '12346', profile: {} });
    });

    it('should call the correct section URL', () => {
      const request: IUpdateSectionRequest = {
        xmin: '100',
        data: { firstName: 'Jane' },
      };

      service
        .updateProfileSection('emp-2', 'personal-info', request)
        .subscribe();

      const req = httpMock.expectOne(`${baseUrl}/emp-2/sections/personal-info`);
      expect(req.request.method).toBe('PATCH');
      req.flush({ xmin: '101', profile: {} });
    });
  });
});
