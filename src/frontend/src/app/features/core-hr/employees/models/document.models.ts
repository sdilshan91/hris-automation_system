/**
 * US-CHR-008: Employee Document Management models.
 *
 * Data Requirements (Section 7):
 *   - document_id: uuid (PK)
 *   - tenant_id: uuid (FK), auto
 *   - employee_id: uuid (FK)
 *   - file_name: varchar(255)
 *   - storage_key: varchar(500)
 *   - file_size_bytes: bigint
 *   - mime_type: varchar(100)
 *   - category: varchar(50) — Contract, ID, Certificate, Other
 *   - description: text, optional
 *   - expiry_date: date, optional
 *   - uploaded_by: uuid
 *   - created_at / updated_at: timestamptz
 *   - is_deleted: boolean, default false
 */

// ─── Types ────────────────────────────────────────────────────

export type DocumentCategory = 'Contract' | 'ID' | 'Certificate' | 'Other';

export const DOCUMENT_CATEGORIES: DocumentCategory[] = [
  'Contract',
  'ID',
  'Certificate',
  'Other',
];

/** Filter tab labels — "All" + each category */
export const DOCUMENT_FILTER_TABS: { key: DocumentCategory | 'All'; label: string }[] = [
  { key: 'All', label: 'All' },
  { key: 'Contract', label: 'Contracts' },
  { key: 'ID', label: 'IDs' },
  { key: 'Certificate', label: 'Certificates' },
  { key: 'Other', label: 'Other' },
];

// ─── Entity ───────────────────────────────────────────────────

/** Employee document metadata returned by the API */
export interface IEmployeeDocument {
  documentId: string;
  tenantId: string;
  employeeId: string;
  fileName: string;
  storageKey: string;
  fileSizeBytes: number;
  mimeType: string;
  category: DocumentCategory;
  description: string | null;
  expiryDate: string | null;
  uploadedBy: string;
  uploadedByName: string | null;
  createdAt: string;
  updatedAt: string;
}

// ─── Request / Response ───────────────────────────────────────

/** Upload document request metadata (sent alongside the file in multipart) */
export interface IUploadDocumentRequest {
  category: DocumentCategory;
  description?: string | null;
  expiryDate?: string | null;
}

/** Download response containing the signed URL */
export interface IDocumentDownloadResponse {
  downloadUrl: string;
  expiresAt: string;
}

// ─── Validation constants (AC-3, BR-7) ──────────────────────

export const MAX_DOCUMENT_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB
export const MAX_DOCUMENT_SIZE_LABEL = '10 MB';

/**
 * Allowed MIME types for document upload (BR-7).
 * PDF, JPEG, PNG, DOCX, XLSX.
 */
export const ALLOWED_DOCUMENT_MIME_TYPES: string[] = [
  'application/pdf',
  'image/jpeg',
  'image/png',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
];

/**
 * Human-readable label for allowed types (AC-3 error message).
 */
export const ALLOWED_DOCUMENT_TYPES_LABEL = 'PDF, JPEG, PNG, DOCX, XLSX';

/**
 * Error messages exactly matching AC-3.
 */
export const DOCUMENT_VALIDATION_ERRORS = {
  FILE_TOO_LARGE: 'File exceeds the 10 MB limit.',
  FILE_TYPE_NOT_ALLOWED: 'File type not allowed. Supported: PDF, JPEG, PNG, DOCX, XLSX.',
} as const;

// ─── Utility functions ────────────────────────────────────────

/**
 * Validate a file against size and MIME type constraints (AC-3, BR-7).
 * Returns null if valid, or an error message string.
 */
export function validateDocumentFile(file: File): string | null {
  if (file.size > MAX_DOCUMENT_SIZE_BYTES) {
    return DOCUMENT_VALIDATION_ERRORS.FILE_TOO_LARGE;
  }
  if (!ALLOWED_DOCUMENT_MIME_TYPES.includes(file.type)) {
    return DOCUMENT_VALIDATION_ERRORS.FILE_TYPE_NOT_ALLOWED;
  }
  return null;
}

/**
 * Format file size in human-readable form.
 */
export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(1024));
  const value = bytes / Math.pow(1024, i);
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

/**
 * Compute the expiry badge status based on the expiry date.
 * Returns 'green' (>30d), 'amber' (<30d), 'red' (<7d or expired), or null (no expiry).
 */
export function getExpiryBadgeStatus(
  expiryDate: string | null,
  today?: Date
): 'green' | 'amber' | 'red' | null {
  if (!expiryDate) return null;
  const now = today ?? new Date();
  const expiry = new Date(expiryDate);
  const diffMs = expiry.getTime() - now.getTime();
  const diffDays = Math.ceil(diffMs / (1000 * 60 * 60 * 24));
  if (diffDays < 7) return 'red'; // <7 days or already expired
  if (diffDays < 30) return 'amber'; // <30 days
  return 'green'; // >30 days
}

/**
 * Return an SVG icon class hint based on the MIME type.
 * Used by the template to pick the right file icon.
 */
export function getMimeTypeIcon(mimeType: string): 'pdf' | 'image' | 'word' | 'excel' | 'file' {
  if (mimeType === 'application/pdf') return 'pdf';
  if (mimeType.startsWith('image/')) return 'image';
  if (mimeType.includes('wordprocessingml')) return 'word';
  if (mimeType.includes('spreadsheetml')) return 'excel';
  return 'file';
}
