/**
 * US-CHR-012: Custom Fields per Tenant — models and pure utility functions.
 *
 * Data Requirements (Section 7):
 *   - field_name: varchar(100), required, unique per tenant + entity
 *   - field_key: varchar(100), slugified, used as JSONB key, immutable after creation
 *   - field_type: varchar(30), required (text, textarea, number, date, dropdown, multi_select, checkbox, email, phone, url)
 *   - is_required: boolean, default false
 *   - options: jsonb, for dropdown/multi_select
 *   - display_order: int, for form rendering order
 *   - is_active: boolean, default true
 */

// ─── Field types (FR-2) ─────────────────────────────────────

export type CustomFieldType =
  | 'text'
  | 'textarea'
  | 'number'
  | 'date'
  | 'dropdown'
  | 'multi_select'
  | 'checkbox'
  | 'email'
  | 'phone'
  | 'url';

export const CUSTOM_FIELD_TYPES: { value: CustomFieldType; label: string; icon: string }[] = [
  { value: 'text', label: 'Short Text', icon: 'Aa' },
  { value: 'textarea', label: 'Long Text', icon: 'T' },
  { value: 'number', label: 'Number', icon: '#' },
  { value: 'date', label: 'Date', icon: 'D' },
  { value: 'dropdown', label: 'Dropdown', icon: 'v' },
  { value: 'multi_select', label: 'Multi-Select', icon: '[]' },
  { value: 'checkbox', label: 'Checkbox', icon: 'cb' },
  { value: 'email', label: 'Email', icon: '@' },
  { value: 'phone', label: 'Phone', icon: 'Ph' },
  { value: 'url', label: 'URL', icon: 'Lk' },
];

// ─── Entity ─────────────────────────────────────────────────

/** Custom field definition returned by the API */
export interface ICustomFieldDefinition {
  customFieldId: string;
  tenantId: string;
  entityType: string;
  fieldName: string;
  fieldKey: string;
  fieldType: CustomFieldType;
  isRequired: boolean;
  options: string[] | null;
  displayOrder: number;
  isActive: boolean;
  usageCount: number;
  createdAt: string;
  updatedAt: string;
}

/** Plan limit info returned alongside the list */
export interface ICustomFieldPlanLimits {
  currentCount: number;
  maxAllowed: number | null; // null = unlimited
}

/** List response wrapping definitions + plan info */
export interface ICustomFieldListResponse {
  definitions: ICustomFieldDefinition[];
  planLimits: ICustomFieldPlanLimits;
}

// ─── Request payloads ───────────────────────────────────────

export interface ICreateCustomFieldRequest {
  fieldName: string;
  fieldKey: string;
  fieldType: CustomFieldType;
  isRequired: boolean;
  options: string[] | null;
  displayOrder: number;
  entityType: string;
}

export interface IUpdateCustomFieldRequest {
  fieldName: string;
  isRequired: boolean;
  options: string[] | null;
  displayOrder: number;
}

export interface IReorderCustomFieldsRequest {
  orderedIds: string[];
}

// ─── Error responses ────────────────────────────────────────

export interface ICustomFieldErrorResponse {
  message: string;
  code?: 'plan_limit_exceeded' | 'duplicate_name' | 'duplicate_key' | 'validation_error' | string;
  maxAllowed?: number;
  currentCount?: number;
}

// ─── Pure utility functions ─────────────────────────────────

/**
 * Slugify a field name into a URL-safe JSONB key.
 * Trims, lowercases, replaces spaces/special chars with underscores,
 * collapses consecutive underscores, and strips leading/trailing underscores.
 *
 * Examples:
 *   "T-Shirt Size" -> "t_shirt_size"
 *   "Employee  ID #" -> "employee_id"
 *   "  Hello World  " -> "hello_world"
 */
export function slugifyFieldKey(name: string): string {
  return name
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/_{2,}/g, '_')
    .replace(/^_|_$/g, '');
}

/**
 * Check whether a field type requires an options array (dropdown/multi_select).
 */
export function fieldTypeHasOptions(type: CustomFieldType): boolean {
  return type === 'dropdown' || type === 'multi_select';
}

/**
 * Map a custom field type to the HTML input type used for rendering.
 */
export function fieldTypeToInputType(type: CustomFieldType): string {
  switch (type) {
    case 'text':        return 'text';
    case 'textarea':    return 'textarea';
    case 'number':      return 'number';
    case 'date':        return 'date';
    case 'email':       return 'email';
    case 'phone':       return 'tel';
    case 'url':         return 'url';
    case 'checkbox':    return 'checkbox';
    case 'dropdown':    return 'select';
    case 'multi_select': return 'multi-select';
    default:            return 'text';
  }
}

/**
 * Map a custom field type to an icon text for the visual selector.
 */
export function fieldTypeIcon(type: CustomFieldType): string {
  return CUSTOM_FIELD_TYPES.find(t => t.value === type)?.icon ?? '?';
}
