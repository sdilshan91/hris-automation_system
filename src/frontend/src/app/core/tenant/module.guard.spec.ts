import { TestBed } from '@angular/core/testing';
import { Router, UrlTree } from '@angular/router';
import { TenantService } from './tenant.service';
import { isModuleEntitled, moduleGuard } from './module.guard';

/**
 * US-ADM-012 AC-2 — module entitlement gating (frontend half).
 *
 * The predicate under test (isModuleEntitled) MUST mirror the backend
 * PlanModules.IsModuleEnabled and FAIL OPEN. The naive/broken implementation this
 * story guards against is a bare `enabledModules.includes(module)` check — each arm
 * below notes how it discriminates against that naive version.
 *
 * The legacy-vocabulary fixture is the real ISSUE-335 data: `enabled_modules` seeded
 * with permission prefixes (Audit, CustomField, Department, Employee, Reports, Roles,
 * Tenant) mixed with tokens that happen to also be canonical (Attendance, Benefits,
 * Leave, Payroll, Training). Because at least one token is non-canonical, the whole
 * list is NOT authoritative → everything must be allowed.
 */
const LEGACY_VOCAB = [
  'Attendance',
  'Audit',
  'Benefits',
  'CustomField',
  'Department',
  'Employee',
  'Leave',
  'Payroll',
  'Reports',
  'Roles',
  'Tenant',
  'Training',
];

describe('isModuleEntitled (US-ADM-012 AC-2)', () => {
  it('allows a module present in an authoritative canonical list', () => {
    // Basic positive. Naive `includes` also passes — not the discriminating arm.
    expect(isModuleEntitled('Payroll', ['CoreHR', 'Leave', 'Payroll'])).toBeTrue();
  });

  it('blocks a module absent from an authoritative canonical list', () => {
    // Basic negative. Every token here IS canonical, so the list is authoritative
    // and the missing module is genuinely blocked. Naive `includes` also blocks.
    expect(isModuleEntitled('Payroll', ['CoreHR', 'Leave', 'Attendance'])).toBeFalse();
  });

  it('FAILS-OPEN on a null/undefined list (nothing configured)', () => {
    // Discriminates vs naive: `undefined.includes` throws / `[]` would block. A tenant
    // with no configured module list must be treated as fully entitled.
    expect(isModuleEntitled('Payroll', undefined)).toBeTrue();
    expect(isModuleEntitled('Payroll', null)).toBeTrue();
  });

  it('FAILS-OPEN on an empty list', () => {
    // Discriminates vs naive: `[].includes('Payroll')` === false would wrongly block.
    expect(isModuleEntitled('Payroll', [])).toBeTrue();
  });

  it('FAILS-OPEN on the ISSUE-335 legacy vocabulary — CoreHR AND Recruitment both allowed', () => {
    // THE key arm. The naive `list.includes(module)` implementation returns false for
    // both 'CoreHR' and 'Recruitment' (neither literal is in the legacy list) and would
    // lock the tenant out of everything — including always-on CoreHR. Because the list
    // contains non-canonical tokens (Audit, CustomField, Department, …) it is not an
    // authoritative module list, so BOTH must be allowed.
    expect(isModuleEntitled('CoreHR', LEGACY_VOCAB)).toBeTrue();
    expect(isModuleEntitled('Recruitment', LEGACY_VOCAB)).toBeTrue();
  });
});

describe('moduleGuard (US-ADM-012 AC-2)', () => {
  let mockRouter: jasmine.SpyObj<Router>;

  function configure(enabledModules: string[] | undefined): void {
    mockRouter = jasmine.createSpyObj('Router', ['createUrlTree']);
    mockRouter.createUrlTree.and.returnValue({} as UrlTree);

    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: mockRouter },
        // Minimal TenantService stand-in exposing only what the guard reads.
        { provide: TenantService, useValue: { enabledModules: () => enabledModules ?? [] } },
      ],
    });
  }

  it('allows an entitled module from a canonical list', () => {
    configure(['CoreHR', 'Payroll']);
    TestBed.runInInjectionContext(() => {
      const guard = moduleGuard('Payroll');
      expect(guard({} as never, {} as never)).toBeTrue();
      expect(mockRouter.createUrlTree).not.toHaveBeenCalled();
    });
  });

  it('redirects to /forbidden for a non-entitled module in an authoritative canonical list', () => {
    configure(['CoreHR', 'Leave']);
    TestBed.runInInjectionContext(() => {
      const guard = moduleGuard('Payroll');
      const result = guard({} as never, {} as never);
      expect(result).not.toBe(true);
      expect(mockRouter.createUrlTree).toHaveBeenCalledWith(['/forbidden']);
    });
  });

  it('FAILS-OPEN through the guard on the ISSUE-335 legacy vocabulary', () => {
    // Discriminates vs naive: a bare-includes guard would redirect Recruitment to
    // /forbidden even though the legacy list is not authoritative.
    configure(LEGACY_VOCAB);
    TestBed.runInInjectionContext(() => {
      const guard = moduleGuard('Recruitment');
      expect(guard({} as never, {} as never)).toBeTrue();
      expect(mockRouter.createUrlTree).not.toHaveBeenCalled();
    });
  });
});
