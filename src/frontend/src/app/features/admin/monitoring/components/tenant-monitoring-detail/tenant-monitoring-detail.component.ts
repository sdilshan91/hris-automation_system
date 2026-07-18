import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { PlatformMonitoringService } from '../../services/platform-monitoring.service';
import { AuthService } from '../../../../../core/auth/auth.service';
import {
  ITenantMonitoringDetail,
  bandClass,
  employeeGaugeLabel,
} from '../../models/monitoring.models';
import { ImpersonateDialogComponent } from '../../../impersonation/components/impersonate-dialog/impersonate-dialog.component';
import { IStartImpersonationResponse } from '../../../impersonation/models/impersonation.models';
import { TenantLifecycleService } from '../../../lifecycle/services/tenant-lifecycle.service';
import { SuspendTenantDialogComponent } from '../../../lifecycle/components/suspend-tenant-dialog/suspend-tenant-dialog.component';
import { TerminateTenantDialogComponent } from '../../../lifecycle/components/terminate-tenant-dialog/terminate-tenant-dialog.component';
import {
  LifecycleConfirmDialogComponent,
  LifecycleConfirmAction,
} from '../../../lifecycle/components/lifecycle-confirm-dialog/lifecycle-confirm-dialog.component';
import { LifecycleHistoryTimelineComponent } from '../../../lifecycle/components/lifecycle-history-timeline/lifecycle-history-timeline.component';
import { PlanOverridesSectionComponent } from '../../../plans/components/plan-overrides-section/plan-overrides-section.component';
import { SystemExportDialogComponent } from '../../../data-export/components/system-export-dialog/system-export-dialog.component';
import {
  ITenantLifecycleEvent,
  ITenantLifecycleResult,
  canReactivate,
  canRestore,
  canSuspend,
  canTerminate,
  lifecycleStatusClass,
} from '../../../lifecycle/models/lifecycle.models';

/**
 * US-ADM-002 AC-4: System Admin tenant monitoring detail.
 *
 * Shows a single tenant's status, plan, owner email, created date, last
 * activity, employee gauge, and Hangfire job status. Trend charts and SLA
 * uptime are rendered as honest "Not available" placeholders when the BE
 * returns null (no observability pipeline yet).
 *
 * US-ADM-004: lifecycle quick-actions (Suspend / Terminate / Reactivate /
 * Restore) are now wired here, gated by the tenant's current status. Suspend/
 * Terminate/Reactivate/Restore are System-Admin only (BR-7) — System Support is
 * read-only. A color-coded status badge plus a lifecycle history timeline (GET
 * .../lifecycle/history) render below. The Impersonate (US-ADM-003) and View
 * Audit Log buttons are unchanged.
 */
@Component({
  selector: 'app-tenant-monitoring-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    TranslateModule,
    ImpersonateDialogComponent,
    SuspendTenantDialogComponent,
    TerminateTenantDialogComponent,
    LifecycleConfirmDialogComponent,
    LifecycleHistoryTimelineComponent,
    PlanOverridesSectionComponent,
    SystemExportDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  templateUrl: './tenant-monitoring-detail.component.html',
})
export class TenantMonitoringDetailComponent implements OnInit {
  private readonly service = inject(PlatformMonitoringService);
  private readonly lifecycle = inject(TenantLifecycleService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly detail = signal<ITenantMonitoringDetail | null>(null);
  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** US-ADM-003 AC-1: whether the impersonation confirmation modal is open. */
  readonly impersonateOpen = signal(false);

  /** US-ADM-004: which lifecycle modal (if any) is open. */
  readonly suspendOpen = signal(false);
  readonly terminateOpen = signal(false);
  /** Non-destructive confirm modal — null when closed, else the action. */
  readonly confirmAction = signal<LifecycleConfirmAction | null>(null);

  /** US-ADM-010 AC-6: whether the System-Admin "Export Data" dialog is open. */
  readonly exportOpen = signal(false);

  /** US-ADM-004: lifecycle history timeline state. */
  readonly history = signal<ITenantLifecycleEvent[]>([]);
  readonly historyLoading = signal(true);

  readonly bandClass = bandClass;
  readonly employeeGaugeLabel = employeeGaugeLabel;
  readonly lifecycleStatusClass = lifecycleStatusClass;

  /**
   * BR-7: privileged quick actions (Suspend / Terminate / Reactivate / Restore /
   * Impersonate) are visible only to System Admin, not System Support (read-only).
   */
  readonly canManage = computed(() => this.auth.hasRole('System Admin'));

  // Status-gated lifecycle action availability (BR-1).
  readonly showSuspend = computed(() => {
    const d = this.detail();
    return !!d && this.canManage() && canSuspend(d.status);
  });
  readonly showTerminate = computed(() => {
    const d = this.detail();
    return !!d && this.canManage() && canTerminate(d.status);
  });
  readonly showReactivate = computed(() => {
    const d = this.detail();
    return !!d && this.canManage() && canReactivate(d.status);
  });
  readonly showRestore = computed(() => {
    const d = this.detail();
    return !!d && this.canManage() && canRestore(d.status);
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.errorMessage.set('No tenant specified.');
      this.isLoading.set(false);
      return;
    }
    this.load(id);
  }

  load(id: string): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.service.getTenantDetail(id).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load tenant detail.');
        this.isLoading.set(false);
      },
    });
    this.loadHistory(id);
  }

  /** US-ADM-004: load the lifecycle history timeline. */
  loadHistory(id: string): void {
    this.historyLoading.set(true);
    this.lifecycle.getHistory(id).subscribe({
      next: (events) => {
        this.history.set(events);
        this.historyLoading.set(false);
      },
      error: () => {
        // History is supplementary; fail quietly to an empty timeline.
        this.history.set([]);
        this.historyLoading.set(false);
      },
    });
  }

  // ─── Impersonation (US-ADM-003) ─────────────────────────────

  /** AC-1: open the impersonation confirmation modal for this tenant. */
  openImpersonate(): void {
    this.impersonateOpen.set(true);
  }

  closeImpersonate(): void {
    this.impersonateOpen.set(false);
  }

  /**
   * AC-1: the modal already activated the impersonation token via AuthService.
   * Cross-subdomain new-tab hand-off is a production concern (an in-memory token
   * cannot cross origins), so in this single-SPA model we navigate into the app
   * — the global banner then renders from the active impersonation token.
   */
  onImpersonationStarted(_res: IStartImpersonationResponse): void {
    this.impersonateOpen.set(false);
    this.router.navigate(['/dashboard']);
  }

  // ─── Lifecycle (US-ADM-004) ─────────────────────────────────

  openSuspend(): void {
    this.suspendOpen.set(true);
  }

  openTerminate(): void {
    this.terminateOpen.set(true);
  }

  openReactivate(): void {
    this.confirmAction.set('reactivate');
  }

  openRestore(): void {
    this.confirmAction.set('restore');
  }

  closeLifecycle(): void {
    this.suspendOpen.set(false);
    this.terminateOpen.set(false);
    this.confirmAction.set(null);
  }

  /**
   * A lifecycle transition succeeded — close the modal, reflect the new status
   * locally, and refresh the detail + history from the backend.
   */
  onLifecycleResult(result: ITenantLifecycleResult): void {
    this.closeLifecycle();
    const current = this.detail();
    if (current) {
      this.detail.set({ ...current, status: result.status });
    }
    this.load(result.tenantId);
  }

  // ─── Data export (US-ADM-010 AC-6) ──────────────────────────

  /** Open the System-Admin "Export Data" dialog for this tenant. */
  openExport(): void {
    this.exportOpen.set(true);
  }

  closeExport(): void {
    this.exportOpen.set(false);
  }

  /** Status badge colour (color-coded — AC / UI notes). */
  statusClass(status: string): string {
    return lifecycleStatusClass(status);
  }
}
