import { TestBed, ComponentFixture, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { UserListComponent } from './user-list.component';
import {
  IUserListResponse,
  IAssignableRole,
} from '../../models/user-management.models';
import { environment } from '../../../../../../environments/environment';

describe('UserListComponent', () => {
  let component: UserListComponent;
  let fixture: ComponentFixture<UserListComponent>;
  let httpMock: HttpTestingController;

  const usersUrl = `${environment.apiBaseUrl}/tenant/users`;

  const mockRoles: IAssignableRole[] = [
    { id: 'r-1', name: 'HR Officer', description: 'HR ops' },
    { id: 'r-2', name: 'Manager', description: 'Team lead' },
  ];

  const page1: IUserListResponse = {
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
      {
        userTenantId: 'ut-2',
        userId: 'u-2',
        displayName: 'Bob Smith',
        email: 'bob@acme.com',
        roles: [],
        status: 'invited',
        lastLoginAt: null,
      },
    ],
    total: 45,
    page: 1,
    pageSize: 20,
  };

  /** Flush the initial assignable-roles + users requests that fire on init. */
  function flushInit(): void {
    fixture.detectChanges();
    httpMock.expectOne(`${usersUrl}/assignable-roles`).flush(mockRoles);
    const usersReq = httpMock.expectOne(
      (r) => r.url === usersUrl && r.method === 'GET'
    );
    usersReq.flush(page1);
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UserListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        provideTranslateService(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(UserListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create and load users + roles', () => {
    flushInit();
    expect(component).toBeTruthy();
    expect(component.users().length).toBe(2);
    expect(component.roles().length).toBe(2);
    expect(component.total()).toBe(45);
  });

  it('renders a row per user', () => {
    flushInit();
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll(
      '[data-testid="user-row"]'
    );
    expect(rows.length).toBe(2);
  });

  it('shows the empty state when no users match', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${usersUrl}/assignable-roles`).flush(mockRoles);
    httpMock
      .expectOne((r) => r.url === usersUrl && r.method === 'GET')
      .flush({ items: [], total: 0, page: 1, pageSize: 20 });
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="users-empty"]')
    ).toBeTruthy();
  });

  it('debounced search updates the query and resets to page 1', fakeAsync(() => {
    flushInit();
    component.page.set(3);

    component.onSearchInput('jane');
    tick(300);

    const req = httpMock.expectOne(
      (r) => r.url === usersUrl && r.method === 'GET'
    );
    expect(req.request.params.get('search')).toBe('jane');
    expect(req.request.params.get('page')).toBe('1');
    expect(component.page()).toBe(1);
    req.flush(page1);
  }));

  it('status filter change re-queries with status param', () => {
    flushInit();
    component.onStatusChange('invited');

    const req = httpMock.expectOne(
      (r) => r.url === usersUrl && r.method === 'GET'
    );
    expect(req.request.params.get('status')).toBe('invited');
    req.flush(page1);
  });

  it('role filter change re-queries with roleId param', () => {
    flushInit();
    component.onRoleChange('r-2');

    const req = httpMock.expectOne(
      (r) => r.url === usersUrl && r.method === 'GET'
    );
    expect(req.request.params.get('roleId')).toBe('r-2');
    req.flush(page1);
  });

  it('computes total pages and pagination range', () => {
    flushInit();
    // 45 total / 20 per page = 3 pages
    expect(component.totalPages()).toBe(3);
    expect(component.rangeStart()).toBe(1);
    expect(component.rangeEnd()).toBe(20);
  });

  it('next page advances the page and re-queries', () => {
    flushInit();
    component.nextPage();
    const req = httpMock.expectOne(
      (r) => r.url === usersUrl && r.method === 'GET'
    );
    expect(req.request.params.get('page')).toBe('2');
    expect(component.page()).toBe(2);
    req.flush({ ...page1, page: 2 });
  });

  it('does not advance past the last page', () => {
    flushInit();
    component.page.set(3);
    component.nextPage();
    // No new request expected.
    httpMock.verify();
    expect(component.page()).toBe(3);
  });

  it('loads invitations when switching to the invitations tab', () => {
    flushInit();
    component.selectTab('invitations');
    const req = httpMock.expectOne(
      `${environment.apiBaseUrl}/tenant/users/invitations`
    );
    expect(req.request.method).toBe('GET');
    req.flush([]);
    expect(component.activeTab()).toBe('invitations');
  });

  it('deactivate confirm posts to the deactivate endpoint', () => {
    flushInit();
    const user = component.users()[0];
    component.askDeactivate(user);
    expect(component.confirmAction()?.kind).toBe('deactivate');

    component.confirm();
    const req = httpMock.expectOne(`${usersUrl}/deactivate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ userTenantId: 'ut-1' });
    req.flush(null);

    // After deactivate it reloads the list.
    httpMock
      .expectOne((r) => r.url === usersUrl && r.method === 'GET')
      .flush(page1);
    expect(component.confirmAction()).toBeNull();
  });

  it('force-reset confirm posts to the force-password-reset endpoint', () => {
    flushInit();
    component.askForceReset(component.users()[0]);
    component.confirm();
    const req = httpMock.expectOne(`${usersUrl}/force-password-reset`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
    expect(component.confirmAction()).toBeNull();
  });

  it('end-sessions confirm posts to the end-sessions endpoint', () => {
    flushInit();
    component.askEndSessions(component.users()[0]);
    component.confirm();
    const req = httpMock.expectOne(`${usersUrl}/end-sessions`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
    expect(component.confirmAction()).toBeNull();
  });

  it('shows an error state when the list fails to load', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${usersUrl}/assignable-roles`).flush(mockRoles);
    httpMock
      .expectOne((r) => r.url === usersUrl && r.method === 'GET')
      .flush('boom', { status: 500, statusText: 'Server Error' });
    expect(component.error()).toBeTruthy();
    expect(component.loading()).toBeFalse();
  });
});
