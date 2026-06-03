import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { RolesService } from './roles.service';
import { IRole, ICreateRoleRequest, IUserWithRoles } from '../models/role.models';
import { environment } from '../../../../../environments/environment';

describe('RolesService', () => {
  let service: RolesService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/roles`;
  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  const mockRole: IRole = {
    roleId: 'role-1',
    tenantId: 'tenant-1',
    name: 'HR Officer',
    description: 'Manages HR operations',
    isBuiltIn: true,
    permissions: ['Employee.View.All', 'Leave.Approve.All'],
    userCount: 5,
    createdAt: '2026-01-01T00:00:00Z',
  };

  const mockUser: IUserWithRoles = {
    userId: 'user-1',
    userTenantId: 'ut-1',
    email: 'john@example.com',
    displayName: 'John Doe',
    roles: [
      {
        roleId: 'role-1',
        roleName: 'HR Officer',
        isBuiltIn: true,
        assignedAt: '2026-01-01T00:00:00Z',
        assignedBy: 'admin',
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        RolesService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(RolesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getRoles', () => {
    it('should return all roles for the tenant', () => {
      service.getRoles().subscribe((roles) => {
        expect(roles.length).toBe(1);
        expect(roles[0].name).toBe('HR Officer');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('GET');
      req.flush([mockRole]);
    });
  });

  describe('getRole', () => {
    it('should return a single role by ID', () => {
      service.getRole('role-1').subscribe((role) => {
        expect(role.roleId).toBe('role-1');
        expect(role.name).toBe('HR Officer');
      });

      const req = httpMock.expectOne(`${baseUrl}/role-1`);
      expect(req.request.method).toBe('GET');
      req.flush(mockRole);
    });
  });

  describe('createRole', () => {
    it('should create a new custom role', () => {
      const request: ICreateRoleRequest = {
        name: 'Custom Role',
        description: 'A custom role',
        permissions: ['Employee.View.All'],
      };

      service.createRole(request).subscribe((role) => {
        expect(role.name).toBe('Custom Role');
      });

      const req = httpMock.expectOne(baseUrl);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      req.flush({ ...mockRole, name: 'Custom Role', isBuiltIn: false });
    });
  });

  describe('updateRole', () => {
    it('should update an existing custom role', () => {
      const request = {
        name: 'Updated Role',
        description: 'Updated description',
        permissions: ['Employee.View.All'],
      };

      service.updateRole('role-1', request).subscribe((role) => {
        expect(role.name).toBe('Updated Role');
      });

      const req = httpMock.expectOne(`${baseUrl}/role-1`);
      expect(req.request.method).toBe('PUT');
      req.flush({ ...mockRole, name: 'Updated Role' });
    });
  });

  describe('deleteRole', () => {
    it('should delete a custom role', () => {
      service.deleteRole('role-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/role-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
    });
  });

  describe('getUserWithRoles', () => {
    it('should return a user with their role assignments', () => {
      service.getUserWithRoles('ut-1').subscribe((user) => {
        expect(user.displayName).toBe('John Doe');
        expect(user.roles.length).toBe(1);
      });

      const req = httpMock.expectOne(`${usersUrl}/ut-1`);
      expect(req.request.method).toBe('GET');
      req.flush(mockUser);
    });
  });

  describe('assignRoles', () => {
    it('should assign roles to a user', () => {
      service
        .assignRoles('ut-1', { roleIds: ['role-1', 'role-2'] })
        .subscribe((user) => {
          expect(user.displayName).toBe('John Doe');
        });

      const req = httpMock.expectOne(`${usersUrl}/ut-1`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ roleIds: ['role-1', 'role-2'] });
      req.flush(mockUser);
    });
  });
});
