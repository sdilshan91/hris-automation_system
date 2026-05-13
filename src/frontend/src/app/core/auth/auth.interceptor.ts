import { inject } from '@angular/core';
import {
  HttpInterceptorFn,
  HttpRequest,
  HttpHandlerFn,
  HttpEvent,
  HttpErrorResponse,
} from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { AuthService } from './auth.service';

/**
 * HTTP interceptor that attaches the JWT access token to outgoing requests
 * and handles 401 responses by silently refreshing the token.
 *
 * Implements FR-10 from US-AUTH-002: queues concurrent 401 responses and
 * replays them after a single silent refresh completes.
 */
export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const authService = inject(AuthService);

  // Skip auth header for auth endpoints that don't need it
  if (isAuthEndpoint(req.url)) {
    return next(req);
  }

  // Attach token if available
  const token = authService.getAccessToken();
  const authedReq = token ? addToken(req, token) : req;

  return next(authedReq).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401 && !isRefreshRequest(req.url)) {
        return handle401(req, next, authService);
      }
      return throwError(() => error);
    })
  );
};

/**
 * Handle 401 by refreshing the token, then retrying the original request.
 */
function handle401(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  authService: AuthService
): Observable<HttpEvent<unknown>> {
  return authService.refreshToken().pipe(
    switchMap((newToken: string) => {
      const retryReq = addToken(req, newToken);
      return next(retryReq);
    }),
    catchError((err) => {
      // Refresh failed -- the AuthService handles logout/redirect
      return throwError(() => err);
    })
  );
}

/**
 * Clone the request and attach the Bearer token.
 */
function addToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: {
      Authorization: `Bearer ${token}`,
    },
  });
}

/**
 * Check if the URL is an auth endpoint that should not have token attached.
 */
function isAuthEndpoint(url: string): boolean {
  const authPaths = ['/auth/login', '/auth/forgot-password', '/auth/reset-password'];
  return authPaths.some((path) => url.includes(path));
}

/**
 * Check if the request is the refresh endpoint (to avoid infinite loops).
 */
function isRefreshRequest(url: string): boolean {
  return url.includes('/auth/refresh');
}
