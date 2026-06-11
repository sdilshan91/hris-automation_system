import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { AdminUserSessionsComponent } from './admin-user-sessions.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { ISession, IMessageResponse } from '../../../../core/auth/auth.models';

describe('AdminUserSessionsComponent', () => {
  let component: AdminUserSessionsComponent;
  let fixture: ComponentFixture<AdminUserSessionsComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const mockSessions: ISession[] = [
    {
      sessionId: 'session-1',
      device: 'Desktop',
      browser: 'Chrome',
      os: 'Windows 11',
      ipAddress: '192.168.1.1',
      issuedAt: new Date().toISOString(),
      lastActiveAt: new Date().toISOString(),
      isCurrent: true,
    },
    {
      sessionId: 'session-2',
      device: 'Mobile',
      browser: 'Safari',
      os: 'iOS 17',
      ipAddress: '10.0.0.2',
      issuedAt: new Date(Date.now() - 3600000).toISOString(),
      lastActiveAt: new Date(Date.now() - 600000).toISOString(),
      isCurrent: false,
    },
    {
      sessionId: 'session-3',
      device: 'Tablet',
      browser: 'Firefox',
      os: 'macOS',
      ipAddress: '10.0.0.3',
      issuedAt: new Date(Date.now() - 7200000).toISOString(),
      lastActiveAt: new Date(Date.now() - 3600000).toISOString(),
      isCurrent: false,
    },
  ];

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'getUserSessions',
      'revokeUserSession',
    ]);
    authServiceSpy.getUserSessions.and.returnValue(of(mockSessions));
    authServiceSpy.revokeUserSession.and.returnValue(
      of({ message: 'Session revoked' } as IMessageResponse)
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [AdminUserSessionsComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: authServiceSpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AdminUserSessionsComponent);
    component = fixture.componentInstance;

    // Set required input
    fixture.componentRef.setInput('userId', 'user-abc');
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load sessions on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.getUserSessions).toHaveBeenCalledWith('user-abc');
    expect(component.sessions().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error when loading fails', () => {
    authServiceSpy.getUserSessions.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Server error' },
      }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should revoke a specific session', () => {
    fixture.detectChanges();
    component.revokeSession(mockSessions[1]);

    expect(authServiceSpy.revokeUserSession).toHaveBeenCalledWith(
      'user-abc',
      'session-2'
    );
    expect(component.sessions().length).toBe(2);
    expect(component.revokingId()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalledWith(
      'Session revoked successfully.'
    );
  });

  it('should handle revoke errors', () => {
    authServiceSpy.revokeUserSession.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Revoke failed' },
      }))
    );
    fixture.detectChanges();
    component.revokeSession(mockSessions[1]);

    expect(component.revokingId()).toBeNull();
    expect(toastrSpy.error).toHaveBeenCalledWith('Revoke failed');
  });

  it('should revoke all sessions', () => {
    fixture.detectChanges();
    component.revokeAllSessions();

    expect(authServiceSpy.revokeUserSession).toHaveBeenCalledWith('user-abc');
    expect(component.sessions().length).toBe(0);
    expect(component.revokingAll()).toBeFalse();
    expect(toastrSpy.success).toHaveBeenCalledWith(
      'All sessions revoked successfully.'
    );
  });

  it('should handle revoke-all errors', () => {
    authServiceSpy.revokeUserSession.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Bulk revoke failed' },
      }))
    );
    fixture.detectChanges();
    component.revokeAllSessions();

    expect(component.revokingAll()).toBeFalse();
    expect(toastrSpy.error).toHaveBeenCalledWith('Bulk revoke failed');
  });

  it('should not revoke when another revoke is in progress', () => {
    fixture.detectChanges();
    component.revokingId.set('session-other');

    component.revokeSession(mockSessions[1]);
    expect(authServiceSpy.revokeUserSession).not.toHaveBeenCalled();
  });

  it('should not revoke all when already revoking all', () => {
    fixture.detectChanges();
    component.revokingAll.set(true);

    component.revokeAllSessions();
    // Should not be called a second time
    expect(authServiceSpy.revokeUserSession).not.toHaveBeenCalled();
  });

  it('should identify desktop sessions', () => {
    const winSession: ISession = { ...mockSessions[0], os: 'Windows 11' };
    const macSession: ISession = { ...mockSessions[0], os: 'macOS Ventura' };
    const mobileSession: ISession = { ...mockSessions[0], os: 'Android 14' };

    expect(component.isDesktop(winSession)).toBeTrue();
    expect(component.isDesktop(macSession)).toBeTrue();
    expect(component.isDesktop(mobileSession)).toBeFalse();
  });

  it('should format dates as relative times', () => {
    const now = new Date().toISOString();
    expect(component.formatDate(now)).toBe('just now');
    expect(component.formatDate('')).toBe('Unknown');
  });
});
