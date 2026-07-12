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
import { HttpClient } from '@angular/common/http';
import {
  provideTranslateService,
  TranslateLoader,
} from '@ngx-translate/core';
import { TranslateHttpLoader } from '@ngx-translate/http-loader';
import { appRoutes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { errorInterceptor } from './core/interceptors/error.interceptor';
import { tenantInterceptor } from './core/interceptors/tenant.interceptor';
import { apiEnvelopeInterceptor } from './core/interceptors/api-envelope.interceptor';
import { TenantService } from './core/tenant/tenant.service';
import { AuthService } from './core/auth/auth.service';

/**
 * App bootstrap initializer (blocks rendering until resolved).
 *
 * Ordering is deliberate and sequential (a single chained factory — NOT two
 * separate APP_INITIALIZER entries, which Angular runs concurrently):
 *   1. tenantService.resolve() — resolve the tenant from the subdomain so the
 *      tenantInterceptor can stamp X-Tenant-Subdomain on every request.
 *   2. authService.restoreSession() — silently re-mint the in-memory access
 *      token from the httpOnly refresh cookie and hydrate the session (BUG-097),
 *      so a full page load / deep link survives instead of bouncing to login.
 *      It needs the tenant header from step 1, and it resolves even on failure
 *      (anonymous visitor) so bootstrap never blocks.
 * Because this completes before the router/guards run, authGuard sees the
 * restored session (currentUser signal) synchronously — no race.
 */
function initializeApp(
  tenantService: TenantService,
  authService: AuthService
): () => Promise<void> {
  return () => tenantService.resolve().then(() => authService.restoreSession());
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),

    // Router with lazy-loaded components and input binding
    provideRouter(appRoutes, withComponentInputBinding()),

    // HTTP client with interceptors. Order matters.
    // Request order is left-to-right; response order is right-to-left:
    //   request : tenant -> auth -> apiEnvelope -> error -> backend
    //   response: backend -> error -> apiEnvelope -> auth -> tenant
    // apiEnvelope sits just inside error so it only ever sees SUCCESSFUL
    // responses (error bodies, incl. `success:false`, stay on the error channel
    // for errorInterceptor) and unwraps the `ApiResponse<T>` envelope before any
    // downstream consumer receives the body. See apiEnvelopeInterceptor docs.
    provideHttpClient(
      withFetch(),
      withInterceptors([
        tenantInterceptor,
        authInterceptor,
        apiEnvelopeInterceptor,
        errorInterceptor,
      ])
    ),

    // Animations (async to reduce bundle size)
    provideAnimationsAsync(),

    // i18n (ngx-translate) — strings served from assets/i18n/{lang}.json.
    // Drives the US-ADM-003 impersonation banner (BR-6) and is available app-wide.
    provideTranslateService({
      defaultLanguage: 'en',
      useDefaultLang: true,
      loader: {
        provide: TranslateLoader,
        useFactory: (http: HttpClient) =>
          new TranslateHttpLoader(http, '/assets/i18n/', '.json'),
        deps: [HttpClient],
      },
    }),

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

    // App initialization: resolve tenant from subdomain, THEN restore any
    // existing session from the refresh cookie (BUG-097) before rendering.
    {
      provide: APP_INITIALIZER,
      useFactory: initializeApp,
      deps: [TenantService, AuthService],
      multi: true,
    },
  ],
};
