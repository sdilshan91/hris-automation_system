/**
 * The standard success envelope returned by the ASP.NET Core API for almost
 * every 2xx response: `ApiResponse<T>.Ok(...)`.
 *
 * The {@link apiEnvelopeInterceptor} transparently unwraps this envelope on the
 * way back from the backend so services and components receive the bare `T`.
 * This type is therefore primarily used by the interceptor itself and by the
 * few services/specs that still reason about the wire shape.
 *
 * Implements US-PLT-001 (Global API Response Envelope Unwrapping).
 */
export interface ApiResponse<T> {
  /** Discriminator: `true` on success envelopes. */
  success: boolean;
  /** The actual payload. */
  data: T;
  /** Optional human-readable message. */
  message?: string | null;
  /** Optional validation/error details (present on failure envelopes). */
  errors?: string[] | Record<string, string[]> | null;
}

/**
 * The consistent pagination contract used by paginated endpoints
 * (e.g. `GET /api/v1/tenant/employees`). The page envelope IS the response
 * body (it is NOT additionally wrapped in {@link ApiResponse}); note it carries
 * a `data` array but NO `success` discriminator, which is exactly why the
 * envelope interceptor leaves it untouched (see US-PLT-001 AC-5).
 */
export interface PaginatedResponse<T> {
  /** The page of items. */
  data: T[];
  /** Total number of items across all pages. */
  total: number;
  /** Current page number (1-based). */
  page: number;
  /** Page size. */
  pageSize: number;
}
