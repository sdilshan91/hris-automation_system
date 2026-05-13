import { HttpInterceptorFn } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export const tenantInterceptor: HttpInterceptorFn = (req, next) => {
  const subdomain = (environment as { tenantSubdomain?: string }).tenantSubdomain;
  if (!subdomain) {
    return next(req);
  }
  return next(
    req.clone({
      setHeaders: { 'X-Tenant-Subdomain': subdomain },
    })
  );
};
