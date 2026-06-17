/**
 * US-ONB-004: Asset Issuance Tracking models.
 *
 * Mirrors the PINNED backend contract under `/api/v1/onboarding/assets` exactly
 * (paths/verbs/field names are fixed). Kept in ONE place so the service mapping,
 * the HR issuance form, and the employee self-service "My Assets" view share a
 * single source of truth. Reuses the upload validation helpers from
 * my-onboarding.models — the acknowledgment upload follows the same MIME/size
 * rules, except it is limited to PDF/JPEG/PNG per FR-6 / Data Requirements §7.
 */

/** Asset condition grades (Data Requirements §7 / UI/UX §8 colored chips). */
export type AssetCondition = 'New' | 'Good' | 'Fair' | 'Poor';

/** Ordered condition options for the selector chips. */
export const ASSET_CONDITIONS: readonly AssetCondition[] = [
  'New',
  'Good',
  'Fair',
  'Poor',
] as const;

/** Lifecycle status of an asset in the register (FR-2). */
export type AssetStatus = 'available' | 'assigned' | 'returned' | 'disposed';

/**
 * One asset as returned by the register endpoints. `assetId` is present for
 * register-tracked assets; ad-hoc entries (BR / Assumptions §10) may omit it on
 * the way in but the server always returns one on the way out.
 */
export interface IAsset {
  assetId: string;
  assetType: string;
  assetTag: string;
  serialNumber?: string | null;
  brand?: string | null;
  model?: string | null;
  condition: AssetCondition;
  status: AssetStatus;
}

/** An asset assigned to an employee (HR view + employee self-service view). */
export interface IAssignedAsset extends IAsset {
  /** ISO date (yyyy-MM-dd) the asset was issued. */
  issueDate: string;
  notes?: string | null;
}

/**
 * One asset line in an issuance request (FR-5 bulk). `assetId` is set when the
 * line was chosen from the available-asset register; ad-hoc lines carry the typed
 * fields instead. The server validates availability (FR-3) and double-assignment
 * (AC-3, BR-1).
 */
export interface IIssueAssetLine {
  assetId?: string | null;
  assetType: string;
  assetTag: string;
  serialNumber?: string | null;
  brand?: string | null;
  model?: string | null;
  condition: AssetCondition;
  /** ISO date (yyyy-MM-dd); cannot be in the future. */
  issueDate: string;
  notes?: string | null;
}

/** The JSON "request" part of the multipart issuance submission (FR-5 / FR-7). */
export interface IIssueAssetsRequest {
  employeeId: string;
  /** Links the issuance to the originating checklist task, if any (FR-1). */
  taskInstanceId?: string | null;
  assets: IIssueAssetLine[];
}

// ─── Acknowledgment upload validation (FR-6 / NFR-4) ────────────

/** Max acknowledgment upload size — 10 MB (NFR-4). */
export const MAX_ACK_BYTES = 10 * 1024 * 1024;

/** Accepted MIME types for the acknowledgment receipt (FR-6 — PDF/JPEG/PNG). */
export const ACCEPTED_ACK_MIME: readonly string[] = [
  'application/pdf',
  'image/jpeg',
  'image/png',
] as const;

/** `accept` attribute string for the acknowledgment file input. */
export const ACK_ACCEPT_ATTR = '.pdf,.jpg,.jpeg,.png';

/**
 * Validate a candidate acknowledgment upload by MIME type + size (FR-6 / NFR-4).
 * Returns a human message on failure, or null when valid. Pure helper — the
 * server re-validates + malware-scans (NFR-4); this is UX only.
 */
export function validateAcknowledgmentFile(file: File): string | null {
  if (!ACCEPTED_ACK_MIME.includes(file.type)) {
    return 'Unsupported file type. Upload a PDF, JPG, or PNG.';
  }
  if (file.size > MAX_ACK_BYTES) {
    return 'File is too large. The maximum size is 10 MB.';
  }
  if (file.size === 0) {
    return 'The selected file is empty.';
  }
  return null;
}

/** Human-readable file size (B/KB/MB) for the selected-file row. Pure helper. */
export function formatAssetFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

// ─── Display helpers ────────────────────────────────────────────

/** Tailwind chip class for a condition (UI/UX §8: green/blue/yellow/red). */
export function conditionChipClass(condition: AssetCondition): string {
  switch (condition) {
    case 'New':
      return 'chip-condition-new';
    case 'Good':
      return 'chip-condition-good';
    case 'Fair':
      return 'chip-condition-fair';
    case 'Poor':
      return 'chip-condition-poor';
  }
}

/**
 * Today's date as an ISO yyyy-MM-dd string in local time — used as the `max`
 * attribute on the issue-date input so future dates can't be picked (FR /
 * Data Requirements §7). Pure helper, `today` injected for testability.
 */
export function todayIso(today: Date = new Date()): string {
  const y = today.getFullYear();
  const m = `${today.getMonth() + 1}`.padStart(2, '0');
  const d = `${today.getDate()}`.padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Whether an ISO date string (yyyy-MM-dd) is strictly after today (local) —
 * drives the issue-date "cannot be in the future" validator. Pure helper.
 */
export function isFutureDate(iso: string, today: Date = new Date()): boolean {
  if (!iso) return false;
  const picked = new Date(`${iso}T00:00:00`);
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  return picked.getTime() > start.getTime();
}

/** Pluralized success-toast message after issuance (AC-2). */
export function issuedToast(count: number, employeeName: string): string {
  const noun = count === 1 ? 'asset' : 'assets';
  return `${count} ${noun} issued to ${employeeName}.`;
}

/** A concise label for an available-asset option in the type-ahead dropdown. */
export function assetOptionLabel(asset: IAsset): string {
  const parts = [asset.assetTag];
  if (asset.serialNumber) parts.push(`S/N ${asset.serialNumber}`);
  const make = [asset.brand, asset.model].filter(Boolean).join(' ');
  if (make) parts.push(make);
  return parts.join(' · ');
}
