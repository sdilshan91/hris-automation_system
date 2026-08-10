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
} from '../models/user-management.models';
import { environment } from '../../../../../environments/environment';

describe('UserManagementService', () => {
  let service: UserManagementService;
  let httpMock: HttpTestingController;

  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;
  const invitationsUrl = `${environment.apiBaseUrl}/tenant/users/invitations`;

  const mockList: IUserListResponse = {
    items: [
      {
        userTenantId: 'ut-1',
        userId: 'u-1',
        displayName: 'Jane Doe',
        email: 'jane@acme.com',
        roles: [{ id: 'r-1', name: 'HR Officer' }],
        status: 'active',
        lastLoginAt: '2026-06-01T10:00:00Z',
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
      service
        .getUsers({
          page: 2,
          pageSize: 20,
          search: 'jane',
          status: 'active',
          roleId: 'r-1',
        })
        .subscribe((res) => {
          expect(res.totalCount).toBe(1);
          expect(res.items[0].displayName).toBe('Jane Doe');
        });

      const req = httpMock.expectOne(
        (r) => r.url === usersUrl && r.method === 'GET'
      );
      expect(req.request.params.get('page')).toBe('2');
      expect(req.request.params.get('pageSize')).toBe('20');
      expect(req.request.params.get('search')).toBe('jane');
      expect(req.request.params.get('status')).toBe('active');
      expect(req.request.params.get('roleId')).toBe('r-1');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockList);
    });

    it('omits empty optional filters', () => {
      service.getUsers({ page: 1, pageSize: 20 }).subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === usersUrl && r.method === 'GET'
      );
      expect(req.request.params.has('search')).toBeFalse();
      expect(req.request.params.has('status')).toBeFalse();
      expect(req.request.params.has('roleId')).toBeFalse();
      req.flush(mockList);
    });
  });

  describe('getUserDetail', () => {
    it('GETs a single user detail', () => {
      const detail: IUserDetail = {
        userTenantId: 'ut-1',
        userId: 'u-1',
        displayName: 'Jane Doe',
        email: 'jane@acme.com',
        status: 'active',
        lastLoginAt: null,
        roles: [],
      };
      service.getUserDetail('ut-1').subscribe((d) => {
        expect(d.userTenantId).toBe('ut-1');
      });
      // GAP-009: the detail route is /{userTenantId}/detail.
      const req = httpMock.expectOne(`${usersUrl}/ut-1/detail`);
      expect(req.request.method).toBe('GET');
      req.flush(detail);
    });
  });

  describe('getAssignableRoles', () => {
    it('GETs assignable roles', () => {
      const roles: IAssignableRole[] = [
        { id: 'r-1', name: 'HR Officer', description: 'HR ops' },
      ];
      service.getAssignableRoles().subscribe((r) => {
        expect(r.length).toBe(1);
      });
      // GAP-009: /users/assignable-roles has never existed; roles come from the roles endpoint.
      const req = httpMock.expectOne(`${environment.apiBaseUrl}/tenant/roles`);
      expect(req.request.method).toBe('GET');
      req.flush(roles);
    });
  });

  describe('inviteUsers', () => {
    it('POSTs emails + roleIds and returns per-email results', () => {
      const results: IInviteResult[] = [
        { email: 'a@acme.com', status: 'invited' },
        { email: 'b@acme.com', status: 'error', error: 'Already a member' },
      ];
      service
        .inviteUsers({ emails: ['a@acme.com', 'b@acme.com'], roleIds: ['r-1'] })
        .subscribe((res) => {
          expect(res.length).toBe(2);
          expect(res[1].status).toBe('error');
        });

      // GAP-009: the API takes ONE { email, roleIds } per call, so a two-address invite is two requests.
      const reqs = httpMock.match(`${usersUrl}/invite`);
      expect(reqs.length).toBe(2);
      expect(reqs[0].request.method).toBe('POST');
      expect(reqs[0].request.body).toEqual({ email: 'a@acme.com', roleIds: ['r-1'] });
      expect(reqs[1].request.body).toEqual({ email: 'b@acme.com', roleIds: ['r-1'] });
      expect(reqs[0].request.withCredentials).toBeTrue();
      reqs[0].flush(results[0]);
      reqs[1].flush(results[1]);
    });
  });

  describe('inviteFromCsv', () => {
    it('POSTs parsed CSV rows wrapped in { rows }', () => {
      const rows = [{ email: 'a@acme.com', roleNames: ['Employee'] }];
      service.inviteFromCsv(rows).subscribe();

      // GAP-009: the bulk endpoint is invite/bulk.
      const req = httpMock.expectOne(`${usersUrl}/invite/bulk`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ rows });
      req.flush([{ email: 'a@acme.com', status: 'invited' }]);
    });
  });

  describe('getInvitations', () => {
    it('GETs pending invitations', () => {
      const invitations: IInvitation[] = [
        {
          id: 'inv-1',
          email: 'pending@acme.com',
          roles: [{ id: 'r-1', name: 'Employee' }],
          invitedAt: '2026-06-01T00:00:00Z',
          expiresAt: '2026-06-04T00:00:00Z',
          status: 'pending',
        },
      ];
      service.getInvitations().subscribe((inv) => {
        expect(inv.length).toBe(1);
      });
      const req = httpMock.expectOne(invitationsUrl);
      expect(req.request.method).toBe('GET');
      req.flush(invitations);
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
