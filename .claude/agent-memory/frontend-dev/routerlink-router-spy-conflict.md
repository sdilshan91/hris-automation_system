---
name: routerlink-router-spy-conflict
description: A component using routerLink in its template breaks if its spec provides a full Router spy (createUrlTree missing); use real provideRouter + spyOn(router,'navigate')
metadata:
  type: feedback
---

When a component template uses `routerLink` (e.g. a "Back to onboarding" link) AND
the component also navigates programmatically (`router.navigate(...)`), do NOT
replace `Router` with a `jasmine.createSpyObj('Router', ['navigate'])`. `RouterLink`
calls `router.createUrlTree`/`serializeUrl` during change detection, which the
partial spy lacks -> `TypeError: this.router.createUrlTree is not a function` on
EVERY test that triggers `detectChanges()`.

Instead: keep the real router via `provideRouter([])`, then
`const router = TestBed.inject(Router); spyOn(router, 'navigate').and.resolveTo(true);`
and assert against that spy.

**Why:** hit this on US-ONB-005 offboarding-initiate spec — 7 specs failed purely
from the RouterLink hook, not the logic under test. Related: [[routerlink-breaks-sibling-spec]]
(adding routerLink also needs provideRouter to satisfy ActivatedRoute).

**How to apply:** any spec for a component that both has a `routerLink` in its
template and injects `Router` for `.navigate()`. Default to the real router + a
`navigate` spy rather than a wholesale Router mock.
