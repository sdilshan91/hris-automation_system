import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { RolesService } from './roles.service';
import {
  IRole,
  ICreateRoleRequest,
  IUserWithRoles,
  RoleWire,
  TenantUserDetailWire,
} from '../models/role.models';
import { environment } from '../../../../../environments/environment';

describe('RolesService', () => {
  let service: RolesService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/roles`;
  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  /** D1 — WIRE fixtures (`Schema<'RolesRoleDto'>`), not view models. */
  const mockRoleWire: RoleWire = {
    id: 'role-1',
    name: 'HR Officer',
    description: 'Manages HR operations',
    isBuiltIn: true,
    permissions: ['Employee.View.All', 'Leave.Approve.All'],
    userCount: 5,
    createdAt: '2026-01-01T00:00:00Z',
  };

  /**
   * `UsersTenantUserDetailDto`. Note the role entries carry `{ roleId, name }` —
   * there is no `roleName`, and no `isBuiltIn`/`assignedAt`/`assignedBy` at all.
   * The previous fixture invented all four.
   */
  const mockUserDetailWire: TenantUserDetailWire = {
    userId: 'user-1',
    userTenantId: 'ut-1',
    email: 'john@example.com',
    displayName: 'John Doe',
    status: 'Active',
    lastLoginAt: '2026-02-01T09:00:00Z',
    linkedEmployeeId: null,
    roles: [{ roleId: 'role-1', name: 'HR Officer' }],
    activeSessions: [],
    invitationHistory: [],
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
      req.flush([mockRoleWire]);
    });
  });

  describe('getRole', () => {
    it('should return a single role by ID', () => {
      service.getRole('role-1').subscribe((role) => {
        expect(role.id).toBe('role-1');
        expect(role.name).toBe('HR Officer');
      });

      const req = httpMock.expectOne(`${baseUrl}/role-1`);
      expect(req.request.method).toBe('GET');
      req.flush(mockRoleWire);
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
      req.flush({ ...mockRoleWire, name: 'Custom Role', isBuiltIn: false });
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
      req.flush({ ...mockRoleWire, name: 'Updated Role' });
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
      let user: IUserWithRoles | undefined;
      service.getUserWithRoles('ut-1').subscribe((u) => (user = u));

      const req = httpMock.expectOne(`${usersUrl}/ut-1`);
      expect(req.request.method).toBe('GET');
      req.flush(mockUserDetailWire);

      expect(user?.displayName).toBe('John Doe');
      // RENAME asserted: the wire sends `name`, the view-model exposes `roleName`.
      expect(user?.roles).toEqual([
        { roleId: 'role-1', roleName: 'HR Officer' },
      ]);
    });

    it('defaults every optional wire field on a sparse detail payload', () => {
      let user: IUserWithRoles | undefined;
      service.getUserWithRoles('ut-1').subscribe((u) => (user = u));

      httpMock.expectOne(`${usersUrl}/ut-1`).flush({ userTenantId: 'ut-1' });

      expect(user).toEqual({
        userId: '',
        userTenantId: 'ut-1',
        email: '',
        displayName: '',
        roles: [],
      });
    });
  });

  describe('assignRoles', () => {
    it('completes with no payload — the PATCH returns a bare envelope', () => {
      let completed = false;
      service
        .assignRoles('ut-1', { roleIds: ['role-1', 'role-2'] })
        .subscribe(() => (completed = true));

      const req = httpMock.expectOne(`${usersUrl}/ut-1`);
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ roleIds: ['role-1', 'role-2'] });
      req.flush(null);

      expect(completed).toBeTrue();
    });
  });

  describe('mapRole', () => {
    it('defaults the narrowed id/name instead of asserting them', () => {
      let roles: IRole[] | undefined;
      service.getRoles().subscribe((r) => (roles = r));

      // `RolesRoleDto` marks every property optional; the UI cannot render a row
      // without id/name, so the mapper — not a cast — is what supplies them.
      httpMock.expectOne(baseUrl).flush([{ description: 'orphan' }]);

      expect(roles?.[0].id).toBe('');
      expect(roles?.[0].name).toBe('');
      expect(roles?.[0].description).toBe('orphan');
    });
  });
});
