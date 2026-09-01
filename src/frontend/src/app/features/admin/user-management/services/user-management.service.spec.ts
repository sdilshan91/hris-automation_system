import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { UserManagementService } from './user-management.service';
import {
  IUserListResponse,
  IUserDetail,
  IInviteResult,
  IInvitation,
  IAssignableRole,
  TenantUserPageWire,
  TenantUserDetailWire,
  InvitationWire,
  InviteResultWire,
  RoleWire,
} from '../models/user-management.models';
import { environment } from '../../../../../environments/environment';

describe('UserManagementService', () => {
  let service: UserManagementService;
  let httpMock: HttpTestingController;

  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;
  const invitationsUrl = `${environment.apiBaseUrl}/tenant/users/invitations`;

  /**
   * D1 — the WIRE shape (`PagedResultOfUsersTenantUserListItemDto`). Note what the
   * old view-model fixture got wrong: role refs are keyed `roleId` (not `id`), the
   * employee link is `linkedEmployeeId` (not `employeeId`), and `status` is the
   * PascalCase C# enum name `Active` (not `'active'`).
   */
  const mockListWire: TenantUserPageWire = {
    items: [
      {
        userTenantId: 'ut-1',
        userId: 'u-1',
        displayName: 'Jane Doe',
        email: 'jane@acme.com',
        roles: [{ roleId: 'r-1', name: 'HR Officer' }],
        status: 'Active',
        lastLoginAt: '2026-06-01T10:00:00Z',
        linkedEmployeeId: 'e-9',
      },
    ],
    totalCount: 1,
    totalPages: 1,
    page: 1,
    pageSize: 20,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        UserManagementService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(UserManagementService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getUsers', () => {
    it('builds query params and GETs the user list', () => {
      let list: IUserListResponse | undefined;
      service
        .getUsers({
          page: 2,
          pageSize: 20,
          search: 'jane',
          status: 'active',
          roleId: 'r-1',
        })
        .subscribe((res) => (list = res));

      const req = httpMock.expectOne(
        (r) => r.url === usersUrl && r.method === 'GET'
      );
      expect(req.request.params.get('page')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('20');
      expect(req.request.params.get('search')).toBe('jane');
      expect(req.request.params.get('status')).toBe('active');
      expect(req.request.params.get('roleId')).toBe('r-1');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockListWire);

      expect(list?.totalCount).toBe(1);
      expect(list?.items[0].displayName).toBe('Jane Doe');
      // RENAMES asserted: roleId -> id, linkedEmployeeId -> employeeId,
      // and the PascalCase enum name -> the FE's normalized token.
      expect(list?.items[0].roles).toEqual([{ id: 'r-1', name: 'HR Officer' }]);
      expect(list?.items[0].employeeId).toBe('e-9');
      expect(list?.items[0].status).toBe('active');
    });

    it('maps the PascalCase Suspended status the FE union used to omit', () => {
      let list: IUserListResponse | undefined;
      service.getUsers({ page: 1, pageSize: 20 }).subscribe((r) => (list = r));

      httpMock
        .expectOne((r) => r.url === usersUrl && r.method === 'GET')
        .flush({ items: [{ userTenantId: 'ut-2', status: 'Suspended' }] });

      expect(list?.items[0].status).toBe('suspended');
      expect(list?.items[0].roles).toEqual([]);
      expect(list?.items[0].employeeId).toBeNull();
    });

    it('omits empty optional filters', () => {
      service.getUsers({ page: 1, pageSize: 20 }).subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === usersUrl && r.method === 'GET'
      );
      expect(req.request.params.has('search')).toBeFalse();
      expect(req.request.params.has('status')).toBeFalse();
      expect(req.request.params.has('roleId')).toBeFalse();
      req.flush(mockListWire);
    });
  });

  describe('getUserDetail', () => {
    it('projects the detail DTO, renaming userAgent -> device', () => {
      const detailWire: TenantUserDetailWire = {
        userTenantId: 'ut-1',
        userId: 'u-1',
        displayName: 'Jane Doe',
        email: 'jane@acme.com',
        status: 'Active',
        lastLoginAt: null,
        linkedEmployeeId: 'e-9',
        roles: [{ roleId: 'r-1', name: 'HR Officer' }],
        activeSessions: [
          {
            id: 's-1',
            userAgent: 'Chrome/140 on Linux',
            ipAddress: '10.0.0.4',
            lastActiveAt: '2026-06-01T10:00:00Z',
            issuedAt: '2026-06-01T09:00:00Z',
            expiresAt: '2026-06-08T09:00:00Z',
          },
        ],
        invitationHistory: [],
      };

      let detail: IUserDetail | undefined;
      service.getUserDetail('ut-1').subscribe((d) => (detail = d));

      // GAP-009: the detail route is /{userTenantId}/detail.
      const req = httpMock.expectOne(`${usersUrl}/ut-1/detail`);
      expect(req.request.method).toBe('GET');
      req.flush(detailWire);

      expect(detail?.userTenantId).toBe('ut-1');
      expect(detail?.status).toBe('active');
      expect(detail?.roles).toEqual([{ id: 'r-1', name: 'HR Officer' }]);
      // RENAME asserted: the wire has no `device`, only `userAgent`.
      expect(detail?.activeSessions).toEqual([
        {
          id: 's-1',
          device: 'Chrome/140 on Linux',
          ipAddress: '10.0.0.4',
          lastActiveAt: '2026-06-01T10:00:00Z',
        },
      ]);
    });

    it('leaves the sections that have NO wire source empty rather than inventing them', () => {
      let detail: IUserDetail | undefined;
      service.getUserDetail('ut-1').subscribe((d) => (detail = d));

      httpMock
        .expectOne(`${usersUrl}/ut-1/detail`)
        .flush({ userTenantId: 'ut-1', linkedEmployeeId: 'e-9' });

      // `UsersTenantUserDetailDto` has no audit field, and carries only the
      // employee's ID — never its name/title/department.
      expect(detail?.recentAudit).toBeUndefined();
      expect(detail?.linkedEmployee).toBeNull();
    });
  });

  describe('getAssignableRoles', () => {
    it('GETs assignable roles', () => {
      // The wire DTO is the full RolesRoleDto, not the 3-field option shape.
      const rolesWire: RoleWire[] = [
        {
          id: 'r-1',
          name: 'HR Officer',
          description: 'HR ops',
          permissions: ['Employee.View.All'],
          isBuiltIn: true,
          userCount: 5,
          createdAt: '2026-01-01T00:00:00Z',
        },
        { id: 'r-2', name: 'Auditor' },
      ];
      let roles: IAssignableRole[] | undefined;
      service.getAssignableRoles().subscribe((r) => (roles = r));
      // GAP-009: /users/assignable-roles has never existed; roles come from the roles endpoint.
      const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/roles`);
      expect(req.request.method).toBe('GET');
      req.flush(rolesWire);

      expect(roles).toEqual([
        { id: 'r-1', name: 'HR Officer', description: 'HR ops' },
        // A role with no description defaults to '' rather than undefined.
        { id: 'r-2', name: 'Auditor', description: '' },
      ]);
    });
  });

  describe('inviteUsers', () => {
    it('POSTs emails + roleIds and returns per-email results', () => {
      // Each POST answers with ONE UsersInviteResultDto { created, errors } —
      // never the per-address IInviteResult the old fixture flushed.
      const okWire: InviteResultWire = {
        created: [
          {
            id: 'inv-1',
            email: 'a@acme.com',
            status: 'Invited',
            invitedRoleIds: ['r-1'],
            invitedAt: '2026-06-01T00:00:00Z',
            expiresAt: '2026-06-04T00:00:00Z',
            isExpired: false,
          },
        ],
        errors: [],
      };
      const failWire: InviteResultWire = {
        created: [],
        errors: [{ email: 'b@acme.com', error: 'Already a member' }],
      };

      let res: IInviteResult[] | undefined;
      service
        .inviteUsers({ emails: ['a@acme.com', 'b@acme.com'], roleIds: ['r-1'] })
        .subscribe((r) => (res = r));

      // GAP-009: the API takes ONE { email, roleIds } per call, so a two-address invite is two requests.
      const reqs = httpMock.match(`${usersUrl}/invite`);
      expect(reqs.length).toBe(2);
      expect(reqs[0].request.method).toBe('POST');
      expect(reqs[0].request.body).toEqual({ email: 'a@acme.com', roleIds: ['r-1'] });
      expect(reqs[1].request.body).toEqual({ email: 'b@acme.com', roleIds: ['r-1'] });
      expect(reqs[0].request.withCredentials).toBeTrue();
      reqs[0].flush(okWire);
      reqs[1].flush(failWire);

      expect(res).toEqual([
        { email: 'a@acme.com', status: 'invited' },
        { email: 'b@acme.com', status: 'error', error: 'Already a member' },
      ]);
    });
  });

  describe('inviteFromCsv', () => {
    it('POSTs parsed CSV rows wrapped in { rows }', () => {
      const rows = [{ email: 'a@acme.com', roleNames: ['Employee'] }];
      let res: IInviteResult[] | undefined;
      service.inviteFromCsv(rows).subscribe((r) => (res = r));

      // GAP-009: the bulk endpoint is invite/bulk.
      const req = httpMock.expectOne(`${usersUrl}/invite/bulk`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ rows });
      // D1: ONE object, not an array — flushing an array here is what let the
      // component's `res.filter(…)` look safe when it was calling an array method
      // on a plain object.
      req.flush({
        created: [
          {
            id: 'inv-2',
            email: 'a@acme.com',
            status: 'Invited',
            invitedRoleIds: [],
            invitedAt: '2026-06-01T00:00:00Z',
            expiresAt: '2026-06-04T00:00:00Z',
            isExpired: false,
          },
        ],
        errors: [{ email: 'bad@acme.com', error: 'Unknown role' }],
      } satisfies InviteResultWire);

      expect(res).toEqual([
        { email: 'a@acme.com', status: 'invited' },
        { email: 'bad@acme.com', status: 'error', error: 'Unknown role' },
      ]);
    });
  });

  describe('getInvitations', () => {
    it('GETs pending invitations', () => {
      const invitationsWire: InvitationWire[] = [
        {
          id: 'inv-1',
          email: 'pending@acme.com',
          // The wire sends only role IDs, and the PascalCase status name.
          invitedRoleIds: ['r-1'],
          status: 'Invited',
          invitedAt: '2026-06-01T00:00:00Z',
          expiresAt: '2026-06-04T00:00:00Z',
          isExpired: false,
        },
      ];
      let inv: IInvitation[] | undefined;
      service.getInvitations().subscribe((r) => (inv = r));
      const req = httpMock.expectOne(invitationsUrl);
      expect(req.request.method).toBe('GET');
      req.flush(invitationsWire);

      // RENAME asserted: wire `Invited` -> view-model `pending`.
      expect(inv?.[0].status).toBe('pending');
      // NO WIRE SOURCE: the name is blank because the API sends ids only. The
      // mapper must not invent a label here.
      expect(inv?.[0].roles).toEqual([{ id: 'r-1', name: '' }]);
    });
  });

  describe('resendInvitation / revokeInvitation', () => {
    it('POSTs to the resend endpoint', () => {
      service.resendInvitation('inv-1').subscribe();
      const req = httpMock.expectOne(`${invitationsUrl}/inv-1/resend`);
      expect(req.request.method).toBe('POST');
      req.flush(null);
    });

    it('POSTs to the revoke endpoint', () => {
      service.revokeInvitation('inv-1').subscribe();
      const req = httpMock.expectOne(`${invitationsUrl}/inv-1/revoke`);
      expect(req.request.method).toBe('POST');
      req.flush(null);
    });
  });

  describe('editRoles', () => {
    it('PUTs the complete role-id set', () => {
      service
        .editRoles({ userTenantId: 'ut-1', roleIds: ['r-1', 'r-2'] })
        .subscribe();

      // GAP-009: the membership id is a PATH segment and the body carries only roleIds.
      const req = httpMock.expectOne(`${usersUrl}/ut-1/roles`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual({ roleIds: ['r-1', 'r-2'] });
      req.flush(null);
    });
  });

  describe('lifecycle actions', () => {
    it('POSTs deactivate with userTenantId', () => {
      service.deactivateUser('ut-1').subscribe();
      // GAP-009: the membership id is a PATH segment; there is no body.
      const req = httpMock.expectOne(`${usersUrl}/ut-1/deactivate`);
      expect(req.request.method).toBe('POST');
      req.flush(null);
    });

    it('POSTs force-password-reset with userTenantId', () => {
      service.forcePasswordReset('ut-1').subscribe();
      // GAP-009: the membership id is a PATH segment; there is no body.
      const req = httpMock.expectOne(`${usersUrl}/ut-1/force-password-reset`);
      expect(req.request.method).toBe('POST');
      req.flush(null);
    });

    it('POSTs end-sessions with userTenantId', () => {
      service.endAllSessions('ut-1').subscribe();
      // GAP-009: the membership id is a PATH segment; there is no body.
      const req = httpMock.expectOne(`${usersUrl}/ut-1/end-sessions`);
      expect(req.request.method).toBe('POST');
      req.flush(null);
    });
  });

  describe('parseCsv (static)', () => {
    it('parses a header row and comma-separated role names', () => {
      const csv =
        'email,role\n' +
        'a@acme.com,"Employee,Manager"\n' +
        'b@acme.com,HR Officer';
      const rows = UserManagementService.parseCsv(csv);
      expect(rows.length).toBe(2);
      expect(rows[0]).toEqual({
        email: 'a@acme.com',
        roleNames: ['Employee', 'Manager'],
      });
      expect(rows[1]).toEqual({
        email: 'b@acme.com',
        roleNames: ['HR Officer'],
      });
    });

    it('handles a CSV without a header row', () => {
      const rows = UserManagementService.parseCsv('a@acme.com,Employee');
      expect(rows.length).toBe(1);
      expect(rows[0].email).toBe('a@acme.com');
    });

    it('skips blank lines and rows with no email', () => {
      const rows = UserManagementService.parseCsv(
        'email,role\n\n,Employee\nc@acme.com,Employee\n'
      );
      expect(rows.length).toBe(1);
      expect(rows[0].email).toBe('c@acme.com');
    });

    it('returns an empty array for empty input', () => {
      expect(UserManagementService.parseCsv('')).toEqual([]);
    });
  });
});
