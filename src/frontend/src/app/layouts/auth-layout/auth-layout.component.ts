import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TenantService } from '../../core/tenant/tenant.service';

@Component({
  selector: 'app-auth-layout',
  standalone: true,
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="auth-layout">
      <!-- Decorative background -->
      <div class="auth-bg">
        <div class="auth-bg-gradient"></div>
        <div class="auth-bg-grid"></div>
      </div>

      <!-- Content -->
      <div class="auth-content">
        <!-- Logo / brand -->
        <div class="auth-brand">
          @if (tenant().logoUrl) {
            <img
              [src]="tenant().logoUrl"
              [alt]="tenantName()"
              class="auth-logo"
            />
          } @else {
            <div class="auth-logo-placeholder">
              <svg
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 24 24"
                fill="none"
                stroke="currentColor"
                stroke-width="1.5"
                class="w-8 h-8"
              >
                <path
                  stroke-linecap="round"
                  stroke-linejoin="round"
                  d="M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z"
                />
              </svg>
            </div>
          }
          <h1 class="auth-title">{{ tenantName() }}</h1>
        </div>

        <!-- Router outlet for auth pages -->
        <router-outlet />

        <!-- Footer -->
        <div class="auth-footer">
          <p>&copy; {{ currentYear }} YourHRM. All rights reserved.</p>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .auth-layout {
      @apply relative min-h-screen flex items-center justify-center px-4 py-8;
      background-color: #fafafa;
    }

    .auth-bg {
      @apply fixed inset-0 -z-10 overflow-hidden;
    }

    .auth-bg-gradient {
      @apply absolute inset-0;
      background: radial-gradient(
        ellipse at top,
        rgba(12, 142, 233, 0.05) 0%,
        transparent 60%
      );
    }

    .auth-bg-grid {
      @apply absolute inset-0;
      background-image: linear-gradient(
        rgba(0, 0, 0, 0.02) 1px,
        transparent 1px
      ),
      linear-gradient(90deg, rgba(0, 0, 0, 0.02) 1px, transparent 1px);
      background-size: 40px 40px;
    }

    .auth-content {
      @apply w-full max-w-md flex flex-col items-center;
    }

    .auth-brand {
      @apply flex flex-col items-center mb-8;
    }

    .auth-logo {
      @apply h-10 w-auto mb-3;
    }

    .auth-logo-placeholder {
      @apply w-12 h-12 rounded-xl bg-brand-600 text-white
        flex items-center justify-center mb-3 shadow-sm;
    }

    .auth-title {
      @apply text-xl font-semibold text-neutral-900 tracking-tight;
    }

    .auth-footer {
      @apply mt-8 text-center;

      p {
        @apply text-xs text-neutral-400;
      }
    }
  `],
})
export class AuthLayoutComponent {
  private readonly tenantService = inject(TenantService);

  readonly tenant = this.tenantService.tenantContext;
  readonly currentYear = new Date().getFullYear();

  tenantName(): string {
    return this.tenantService.displayName();
  }
}
