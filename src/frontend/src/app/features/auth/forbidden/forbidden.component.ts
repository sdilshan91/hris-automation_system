import { Component, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-forbidden',
  standalone: true,
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="forbidden-page">
      <div class="forbidden-card">
        <div class="icon-wrapper">
          <svg
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
            class="w-10 h-10 text-amber-500"
          >
            <path
              stroke-linecap="round"
              stroke-linejoin="round"
              d="M16.5 10.5V6.75a4.5 4.5 0 1 0-9 0v3.75m-.75 11.25h10.5a2.25 2.25 0 0 0 2.25-2.25v-6.75a2.25 2.25 0 0 0-2.25-2.25H6.75a2.25 2.25 0 0 0-2.25 2.25v6.75a2.25 2.25 0 0 0 2.25 2.25Z"
            />
          </svg>
        </div>
        <h1 class="text-xl font-semibold text-neutral-900 mt-4">
          Access Denied
        </h1>
        <p class="text-sm text-neutral-500 mt-2 max-w-sm mx-auto">
          You don't have permission to access this page. If you think this is a
          mistake, please contact your administrator.
        </p>
        <a
          routerLink="/dashboard"
          class="btn-primary mt-6 no-underline"
        >
          Go to Dashboard
        </a>
      </div>
    </div>
  `,
  styles: [`
    .forbidden-page {
      @apply min-h-screen flex items-center justify-center bg-surface-secondary px-4;
    }

    .forbidden-card {
      @apply text-center;
    }

    .icon-wrapper {
      @apply w-16 h-16 rounded-full bg-amber-50 flex items-center justify-center mx-auto;
    }
  `],
})
export class ForbiddenComponent {}
