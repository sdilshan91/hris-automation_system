/**
 * US-NTF-002: Email Notification Templates per Tenant.
 *
 * Frontend models for the Tenant Admin template editor. The shapes mirror the
 * PINNED FE↔BE contract: JSON is camelCase, enums arrive as strings, and the
 * apiEnvelopeInterceptor (US-PLT-001) has already unwrapped the ApiResponse<T>
 * envelope, so the service receives the bare payload (these types directly).
 *
 * Tenant isolation is server-side (RLS + EF global query filters, AC-5); the FE
 * just carries the auth cookie + the X-Tenant-Subdomain header (tenantInterceptor).
 */

/** One row in the template list (GET /notification-templates) — AC-1. */
export interface ITemplateListItem {
  /** Stable event identifier, e.g. "leave_approved", "onboarding_welcome". */
  eventKey: string;
  /** Human-readable event name, e.g. "Leave Approved". */
  eventName: string;
  /** ISO 639-1 language code, e.g. "en". */
  language: string;
  /** true → tenant override in effect; false → system default (BR-1/FR-6). */
  isCustom: boolean;
  /** ISO-8601 timestamp of the last override edit; null for an untouched default. */
  lastModified: string | null;
}

/** Full template detail for the editor (GET /notification-templates/{eventKey}) — AC-2. */
export interface ITemplateDetail {
  eventKey: string;
  eventName: string;
  language: string;
  /** Subject line (supports placeholders). */
  subject: string;
  /** HTML body (supports placeholders). */
  bodyHtml: string;
  /** Plain-text body for email-client compatibility (BR-3). */
  bodyText: string;
  isCustom: boolean;
  /** Auto-incremented on each saved override. */
  version: number;
  /** Placeholder names available for this event, e.g. "employee.firstName" (FR-3). */
  availablePlaceholders: string[];
  /** Sample data used for client/server preview rendering (FR-4). */
  sampleData: Record<string, unknown>;
}

/** PUT body — the editable fields persisted as a tenant override (AC-3). */
export interface ITemplateUpdate {
  subject: string;
  bodyHtml: string;
  bodyText: string;
}

/** POST /preview body — render the in-progress draft with sample data (FR-4). */
export interface ITemplatePreviewRequest {
  subject: string;
  bodyHtml: string;
  bodyText: string;
  language: string;
}

/** Rendered output of a template + sample-data merge. */
export interface IRenderedPreview {
  subject: string;
  bodyHtml: string;
  bodyText: string;
}

/** POST /test-email body — send a one-off rendered test to an address (FR-8). */
export interface ITestEmailRequest {
  recipientEmail: string;
  language: string;
}

/** POST /test-email response. */
export interface ITestEmailResult {
  sent: boolean;
}

/** Status badge label derived from isCustom — AC-1 ("Default" / "Custom"). */
export function templateBadge(isCustom: boolean): 'Custom' | 'Default' {
  return isCustom ? 'Custom' : 'Default';
}

/**
 * Wrap a placeholder name in the `{{ ... }}` mustache syntax used at send time
 * (FR-2). Centralised so the editor, variable panel and any future preview share
 * exactly one convention.
 */
export function placeholderToken(name: string): string {
  return `{{${name}}}`;
}
