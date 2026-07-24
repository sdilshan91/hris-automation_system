import * as Sentry from '@sentry/angular';
import type { ErrorEvent, EventHint } from '@sentry/angular';
import { environment } from '../../../environments/environment';

/**
 * US-PLT-006 AC-6 — client-side error tracking for the Angular SPA against the
 * self-hosted, Sentry-API-compatible GlitchTip instance.
 *
 * Two hard conditions, mirroring the backend slice (AC-2 / AC-3):
 *  1. **Inert when the DSN is blank.** With no DSN (the shipped default) `initSentry`
 *     does NOT call `Sentry.init` — no SDK client, no network, no behavioural change.
 *  2. **PII is scrubbed before it leaves the browser.** `beforeSend` strips request
 *     bodies, the `Authorization` header, cookies, query strings and known PII fields
 *     (email, national id); `sendDefaultPii` is off. Matches the backend `BeforeSend`.
 *
 * Never hardcode a real DSN here — it is supplied only via `environment.sentryDsn`.
 */

/**
 * Minimal seam over the `@sentry/angular` calls we use. Production passes the real
 * SDK; tests inject a spy object. `@sentry/angular` is shipped as a frozen ESM
 * namespace whose exports are non-writable, so `spyOn(Sentry, 'init')` throws —
 * this seam is the supported way to unit-test the wiring without spying it.
 */
export interface SentryApi {
  init: (options: Parameters<typeof Sentry.init>[0]) => unknown;
  setTag: (key: string, value: string) => unknown;
}

const realSentry: SentryApi = {
  init: (options) => Sentry.init(options),
  setTag: (key, value) => Sentry.setTag(key, value),
};

/** Tracks whether the SDK actually initialised, so tenant tagging stays inert too. */
let sentryEnabled = false;

/**
 * Field-name pattern for known PII we must never ship, regardless of where it appears
 * in the captured event's `extra` payload. Mirrors the backend scrub (email, national id).
 */
const PII_KEY_PATTERN =
  /^(e[-_]?mail|national[-_]?id|nationalid|nic|ssn|password|token|authorization)$/i;

const REDACTED = '[redacted]';

/**
 * Recursively redact known-PII-named fields anywhere inside a captured value.
 * Bounded, key-based — deliberately simple; the primary defences are the request /
 * header / cookie strips below plus `sendDefaultPii: false`.
 */
function redactPii(node: unknown, depth = 0): void {
  if (depth > 6 || node === null || typeof node !== 'object') {
    return;
  }
  if (Array.isArray(node)) {
    for (const item of node) {
      redactPii(item, depth + 1);
    }
    return;
  }
  const record = node as Record<string, unknown>;
  for (const key of Object.keys(record)) {
    if (PII_KEY_PATTERN.test(key)) {
      record[key] = REDACTED;
    } else {
      redactPii(record[key], depth + 1);
    }
  }
}

/**
 * `beforeSend` scrub — mirrors the backend `BeforeSend`. Exported for direct unit
 * testing (AC-2 parity). Returns the mutated event so the SDK still sends the
 * de-identified error.
 */
export function scrubEvent(event: ErrorEvent, _hint?: EventHint): ErrorEvent {
  const request = event.request;
  if (request) {
    // Request body, cookies/session and query parameters never leave the browser.
    delete request.data;
    delete request.cookies;
    delete request.query_string;
    if (request.headers) {
      for (const header of Object.keys(request.headers)) {
        const name = header.toLowerCase();
        if (name === 'authorization' || name === 'cookie') {
          delete request.headers[header];
        }
      }
    }
  }

  // Do not attach default PII carried on the user context.
  if (event.user) {
    delete event.user.email;
    delete event.user.ip_address;
    delete (event.user as Record<string, unknown>)['username'];
  }

  // Redact known PII fields captured incidentally in extra data.
  if (event.extra) {
    redactPii(event.extra);
  }

  return event;
}

/**
 * Initialise client-side error tracking. Inert (returns `false`, no `Sentry.init`)
 * when the DSN is blank — AC-3 parity. Returns `true` when the SDK was initialised.
 */
export function initSentry(
  dsn: string = environment.sentryDsn,
  sdk: SentryApi = realSentry
): boolean {
  if (!dsn) {
    sentryEnabled = false;
    return false;
  }

  sdk.init({
    dsn,
    sendDefaultPii: false,
    beforeSend: scrubEvent,
  });
  sentryEnabled = true;
  return true;
}

/**
 * Tag captured events with the current tenant so GlitchTip issues are filterable
 * per tenant — matching the backend `tenant_id` / `tenant_subdomain` tags (AC-6).
 * No-op when the SDK is inert (blank DSN), so it never triggers SDK activity.
 */
export function setSentryTenant(
  subdomain: string,
  tenantId?: string,
  sdk: SentryApi = realSentry
): void {
  if (!sentryEnabled) {
    return;
  }
  if (subdomain) {
    sdk.setTag('tenant_subdomain', subdomain);
  }
  if (tenantId) {
    sdk.setTag('tenant_id', tenantId);
  }
}
