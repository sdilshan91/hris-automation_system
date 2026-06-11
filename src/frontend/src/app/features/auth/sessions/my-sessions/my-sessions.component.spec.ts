import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { MySessionsComponent } from './my-sessions.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { ISession, IMessageResponse } from '../../../../core/auth/auth.models';

describe('MySessionsComponent', () => {
  let component: MySessionsComponent;
  let fixture: ComponentFixture<MySessionsComponent>;
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
  ];

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'getMySessions',
      'revokeSession',
    ]);
    authServiceSpy.getMySessions.and.returnValue(of(mockSessions));
    authServiceSpy.revokeSession.and.returnValue(
      of({ message: 'Session revoked' } as IMessageResponse)
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [MySessionsComponent],
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

    fixture = TestBed.createComponent(MySessionsComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load sessions on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.getMySessions).toHaveBeenCalled();
    expect(component.sessions().length).toBe(2);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error when loading fails', () => {
    authServiceSpy.getMySessions.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Server error' },
      }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Server error');
    expect(component.isLoading()).toBeFalse();
  });

  it('should show default error message when no error body', () => {
    authServiceSpy.getMySessions.and.returnValue(
      throwError(() => ({ status: 500, error: {} }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Failed to load active sessions.');
  });

  it('should revoke a non-current session', () => {
    fixture.detectChanges();
    const nonCurrentSession = mockSessions[1];

    component.revokeSession(nonCurrentSession);

    expect(authServiceSpy.revokeSession).toHaveBeenCalledWith('session-2');
    expect(component.sessions().length).toBe(1);
    expect(component.revokingId()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalledWith(
      'Session revoked successfully.'
    );
  });

  it('should not revoke the current session (BR-4)', () => {
    fixture.detectChanges();
    const currentSession = mockSessions[0];

    component.revokeSession(currentSession);

    expect(authServiceSpy.revokeSession).not.toHaveBeenCalled();
  });

  it('should handle revoke errors', () => {
    authServiceSpy.revokeSession.and.returnValue(
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

  it('should identify desktop sessions correctly', () => {
    const desktopSession: ISession = {
      ...mockSessions[0],
      os: 'Windows 11',
    };
    const mobileSession: ISession = {
      ...mockSessions[1],
      os: 'iOS 17',
    };

    expect(component.isDesktop(desktopSession)).toBeTrue();
    expect(component.isDesktop(mobileSession)).toBeFalse();
  });

  it('should format dates as relative times', () => {
    const now = new Date().toISOString();
    expect(component.formatDate(now)).toBe('just now');
    expect(component.formatDate('')).toBe('Unknown');

    const tenMinAgo = new Date(Date.now() - 10 * 60000).toISOString();
    expect(component.formatDate(tenMinAgo)).toBe('10m ago');

    const twoHoursAgo = new Date(Date.now() - 2 * 3600000).toISOString();
    expect(component.formatDate(twoHoursAgo)).toBe('2h ago');

    const twoDaysAgo = new Date(Date.now() - 2 * 86400000).toISOString();
    expect(component.formatDate(twoDaysAgo)).toBe('2d ago');
  });

  it('should not revoke if another revoke is in progress', () => {
    fixture.detectChanges();
    component.revokingId.set('session-other');

    component.revokeSession(mockSessions[1]);

    expect(authServiceSpy.revokeSession).not.toHaveBeenCalled();
  });
});
