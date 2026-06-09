import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { TenantService } from '../../core/tenant/tenant.service';

@Component({
  selector: 'app-workspace-not-found',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <main class="workspace-page">
      <section class="workspace-panel" aria-labelledby="workspace-title">
        <div class="workspace-mark">Y</div>
        <p class="workspace-eyebrow">{{ attemptedWorkspace() }}</p>
        <h1 id="workspace-title">This workspace does not exist</h1>
        <p class="workspace-copy">
          Check the address in your browser or contact your HR administrator.
        </p>
        <a class="workspace-link" href="https://yourhrm.com">Go to YourHRM</a>
      </section>
    </main>
  `,
  styles: [`
    .workspace-page {
      @apply min-h-screen px-4 py-8 flex items-center justify-center bg-surface-secondary;
    }

    .workspace-panel {
      @apply w-full max-w-md rounded-xl border border-neutral-100 bg-white p-8 text-center shadow-notion;
    }

    .workspace-mark {
      @apply mx-auto mb-5 flex h-12 w-12 items-center justify-center rounded-xl bg-brand-600 text-lg font-semibold text-white;
    }

    .workspace-eyebrow {
      @apply mb-2 text-xs font-medium uppercase tracking-wide text-neutral-400;
    }

    h1 {
      @apply text-2xl font-semibold tracking-tight text-neutral-900;
    }

    .workspace-copy {
      @apply mt-3 text-sm text-neutral-500;
    }

    .workspace-link {
      @apply mt-6 inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2.5 text-sm font-medium text-white no-underline transition-colors hover:bg-brand-700;
    }
  `],
})
export class WorkspaceNotFoundComponent {
  private readonly tenantService = inject(TenantService);

  attemptedWorkspace(): string {
    const subdomain = this.tenantService.subdomain();
    return subdomain ? `${subdomain}.yourhrm.com` : 'Workspace unavailable';
  }
}

