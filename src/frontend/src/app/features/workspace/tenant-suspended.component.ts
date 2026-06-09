import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TenantService } from '../../core/tenant/tenant.service';

@Component({
  selector: 'app-tenant-suspended',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="suspended-page">
      <section class="suspended-panel" aria-labelledby="suspended-title">
        @if (tenant().logoUrl) {
          <img class="tenant-logo" [src]="tenant().logoUrl" [alt]="tenantName()" />
        } @else {
          <div class="tenant-mark">{{ tenantInitial() }}</div>
        }

        <p class="suspended-eyebrow">{{ tenantName() }}</p>
        <h1 id="suspended-title">Workspace suspended</h1>
        <p class="suspended-copy">
          {{ suspensionMessage() }}
        </p>
        <a class="support-link" href="mailto:support@yourhrm.com">Contact support</a>
      </section>
    </main>
  `,
  styles: [`
    .suspended-page {
      @apply min-h-screen px-4 py-8 flex items-center justify-center bg-surface-secondary;
    }

    .suspended-panel {
      @apply w-full max-w-md rounded-xl border border-neutral-100 bg-white p-8 text-center shadow-notion;
    }

    .tenant-logo {
      @apply mx-auto mb-5 max-h-12 max-w-40 object-contain;
    }

    .tenant-mark {
      @apply mx-auto mb-5 flex h-12 w-12 items-center justify-center rounded-xl bg-brand-600 text-lg font-semibold text-white;
    }

    .suspended-eyebrow {
      @apply mb-2 text-sm font-medium text-neutral-500;
    }

    h1 {
      @apply text-2xl font-semibold tracking-tight text-neutral-900;
    }

    .suspended-copy {
      @apply mt-3 text-sm text-neutral-500;
    }

    .support-link {
      @apply mt-6 inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white no-underline transition-colors hover:bg-brand-700;
    }
  `],
})
export class TenantSuspendedComponent {
  private readonly tenantService = inject(TenantService);

  readonly tenant = this.tenantService.tenantContext;

  tenantName(): string {
    return this.tenantService.displayName();
  }

  tenantInitial(): string {
    return this.tenantName().trim().charAt(0).toUpperCase() || 'Y';
  }

  suspensionMessage(): string {
    return (
      this.tenant().suspensionReason ||
      'Login is currently unavailable for this workspace.'
    );
  }
}

