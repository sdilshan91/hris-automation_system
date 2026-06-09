import { HttpBackend, HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { environment } from '../../../environments/environment';
import { TenantStatus } from '../auth/auth.models';

/** Resolved tenant context from subdomain */
export interface ITenantContext {
  tenantId?: string;
  subdomain: string;
  name?: string;
  logoUrl?: string;
  primaryColor?: string;
  status?: TenantStatus;
  suspensionReason?: string;
  plan?: string;
  enabledModules?: string[];
  isSystemContext: boolean;
  isReserved: boolean;
  isValid: boolean;
  state:
    | 'root'
    | 'fallback'
    | 'resolved'
    | 'system'
    | 'reserved'
    | 'invalid'
    | 'not-found'
    | 'suspended';
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

interface IApiResponse<T> {
  data?: T;
  success?: boolean;
  message?: string;
  error?: string;
  errors?: string[] | Record<string, string[]>;
}

type TenantContextApiResponse = Partial<ITenantContext> | IApiResponse<Partial<ITenantContext>>;

/**
 * TenantService resolves the current tenant from the browser subdomain.
 * This runs at bootstrap and provides tenant context throughout the app.
 *
 * Implements US-AUTH-007: Tenant resolution from subdomain.
 */
@Injectable({ providedIn: 'root' })
export class TenantService {
  private readonly http = new HttpClient(inject(HttpBackend));

  /** The resolved tenant context */
  readonly tenantContext = signal<ITenantContext>({
    subdomain: '',
    isSystemContext: false,
    isReserved: false,
    isValid: false,
    state: 'root',
  });

  /** Tenant status (populated after login/API call) */
  readonly tenantStatus = computed(() => this.tenantContext().status ?? null);

  /** Quick access to current subdomain */
  readonly subdomain = computed(() => this.tenantContext().subdomain);

  /** Subdomain to send to the backend tenant middleware in local/dev flows */
  readonly requestSubdomain = computed(() => this.tenantContext().subdomain);

  /** Whether this is the system admin context */
  readonly isSystemContext = computed(() => this.tenantContext().isSystemContext);

  /** Whether the resolved subdomain is valid for a tenant workspace */
  readonly isValidTenant = computed(() => this.tenantContext().isValid);

  readonly isWorkspaceNotFound = computed(
    () => this.tenantContext().state === 'not-found'
  );

  readonly isTenantSuspended = computed(
    () => this.tenantContext().state === 'suspended'
  );

  readonly displayName = computed(() => {
    const tenant = this.tenantContext();
    return tenant.name || this.formatWorkspaceName(tenant.subdomain) || environment.appName;
  });

  /**
   * Resolve tenant from the current hostname.
   * Called during app initialization.
   */
  resolve(): Promise<void> {
    const hostname = window.location.hostname;
    return this.resolveForHostname(hostname);
  }

  resolveForHostname(hostname: string): Promise<void> {
    const subdomain = this.extractSubdomain(hostname);

    if (!subdomain) {
      const fallbackSubdomain = this.getDevFallbackSubdomain();
      if (fallbackSubdomain) {
        this.setContext({
          subdomain: fallbackSubdomain,
          isSystemContext: fallbackSubdomain === SYSTEM_SUBDOMAIN,
          isReserved: false,
          isValid: true,
          state: 'fallback',
        });
        return this.hydrateTenantContext({ keepFallbackOnMissingApi: true });
      }

      // No subdomain -- likely the root domain or localhost without dev fallback
      this.tenantContext.set({
        subdomain: '',
        isSystemContext: false,
        isReserved: false,
        isValid: false,
        state: 'root',
      });
      this.applyBrandColor();
      return Promise.resolve();
    }

    // Validate subdomain format: lowercase alphanumeric + hyphens, 3-63 chars
    if (!this.isValidSubdomainFormat(subdomain)) {
      this.tenantContext.set({
        subdomain,
        isSystemContext: false,
        isReserved: false,
        isValid: false,
        state: 'invalid',
      });
      this.applyBrandColor();
      return Promise.resolve();
    }

    // Check system admin context
    if (subdomain === SYSTEM_SUBDOMAIN) {
      this.setContext({
        subdomain,
        isSystemContext: true,
        isReserved: false,
        isValid: true,
        state: 'system',
      });
      return this.hydrateTenantContext({ keepFallbackOnMissingApi: true });
    }

    // Check reserved subdomains
    if (RESERVED_SUBDOMAINS.has(subdomain)) {
      this.tenantContext.set({
        subdomain,
        isSystemContext: false,
        isReserved: true,
        isValid: false,
        state: 'reserved',
      });
      this.applyBrandColor();
      return Promise.resolve();
    }

    // Regular tenant subdomain
    this.setContext({
      subdomain,
      isSystemContext: false,
      isReserved: false,
      isValid: true,
      state: 'resolved',
    });
    return this.hydrateTenantContext({ keepFallbackOnMissingApi: true });
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
  extractSubdomain(hostname: string): string {
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

  setTenantFromAuth(tenant: Partial<ITenantContext>): void {
    this.setContext({
      ...this.tenantContext(),
      ...tenant,
      status: tenant.status ?? this.tenantContext().status ?? 'active',
      state: tenant.status === 'suspended' ? 'suspended' : this.tenantContext().state,
      isValid: true,
    });
  }

  /**
   * Validate subdomain format per NFR-4 of US-AUTH-007:
   * - Lowercase alphanumeric and hyphens only
   * - 3-63 characters
   * - No leading/trailing hyphens
   */
  isValidSubdomainFormat(subdomain: string): boolean {
    const pattern = /^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$/;
    return pattern.test(subdomain) && subdomain.length >= 3 && subdomain.length <= 63;
  }

  private hydrateTenantContext(options: {
    keepFallbackOnMissingApi: boolean;
  }): Promise<void> {
    const endpoint = `${environment.apiBaseUrl}/tenant/context`;
    const headers = this.requestSubdomain()
      ? new HttpHeaders({ 'X-Tenant-Subdomain': this.requestSubdomain() })
      : undefined;

    return new Promise((resolve) => {
      this.http
        .get<TenantContextApiResponse>(endpoint, { headers, withCredentials: true })
        .subscribe({
          next: (response) => {
            const tenant = this.unwrapTenantResponse(response);
            this.setContext({
              ...this.tenantContext(),
              ...tenant,
              status: tenant.status ?? this.tenantContext().status ?? 'active',
              state: tenant.status === 'suspended' ? 'suspended' : this.tenantContext().state,
              isValid: true,
            });
            resolve();
          },
          error: (error: HttpErrorResponse) => {
            if (this.isTenantNotFoundError(error)) {
              this.setContext({
                ...this.tenantContext(),
                isValid: false,
                state: 'not-found',
              });
            } else if (!options.keepFallbackOnMissingApi) {
              this.setContext({
                ...this.tenantContext(),
                isValid: false,
              });
            } else {
              this.applyBrandColor();
            }
            resolve();
          },
        });
    });
  }

  private unwrapTenantResponse(response: TenantContextApiResponse): Partial<ITenantContext> {
    if ('data' in response && response.data) {
      return response.data;
    }

    return response as Partial<ITenantContext>;
  }

  private isTenantNotFoundError(error: HttpErrorResponse): boolean {
    const body = error.error as { error?: string; message?: string } | string | undefined;
    const message =
      typeof body === 'string'
        ? body
        : body?.error || body?.message || error.message || '';

    return error.status === 404 && message.toLowerCase().includes('workspace does not exist');
  }

  private getDevFallbackSubdomain(): string {
    const subdomain = (environment as { tenantSubdomain?: string }).tenantSubdomain;
    return environment.production ? '' : (subdomain ?? '').trim().toLowerCase();
  }

  private setContext(context: ITenantContext): void {
    this.tenantContext.set(context);
    this.applyBrandColor(context.primaryColor);
  }

  private applyBrandColor(color?: string): void {
    const primary = this.normalizeHexColor(color) ?? '#0c8ee9';
    document.documentElement.style.setProperty('--brand-primary', primary);
  }

  private normalizeHexColor(color?: string): string | null {
    if (!color) return null;
    const normalized = color.trim();
    return /^#[0-9a-fA-F]{6}$/.test(normalized) ? normalized : null;
  }

  private formatWorkspaceName(subdomain: string): string {
    if (!subdomain) return '';
    return subdomain
      .split('-')
      .filter(Boolean)
      .map((part) => part[0].toUpperCase() + part.slice(1))
      .join(' ');
  }
}
