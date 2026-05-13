import {
  Component,
  ChangeDetectionStrategy,
  inject,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-container">
      <div class="mb-8">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
          Welcome back, {{ authService.currentUser()?.displayName || 'User' }}
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          Here's an overview of your workspace.
        </p>
      </div>

      <!-- Placeholder cards -->
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        @for (card of summaryCards; track card.title) {
          <div class="card-notion">
            <div class="flex items-center gap-3 mb-3">
              <div
                class="w-9 h-9 rounded-lg flex items-center justify-center"
                [style.background-color]="card.bgColor"
              >
                <span [style.color]="card.iconColor" [innerHTML]="card.icon"></span>
              </div>
              <span class="text-sm font-medium text-neutral-500">{{ card.title }}</span>
            </div>
            <div class="text-2xl font-semibold text-neutral-900">{{ card.value }}</div>
            <div class="mt-1 text-xs text-neutral-400">{{ card.subtitle }}</div>
          </div>
        }
      </div>

      <div class="mt-8 card-notion">
        <h2 class="text-lg font-semibold text-neutral-900 mb-2">Getting Started</h2>
        <p class="text-sm text-neutral-500">
          This is the dashboard placeholder. Feature modules (employees, leave, attendance, payroll, etc.)
          will be added as they are implemented.
        </p>
      </div>
    </div>
  `,
})
export class DashboardComponent {
  readonly authService = inject(AuthService);

  readonly summaryCards = [
    {
      title: 'Total Employees',
      value: '--',
      subtitle: 'Across all departments',
      icon: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5"><path d="M7 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM14.5 9a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM1.615 16.428a1.224 1.224 0 0 1-.569-1.175 6.002 6.002 0 0 1 11.908 0c.058.467-.172.92-.57 1.174A9.953 9.953 0 0 1 7 18a9.953 9.953 0 0 1-5.385-1.572ZM14.5 16h-.106c.07-.297.088-.611.048-.933a7.47 7.47 0 0 0-1.588-3.755 4.502 4.502 0 0 1 5.874 2.636.818.818 0 0 1-.36.98A7.465 7.465 0 0 1 14.5 16Z"/></svg>',
      bgColor: '#eff6ff',
      iconColor: '#2563eb',
    },
    {
      title: 'Pending Leaves',
      value: '--',
      subtitle: 'Awaiting approval',
      icon: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5"><path fill-rule="evenodd" d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2Zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75Z" clip-rule="evenodd"/></svg>',
      bgColor: '#fef3c7',
      iconColor: '#d97706',
    },
    {
      title: 'Present Today',
      value: '--',
      subtitle: 'Checked in',
      icon: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5"><path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-13a.75.75 0 0 0-1.5 0v5c0 .414.336.75.75.75h4a.75.75 0 0 0 0-1.5h-3.25V5Z" clip-rule="evenodd"/></svg>',
      bgColor: '#ecfdf5',
      iconColor: '#059669',
    },
    {
      title: 'Open Positions',
      value: '--',
      subtitle: 'Active recruitment',
      icon: '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5"><path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"/></svg>',
      bgColor: '#faf5ff',
      iconColor: '#7c3aed',
    },
  ];
}
