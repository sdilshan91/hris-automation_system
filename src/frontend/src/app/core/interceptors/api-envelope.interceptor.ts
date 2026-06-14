import {
  HttpEvent,
  HttpInterceptorFn,
  HttpResponse,
} from '@angular/common/http';
import { map } from 'rxjs/operators';
import { ApiResponse } from '../models/api-response.model';

/**
 * Global response interceptor that transparently unwraps the backend's
 * `ApiResponse<T>` envelope (`{ success, data, message?, errors? }`) so that
 * services and components receive the bare `T` payload.
 *
 * Implements US-PLT-001. The .NET API wraps nearly every successful response in
 * this envelope; without this interceptor, services that type `http.get<T>()`
 * silently receive `{ success, data }` instead of `T` and data binding breaks
 * against the real backend (it only "works" against mocked unit tests).
 *
 * Conservative by design — it ONLY rewrites the body when ALL of these hold,
 * otherwise the event passes through UNCHANGED:
 *   - the event is an `HttpResponse` (we ignore upload/download progress events);
 *   - the response is a 2xx (`response.ok`);
 *   - the body is a non-null, plain object (NOT an array, Blob, ArrayBuffer,
 *     string, FormData, etc.);
 *   - the body has an OWN boolean `success` property AND an OWN `data` property.
 *
 * Consequently a 204/no-body, a bare array, a blob/file download, a string, a
 * paginated page envelope (`{ data, total, page, pageSize }` — has `data` but no
 * boolean `success`) and any non-enveloped object all pass through untouched.
 *
 * Error responses (non-2xx, including `success:false` bodies) never reach the
 * map here as `next` values — they surface on the error channel and are left for
 * `errorInterceptor` to handle. This interceptor therefore must sit INSIDE
 * (i.e. before) `errorInterceptor` in the chain so it only ever sees successful
 * responses on the way back up. See the registration in `app.config.ts`.
 */
export const apiEnvelopeInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    map((event: HttpEvent<unknown>) => {
      if (!(event instanceof HttpResponse) || !event.ok) {
        return event;
      }

      const body = event.body;
      if (!isEnvelope(body)) {
        return event;
      }

      return event.clone({ body: body.data });
    })
  );
};

/**
 * Type guard: is `body` a plain `ApiResponse<unknown>` envelope?
 * Rejects null, arrays, Blob/ArrayBuffer/string/FormData and any object missing
 * the discriminating `success` (boolean) + `data` own-properties.
 */
function isEnvelope(body: unknown): body is ApiResponse<unknown> {
  if (
    body === null ||
    typeof body !== 'object' ||
    Array.isArray(body) ||
    body instanceof Blob ||
    body instanceof ArrayBuffer ||
    body instanceof FormData
  ) {
    return false;
  }

  const record = body as Record<string, unknown>;
  return (
    Object.prototype.hasOwnProperty.call(record, 'success') &&
    typeof record['success'] === 'boolean' &&
    Object.prototype.hasOwnProperty.call(record, 'data')
  );
}
