import { Injectable, signal, computed } from '@angular/core';
import { environment } from '../../../environments/environment';
import { TenantStatus } from '../auth/auth.models';

/** Resolved tenant context from subdomain */
export interface ITenantContext {
  subdomain: string;
  isSystemContext: boolean;
  isReserved: boolean;
  isValid: boolean;
}

/** Reserved subdomains that cannot be used by tenants */
const RESERVED_SUBDOMAINS = new Set([
  'www',
  'api',
  'app',
  'mail',
  'status',
  'docs',
  'help',
  'support',
  'static',
  'cdn',
  'dev',
  'stage',
  'prod',
  'test',
  'qa',
]);

/** System admin subdomain */
const SYSTEM_SUBDOMAIN = 'admin';

/**
 * TenantService resolves the current tenant from the browser subdomain.
 * This runs at bootstrap and provides tenant context throughout the app.
 *
 * Implements US-AUTH-007: Tenant resolution from subdomain.
 */
@Injectable({ providedIn: 'root' })
export class TenantService {
  /** The resolved tenant context */
  readonly tenantContext = signal<ITenantContext>({
    subdomain: '',
    isSystemContext: false,
    isReserved: false,
    isValid: false,
  });

  /** Tenant status (populated after login/API call) */
  readonly tenantStatus = signal<TenantStatus | null>(null);

  /** Quick access to current subdomain */
  readonly subdomain = computed(() => this.tenantContext().subdomain);

  /** Whether this is the system admin context */
  readonly isSystemContext = computed(() => this.tenantContext().isSystemContext);

  /** Whether the resolved subdomain is valid for a tenant workspace */
  readonly isValidTenant = computed(() => this.tenantContext().isValid);

  /**
   * Resolve tenant from the current hostname.
   * Called during app initialization.
   */
  resolve(): void {
    const hostname = window.location.hostname;
    const subdomain = this.extractSubdomain(hostname);

    if (!subdomain) {
      // No subdomain -- likely the root domain or localhost
      this.tenantContext.set({
        subdomain: '',
        isSystemContext: false,
        isReserved: false,
        isValid: false,
      });
      return;
    }

    // Validate subdomain format: lowercase alphanumeric + hyphens, 3-63 chars
    if (!this.isValidSubdomainFormat(subdomain)) {
      this.tenantContext.set({
        subdomain,
        isSystemContext: false,
        isReserved: false,
        isValid: false,
      });
      return;
    }

    // Check system admin context
    if (subdomain === SYSTEM_SUBDOMAIN) {
      this.tenantContext.set({
        subdomain,
        isSystemContext: true,
        isReserved: false,
        isValid: true,
      });
      return;
    }

    // Check reserved subdomains
    if (RESERVED_SUBDOMAINS.has(subdomain)) {
      this.tenantContext.set({
        subdomain,
        isSystemContext: false,
        isReserved: true,
        isValid: false,
      });
      return;
    }

    // Regular tenant subdomain
    this.tenantContext.set({
      subdomain,
      isSystemContext: false,
      isReserved: false,
      isValid: true,
    });
  }

  /**
   * Build the full URL for a given tenant subdomain.
   */
  getTenantUrl(subdomain: string, path: string = '/'): string {
    const protocol = environment.production ? 'https' : 'http';
    const baseDomain = environment.baseDomain;
    return `${protocol}://${subdomain}.${baseDomain}${path}`;
  }

  /**
   * Extract subdomain from hostname.
   * Handles both production (acme.yourhrm.com) and dev (acme.localhost:4200).
   */
  private extractSubdomain(hostname: string): string {
    // Remove port if present
    const host = hostname.split(':')[0];
    const baseDomain = environment.baseDomain.split(':')[0];

    // Handle localhost development: e.g., acme.localhost
    if (baseDomain === 'localhost') {
      const parts = host.split('.');
      if (parts.length >= 2 && parts[parts.length - 1] === 'localhost') {
        return parts.slice(0, -1).join('.').toLowerCase();
      }
      return '';
    }

    // Handle production: e.g., acme.yourhrm.com
    if (host.endsWith(`.${baseDomain}`)) {
      const sub = host.slice(0, -(baseDomain.length + 1));
      return sub.toLowerCase();
    }

    return '';
  }

  /**
   * Validate subdomain format per NFR-4 of US-AUTH-007:
   * - Lowercase alphanumeric and hyphens only
   * - 3-63 characters
   * - No leading/trailing hyphens
   */
  private isValidSubdomainFormat(subdomain: string): boolean {
    const pattern = /^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$/;
    return pattern.test(subdomain) && subdomain.length >= 3 && subdomain.length <= 63;
  }
}
