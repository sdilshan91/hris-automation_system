import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, ParamMap } from '@angular/router';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { signal } from '@angular/core';

import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/auth/auth.service';
import { TenantService } from '../../../core/tenant/tenant.service';

/**
 * Focused on the SSO error-code -> message mapping in ngOnInit (DF-27a). The
 * template is intentionally NOT rendered (no detectChanges); we drive ngOnInit
 * directly so the mapping can be asserted without wiring the full auth template.
 */
describe('LoginComponent (SSO error mapping)', () => {
  let component: LoginComponent;
  let fixture: ComponentFixture<LoginComponent>;

  function configure(ssoError: string | null): void {
    const queryParamMap: ParamMap = convertToParamMap(
      ssoError ? { sso_error: ssoError } : {}
    );

    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            login: () => ({ subscribe: () => undefined }),
            loginEmail: signal(''),
            mfaRequiresEnrollment: () => false,
            mfaChallenge: signal(false),
            isLoading: signal(false),
          },
        },
        {
          provide: TenantService,
          useValue: { subdomain: () => 'acme' },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap } },
        },
      ],
    });

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    component.ngOnInit();
  }

  afterEach(() => {
    TestBed.resetTestingModule();
  });

  it('maps tenant_required to a distinct "no organization" message (DF-27a)', () => {
    configure('tenant_required');
    expect(component.errorMessage()).toContain('No organization was specified');
  });

  it('maps not_configured to a different message than tenant_required (DF-27a)', () => {
    configure('not_configured');
    expect(component.errorMessage()).toContain("isn't set up for this workspace");
    expect(component.errorMessage()).not.toContain('No organization was specified');
  });

  it('leaves the error empty when there is no sso_error param', () => {
    configure(null);
    expect(component.errorMessage()).toBe('');
  });
});

/**
 * US-AUTH-016 AC-1: sso_only login rendering — Microsoft-primary + a discreet
 * "Administrator sign-in" (break-glass) link, with the password form hidden until
 * the link is used.
 */
describe('LoginComponent (sso_only rendering)', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;

  function configure(enforcementMode: 'optional' | 'sso_only'): void {
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        provideNoopAnimations(),
        {
          provide: AuthService,
          useValue: {
            login: () => ({ subscribe: () => undefined }),
            loginEmail: signal(''),
            mfaRequiresEnrollment: () => false,
            mfaChallenge: signal(false),
            isLoading: signal(false),
          },
        },
        {
          provide: TenantService,
          useValue: {
            subdomain: () => 'acme',
            tenantContext: () => ({ subdomain: 'acme', enforcementMode }),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}) } },
        },
      ],
    });

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  afterEach(() => TestBed.resetTestingModule());

  it('renders the Microsoft-primary button and break-glass link under sso_only, password form hidden', () => {
    configure('sso_only');
    expect(component.ssoOnly()).toBeTrue();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="sso-only-microsoft"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="break-glass-link"]')).toBeTruthy();
    // Password form is hidden until the break-glass link is used.
    expect(el.querySelector('[data-testid="login-password"]')).toBeNull();
  });

  it('reveals the break-glass password form when the administrator link is clicked', () => {
    configure('sso_only');
    component.revealBreakGlassForm();
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="login-password"]')).toBeTruthy();
    // Once revealed, the link is gone.
    expect(el.querySelector('[data-testid="break-glass-link"]')).toBeNull();
  });

  it('renders the normal password form (no break-glass link) under optional enforcement', () => {
    configure('optional');
    expect(component.ssoOnly()).toBeFalse();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="login-password"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="break-glass-link"]')).toBeNull();
  });
});
