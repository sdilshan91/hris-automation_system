import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { TenantService } from './tenant.service';

export const tenantAvailabilityGuard: CanActivateFn = () => {
  const tenantService = inject(TenantService);
  const router = inject(Router);

  if (tenantService.isWorkspaceNotFound()) {
    return router.createUrlTree(['/workspace-not-found']);
  }

  if (tenantService.isTenantSuspended()) {
    return router.createUrlTree(['/tenant-suspended']);
  }

  return true;
};

