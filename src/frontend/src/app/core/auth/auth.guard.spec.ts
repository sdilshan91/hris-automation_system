import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { AuthService } from './auth.service';
import { permissionGuard, roleGuard } from './auth.guard';

describe('Auth Guards', () => {
  let mockAuthService: jasmine.SpyObj<AuthService>;
  let mockRouter: jasmine.SpyObj<Router>;

  beforeEach(() => {
    mockAuthService = jasmine.createSpyObj('AuthService', [
      'isAuthenticated',
      'hasAnyPermission',
      'hasRole',
    ]);
    mockRouter = jasmine.createSpyObj('Router', ['createUrlTree']);
    mockRouter.createUrlTree.and.returnValue({} as UrlTree);

    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: mockAuthService },
        { provide: Router, useValue: mockRouter },
      ],
    });
  });

  describe('permissionGuard', () => {
    it('should return true when user has required permission', () => {
      mockAuthService.hasAnyPermission.and.returnValue(true);
      const guard = permissionGuard(['Admin.Roles.Manage']);

      TestBed.runInInjectionContext(() => {
        const result = guard({} as never, {} as never);
        expect(result).toBe(true);
      });
    });

    it('should redirect to /forbidden when user lacks permission', () => {
      mockAuthService.hasAnyPermission.and.returnValue(false);
      const guard = permissionGuard(['Admin.Roles.Manage']);

      TestBed.runInInjectionContext(() => {
        const result = guard({} as never, {} as never);
        expect(result).not.toBe(true);
        expect(mockRouter.createUrlTree).toHaveBeenCalledWith(['/forbidden']);
      });
    });
  });

  describe('roleGuard', () => {
    it('should return true when user has required role', () => {
      mockAuthService.hasRole.and.callFake(
        (role: string) => role === 'Tenant Admin'
      );
      const guard = roleGuard(['Tenant Admin']);

      TestBed.runInInjectionContext(() => {
        const result = guard({} as never, {} as never);
        expect(result).toBe(true);
      });
    });

    it('should redirect to /forbidden when user lacks role', () => {
      mockAuthService.hasRole.and.returnValue(false);
      const guard = roleGuard(['Tenant Admin']);

      TestBed.runInInjectionContext(() => {
        const result = guard({} as never, {} as never);
        expect(result).not.toBe(true);
        expect(mockRouter.createUrlTree).toHaveBeenCalledWith(['/forbidden']);
      });
    });
  });
});
