import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { PlatformMonitoringService } from '../../services/platform-monitoring.service';
import { AuthService } from '../../../../../core/auth/auth.service';
import {
  ITenantMonitoringDetail,
  bandClass,
} from '../../models/monitoring.models';

/**
 * US-ADM-002 AC-4: System Admin tenant monitoring detail.
 *
 * Shows a single tenant's status, plan, owner email, created date, last
 * activity, employee gauge, and Hangfire job status. Trend charts and SLA
 * uptime are rendered as honest "Not available" placeholders when the BE
 * returns null (no observability pipeline yet).
 *
 * Quick actions (Impersonate / Suspend / View Audit Log) target US-ADM-003/004/008
 * which are not built — they render DISABLED with an "Available in a later
 * release" tooltip and are NOT wired to any route. Suspend/Impersonate are
 * additionally gated so they only appear for System Admin, never System Support
 * (BR-1).
 */
@Component({
  selector: 'app-tenant-monitoring-detail',
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
  templateUrl: './tenant-monitoring-detail.component.html',
})
export class TenantMonitoringDetailComponent implements OnInit {
  private readonly service = inject(PlatformMonitoringService);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  readonly detail = signal<ITenantMonitoringDetail | null>(null);
  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  readonly bandClass = bandClass;

  /**
   * BR-1: privileged quick actions (Suspend / Impersonate) are visible only to
   * System Admin, not System Support (read-only). Read straight from the auth
   * service's role claim.
   */
  readonly canManage = computed(() => this.auth.hasRole('System Admin'));

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
  }

  /** Status badge colour. */
  statusClass(status: string): string {
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
