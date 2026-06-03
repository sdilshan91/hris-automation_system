import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { signal } from '@angular/core';
import { RoleListComponent } from './role-list.component';
import { AuthService } from '../../../../../core/auth/auth.service';
import { IRole } from '../../models/role.models';
import { environment } from '../../../../../../environments/environment';

describe('RoleListComponent', () => {
  let component: RoleListComponent;
  let fixture: ComponentFixture<RoleListComponent>;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/roles`;

  const mockRoles: IRole[] = [
    {
      roleId: 'role-1',
      tenantId: null,
      name: 'Tenant Admin',
      description: 'Full admin access',
      isBuiltIn: true,
      permissions: ['Admin.View', 'Admin.Roles.Manage'],
      userCount: 2,
      createdAt: '2026-01-01T00:00:00Z',
    },
    {
      roleId: 'role-2',
      tenantId: 'tenant-1',
      name: 'Custom HR',
      description: 'Custom HR role',
      isBuiltIn: false,
      permissions: ['Employee.View.All'],
      userCount: 3,
      createdAt: '2026-02-01T00:00:00Z',
    },
  ];

  beforeEach(async () => {
    const mockAuthService = {
      permissions: signal(['Admin.View', 'Admin.Roles.Manage']),
      hasPermission: (p: string) => ['Admin.View', 'Admin.Roles.Manage'].includes(p),
      hasAnyPermission: (perms: string[]) =>
        perms.some((p) => ['Admin.View', 'Admin.Roles.Manage'].includes(p)),
      hasRole: () => true,
      isAuthenticated: signal(true),
      currentUser: signal({ displayName: 'Test', email: 'test@test.com' }),
    };

    await TestBed.configureTestingModule({
      imports: [RoleListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: mockAuthService },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RoleListComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should create', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(baseUrl);
    req.flush(mockRoles);
    expect(component).toBeTruthy();
  });

  it('should load and categorize roles', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(baseUrl);
    req.flush(mockRoles);

    expect(component.builtInRoles().length).toBe(1);
    expect(component.customRoles().length).toBe(1);
    expect(component.builtInRoles()[0].name).toBe('Tenant Admin');
    expect(component.customRoles()[0].name).toBe('Custom HR');
  });

  it('should show loading state initially', () => {
    expect(component.isLoading()).toBeTrue();
    fixture.detectChanges();
    const req = httpMock.expectOne(baseUrl);
    req.flush(mockRoles);
    expect(component.isLoading()).toBeFalse();
  });

  it('should handle API errors gracefully', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(baseUrl);
    req.flush('error', { status: 500, statusText: 'Server Error' });

    expect(component.errorMessage()).toBeTruthy();
    expect(component.isLoading()).toBeFalse();
  });
});
