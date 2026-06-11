import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';

import { AdminUserLockoutComponent } from './admin-user-lockout.component';
import { AuthService } from '../../../../core/auth/auth.service';
import { ITenantUser, IMessageResponse } from '../../../../core/auth/auth.models';

describe('AdminUserLockoutComponent', () => {
  let component: AdminUserLockoutComponent;
  let fixture: ComponentFixture<AdminUserLockoutComponent>;
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  /** Helper: create a future ISO timestamp (locked) */
  const futureTimestamp = (): string =>
    new Date(Date.now() + 600_000).toISOString();

  /** Helper: create a past ISO timestamp (expired lock) */
  const pastTimestamp = (): string =>
    new Date(Date.now() - 600_000).toISOString();

  const mockUsers: ITenantUser[] = [
    {
      userId: 'user-1',
      email: 'alice@acme.com',
      displayName: 'Alice Smith',
      avatarUrl: undefined,
      roles: ['Employee'],
      isActive: true,
      lockedUntil: futureTimestamp(),
      failedLoginCount: 5,
      lastLoginAt: '2026-06-01T10:00:00Z',
    },
    {
      userId: 'user-2',
      email: 'bob@acme.com',
      displayName: 'Bob Jones',
      avatarUrl: 'https://example.com/bob.jpg',
      roles: ['HR Manager'],
      isActive: true,
      lockedUntil: null,
      failedLoginCount: 0,
      lastLoginAt: '2026-06-02T08:00:00Z',
    },
    {
      userId: 'user-3',
      email: 'carol@acme.com',
      displayName: 'Carol Davis',
      avatarUrl: undefined,
      roles: ['Employee', 'Manager'],
      isActive: true,
      lockedUntil: pastTimestamp(), // lock expired
      failedLoginCount: 3,
      lastLoginAt: null,
    },
  ];

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', [
      'getTenantUsers',
      'unlockUser',
    ]);
    authServiceSpy.getTenantUsers.and.returnValue(of(mockUsers));
    authServiceSpy.unlockUser.and.returnValue(
      of({ message: 'Account unlocked' } as IMessageResponse)
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);

    await TestBed.configureTestingModule({
      imports: [AdminUserLockoutComponent],
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

    fixture = TestBed.createComponent(AdminUserLockoutComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should load users on init', () => {
    fixture.detectChanges();
    expect(authServiceSpy.getTenantUsers).toHaveBeenCalled();
    expect(component.users().length).toBe(3);
    expect(component.isLoading()).toBeFalse();
  });

  it('should show error when loading fails', () => {
    authServiceSpy.getTenantUsers.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Server error' },
      }))
    );
    fixture.detectChanges();
    expect(component.loadError()).toBe('Server error');
    expect(component.isLoading()).toBeFalse();
  });

  // ─── Lockout detection ────────────────────────────────────

  it('should detect locked users (future lockedUntil)', () => {
    fixture.detectChanges();
    const alice = component.users()[0];
    expect(component.isLocked(alice)).toBeTrue();
  });

  it('should not consider users with expired lockout as locked', () => {
    fixture.detectChanges();
    const carol = component.users()[2];
    expect(component.isLocked(carol)).toBeFalse();
  });

  it('should not consider users with null lockedUntil as locked', () => {
    fixture.detectChanges();
    const bob = component.users()[1];
    expect(component.isLocked(bob)).toBeFalse();
  });

  it('should compute locked count correctly', () => {
    fixture.detectChanges();
    // Only Alice is locked (future timestamp); Carol's lock expired
    expect(component.lockedCount()).toBe(1);
  });

  // ─── Unlock ───────────────────────────────────────────────

  it('should unlock a locked user (AC-5)', () => {
    fixture.detectChanges();
    const alice = component.users()[0];
    component.unlockUser(alice);

    expect(authServiceSpy.unlockUser).toHaveBeenCalledWith('user-1');
    // After unlock, the user should no longer be locked
    const updatedAlice = component.users().find((u) => u.userId === 'user-1')!;
    expect(updatedAlice.lockedUntil).toBeNull();
    expect(updatedAlice.failedLoginCount).toBe(0);
    expect(component.unlockingId()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalledWith(
      "Alice Smith's account has been unlocked."
    );
  });

  it('should handle unlock errors', () => {
    authServiceSpy.unlockUser.and.returnValue(
      throwError(() => ({
        status: 500,
        error: { message: 'Unlock failed' },
      }))
    );
    fixture.detectChanges();
    const alice = component.users()[0];
    component.unlockUser(alice);

    expect(component.unlockingId()).toBeNull();
    expect(toastrSpy.error).toHaveBeenCalledWith('Unlock failed');
  });

  it('should not unlock when another unlock is in progress', () => {
    fixture.detectChanges();
    component.unlockingId.set('user-other');
    const alice = component.users()[0];

    component.unlockUser(alice);
    expect(authServiceSpy.unlockUser).not.toHaveBeenCalled();
  });

  // ─── Utility methods ──────────────────────────────────────

  it('should compute initials from display name', () => {
    expect(component.getInitials('Alice Smith')).toBe('AS');
    expect(component.getInitials('Bob')).toBe('B');
    expect(component.getInitials('Carol Ann Davis')).toBe('CD');
    expect(component.getInitials('')).toBe('?');
  });
});
