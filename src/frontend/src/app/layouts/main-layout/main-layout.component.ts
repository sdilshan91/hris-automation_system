import {
  Component,
  ChangeDetectionStrategy,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { EMPTY } from 'rxjs';
import { catchError, finalize } from 'rxjs/operators';
import { AuthService } from '../../core/auth/auth.service';
import { IUserTenant } from '../../core/auth/auth.models';
import { TenantService } from '../../core/tenant/tenant.service';
import { IdleTimeoutService } from '../../core/services/idle-timeout.service';
import { IdleTimeoutWarningComponent } from '../../shared/components/idle-timeout-warning/idle-timeout-warning.component';

interface INavItem {
  label: string;
  icon: string;
  route: string;
  permission?: string;
}

@Component({
  selector: 'app-main-layout',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, IdleTimeoutWarningComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="main-layout" [class.sidebar-collapsed]="sidebarCollapsed()">
      <!-- Mobile overlay -->
      @if (mobileMenuOpen()) {
        <div
          class="mobile-overlay"
          (click)="mobileMenuOpen.set(false)"
        ></div>
      }

      <!-- Sidebar -->
      <aside
        class="sidebar"
        [class.sidebar-open]="mobileMenuOpen()"
        role="navigation"
        aria-label="Main navigation"
      >
        <!-- Sidebar header -->
        <div class="sidebar-header">
          <div class="tenant-switcher" [class.tenant-switcher-collapsed]="sidebarCollapsed()">
            <button
              type="button"
              class="tenant-trigger"
              [class.icon-only]="sidebarCollapsed()"
              [attr.aria-expanded]="tenantMenuOpen()"
              aria-haspopup="menu"
              aria-label="Switch organization"
              (click)="toggleTenantMenu()"
            >
              <span class="tenant-logo">
                @if (currentTenantLogo()) {
                  <img [src]="currentTenantLogo()" [alt]="tenantName()" />
                } @else {
                  <span>{{ tenantInitial() }}</span>
                }
              </span>
              @if (!sidebarCollapsed()) {
                <span class="tenant-trigger-copy">
                  <span class="tenant-name">{{ tenantName() }}</span>
                  <span class="tenant-role">{{ currentPrimaryRole() }}</span>
                </span>
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  viewBox="0 0 20 20"
                  fill="currentColor"
                  class="tenant-chevron"
                  [class.rotate-180]="tenantMenuOpen()"
                >
                  <path
                    fill-rule="evenodd"
                    d="M5.22 8.22a.75.75 0 0 1 1.06 0L10 11.94l3.72-3.72a.75.75 0 1 1 1.06 1.06l-4.25 4.25a.75.75 0 0 1-1.06 0L5.22 9.28a.75.75 0 0 1 0-1.06Z"
                    clip-rule="evenodd"
                  />
                </svg>
              }
            </button>

            @if (tenantMenuOpen()) {
              <div class="tenant-menu" role="menu" aria-label="Organizations">
                <div class="tenant-menu-header">
                  <span>Organizations</span>
                  @if (tenantsLoading()) {
                    <span class="tenant-loading">Loading</span>
                  }
                </div>
                @if (tenantError()) {
                  <p class="tenant-error">{{ tenantError() }}</p>
                }
                @for (tenant of tenants(); track tenant.tenantId) {
                  <button
                    type="button"
                    class="tenant-option"
                    role="menuitemradio"
                    [class.current]="tenant.isCurrentTenant"
                    [class.unavailable]="!isTenantSwitchable(tenant)"
                    [disabled]="!isTenantSwitchable(tenant) || switchingTenantId() === tenant.tenantId"
                    [attr.aria-checked]="tenant.isCurrentTenant"
                    [attr.aria-disabled]="!isTenantSwitchable(tenant)"
                    [title]="tenantUnavailableMessage(tenant)"
                    (click)="switchTenant(tenant)"
                  >
                    <span class="tenant-logo option-logo">
                      @if (tenant.logoUrl) {
                        <img [src]="tenant.logoUrl" [alt]="tenant.name" />
                      } @else {
                        <span>{{ tenantInitial(tenant.name) }}</span>
                      }
                    </span>
                    <span class="tenant-option-copy">
                      <span class="tenant-option-name">{{ tenant.name }}</span>
                      <span class="tenant-option-role">{{ primaryRole(tenant) }}</span>
                    </span>
                    @if (tenant.status !== 'active') {
                      <span class="tenant-status">{{ statusLabel(tenant.status) }}</span>
                    }
                    @if (tenant.isCurrentTenant) {
                      <svg
                        xmlns="http://www.w3.org/2000/svg"
                        viewBox="0 0 20 20"
                        fill="currentColor"
                        class="tenant-check"
                        aria-hidden="true"
                      >
                        <path
                          fill-rule="evenodd"
                          d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z"
                          clip-rule="evenodd"
                        />
                      </svg>
                    }
                  </button>
                } @empty {
                  @if (!tenantsLoading()) {
                    <p class="tenant-empty">No organization memberships found.</p>
                  }
                }
              </div>
            }
          </div>

          <!-- Collapse toggle (desktop) -->
          <button
            class="collapse-btn hidden lg:flex"
            (click)="toggleSidebar()"
            [attr.aria-label]="sidebarCollapsed() ? 'Expand sidebar' : 'Collapse sidebar'"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="w-4 h-4 transition-transform"
              [class.rotate-180]="sidebarCollapsed()"
            >
              <path
                fill-rule="evenodd"
                d="M11.78 5.22a.75.75 0 0 1 0 1.06L8.06 10l3.72 3.72a.75.75 0 1 1-1.06 1.06l-4.25-4.25a.75.75 0 0 1 0-1.06l4.25-4.25a.75.75 0 0 1 1.06 0Z"
                clip-rule="evenodd"
              />
            </svg>
          </button>
        </div>

        <!-- Navigation items -->
        <nav class="sidebar-nav">
          @for (item of navItems; track item.route) {
            @if (!item.permission || authService.hasPermission(item.permission)) {
              <a
                [routerLink]="item.route"
                routerLinkActive="nav-active"
                class="nav-item"
                [title]="sidebarCollapsed() ? item.label : ''"
                (click)="mobileMenuOpen.set(false)"
              >
                <span class="nav-icon" [innerHTML]="item.icon"></span>
                @if (!sidebarCollapsed()) {
                  <span class="nav-label">{{ item.label }}</span>
                }
              </a>
            }
          }
        </nav>

        <!-- Sidebar footer -->
        <div class="sidebar-footer">
          <div class="user-menu">
            <div class="user-avatar">
              {{ userInitials() }}
            </div>
            @if (!sidebarCollapsed()) {
              <div class="user-info">
                <span class="user-name">
                  {{ authService.currentUser()?.displayName }}
                </span>
                <span class="user-email">
                  {{ authService.currentUser()?.email }}
                </span>
              </div>
              <button
                class="logout-btn"
                (click)="authService.logout()"
                aria-label="Log out"
                title="Log out"
              >
                <svg
                  xmlns="http://www.w3.org/2000/svg"
                  viewBox="0 0 20 20"
                  fill="currentColor"
                  class="w-4 h-4"
                >
                  <path
                    fill-rule="evenodd"
                    d="M3 4.25A2.25 2.25 0 0 1 5.25 2h5.5A2.25 2.25 0 0 1 13 4.25v2a.75.75 0 0 1-1.5 0v-2a.75.75 0 0 0-.75-.75h-5.5a.75.75 0 0 0-.75.75v11.5c0 .414.336.75.75.75h5.5a.75.75 0 0 0 .75-.75v-2a.75.75 0 0 1 1.5 0v2A2.25 2.25 0 0 1 10.75 18h-5.5A2.25 2.25 0 0 1 3 15.75V4.25Z"
                    clip-rule="evenodd"
                  />
                  <path
                    fill-rule="evenodd"
                    d="M19 10a.75.75 0 0 0-.75-.75H8.704l1.048-.943a.75.75 0 1 0-1.004-1.114l-2.5 2.25a.75.75 0 0 0 0 1.114l2.5 2.25a.75.75 0 1 0 1.004-1.114l-1.048-.943h9.546A.75.75 0 0 0 19 10Z"
                    clip-rule="evenodd"
                  />
                </svg>
              </button>
            }
          </div>
        </div>
      </aside>

      <!-- Main content area -->
      <div class="main-content">
        <!-- Top bar -->
        <header class="topbar">
          <!-- Mobile menu toggle -->
          <button
            class="mobile-menu-btn lg:hidden"
            (click)="mobileMenuOpen.set(true)"
            aria-label="Open navigation menu"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="w-5 h-5"
            >
              <path
                fill-rule="evenodd"
                d="M2 4.75A.75.75 0 0 1 2.75 4h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 4.75Zm0 10.5a.75.75 0 0 1 .75-.75h7.5a.75.75 0 0 1 0 1.5h-7.5a.75.75 0 0 1-.75-.75ZM2 10a.75.75 0 0 1 .75-.75h14.5a.75.75 0 0 1 0 1.5H2.75A.75.75 0 0 1 2 10Z"
                clip-rule="evenodd"
              />
            </svg>
          </button>

          <button
            type="button"
            class="mobile-tenant-trigger lg:hidden"
            (click)="toggleTenantMenu()"
            [attr.aria-expanded]="tenantMenuOpen()"
            aria-haspopup="menu"
            aria-label="Switch organization"
          >
            <span class="tenant-logo">
              @if (currentTenantLogo()) {
                <img [src]="currentTenantLogo()" [alt]="tenantName()" />
              } @else {
                <span>{{ tenantInitial() }}</span>
              }
            </span>
            <span class="mobile-tenant-name">{{ tenantName() }}</span>
          </button>

          @if (tenantMenuOpen()) {
            <div class="tenant-menu mobile-tenant-menu" role="menu" aria-label="Organizations">
              <div class="tenant-menu-header">
                <span>Organizations</span>
                @if (tenantsLoading()) {
                  <span class="tenant-loading">Loading</span>
                }
              </div>
              @if (tenantError()) {
                <p class="tenant-error">{{ tenantError() }}</p>
              }
              @for (tenant of tenants(); track tenant.tenantId) {
                <button
                  type="button"
                  class="tenant-option"
                  role="menuitemradio"
                  [class.current]="tenant.isCurrentTenant"
                  [class.unavailable]="!isTenantSwitchable(tenant)"
                  [disabled]="!isTenantSwitchable(tenant) || switchingTenantId() === tenant.tenantId"
                  [attr.aria-checked]="tenant.isCurrentTenant"
                  [attr.aria-disabled]="!isTenantSwitchable(tenant)"
                  [title]="tenantUnavailableMessage(tenant)"
                  (click)="switchTenant(tenant)"
                >
                  <span class="tenant-logo option-logo">
                    @if (tenant.logoUrl) {
                      <img [src]="tenant.logoUrl" [alt]="tenant.name" />
                    } @else {
                      <span>{{ tenantInitial(tenant.name) }}</span>
                    }
                  </span>
                  <span class="tenant-option-copy">
                    <span class="tenant-option-name">{{ tenant.name }}</span>
                    <span class="tenant-option-role">{{ primaryRole(tenant) }}</span>
                  </span>
                  @if (tenant.status !== 'active') {
                    <span class="tenant-status">{{ statusLabel(tenant.status) }}</span>
                  }
                  @if (tenant.isCurrentTenant) {
                    <svg
                      xmlns="http://www.w3.org/2000/svg"
                      viewBox="0 0 20 20"
                      fill="currentColor"
                      class="tenant-check"
                      aria-hidden="true"
                    >
                      <path
                        fill-rule="evenodd"
                        d="M16.704 4.153a.75.75 0 0 1 .143 1.052l-8 10.5a.75.75 0 0 1-1.127.075l-4.5-4.5a.75.75 0 0 1 1.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 0 1 1.05-.143Z"
                        clip-rule="evenodd"
                      />
                    </svg>
                  }
                </button>
              } @empty {
                @if (!tenantsLoading()) {
                  <p class="tenant-empty">No organization memberships found.</p>
                }
              }
            </div>
          }

          <div class="topbar-spacer"></div>

          <!-- Top bar right actions -->
          <div class="topbar-actions">
            <!-- Mobile logout -->
            <button
              class="logout-btn-mobile lg:hidden"
              (click)="authService.logout()"
              aria-label="Log out"
            >
              <svg
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 20 20"
                fill="currentColor"
                class="w-5 h-5"
              >
                <path
                  fill-rule="evenodd"
                  d="M3 4.25A2.25 2.25 0 0 1 5.25 2h5.5A2.25 2.25 0 0 1 13 4.25v2a.75.75 0 0 1-1.5 0v-2a.75.75 0 0 0-.75-.75h-5.5a.75.75 0 0 0-.75.75v11.5c0 .414.336.75.75.75h5.5a.75.75 0 0 0 .75-.75v-2a.75.75 0 0 1 1.5 0v2A2.25 2.25 0 0 1 10.75 18h-5.5A2.25 2.25 0 0 1 3 15.75V4.25Z"
                  clip-rule="evenodd"
                />
                <path
                  fill-rule="evenodd"
                  d="M19 10a.75.75 0 0 0-.75-.75H8.704l1.048-.943a.75.75 0 1 0-1.004-1.114l-2.5 2.25a.75.75 0 0 0 0 1.114l2.5 2.25a.75.75 0 1 0 1.004-1.114l-1.048-.943h9.546A.75.75 0 0 0 19 10Z"
                  clip-rule="evenodd"
                />
              </svg>
            </button>
          </div>
        </header>

        <!-- Page content -->
        <main class="page-content">
          <router-outlet />
        </main>
      </div>

      <!-- Idle timeout warning modal (US-AUTH-009 BR-6) -->
      <app-idle-timeout-warning />
    </div>
  `,
  styles: [`
    .main-layout {
      @apply flex min-h-screen bg-surface-secondary;
    }

    /* ─── Sidebar ──────────────────────────────── */

    .sidebar {
      @apply fixed top-0 left-0 z-40 flex h-full w-64 flex-col
        border-r border-neutral-100 bg-white transition-all duration-200;
      @apply lg:relative;

      /* Off-screen on mobile by default */
      @apply -translate-x-full lg:translate-x-0;
    }

    .sidebar-open {
      @apply translate-x-0;
    }

    .sidebar-collapsed .sidebar {
      @apply w-16;
    }

    .mobile-overlay {
      @apply fixed inset-0 z-30 bg-black/20 backdrop-blur-sm lg:hidden;
    }

    .sidebar-header {
      @apply relative flex items-center justify-between gap-2 px-4 py-4 border-b border-neutral-50;
    }

    .tenant-switcher {
      @apply relative min-w-0 flex-1;
    }

    .tenant-switcher-collapsed {
      @apply flex-none;
    }

    .tenant-trigger,
    .mobile-tenant-trigger {
      @apply flex min-w-0 items-center gap-2 rounded-lg border border-transparent
        text-left transition-colors duration-150 hover:bg-neutral-50
        focus:outline-none focus:ring-2 focus:ring-brand-500 focus:ring-offset-2;
    }

    .tenant-trigger {
      @apply w-full px-2 py-1.5;
    }

    .tenant-trigger.icon-only {
      @apply h-9 w-9 justify-center p-0;
    }

    .tenant-logo {
      @apply flex h-8 w-8 flex-shrink-0 items-center justify-center overflow-hidden
        rounded-lg bg-brand-600 text-xs font-semibold text-white;
    }

    .tenant-logo img {
      @apply h-full w-full object-cover;
    }

    .tenant-trigger-copy,
    .tenant-option-copy {
      @apply min-w-0 flex-1;
    }

    .tenant-name,
    .tenant-option-name {
      @apply block truncate text-sm font-semibold text-neutral-900;
    }

    .tenant-role,
    .tenant-option-role {
      @apply block truncate text-xs text-neutral-500;
    }

    .tenant-chevron {
      @apply h-4 w-4 flex-shrink-0 text-neutral-400 transition-transform duration-150;
    }

    .tenant-menu {
      @apply absolute left-3 right-3 top-full z-50 mt-2 rounded-xl border border-neutral-100
        bg-white p-2 shadow-lg;
    }

    .tenant-menu-header {
      @apply flex items-center justify-between px-2 pb-2 text-xs font-semibold uppercase
        tracking-wide text-neutral-400;
    }

    .tenant-loading {
      @apply normal-case tracking-normal text-brand-600;
    }

    .tenant-error,
    .tenant-empty {
      @apply m-0 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700;
    }

    .tenant-empty {
      @apply bg-neutral-50 text-neutral-500;
    }

    .tenant-option {
      @apply flex w-full items-center gap-2 rounded-lg px-2 py-2 text-left
        transition-colors duration-150 hover:bg-neutral-50 focus:outline-none
        focus:ring-2 focus:ring-brand-500 focus:ring-offset-1;
    }

    .tenant-option.current {
      @apply bg-brand-50;
    }

    .tenant-option.unavailable {
      @apply cursor-not-allowed opacity-55 grayscale hover:bg-transparent;
    }

    .option-logo {
      @apply h-9 w-9;
    }

    .tenant-status {
      @apply rounded-full bg-neutral-100 px-2 py-0.5 text-xs font-medium text-neutral-600;
    }

    .tenant-check {
      @apply h-4 w-4 flex-shrink-0 text-brand-600;
    }

    .collapse-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors;
    }

    /* ─── Navigation ───────────────────────────── */

    .sidebar-nav {
      @apply flex-1 overflow-y-auto px-2 py-3 space-y-0.5;
    }

    .nav-item {
      @apply flex items-center gap-3 px-3 py-2 rounded-lg text-sm
        text-neutral-600 hover:bg-neutral-50 hover:text-neutral-900
        transition-colors duration-150 cursor-pointer no-underline;
    }

    .sidebar-collapsed .nav-item {
      @apply justify-center px-0;
    }

    .nav-active {
      @apply bg-brand-50 text-brand-700 font-medium;
    }

    .nav-icon {
      @apply flex-shrink-0 w-5 h-5;

      :host ::ng-deep svg {
        @apply w-5 h-5;
      }
    }

    .nav-label {
      @apply truncate;
    }

    /* ─── Sidebar footer ──────────────────────── */

    .sidebar-footer {
      @apply border-t border-neutral-50 px-3 py-3;
    }

    .user-menu {
      @apply flex items-center gap-2.5;
    }

    .user-avatar {
      @apply flex-shrink-0 w-8 h-8 rounded-full bg-brand-100 text-brand-700
        text-xs font-semibold flex items-center justify-center;
    }

    .user-info {
      @apply flex-1 min-w-0;
    }

    .user-name {
      @apply block text-sm font-medium text-neutral-900 truncate leading-tight;
    }

    .user-email {
      @apply block text-xs text-neutral-400 truncate leading-tight;
    }

    .logout-btn {
      @apply flex-shrink-0 w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-red-600 hover:bg-red-50 transition-colors;
    }

    /* ─── Main content ─────────────────────────── */

    .main-content {
      @apply flex-1 flex flex-col min-w-0;
    }

    .sidebar-collapsed .main-content {
      @apply lg:ml-0;
    }

    .topbar {
      @apply relative flex items-center h-14 px-4 border-b border-neutral-100 bg-white;
      @apply lg:px-6;
    }

    .mobile-menu-btn {
      @apply w-9 h-9 rounded-lg flex items-center justify-center
        text-neutral-500 hover:bg-neutral-100 transition-colors;
    }

    .mobile-tenant-trigger {
      @apply ml-2 max-w-[calc(100vw-8rem)] px-2 py-1;
    }

    .mobile-tenant-name {
      @apply truncate text-sm font-semibold text-neutral-900;
    }

    .mobile-tenant-menu {
      @apply left-3 right-3 top-14 lg:hidden;
    }

    .topbar-spacer {
      @apply flex-1;
    }

    .topbar-actions {
      @apply flex items-center gap-2;
    }

    .logout-btn-mobile {
      @apply w-9 h-9 rounded-lg flex items-center justify-center
        text-neutral-500 hover:text-red-600 hover:bg-red-50 transition-colors;
    }

    .page-content {
      @apply flex-1 overflow-y-auto;
    }
  `],
})
export class MainLayoutComponent implements OnInit {
  readonly authService = inject(AuthService);
  private readonly tenantService = inject(TenantService);
  private readonly idleTimeoutService = inject(IdleTimeoutService);

  readonly sidebarCollapsed = signal(false);
  readonly mobileMenuOpen = signal(false);
  readonly tenantMenuOpen = signal(false);
  readonly tenants = signal<IUserTenant[]>([]);
  readonly tenantsLoading = signal(false);
  readonly tenantError = signal('');
  readonly switchingTenantId = signal<string | null>(null);

  readonly navItems: INavItem[] = [
    {
      label: 'Dashboard',
      route: '/dashboard',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10.707 2.293a1 1 0 0 0-1.414 0l-7 7a1 1 0 0 0 1.414 1.414L4 10.414V17a1 1 0 0 0 1 1h2a1 1 0 0 0 1-1v-2a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v2a1 1 0 0 0 1 1h2a1 1 0 0 0 1-1v-6.586l.293.293a1 1 0 0 0 1.414-1.414l-7-7Z"/></svg>`,
    },
    {
      label: 'Departments',
      route: '/departments',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v2.5A2.25 2.25 0 0 0 4.25 9h2.5A2.25 2.25 0 0 0 9 6.75v-2.5A2.25 2.25 0 0 0 6.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 2 13.25v2.5A2.25 2.25 0 0 0 4.25 18h2.5A2.25 2.25 0 0 0 9 15.75v-2.5A2.25 2.25 0 0 0 6.75 11h-2.5Zm9-9A2.25 2.25 0 0 0 11 4.25v2.5A2.25 2.25 0 0 0 13.25 9h2.5A2.25 2.25 0 0 0 18 6.75v-2.5A2.25 2.25 0 0 0 15.75 2h-2.5Zm0 9A2.25 2.25 0 0 0 11 13.25v2.5A2.25 2.25 0 0 0 13.25 18h2.5A2.25 2.25 0 0 0 18 15.75v-2.5A2.25 2.25 0 0 0 15.75 11h-2.5Z" clip-rule="evenodd"/></svg>`,
    },
    {
      label: 'Job Titles',
      route: '/job-titles',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M6 3.75A2.75 2.75 0 0 1 8.75 1h2.5A2.75 2.75 0 0 1 14 3.75v.443c.572.055 1.14.122 1.706.2C17.053 4.582 18 5.75 18 7.07v3.469c0 1.126-.694 2.191-1.83 2.54-1.952.599-4.024.921-6.17.921s-4.219-.322-6.17-.921C2.694 12.73 2 11.665 2 10.539V7.07c0-1.321.947-2.489 2.294-2.676A41.047 41.047 0 0 1 6 4.193V3.75Zm6.5 0v.325a41.622 41.622 0 0 0-5 0V3.75c0-.69.56-1.25 1.25-1.25h2.5c.69 0 1.25.56 1.25 1.25ZM10 10a1 1 0 0 0-1 1v.01a1 1 0 0 0 1 1h.01a1 1 0 0 0 1-1V11a1 1 0 0 0-1-1H10Z" clip-rule="evenodd"/><path d="M3 15.055v-.684c.126.053.255.1.39.142 2.092.642 4.313.987 6.61.987 2.297 0 4.518-.345 6.61-.987.135-.041.264-.089.39-.142v.684c0 1.347-.985 2.53-2.363 2.686a41.454 41.454 0 0 1-9.274 0C3.985 17.585 3 16.402 3 15.055Z"/></svg>`,
    },
    {
      label: 'Employees',
      route: '/employees',
      permission: 'Employee.View.All',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M7 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm7.5 1a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM1.615 16.428a1.224 1.224 0 0 1-.569-1.175 6.002 6.002 0 0 1 11.908 0c.058.467-.172.92-.57 1.174A9.953 9.953 0 0 1 7 18a9.953 9.953 0 0 1-5.385-1.572ZM14.5 16h-.106c.07-.297.088-.611.048-.933a7.47 7.47 0 0 0-1.588-3.755 4.502 4.502 0 0 1 5.874 2.636.818.818 0 0 1-.36.98A7.465 7.465 0 0 1 14.5 16Z"/></svg>`,
    },
    {
      label: 'Leave',
      route: '/leave',
      permission: 'Leave.View',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M5.75 2a.75.75 0 0 1 .75.75V4h7V2.75a.75.75 0 0 1 1.5 0V4h.25A2.75 2.75 0 0 1 18 6.75v8.5A2.75 2.75 0 0 1 15.25 18H4.75A2.75 2.75 0 0 1 2 15.25v-8.5A2.75 2.75 0 0 1 4.75 4H5V2.75A.75.75 0 0 1 5.75 2Zm-1 5.5c-.69 0-1.25.56-1.25 1.25v6.5c0 .69.56 1.25 1.25 1.25h10.5c.69 0 1.25-.56 1.25-1.25v-6.5c0-.69-.56-1.25-1.25-1.25H4.75Z" clip-rule="evenodd"/></svg>`,
    },
    {
      label: 'Attendance',
      route: '/attendance',
      permission: 'Attendance.View',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-13a.75.75 0 0 0-1.5 0v5c0 .414.336.75.75.75h4a.75.75 0 0 0 0-1.5h-3.25V5Z" clip-rule="evenodd"/></svg>`,
    },
    {
      label: 'Payroll',
      route: '/payroll',
      permission: 'Payroll.View',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10.75 10.818v2.614A3.13 3.13 0 0 0 11.888 13c.482-.315.612-.648.612-.875 0-.227-.13-.56-.612-.875a3.13 3.13 0 0 0-1.138-.432ZM8.33 8.62c.053.055.115.11.184.164.208.16.46.284.736.363V6.603a2.45 2.45 0 0 0-.35.13c-.14.065-.27.143-.386.233-.377.292-.514.627-.514.909 0 .184.058.39.202.592.037.051.08.102.128.152Z"/><path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-6a.75.75 0 0 1 .75.75v.316a3.78 3.78 0 0 1 1.653.713c.426.33.744.74.925 1.2a.75.75 0 0 1-1.395.55 1.35 1.35 0 0 0-.447-.563 2.187 2.187 0 0 0-.736-.363V9.3c.698.093 1.383.32 1.959.696.787.514 1.29 1.27 1.29 2.13 0 .86-.504 1.616-1.29 2.13-.576.377-1.261.603-1.96.696v.299a.75.75 0 1 1-1.5 0v-.3a3.78 3.78 0 0 1-1.653-.712 3.22 3.22 0 0 1-.925-1.2.75.75 0 0 1 1.395-.55c.12.3.3.54.447.563a2.19 2.19 0 0 0 .736.363V10.7a5.007 5.007 0 0 1-1.96-.696C4.504 9.49 4 8.735 4 7.875c0-.86.504-1.616 1.29-2.13.577-.377 1.262-.603 1.96-.696V4.75A.75.75 0 0 1 10 4Z" clip-rule="evenodd"/></svg>`,
    },
    {
      label: 'Recruitment',
      route: '/recruitment',
      permission: 'Recruitment.View',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M10 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM6 8a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM1.49 15.326a.78.78 0 0 1-.358-.442 3 3 0 0 1 4.308-3.516 6.484 6.484 0 0 0-1.905 3.959c-.023.222-.014.442.025.654a4.97 4.97 0 0 1-2.07-.655ZM16.44 15.98a4.97 4.97 0 0 0 2.07-.654.78.78 0 0 0 .357-.442 3 3 0 0 0-4.308-3.517 6.484 6.484 0 0 1 1.907 3.96 2.32 2.32 0 0 1-.026.654ZM18 8a2 2 0 1 1-4 0 2 2 0 0 1 4 0ZM5.304 16.19a.844.844 0 0 1-.277-.71 5 5 0 0 1 9.947 0 .843.843 0 0 1-.277.71A6.975 6.975 0 0 1 10 18a6.974 6.974 0 0 1-4.696-1.81Z"/></svg>`,
    },
    {
      label: 'Performance',
      route: '/performance',
      permission: 'Performance.View',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path d="M15.98 1.804a1 1 0 0 0-1.96 0l-.24 1.192a1 1 0 0 1-.784.785l-1.192.238a1 1 0 0 0 0 1.962l1.192.238a1 1 0 0 1 .785.785l.238 1.192a1 1 0 0 0 1.962 0l.238-1.192a1 1 0 0 1 .785-.785l1.192-.238a1 1 0 0 0 0-1.962l-1.192-.238a1 1 0 0 1-.785-.785l-.238-1.192ZM6.949 5.684a1 1 0 0 0-1.898 0l-.683 2.051a1 1 0 0 1-.633.633l-2.051.683a1 1 0 0 0 0 1.898l2.051.684a1 1 0 0 1 .633.632l.683 2.051a1 1 0 0 0 1.898 0l.683-2.051a1 1 0 0 1 .633-.633l2.051-.683a1 1 0 0 0 0-1.898l-2.051-.683a1 1 0 0 1-.633-.633L6.95 5.684ZM13.949 13.684a1 1 0 0 0-1.898 0l-.184.551a1 1 0 0 1-.632.633l-.551.183a1 1 0 0 0 0 1.898l.551.183a1 1 0 0 1 .633.633l.183.551a1 1 0 0 0 1.898 0l.184-.551a1 1 0 0 1 .632-.633l.551-.183a1 1 0 0 0 0-1.898l-.551-.184a1 1 0 0 1-.633-.632l-.183-.551Z"/></svg>`,
    },
    {
      label: 'Roles',
      route: '/admin/roles',
      permission: 'Admin.Roles.Manage',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M10 1a4.5 4.5 0 0 0-4.5 4.5V9H5a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-6a2 2 0 0 0-2-2h-.5V5.5A4.5 4.5 0 0 0 10 1Zm3 8V5.5a3 3 0 1 0-6 0V9h6Z" clip-rule="evenodd"/></svg>`,
    },
    {
      label: 'Settings',
      route: '/admin',
      permission: 'Admin.View',
      icon: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor"><path fill-rule="evenodd" d="M7.84 1.804A1 1 0 0 1 8.82 1h2.36a1 1 0 0 1 .98.804l.331 1.652a6.993 6.993 0 0 1 1.929 1.115l1.598-.54a1 1 0 0 1 1.186.447l1.18 2.044a1 1 0 0 1-.205 1.251l-1.267 1.113a7.047 7.047 0 0 1 0 2.228l1.267 1.113a1 1 0 0 1 .206 1.25l-1.18 2.045a1 1 0 0 1-1.187.447l-1.598-.54a6.993 6.993 0 0 1-1.929 1.115l-.33 1.652a1 1 0 0 1-.98.804H8.82a1 1 0 0 1-.98-.804l-.331-1.652a6.993 6.993 0 0 1-1.929-1.115l-1.598.54a1 1 0 0 1-1.186-.447l-1.18-2.044a1 1 0 0 1 .205-1.251l1.267-1.114a7.05 7.05 0 0 1 0-2.227L1.821 7.773a1 1 0 0 1-.206-1.25l1.18-2.045a1 1 0 0 1 1.187-.447l1.598.54A6.993 6.993 0 0 1 7.51 3.456l.33-1.652ZM10 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" clip-rule="evenodd"/></svg>`,
    },
  ];

  ngOnInit(): void {
    this.loadTenants();
    this.initIdleTimeout();
  }

  userInitials(): string {
    const name = this.authService.currentUser()?.displayName || '';
    const parts = name.split(' ');
    if (parts.length >= 2) {
      return (parts[0][0] + parts[1][0]).toUpperCase();
    }
    return name.substring(0, 2).toUpperCase();
  }

  toggleSidebar(): void {
    this.sidebarCollapsed.update((v) => !v);
  }

  toggleTenantMenu(): void {
    this.tenantMenuOpen.update((open) => !open);
    if (!this.tenants().length && !this.tenantsLoading()) {
      this.loadTenants();
    }
  }

  switchTenant(tenant: IUserTenant): void {
    if (!this.isTenantSwitchable(tenant) || this.switchingTenantId()) {
      return;
    }

    this.switchingTenantId.set(tenant.tenantId);
    this.tenantError.set('');

    this.authService
      .switchTenant({ tenantId: tenant.tenantId })
      .pipe(
        catchError((error) => {
          this.tenantError.set(
            error?.error?.message ||
              error?.error?.error ||
              'This organization is unavailable right now.'
          );
          return EMPTY;
        }),
        finalize(() => this.switchingTenantId.set(null))
      )
      .subscribe();
  }

  isTenantSwitchable(tenant: IUserTenant): boolean {
    return !tenant.isCurrentTenant && (tenant.status === 'active' || tenant.status === 'trial');
  }

  primaryRole(tenant: IUserTenant): string {
    return tenant.roles[0] || 'Member';
  }

  currentPrimaryRole(): string {
    const currentTenantId = this.authService.currentTenant()?.tenantId;
    const current = this.tenants().find((tenant) => tenant.tenantId === currentTenantId || tenant.isCurrentTenant);
    return current ? this.primaryRole(current) : this.authService.roles()[0] || 'Member';
  }

  currentTenantLogo(): string | undefined {
    const currentTenantId = this.authService.currentTenant()?.tenantId;
    return (
      this.tenants().find((tenant) => tenant.tenantId === currentTenantId || tenant.isCurrentTenant)?.logoUrl ||
      this.authService.currentTenant()?.logoUrl ||
      this.tenantService.tenantContext().logoUrl
    );
  }

  tenantInitial(name = this.tenantName()): string {
    return name.trim().charAt(0).toUpperCase() || 'H';
  }

  statusLabel(status: IUserTenant['status']): string {
    return status
      .split('_')
      .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
      .join(' ');
  }

  tenantUnavailableMessage(tenant: IUserTenant): string {
    if (tenant.isCurrentTenant) {
      return 'Current organization';
    }

    if (this.isTenantSwitchable(tenant)) {
      return `Switch to ${tenant.name}`;
    }

    return `${tenant.name} is ${this.statusLabel(tenant.status)} and cannot be opened.`;
  }

  tenantName(): string {
    return this.authService.currentTenant()?.name || this.tenantService.displayName();
  }

  /** Load tenant auth settings and start idle timeout tracking (US-AUTH-009). */
  private initIdleTimeout(): void {
    this.authService.getTenantAuthSettings().subscribe({
      next: (settings) => {
        const timeout = settings.idleTimeoutMinutes ?? 60;
        if (timeout > 0) {
          this.idleTimeoutService.start(timeout);
        }
      },
      error: () => {
        // Fallback to default 60 min idle timeout on settings load failure
        this.idleTimeoutService.start(60);
      },
    });
  }

  private loadTenants(): void {
    this.tenantsLoading.set(true);
    this.tenantError.set('');

    this.authService
      .getMyTenants()
      .pipe(
        catchError(() => {
          this.tenantError.set('Unable to load organization memberships.');
          return EMPTY;
        }),
        finalize(() => this.tenantsLoading.set(false))
      )
      .subscribe((tenants) => {
        this.tenants.set(tenants);
      });
  }
}
