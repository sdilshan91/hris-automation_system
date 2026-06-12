/**
 * US-CHR-007: Location models matching the backend API contract.
 *
 * Backend endpoint: /api/v1/tenant/locations
 * The backend agent is building this in parallel. Assumed contract:
 *   - locationId (uuid), tenantId (uuid), name (string, unique per tenant),
 *     addressLine1, addressLine2, city, stateProvince, country, postalCode,
 *     timeZone (IANA string), phone, isActive (boolean), employeeCount (number),
 *     createdAt, updatedAt (ISO 8601 strings).
 */

/** Location entity returned by the API */
export interface ILocation {
  locationId: string;
  tenantId: string;
  name: string;
  addressLine1: string | null;
  addressLine2: string | null;
  city: string | null;
  stateProvince: string | null;
  country: string | null;
  postalCode: string | null;
  timeZone: string;
  phone: string | null;
  isActive: boolean;
  employeeCount: number;
  createdAt: string;
  updatedAt: string;
}

/** Request payload for creating a location (FR-1) */
export interface ICreateLocationRequest {
  name: string;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  stateProvince?: string | null;
  country?: string | null;
  postalCode?: string | null;
  timeZone: string;
  phone?: string | null;
  isActive: boolean;
}

/** Request payload for updating a location (FR-1) */
export interface IUpdateLocationRequest {
  name: string;
  addressLine1?: string | null;
  addressLine2?: string | null;
  city?: string | null;
  stateProvince?: string | null;
  country?: string | null;
  postalCode?: string | null;
  timeZone: string;
  phone?: string | null;
  isActive: boolean;
}

/** Error response shape from the backend for location operations */
export interface ILocationErrorResponse {
  message: string;
  code?: 'duplicate_name' | 'has_active_employees' | string;
  employeeCount?: number;
}
