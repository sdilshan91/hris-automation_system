import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { TenantProvisioningService } from '../../services/tenant-provisioning.service';
import { ITenantSummary } from '../../models/tenant.models';

/**
 * US-ADM-001 AC-4: System Admin Console tenant list. Shows each provisioned
 * tenant's name, subdomain, status badge, plan and creation timestamp.
 */
@Component({
  selector: 'app-tenant-list',
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
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">Tenants</h1>
          <p class="mt-1 text-sm text-neutral-500">
            Customer workspaces provisioned on the platform.
          </p>
        </div>
        <a routerLink="create" class="btn-primary">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
          </svg>
          Create Tenant
        </a>
      </div>

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion animate-pulse space-y-3">
          @for (i of [1, 2, 3, 4, 5]; track i) {
            <div class="h-10 bg-neutral-50 rounded"></div>
          }
        </div>
      }

      <!-- Error -->
      @if (!isLoading() && errorMessage()) {
        <div class="card-notion text-center py-12">
          <p class="text-sm text-red-500">{{ errorMessage() }}</p>
          <button type="button" class="btn-secondary mt-4" (click)="load()">Retry</button>
        </div>
      }

      <!-- Empty -->
      @if (!isLoading() && !errorMessage() && tenants().length === 0) {
        <div class="card-notion text-center py-12">
          <p class="text-sm text-neutral-500">No tenants have been provisioned yet.</p>
          <a routerLink="create" class="btn-primary mt-4 inline-flex">Create your first tenant</a>
        </div>
      }

      <!-- Table -->
      @if (!isLoading() && !errorMessage() && tenants().length > 0) {
        <div @fadeSlideIn class="card-notion !p-0 overflow-hidden">
          <div class="overflow-x-auto">
            <table class="w-full text-sm" aria-label="Tenants">
              <thead>
                <tr class="border-b border-neutral-100 text-left text-xs uppercase tracking-wider text-neutral-400">
                  <th scope="col" class="px-5 py-3 font-medium">Name</th>
                  <th scope="col" class="px-5 py-3 font-medium">Subdomain</th>
                  <th scope="col" class="px-5 py-3 font-medium">Status</th>
                  <th scope="col" class="px-5 py-3 font-medium">Plan</th>
                  <th scope="col" class="px-5 py-3 font-medium">Created</th>
                </tr>
              </thead>
              <tbody>
                @for (t of tenants(); track t.tenantId) {
                  <tr class="border-b border-neutral-50 last:border-0 hover:bg-neutral-50 transition-colors">
                    <td class="px-5 py-3.5 font-medium text-neutral-900">{{ t.name }}</td>
                    <td class="px-5 py-3.5 text-neutral-600 font-mono text-xs">
                      {{ t.subdomain }}.yourhrm.com
                    </td>
                    <td class="px-5 py-3.5">
                      <span class="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium" [class]="statusClass(t.status)">
                        {{ t.status }}
                      </span>
                    </td>
                    <td class="px-5 py-3.5 text-neutral-600">{{ t.plan }}</td>
                    <td class="px-5 py-3.5 text-neutral-500">{{ t.createdAt | date: 'medium' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }
    </div>
  `,
})
export class TenantListComponent implements OnInit {
  private readonly service = inject(TenantProvisioningService);

  readonly isLoading = signal(true);
  readonly errorMessage = signal<string | null>(null);
  readonly tenants = signal<ITenantSummary[]>([]);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.service.getTenants().subscribe({
      next: (tenants) => {
        this.tenants.set(tenants);
        this.isLoading.set(false);
      },
      error: () => {
        this.errorMessage.set('Failed to load tenants.');
        this.isLoading.set(false);
      },
    });
  }

  /** Status badge colour (trial / active / suspended / others). */
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
