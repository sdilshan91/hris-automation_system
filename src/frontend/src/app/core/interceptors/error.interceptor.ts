import { inject } from '@angular/core';
import {
  HttpInterceptorFn,
  HttpRequest,
  HttpHandlerFn,
  HttpEvent,
  HttpErrorResponse,
} from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { Router } from '@angular/router';
import { ToastrService } from 'ngx-toastr';

/**
 * Global HTTP error interceptor.
 * Handles common error responses and shows user-friendly notifications.
 */
export const errorInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn
): Observable<HttpEvent<unknown>> => {
  const toastr = inject(ToastrService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      // Skip handling for auth interceptor managed errors
      if (error.status === 401) {
        return throwError(() => error);
      }

      switch (error.status) {
        case 0:
          toastr.error(
            'Unable to connect to the server. Please check your network connection.',
            'Connection Error'
          );
          break;

        case 403:
          handleForbidden(error, toastr, router);
          break;

        case 404:
          if (!isApiRequest(req.url)) {
            break;
          }
          toastr.warning('The requested resource was not found.', 'Not Found');
          break;

        case 422:
          handleValidationErrors(error, toastr);
          break;

        case 429:
          toastr.warning(
            'Too many requests. Please wait a moment and try again.',
            'Rate Limited'
          );
          break;

        case 500:
        case 502:
        case 503:
          toastr.error(
            'An unexpected server error occurred. Please try again later.',
            'Server Error'
          );
          break;

        default:
          if (error.status >= 400) {
            const message =
              error.error?.message || 'An unexpected error occurred.';
            toastr.error(message, 'Error');
          }
          break;
      }

      return throwError(() => error);
    })
  );
};

function handleForbidden(
  error: HttpErrorResponse,
  toastr: ToastrService,
  router: Router
): void {
  const message =
    error.error?.message || "You don't have permission to perform this action.";

  // Tenant-specific forbidden messages
  if (message.includes('suspended') || message.includes('unavailable')) {
    toastr.error(message, 'Workspace Unavailable');
    return;
  }

  if (message.includes('membership')) {
    toastr.error(message, 'Access Denied');
    return;
  }

  toastr.warning(message, 'Forbidden');
  router.navigate(['/forbidden']);
}

function handleValidationErrors(
  error: HttpErrorResponse,
  toastr: ToastrService
): void {
  if (error.error?.errors && typeof error.error.errors === 'object') {
    const errors = error.error.errors as Record<string, string[]>;
    const messages = Object.values(errors).flat();
    messages.forEach((msg) => toastr.warning(msg, 'Validation'));
  } else {
    const message = error.error?.message || 'Validation failed.';
    toastr.warning(message, 'Validation Error');
  }
}

function isApiRequest(url: string): boolean {
  return url.includes('/api/');
}
