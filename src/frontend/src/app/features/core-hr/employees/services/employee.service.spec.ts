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
  IEmployeeDirectoryParams,
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

  // ─── US-CHR-003: Directory queries ────────────────────────

  describe('queryDirectory', () => {
    it('should send GET with search, page, and pageSize params', () => {
      const params: IEmployeeDirectoryParams = {
        search: 'John',
        page: 1,
        pageSize: 20,
        sort: 'name',
        sortDirection: 'asc',
      };

      service.queryDirectory(params).subscribe((response) => {
        expect(response.data.length).toBe(1);
        expect(response.total).toBe(1);
      });

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('search') === 'John' &&
          r.params.get('page') === '1' &&
          r.params.get('pageSize') === '20' &&
          r.params.get('sort') === 'name' &&
          r.params.get('sortDirection') === 'asc'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ data: [mockEmployee], total: 1, page: 1, pageSize: 20 });
    });

    it('should send multi-select filters as comma-separated values', () => {
      const params: IEmployeeDirectoryParams = {
        departments: ['Engineering', 'Sales'],
        statuses: ['active', 'probation'],
        employmentTypes: ['Full-Time', 'Contract'],
        page: 1,
        pageSize: 20,
      };

      service.queryDirectory(params).subscribe();

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('departments') === 'Engineering,Sales' &&
          r.params.get('statuses') === 'active,probation' &&
          r.params.get('employmentTypes') === 'Full-Time,Contract'
      );
      req.flush({ data: [], total: 0, page: 1, pageSize: 20 });
    });

    it('should send date range filter params', () => {
      const params: IEmployeeDirectoryParams = {
        dateOfJoiningFrom: '2026-01-01',
        dateOfJoiningTo: '2026-12-31',
        page: 1,
        pageSize: 20,
      };

      service.queryDirectory(params).subscribe();

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('dateOfJoiningFrom') === '2026-01-01' &&
          r.params.get('dateOfJoiningTo') === '2026-12-31'
      );
      req.flush({ data: [], total: 0, page: 1, pageSize: 20 });
    });

    it('should include includeArchived param when set', () => {
      const params: IEmployeeDirectoryParams = {
        includeArchived: true,
        page: 1,
        pageSize: 20,
      };

      service.queryDirectory(params).subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('includeArchived') === 'true'
      );
      req.flush({ data: [], total: 0, page: 1, pageSize: 20 });
    });

    it('should omit undefined/empty params', () => {
      const params: IEmployeeDirectoryParams = {
        page: 1,
        pageSize: 20,
      };

      service.queryDirectory(params).subscribe();

      const req = httpMock.expectOne((r) => r.url === baseUrl);
      expect(req.request.params.has('search')).toBeFalse();
      expect(req.request.params.has('departments')).toBeFalse();
      expect(req.request.params.has('location')).toBeFalse();
      expect(req.request.params.has('includeArchived')).toBeFalse();
      req.flush({ data: [], total: 0, page: 1, pageSize: 20 });
    });
  });

  describe('exportDirectory', () => {
    it('should request CSV export as blob', () => {
      const params: IEmployeeDirectoryParams = {
        search: 'John',
        page: 1,
        pageSize: 20,
      };

      service.exportDirectory(params, 'csv').subscribe((blob) => {
        expect(blob).toBeTruthy();
        expect(blob instanceof Blob).toBeTrue();
      });

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/export` &&
          r.params.get('format') === 'csv' &&
          r.params.get('search') === 'John'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.responseType).toBe('blob');
      req.flush(new Blob(['csv-data'], { type: 'text/csv' }));
    });

    it('should request Excel export as blob', () => {
      const params: IEmployeeDirectoryParams = {
        departments: ['Engineering'],
        page: 1,
        pageSize: 20,
      };

      service.exportDirectory(params, 'excel').subscribe();

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/export` &&
          r.params.get('format') === 'excel' &&
          r.params.get('departments') === 'Engineering'
      );
      req.flush(
        new Blob(['xlsx-data'], {
          type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        })
      );
    });
  });

  describe('buildDirectoryParams', () => {
    it('should build HttpParams from directory params', () => {
      const params: IEmployeeDirectoryParams = {
        search: 'test',
        departments: ['A', 'B'],
        sort: 'department',
        sortDirection: 'desc',
        page: 2,
        pageSize: 50,
      };

      const httpParams = service.buildDirectoryParams(params);

      expect(httpParams.get('search')).toBe('test');
      expect(httpParams.get('departments')).toBe('A,B');
      expect(httpParams.get('sort')).toBe('department');
      expect(httpParams.get('sortDirection')).toBe('desc');
      expect(httpParams.get('page')).toBe('2');
      expect(httpParams.get('pageSize')).toBe('50');
    });

    it('should skip undefined/empty values', () => {
      const params: IEmployeeDirectoryParams = {};

      const httpParams = service.buildDirectoryParams(params);

      expect(httpParams.keys().length).toBe(0);
    });
  });

  // ─── US-CHR-001 existing tests ────────────────────────────

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

  // ─── US-CHR-009: Status management ─────────────────────────

  describe('getValidTransitions (US-CHR-009)', () => {
    it('should GET valid transitions for an employee', () => {
      const mockTransitions = [
        { targetStatus: 'suspended' as const, label: 'Suspended', sideEffects: ['Disable portal access'] },
        { targetStatus: 'terminated' as const, label: 'Terminated', sideEffects: ['Disable portal access', 'Exclude from payroll'] },
      ];

      service.getValidTransitions('emp-1').subscribe((transitions) => {
        expect(transitions.length).toBe(2);
        expect(transitions[0].targetStatus).toBe('suspended');
        expect(transitions[1].sideEffects.length).toBe(2);
      });

      const req = httpMock.expectOne(`${baseUrl}/emp-1/status/transitions`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockTransitions);
    });

    it('should return empty array for terminal status', () => {
      service.getValidTransitions('emp-2').subscribe((transitions) => {
        expect(transitions.length).toBe(0);
      });

      const req = httpMock.expectOne(`${baseUrl}/emp-2/status/transitions`);
      req.flush([]);
    });
  });

  describe('changeStatus (US-CHR-009)', () => {
    it('should POST status change with Idempotency-Key header', () => {
      const request = {
        newStatus: 'suspended' as const,
        effectiveDate: '2026-06-15',
        reason: 'Pending investigation',
      };
      const idempotencyKey = 'test-uuid-1234';

      service.changeStatus('emp-1', request, idempotencyKey).subscribe((response) => {
        expect(response.profile).toBeTruthy();
      });

      const req = httpMock.expectOne(`${baseUrl}/emp-1/status`);
      expect(req.request.method).toBe('POST');
      expect(req.request.withCredentials).toBeTrue();
      expect(req.request.headers.get('Idempotency-Key')).toBe('test-uuid-1234');
      expect(req.request.body.newStatus).toBe('suspended');
      expect(req.request.body.effectiveDate).toBe('2026-06-15');
      expect(req.request.body.reason).toBe('Pending investigation');
      req.flush({ profile: { ...mockEmployee, status: 'suspended' } });
    });

    it('should handle 400 error for invalid transition', () => {
      const request = {
        newStatus: 'probation' as const,
        effectiveDate: '2026-06-15',
        reason: 'Attempting invalid transition',
      };

      service.changeStatus('emp-1', request, 'key-123').subscribe({
        error: (err) => {
          expect(err.status).toBe(400);
        },
      });

      const req = httpMock.expectOne(`${baseUrl}/emp-1/status`);
      req.flush(
        { message: 'Invalid status transition. Terminated employees cannot be moved to probation.' },
        { status: 400, statusText: 'Bad Request' }
      );
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
