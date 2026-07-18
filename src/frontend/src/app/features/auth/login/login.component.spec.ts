import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, ParamMap } from '@angular/router';
import { provideRouter } from '@angular/router';
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
