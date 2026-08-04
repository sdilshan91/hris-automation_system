import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { AcceptInviteComponent } from './accept-invite.component';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * BUG-294: the invitation-redemption screen. Before the fix there was no route and no component behind the
 * emailed link at all, so an invited user could never set a password or sign in.
 */
describe('AcceptInviteComponent', () => {
  let fixture: ComponentFixture<AcceptInviteComponent>;
  let component: AcceptInviteComponent;
  let auth: jasmine.SpyObj<AuthService>;
  let router: Router;

  const VALID_PASSWORD = 'BrandNewPassw0rd!';

  function setup(queryParams: Record<string, string>): void {
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['acceptInvitation']);
    auth.acceptInvitation.and.returnValue(of({ message: 'ok' }));

    // A real Router, not a spy: the template uses routerLink, which needs createUrlTree. Stubbing only
    // navigate() made every arm blow up in change detection rather than exercising the component.
    TestBed.configureTestingModule({
      imports: [AcceptInviteComponent],
      providers: [
        provideNoopAnimations(),
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: ActivatedRoute, useValue: { queryParams: of(queryParams) } },
      ],
    });

    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);

    fixture = TestBed.createComponent(AcceptInviteComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  function fillForm(password: string, confirm = password): void {
    component.form.setValue({ newPassword: password, confirmPassword: confirm });
    fixture.detectChanges();
  }

  describe('token handling', () => {
    it('accepts a link carrying only a token (BUG-294/BUG-295)', () => {
      // The emitted link is `?token=…` and nothing else. Requiring a second param here is exactly what made
      // the sibling reset screen unusable, so this pins that the token alone is enough.
      setup({ token: 'the-real-token' });

      expect(component.invalidToken()).toBeFalse();
    });

    it('shows the terminal state when the link carries no token', () => {
      setup({});

      expect(component.invalidToken()).toBeTrue();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Invitation unavailable');
    });
  });

  describe('submission', () => {
    it('posts only the token and the chosen password', () => {
      setup({ token: 'the-real-token' });
      fillForm(VALID_PASSWORD);

      component.onSubmit();

      expect(auth.acceptInvitation).toHaveBeenCalledWith({
        token: 'the-real-token',
        newPassword: VALID_PASSWORD,
      });
    });

    it('does not submit a form that fails the password rules', () => {
      setup({ token: 'the-real-token' });
      fillForm('short');

      component.onSubmit();

      expect(auth.acceptInvitation).not.toHaveBeenCalled();
    });

    it('does not submit when the confirmation does not match', () => {
      setup({ token: 'the-real-token' });
      fillForm(VALID_PASSWORD, 'DifferentPassw0rd!');

      component.onSubmit();

      expect(auth.acceptInvitation).not.toHaveBeenCalled();
    });

    it('shows the success state and heads to login', () => {
      setup({ token: 'the-real-token' });
      fillForm(VALID_PASSWORD);

      component.onSubmit();
      fixture.detectChanges();

      expect(component.success()).toBeTrue();
      expect(component.isLoading()).toBeFalse();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain("You're all set");
    });
  });

  describe('error handling', () => {
    it('switches to the terminal state when the invitation is spent', () => {
      // A dead invitation cannot be retried, so offering the form again would be a trap.
      setup({ token: 'the-real-token' });
      auth.acceptInvitation.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { message: 'This invitation link is invalid, has expired, or has already been used.' },
            }),
        ),
      );
      fillForm(VALID_PASSWORD);

      component.onSubmit();
      fixture.detectChanges();

      expect(component.invalidToken()).toBeTrue();
      expect(component.isLoading()).toBeFalse();
    });

    it('keeps the form usable when it is the PASSWORD the server rejected', () => {
      // A tenant password-policy rejection leaves the invitation valid — the user must be able to try a
      // stronger password rather than being told their link is dead.
      setup({ token: 'the-real-token' });
      auth.acceptInvitation.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: { message: 'Password must be at least 16 characters.' },
            }),
        ),
      );
      fillForm(VALID_PASSWORD);

      component.onSubmit();
      fixture.detectChanges();

      expect(component.invalidToken())
        .withContext('the link is still good — only the password was refused')
        .toBeFalse();
      expect(component.errorMessage()).toContain('16 characters');
    });

    it('surfaces a generic message on an unexpected failure', () => {
      setup({ token: 'the-real-token' });
      auth.acceptInvitation.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500 })),
      );
      fillForm(VALID_PASSWORD);

      component.onSubmit();
      fixture.detectChanges();

      expect(component.errorMessage()).toContain('unexpected error');
      expect(component.invalidToken()).toBeFalse();
    });
  });
});
