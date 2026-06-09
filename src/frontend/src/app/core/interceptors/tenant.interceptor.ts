import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { TenantService } from '../tenant/tenant.service';

export const tenantInterceptor: HttpInterceptorFn = (req, next) => {
  const tenantService = inject(TenantService);
  const subdomain = tenantService.requestSubdomain();

  if (!subdomain || req.headers.has('X-Tenant-Subdomain')) {
    return next(req);
  }

  return next(
    req.clone({
      setHeaders: { 'X-Tenant-Subdomain': subdomain },
    })
  );
};
