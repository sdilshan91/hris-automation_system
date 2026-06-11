import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { DepartmentService } from './department.service';
import {
  IDepartment,
  ICreateDepartmentRequest,
  IUpdateDepartmentRequest,
} from '../models/department.models';
import { environment } from '../../../../../environments/environment';

describe('DepartmentService', () => {
  let service: DepartmentService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/departments`;

  const mockDepartment: IDepartment = {
    departmentId: 'dept-1',
    tenantId: 'tenant-1',
    name: 'Engineering',
    description: 'Software engineering team',
    parentDepartmentId: null,
    parentDepartmentName: null,
    managerEmployeeId: null,
    managerName: null,
    isActive: true,
    employeeCount: 10,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  };

  const mockChildDepartment: IDepartment = {
    departmentId: 'dept-2',
    tenantId: 'tenant-1',
    name: 'Frontend',
    description: 'Frontend development',
    parentDepartmentId: 'dept-1',
    parentDepartmentName: 'Engineering',
    managerEmployeeId: null,
    managerName: null,
    isActive: true,
    employeeCount: 5,
    createdAt: '2026-01-15T00:00:00Z',
    updatedAt: '2026-01-15T00:00:00Z',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        DepartmentService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(DepartmentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getDepartments', () => {
    it('should return all departments for the tenant', () => {
      service.getDepartments().subscribe((departments) => {
        expect(departments.length).toBe(2);
        expect(departments[0].name).toBe('Engineering');
        expect(departments[1].name).toBe('Frontend');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockDepartment, mockChildDepartment]);
    });

    it('should return an empty array when no departments exist', () => {
      service.getDepartments().subscribe((departments) => {
        expect(departments.length).toBe(0);
      });

      const req = httpMock.expectOne(baseUrl);
      req.flush([]);
    });
  });

  describe('getDepartment', () => {
    it('should return a single department by ID', () => {
      service.getDepartment('dept-1').subscribe((department) => {
        expect(department.departmentId).toBe('dept-1');
        expect(department.name).toBe('Engineering');
      });

      const req = httpMock.expectOne(`${baseUrl}/dept-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockDepartment);
    });
  });

  describe('createDepartment', () => {
    it('should create a new department', () => {
      const request: ICreateDepartmentRequest = {
        name: 'Design',
        description: 'UI/UX design team',
        parentDepartmentId: 'dept-1',
        isActive: true,
      };

      service.createDepartment(request).subscribe((department) => {
        expect(department.name).toBe('Design');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({
        ...mockDepartment,
        departmentId: 'dept-3',
        name: 'Design',
        parentDepartmentId: 'dept-1',
        parentDepartmentName: 'Engineering',
      });
    });

    it('should create a root department without parent', () => {
      const request: ICreateDepartmentRequest = {
        name: 'Operations',
        isActive: true,
      };

      service.createDepartment(request).subscribe((department) => {
        expect(department.name).toBe('Operations');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.body.parentDepartmentId).toBeUndefined();
      req.flush({ ...mockDepartment, name: 'Operations' });
    });
  });

  describe('updateDepartment', () => {
    it('should update an existing department', () => {
      const request: IUpdateDepartmentRequest = {
        name: 'Engineering (Updated)',
        description: 'Updated description',
        parentDepartmentId: null,
        isActive: true,
      };

      service.updateDepartment('dept-1', request).subscribe((department) => {
        expect(department.name).toBe('Engineering (Updated)');
      });

      const req = httpMock.expectOne(`${baseUrl}/dept-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockDepartment, name: 'Engineering (Updated)' });
    });

    it('should update parent department for hierarchy change (FR-4)', () => {
      const request: IUpdateDepartmentRequest = {
        name: 'Frontend',
        parentDepartmentId: 'dept-3',
        isActive: true,
      };

      service.updateDepartment('dept-2', request).subscribe((department) => {
        expect(department.parentDepartmentId).toBe('dept-3');
      });

      const req = httpMock.expectOne(`${baseUrl}/dept-2`);
      expect(req.request.method).toBe('PUT');
      req.flush({
        ...mockChildDepartment,
        parentDepartmentId: 'dept-3',
        parentDepartmentName: 'Design',
      });
    });
  });

  describe('deactivateDepartment', () => {
    it('should deactivate a department (FR-6, FR-7)', () => {
      service.deactivateDepartment('dept-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/dept-1/deactivate`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(null);
    });
  });
});
