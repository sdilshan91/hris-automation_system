import {
  Component,
  ChangeDetectionStrategy,
  inject,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TenantService } from '../../../../../core/tenant/tenant.service';

/**
 * US-REC-002: Tenant-branded header for the public careers experience (§8).
 *
 * Shows the tenant logo (when available) + workspace name. The brand primary
 * color is already applied to the `--brand-primary` CSS variable by TenantService
 * on resolve, so accents elsewhere inherit it automatically. Anonymous-safe — it
 * only reads the resolved tenant context signal, never requires a session.
 */
@Component({
  selector: 'app-careers-branding',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="border-b border-neutral-200 bg-white">
      <div
        class="mx-auto flex max-w-5xl items-center gap-3 px-4 py-4 sm:px-6"
      >
        @if (logoUrl(); as logo) {
          <img
            [src]="logo"
            [alt]="name() + ' logo'"
            class="h-8 w-auto max-w-[160px] object-contain"
          />
        } @else {
          <div
            class="flex h-8 w-8 items-center justify-center rounded-lg text-sm font-semibold text-white"
            [style.background-color]="'var(--brand-primary)'"
            aria-hidden="true"
          >
            {{ initial() }}
          </div>
        }
        <div class="min-w-0">
          <p class="truncate text-sm font-semibold text-neutral-900">
            {{ name() }}
          </p>
          <p class="text-xs text-neutral-400">Careers</p>
        </div>
      </div>
    </header>
  `,
  styles: [
    `
      :host {
        display: block;
      }
    `,
  ],
})
export class CareersBrandingComponent {
  private readonly tenant = inject(TenantService);

  readonly name = this.tenant.displayName;
  readonly logoUrl = computed(() => this.tenant.tenantContext().logoUrl ?? null);
  readonly initial = computed(
    () => (this.name()?.trim()?.[0] ?? 'C').toUpperCase(),
  );
}
