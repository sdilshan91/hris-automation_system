import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Route, Routes, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ToastrService } from 'ngx-toastr';
import { of } from 'rxjs';

import { MainLayoutComponent } from './main-layout.component';
import { INavItem, NAV_ITEMS } from './main-layout.component';
import { appRoutes } from '../../app.routes';
import { AuthService } from '../../core/auth/auth.service';
import {
  SYSTEM_ROLES,
  authGuard,
  isPermissionGuard,
  isRoleGuard,
  noAuthGuard,
} from '../../core/auth/auth.guard';
import { isModuleGuard } from '../../core/tenant/module.guard';
import { tenantAvailabilityGuard } from '../../core/tenant/tenant.guard';
import { mfaChallengeGuard, mfaEnrollGuard } from '../../core/auth/mfa.guard';
import { ImpersonationBannerComponent } from '../../features/admin/impersonation/components/impersonation-banner/impersonation-banner.component';
import { NotificationBellComponent } from '../../features/notifications/components/notification-bell/notification-bell.component';
import { IdleTimeoutWarningComponent } from '../../shared/components/idle-timeout-warning/idle-timeout-warning.component';

/**
 * BUG-493 — SIDEBAR NAV GATE ↔ ROUTE GUARD INVARIANT.
 * TC id: TC-ADM-005-14 (nav-gate invariant regression; free suffix in the US-ADM-005 set).
 *
 * THE DEFECT CLASS
 * ----------------
 * Two independent systems were answering the same question, "may this persona use this feature?":
 * the sidebar's per-item gate, and the `canActivate` guard on the route the item links to. Roughly
 * thirty nav items gated on a PERMISSION key while their route gated on `roleGuard([...])`. Nothing
 * held the two together, so they drifted — in both directions:
 *
 *   - shown-but-rejected: an Employee holding `Attendance.CheckIn` saw "Clock In", and
 *     /attendance's roleGuard bounced them to /forbidden;
 *   - admitted-but-hidden: a Tenant Owner passes EVERY tenant roleGuard (auth.guard.ts
 *     TENANT_SUPER_ROLES) yet saw no "Employees" link, because the item demanded
 *     `Employee.View.All`. A working page nobody can find.
 *
 * ISSUE-210 was the first instance, BUG-450 the second (items with no gate at all), and each was
 * fixed by patching the named items. That is why a third instance existed: a spec that enumerates
 * today's broken items goes green the moment someone adds tomorrow's. BUG-450's own spec pinned
 * /performance by name while /departments and /job-titles sat ungated right beside it.
 *
 * WHAT THIS FILE ASSERTS INSTEAD
 * ------------------------------
 * One invariant over the WHOLE table: for every nav item, the gate declared on the item must equal
 * the gate the router actually enforces on its route — derived from the route config at runtime,
 * never transcribed. Add a 46th item with a mismatched gate and this fails, by name, without anyone
 * editing this file.
 *
 * MAKING GUARDS INTROSPECTABLE (the hard part)
 * -------------------------------------------
 * `roleGuard(['Tenant Admin'])` returns a closure; its role list is unrecoverable from the function
 * value. The production change (auth.guard.ts / module.guard.ts) publishes each guard's own criteria
 * as a property on the returned function — and, critically, the guard BODY reads the very same frozen
 * array that is published. The property is not a description of the check, it IS the check's input.
 *
 * The rejected alternative was a route→roles map maintained here in the spec. That is a second copy
 * of the truth, i.e. precisely the defect being fixed, one layer down: the map would drift from
 * app.routes.ts exactly as the nav table drifted, and the spec would go green while lying.
 *
 * WHAT COUNTS AS AGREEMENT
 * ------------------------
 * The gate on a route is the CONJUNCTION of every persona-discriminating guard along its full chain
 * (root → lazy child), so /attendance/shifts is the parent's roleGuard(['Employee','Manager',
 * 'HR Officer','Tenant Admin']) INTERSECTED with the child's roleGuard(['HR Officer','HR Manager',
 * 'Tenant Admin']) — an HR Manager is admitted by the child and refused by the parent. The nav item
 * must therefore carry the intersection, not either half.
 *
 * Guards that do not discriminate between authenticated personas (authGuard, tenantAvailabilityGuard,
 * the MFA guards) are listed explicitly below and contribute nothing. Anything else is UNKNOWN and
 * fails the suite on purpose: a new guard kind must be taught to this invariant, not silently ignored.
 */

@Component({ selector: 'app-impersonation-banner', standalone: true, template: '' })
class StubImpersonationBannerComponent {}

@Component({ selector: 'app-notification-bell', standalone: true, template: '' })
class StubNotificationBellComponent {}

@Component({ selector: 'app-idle-timeout-warning', standalone: true, template: '' })
class StubIdleTimeoutWarningComponent {}

/**
 * Guards that every authenticated persona passes identically. They gate on session/tenant state, not
 * on who you are, so they place no requirement on a nav gate. Compared by identity, not by name.
 */
const NON_PERSONA_GUARDS: readonly unknown[] = [
  authGuard,
  noAuthGuard,
  tenantAvailabilityGuard,
  mfaChallengeGuard,
  mfaEnrollGuard,
];

/** Every persona-discriminating guard found along one route chain. */
interface RouteGates {
  /** One entry per roleGuard: its `effectiveRoles` (i.e. after TENANT_SUPER_ROLES widening). */
  roleSets: string[][];
  /** One entry per permissionGuard: its any-of permission list. */
  permissionSets: string[][];
  /** One entry per moduleGuard. */
  modules: string[];
  /** Guards this spec cannot classify — non-empty means the invariant is not being checked. */
  unknown: string[];
}

const noGates = (): RouteGates => ({
  roleSets: [],
  permissionSets: [],
  modules: [],
  unknown: [],
});

function mergeGates(a: RouteGates, b: RouteGates): RouteGates {
  return {
    roleSets: [...a.roleSets, ...b.roleSets],
    permissionSets: [...a.permissionSets, ...b.permissionSets],
    modules: [...a.modules, ...b.modules],
    unknown: [...a.unknown, ...b.unknown],
  };
}

/** Read the criteria a route's own `canActivate` entries enforce. */
function classifyGuards(canActivate: unknown[] | undefined): RouteGates {
  const gates = noGates();
  for (const guard of canActivate ?? []) {
    if (NON_PERSONA_GUARDS.includes(guard)) {
      continue;
    }
    if (isRoleGuard(guard)) {
      gates.roleSets.push([...guard.effectiveRoles]);
    } else if (isPermissionGuard(guard)) {
      gates.permissionSets.push([...guard.requiredPermissions]);
    } else if (isModuleGuard(guard)) {
      gates.modules.push(guard.moduleKey);
    } else {
      gates.unknown.push((guard as { name?: string })?.name || String(guard));
    }
  }
  return gates;
}

async function childRoutesOf(route: Route): Promise<Routes | null> {
  if (route.children) {
    return route.children;
  }
  if (route.loadChildren) {
    return (await (route.loadChildren as () => Promise<Routes>)()) as Routes;
  }
  return null;
}

/**
 * Walk the real router config for `segments`, accumulating every guard the router would run.
 * Returns null when nothing matches, so a caller can try the next candidate branch.
 *
 * Only literal segments are matched: nav items never link to a parameterised or wildcard path, and
 * matching one would make the resolved guard set ambiguous.
 */
async function walkRoutes(
  routes: Routes,
  segments: string[],
  depth: number
): Promise<RouteGates | null> {
  if (depth > 12) {
    throw new Error('route walk exceeded 12 levels — cyclic redirect?');
  }

  // All segments consumed: the router lands on this level's index route, whose guards count too
  // (e.g. /reports' permissionGuard(['Reports.View']) lives on REPORTS_ROUTES' `path: ''`).
  if (segments.length === 0) {
    const index = routes.find((r) => r.path === '');
    if (!index) {
      return noGates();
    }
    const own = classifyGuards(index.canActivate);
    if (typeof index.redirectTo === 'string') {
      const target = index.redirectTo.replace(/^\//, '').split('/').filter(Boolean);
      if (target.length === 0) {
        return own;
      }
      const redirected = await walkRoutes(routes, target, depth + 1);
      return redirected ? mergeGates(own, redirected) : own;
    }
    const kids = await childRoutesOf(index);
    if (!kids) {
      return own;
    }
    const deeper = await walkRoutes(kids, [], depth + 1);
    return deeper ? mergeGates(own, deeper) : own;
  }

  const literalMatches = routes
    .filter(
      (r) =>
        typeof r.path === 'string' &&
        r.path !== '' &&
        !r.path.includes('*') &&
        !r.path.includes(':')
    )
    .map((r) => ({ route: r, segs: (r.path as string).split('/') }))
    .filter(
      ({ segs }) =>
        segs.length <= segments.length && segs.every((s, i) => s === segments[i])
    )
    // Longest literal prefix wins, so 'admin/users' beats a hypothetical 'admin'.
    .sort((a, b) => b.segs.length - a.segs.length);

  for (const { route, segs } of literalMatches) {
    const own = classifyGuards(route.canActivate);
    const rest = segments.slice(segs.length);
    const kids = await childRoutesOf(route);
    if (!kids) {
      if (rest.length === 0) {
        return own;
      }
      continue;
    }
    const deeper = await walkRoutes(kids, rest, depth + 1);
    if (deeper) {
      return mergeGates(own, deeper);
    }
    if (rest.length === 0) {
      return own;
    }
  }

  // Pass-through container (the `path: ''` MainLayout shell) — consumes no segment but does carry
  // guards.
  for (const container of routes.filter(
    (r) => r.path === '' && !r.redirectTo && (r.children || r.loadChildren)
  )) {
    const kids = await childRoutesOf(container);
    if (!kids) {
      continue;
    }
    const deeper = await walkRoutes(kids, segments, depth + 1);
    if (deeper) {
      return mergeGates(classifyGuards(container.canActivate), deeper);
    }
  }

  return null;
}

/** The gate a nav item must declare, if its route is to admit exactly who the item shows it to. */
interface RequiredGate {
  roles: string[] | null;
  permissions: string[] | null;
  module: string | null;
}

function intersect(sets: string[][]): string[] {
  return sets.reduce((acc, set) => acc.filter((v) => set.includes(v)));
}

function requiredGateFrom(route: string, gates: RouteGates): RequiredGate {
  if (gates.unknown.length) {
    throw new Error(
      `${route}: unclassifiable guard(s) [${gates.unknown.join(', ')}]. A new guard kind must be ` +
        'taught to this invariant (classifyGuards) or added to NON_PERSONA_GUARDS — leaving it ' +
        'unhandled would silently stop checking this route.'
    );
  }

  const modules = [...new Set(gates.modules)];
  if (modules.length > 1) {
    throw new Error(`${route}: chain requires two different modules [${modules.join(', ')}]`);
  }

  // Several permissionGuards on one chain are ANDed by the router, but INavItem.permission is a
  // single any-of list. That is expressible only when one group is a subset of all the others (the
  // narrowest wins) — which is how /benefits/my-benefits works: {Own,All,Manage} AND {Own} == {Own}.
  let permissions: string[] | null = null;
  if (gates.permissionSets.length === 1) {
    permissions = [...gates.permissionSets[0]];
  } else if (gates.permissionSets.length > 1) {
    const narrowest = gates.permissionSets.find((candidate) =>
      gates.permissionSets.every((other) => candidate.every((p) => other.includes(p)))
    );
    if (!narrowest) {
      throw new Error(
        `${route}: chain ANDs non-nested permission groups ` +
          `${JSON.stringify(gates.permissionSets)}, which INavItem.permission (a single any-of ` +
          'list) cannot express. Widen the model rather than approximating the gate.'
      );
    }
    permissions = [...narrowest];
  }

  return {
    roles: gates.roleSets.length ? intersect(gates.roleSets) : null,
    permissions,
    module: modules[0] ?? null,
  };
}

/** The gate the nav item declares. */
function declaredGate(item: INavItem): RequiredGate {
  const roles = item.systemRoles ?? item.tenantRoles ?? null;
  const permission = item.permission;
  return {
    roles: roles ? [...roles] : null,
    permissions: permission
      ? Array.isArray(permission)
        ? [...permission]
        : [permission]
      : null,
    module: item.module ?? null,
  };
}

function sameSet(a: string[] | null, b: string[] | null): boolean {
  if (a === null || b === null) {
    return a === b;
  }
  const x = [...new Set(a)].sort();
  const y = [...new Set(b)].sort();
  return x.length === y.length && x.every((v, i) => v === y[i]);
}

const show = (v: string[] | string | null): string =>
  v === null ? 'none' : Array.isArray(v) ? `[${[...v].sort().join(', ')}]` : v;

describe('MainLayoutComponent nav gate ↔ route guard invariant (BUG-493 / TC-ADM-005-14)', () => {
  /** Resolved once: walking the config resolves every lazy feature route chunk. */
  const required = new Map<string, RequiredGate>();

  beforeAll(async () => {
    for (const item of NAV_ITEMS) {
      const segments = item.route.replace(/^\//, '').split('/').filter(Boolean);
      const gates = await walkRoutes(appRoutes, segments, 0);
      if (!gates) {
        throw new Error(
          `nav item "${item.label}" points at ${item.route}, which matches NO route in ` +
            'app.routes.ts — the link is dead.'
        );
      }
      required.set(item.route, requiredGateFrom(item.route, gates));
    }
  });

  // ── ARM 1: the invariant, over every item ───────────────────────────────────────────────────
  it('every nav item declares exactly the gate its route enforces', () => {
    const mismatches: string[] = [];

    for (const item of NAV_ITEMS) {
      const want = required.get(item.route) as RequiredGate;
      const got = declaredGate(item);

      if (!sameSet(want.roles, got.roles)) {
        mismatches.push(
          `${item.label} (${item.route}): route roleGuard admits ${show(want.roles)}, ` +
            `nav gates on roles ${show(got.roles)}`
        );
      }
      if (!sameSet(want.permissions, got.permissions)) {
        mismatches.push(
          `${item.label} (${item.route}): route requires permissions ${show(want.permissions)}, ` +
            `nav gates on ${show(got.permissions)}`
        );
      }
      if (want.module !== got.module) {
        mismatches.push(
          `${item.label} (${item.route}): route moduleGuard is ${show(want.module)}, ` +
            `nav tags module ${show(got.module)}`
        );
      }
    }

    expect(mismatches)
      .withContext(
        'Each line is a nav gate that disagrees with the guard on the route it links to (BUG-493). ' +
          'A gate NARROWER than the route hides a working page; a gate WIDER hands someone a link ' +
          'that dead-ends at /forbidden. Fix the nav item to mirror the route — do not relax this ' +
          "assertion. If a ROUTE's guard is what's wrong, that is a deliberate access-control " +
          'change and needs review, not a nav edit.\n' +
          mismatches.join('\n')
      )
      .toEqual([]);
  });

  // ── ARM 2: ungated items are a deliberate, named set ─────────────────────────────────────────
  /**
   * Items whose route carries no persona guard beyond authGuard, so they are correctly visible to
   * every authenticated tenant user. BUG-450 was two items that reached this state by ACCIDENT
   * (a gate was simply never written) and were therefore shown to everyone while their routes were
   * roleGuard(['Tenant Admin','HR Officer']). Arm 1 already catches that, but naming the legitimate
   * cases makes "no gate" a decision someone has to make on purpose.
   */
  const INTENTIONALLY_UNGATED: Record<string, string> = {
    '/dashboard':
      'the post-login landing page; every authenticated user of every persona must reach it',
    '/profile/notification-preferences':
      'personal per-tenant preferences — the backend scopes to the caller identity + membership ' +
      '(US-NTF-003 BR-4), so there is nothing to gate on',
  };

  it('the set of ungated nav items is exactly the documented set', () => {
    const ungated = NAV_ITEMS.filter((item) => {
      const gate = required.get(item.route) as RequiredGate;
      return gate.roles === null && gate.permissions === null && gate.module === null;
    }).map((item) => item.route);

    expect(ungated.sort())
      .withContext(
        'A nav item with no gate is visible to EVERY authenticated user. That is right for a ' +
          'landing/self-service page and wrong for anything else (BUG-450). If a new route belongs ' +
          'here, add it to INTENTIONALLY_UNGATED with the reason; if it does not, gate it.'
      )
      .toEqual(Object.keys(INTENTIONALLY_UNGATED).sort());
  });

  // ── ARM 3: the invariant, observed through the rendered DOM ─────────────────────────────────
  /**
   * Arm 1 compares declarations. This arm proves the declarations actually govern what renders —
   * a correct table behind a broken filter would still ship the bug.
   *
   * Method: log in as one role at a time while holding EVERY permission the app mentions. Granting
   * all permissions neutralises the permission dimension on both sides (nav gate and route guard),
   * isolating the role dimension — which is where BUG-493's drift lived. A link must then render
   * if and only if the route's roleGuard admits that role.
   *
   * Scoped to TENANT roles: the system/tenant persona split (visibleNavItems) additionally hides
   * the tenant menu from a platform operator and vice-versa. That split is a deliberate product
   * rule, not a route guard — it is asserted separately in Arm 4 rather than folded in here, where
   * it would look like a guard mismatch.
   */
  let fixture: ComponentFixture<MainLayoutComponent>;
  let authService: AuthService;

  function loginAs(roles: string[], permissions: string[]): void {
    const header = btoa(JSON.stringify({ alg: 'HS256', typ: 'JWT' }));
    const payload = btoa(
      JSON.stringify({
        sub: 'user-1',
        roles,
        permissions,
        exp: Math.floor(Date.now() / 1000) + 3600,
      })
    );
    authService.activateImpersonation(`${header}.${payload}.sig`);
  }

  function renderedRoutes(): (string | null)[] {
    return fixture.debugElement
      .queryAll(By.css('nav.sidebar-nav a.nav-item'))
      .map((de) => de.nativeElement.getAttribute('href'));
  }

  function navFor(roles: string[], permissions: string[]): (string | null)[] {
    loginAs(roles, permissions);
    fixture = TestBed.createComponent(MainLayoutComponent);
    fixture.detectChanges();
    return renderedRoutes();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MainLayoutComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        {
          provide: ToastrService,
          useValue: jasmine.createSpyObj<ToastrService>('ToastrService', [
            'info',
            'warning',
            'success',
            'error',
          ]),
        },
      ],
    });

    TestBed.overrideComponent(MainLayoutComponent, {
      remove: {
        imports: [
          ImpersonationBannerComponent,
          NotificationBellComponent,
          IdleTimeoutWarningComponent,
        ],
      },
      add: {
        imports: [
          StubImpersonationBannerComponent,
          StubNotificationBellComponent,
          StubIdleTimeoutWarningComponent,
        ],
      },
    });

    authService = TestBed.inject(AuthService);
    spyOn(authService, 'getMyTenants').and.returnValue(of([]));
    spyOn(authService, 'getTenantAuthSettings').and.returnValue(
      of({ idleTimeoutMinutes: 0 }) as ReturnType<AuthService['getTenantAuthSettings']>
    );
  });

  it('for every tenant role, a link renders iff that role can enter its route', () => {
    // Every permission any nav item or any route guard mentions — granted in full so that only the
    // role dimension can decide visibility.
    const allPermissions = [
      ...new Set(
        [...required.values()].flatMap((g) => g.permissions ?? []).concat(
          NAV_ITEMS.flatMap((i) =>
            i.permission ? (Array.isArray(i.permission) ? i.permission : [i.permission]) : []
          )
        )
      ),
    ];

    // Every tenant role named by any route guard, plus Employee — the least-privileged persona and
    // the one both prior instances of this bug stranded.
    const tenantRoles = [
      ...new Set(
        [...required.values()]
          .flatMap((g) => g.roles ?? [])
          .filter((r) => !SYSTEM_ROLES.includes(r))
          .concat('Employee')
      ),
    ].sort();

    const failures: string[] = [];

    for (const role of tenantRoles) {
      const shown = navFor([role], allPermissions);

      for (const item of NAV_ITEMS) {
        if (item.systemRoles) {
          continue; // covered by Arm 4
        }
        const gate = required.get(item.route) as RequiredGate;
        const routeAdmits = gate.roles === null || gate.roles.includes(role);
        const navShows = shown.includes(item.route);

        if (routeAdmits && !navShows) {
          failures.push(
            `${role}: can enter ${item.route} but the nav does not offer it (a working page ` +
              'nobody can find)'
          );
        } else if (!routeAdmits && navShows) {
          failures.push(
            `${role}: is shown ${item.route}, which its route guard rejects (dead-ends at ` +
              '/forbidden)'
          );
        }
      }
    }

    expect(failures)
      .withContext(
        'Rendered-nav vs route-guard disagreements, with every permission granted so only roles ' +
          'differ (BUG-493):\n' + failures.join('\n')
      )
      .toEqual([]);
  });

  // ── ARM 4: the platform personas ─────────────────────────────────────────────────────────────
  it('each platform role sees exactly the system-console routes its guard admits', () => {
    const systemItems = NAV_ITEMS.filter((i) => i.systemRoles);
    expect(systemItems.length).toBeGreaterThan(0);

    for (const role of SYSTEM_ROLES) {
      const shown = navFor([role], []);

      for (const item of systemItems) {
        const gate = required.get(item.route) as RequiredGate;
        const routeAdmits = (gate.roles ?? []).includes(role);
        expect(shown.includes(item.route))
          .withContext(
            `${role} ${routeAdmits ? 'IS' : 'is NOT'} admitted to ${item.route} by its roleGuard, ` +
              `so the nav ${routeAdmits ? 'must' : 'must not'} offer it`
          )
          .toBe(routeAdmits);
      }

      // The other half of the persona split: a platform operator gets no tenant links, because
      // roleGuard never widens a system role into a tenant route.
      const tenantLinks = NAV_ITEMS.filter((i) => !i.systemRoles).map((i) => i.route);
      const leaked = shown.filter((r) => r !== null && tenantLinks.includes(r));
      expect(leaked)
        .withContext(
          `${role} is a platform operator; every one of these tenant links dead-ends at /forbidden`
        )
        .toEqual([]);
    }
  });

  it('a tenant persona is offered no system-console link', () => {
    const systemRoutes = NAV_ITEMS.filter((i) => i.systemRoles).map((i) => i.route);
    // Tenant Owner is the widest tenant persona — roleGuard admits it everywhere in the tenant —
    // and it still must not reach the platform console (roleGuard does not widen system guards).
    const shown = navFor(['Tenant Owner'], []);
    for (const route of systemRoutes) {
      expect(shown).not.toContain(route);
    }
  });
});
