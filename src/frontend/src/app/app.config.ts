import {
  ApplicationConfig,
  provideZoneChangeDetection,
  APP_INITIALIZER,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import {
  provideHttpClient,
  withInterceptors,
  withFetch,
} from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { appRoutes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { tenantInterceptor } from './core/interceptors/tenant.interceptor';
import { TenantService } from './core/tenant/tenant.service';

/**
 * Factory for tenant resolution at app startup.
 * Resolves the tenant from the subdomain before the app renders.
 */
function initializeTenant(tenantService: TenantService): () => void {
  return () => tenantService.resolve();
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),

    // Router with lazy-loaded components and input binding
    provideRouter(appRoutes, withComponentInputBinding()),

    // HTTP client with interceptors (order matters: tenant -> auth -> error)
    provideHttpClient(
      withFetch(),
      withInterceptors([tenantInterceptor, authInterceptor, errorInterceptor])
    ),

    // Animations (async to reduce bundle size)
    provideAnimationsAsync(),

    // Toast notifications (Notion-like styling)
    provideToastr({
      timeOut: 4000,
      positionClass: 'toast-bottom-right',
      preventDuplicates: true,
      progressBar: true,
      progressAnimation: 'decreasing',
      closeButton: true,
      newestOnTop: true,
      maxOpened: 3,
      autoDismiss: true,
    }),

    // App initialization: resolve tenant from subdomain before rendering
    {
      provide: APP_INITIALIZER,
      useFactory: initializeTenant,
      deps: [TenantService],
      multi: true,
    },
  ],
};
