import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { Subject, interval } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { PlatformMonitoringService } from '../../services/platform-monitoring.service';
import {
  IPlatformHealth,
  ITenantUsageSummary,
  ITenantUsageDashboard,
  ITenantUsageFilters,
  IUsageGauge,
  DEFAULT_REFRESH_INTERVAL_MS,
  bandClass,
  needsAttention,
  requiresObservability,
} from '../../models/monitoring.models';

/**
 * US-ADM-002 (AC-1/2/3): System Admin platform monitoring dashboard.
 *
 * Renders platform health KPIs, a per-tenant usage table with inline gauges,
 * filters/search, and an "Attention Required" breach panel. Auto-refreshes via
 * POLLING (no SignalR exists yet) at a configurable interval (default 30s) and
 * shows a "last updated" timestamp + manual refresh.
 *
 * Honest rendering: where a metric is null / metricsStatus is
 * "RequiresObservabilityPipeline" the UI shows a muted "Not available — requires
 * observability pipeline" placeholder instead of a fabricated 0/empty chart.
 */
@Component({
  selector: 'app-monitoring-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  templateUrl: './monitoring-dashboard.component.html',
})
export class MonitoringDashboardComponent implements OnInit, OnDestroy {
  private readonly service = inject(PlatformMonitoringService);
  private readonly destroy$ = new Subject<void>();

  /** Polling interval (ms) — configurable; default 30s (AC-1). */
  readonly refreshIntervalMs = DEFAULT_REFRESH_INTERVAL_MS;

  // ── Health KPIs ──
  readonly health = signal<IPlatformHealth | null>(null);
  readonly healthLoading = signal(true);
  readonly healthError = signal<string | null>(null);

  // ── Tenant usage table ──
  readonly tenants = signal<ITenantUsageSummary[]>([]);
  readonly tenantsLoading = signal(true);
  readonly tenantsError = signal<string | null>(null);

  /** The server-computed quota-breach queue (tenants >=80% of the employee limit). */
  readonly quotaBreachQueue = signal<ITenantUsageSummary[]>([]);
  /** Status flag for the deferred error-rate "Attention Required" queue (AC-3). */
  readonly attentionQueueStatus = signal<string | null>(null);

  /** Timestamp of the last successful refresh (AC-1 "last updated"). */
  readonly lastUpdated = signal<Date | null>(null);

  // ── Filters (FR-4) ──
  readonly searchTerm = signal('');
  readonly statusFilter = signal('');
  readonly planFilter = signal('');
  readonly regionFilter = signal('');
  readonly createdFrom = signal('');
  readonly createdTo = signal('');

  // ── Pagination ──
  readonly pageSize = signal(10);
  readonly pageIndex = signal(0);

  /**
   * Quota-breach / attention list: tenants at/above 80% of the employee limit
   * (FR-3). The BE returns a server-computed `quotaBreachQueue`; we prefer it and
   * fall back to deriving from the visible tenant list if it is empty.
   */
  readonly attentionTenants = computed(() => {
    const queue = this.quotaBreachQueue();
    if (queue.length > 0) {
      return queue;
    }
    return this.tenants().filter((t) => this.tenantNeedsAttention(t));
  });

  /** Distinct status values for the filter dropdown. */
  readonly statusOptions = computed(() =>
    Array.from(new Set(this.tenants().map((t) => t.status))).sort(),
  );

  /** Distinct plan values for the filter dropdown. */
  readonly planOptions = computed(() =>
    Array.from(new Set(this.tenants().map((t) => t.plan))).sort(),
  );

  readonly totalPages = computed(() =>
    Math.max(1, Math.ceil(this.tenants().length / this.pageSize())),
  );

  /** Current page slice of the (already server-filtered) tenant list. */
  readonly pagedTenants = computed(() => {
    const start = this.pageIndex() * this.pageSize();
    return this.tenants().slice(start, start + this.pageSize());
  });

  // Expose helpers to the template.
  readonly bandClass = bandClass;
  readonly requiresObservability = requiresObservability;

  ngOnInit(): void {
    this.refresh();
    // Polling auto-refresh — cleaned up on destroy via takeUntil (AC-1).
    interval(this.refreshIntervalMs)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => this.refresh());
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  /** Fetch both health + tenant usage (manual button + polling tick). */
  refresh(): void {
    this.loadHealth();
    this.loadTenants();
  }

  private loadHealth(): void {
    this.healthLoading.set(true);
    this.healthError.set(null);
    this.service.getHealth().subscribe({
      next: (health) => {
        this.health.set(health);
        this.healthLoading.set(false);
        this.lastUpdated.set(new Date());
      },
      error: () => {
        this.healthError.set('Failed to load platform health.');
        this.healthLoading.set(false);
      },
    });
  }

  private loadTenants(): void {
    this.tenantsLoading.set(true);
    this.tenantsError.set(null);
    this.service.getTenantUsage(this.currentFilters()).subscribe({
      next: (dashboard: ITenantUsageDashboard) => {
        this.tenants.set(dashboard.tenants ?? []);
        this.quotaBreachQueue.set(dashboard.quotaBreachQueue ?? []);
        this.attentionQueueStatus.set(dashboard.attentionQueueStatus ?? null);
        this.tenantsLoading.set(false);
        // Clamp the current page if the result set shrank.
        if (this.pageIndex() > this.totalPages() - 1) {
          this.pageIndex.set(this.totalPages() - 1);
        }
      },
      error: () => {
        this.tenantsError.set('Failed to load tenant usage.');
        this.tenantsLoading.set(false);
      },
    });
  }

  /** Build the active filter object from the signal state. */
  private currentFilters(): ITenantUsageFilters {
    return {
      search: this.searchTerm() || undefined,
      status: this.statusFilter() || undefined,
      plan: this.planFilter() || undefined,
      region: this.regionFilter() || undefined,
      createdFrom: this.createdFrom() || undefined,
      createdTo: this.createdTo() || undefined,
    };
  }

  /** Re-query with the current filters (search / dropdown change). */
  applyFilters(): void {
    this.pageIndex.set(0);
    this.loadTenants();
  }

  clearFilters(): void {
    this.searchTerm.set('');
    this.statusFilter.set('');
    this.planFilter.set('');
    this.regionFilter.set('');
    this.createdFrom.set('');
    this.createdTo.set('');
    this.applyFilters();
  }

  onSearchInput(value: string): void {
    this.searchTerm.set(value);
  }

  prevPage(): void {
    if (this.pageIndex() > 0) this.pageIndex.update((i) => i - 1);
  }

  nextPage(): void {
    if (this.pageIndex() < this.totalPages() - 1) this.pageIndex.update((i) => i + 1);
  }

  /**
   * A tenant needs attention if its headline employee usage is >=80% / breached
   * (FR-3). The BE only sources the employee gauge; deferred gauges have no data.
   */
  tenantNeedsAttention(t: ITenantUsageSummary): boolean {
    return needsAttention(t);
  }

  /** Look up a named gauge (e.g. 'Storage', 'ApiCalls', 'EmailSends') on a row. */
  gauge(t: ITenantUsageSummary, resource: string): IUsageGauge | undefined {
    return t.gauges?.find((g) => g.resource === resource);
  }

  /** Overall health badge colour (BE enum string: Healthy / Degraded / Down). */
  healthStatusClass(status: string): string {
    switch ((status || '').toLowerCase()) {
      case 'healthy':
        return 'bg-green-50 text-green-700';
      case 'degraded':
        return 'bg-amber-50 text-amber-700';
      case 'down':
        return 'bg-red-50 text-red-700';
      default:
        return 'bg-neutral-100 text-neutral-600';
    }
  }

  /** Dependency (DB/Redis) health badge colour (Healthy / Down / NotConfigured). */
  componentHealthClass(health: string): string {
    switch ((health || '').toLowerCase()) {
      case 'healthy':
        return 'bg-green-50 text-green-700';
      case 'down':
        return 'bg-red-50 text-red-700';
      case 'notconfigured':
        return 'bg-neutral-100 text-neutral-500';
      default:
        return 'bg-neutral-100 text-neutral-600';
    }
  }

  /** Status badge colour for a tenant row. */
  tenantStatusClass(status: string): string {
    switch ((status || '').toLowerCase()) {
      case 'active':
        return 'bg-green-50 text-green-700';
      case 'trial':
        return 'bg-blue-50 text-blue-700';
      case 'suspended':
        return 'bg-amber-50 text-amber-700';
      case 'terminated':
        return 'bg-red-50 text-red-700';
      default:
        return 'bg-neutral-100 text-neutral-600';
    }
  }
}
