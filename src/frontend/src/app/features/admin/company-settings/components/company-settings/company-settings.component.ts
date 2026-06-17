import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { CompanySettingsService } from '../../services/company-settings.service';
import { ICompanySettings } from '../../models/company-settings.models';
import { OrgProfileSectionComponent } from '../org-profile-section/org-profile-section.component';
import { BrandingSectionComponent } from '../branding-section/branding-section.component';
import { LocalizationSectionComponent } from '../localization-section/localization-section.component';
import { SecuritySectionComponent } from '../security-section/security-section.component';

type SettingsTab = 'org' | 'branding' | 'localization' | 'security';

/**
 * US-ADM-006: Company Settings container (smart component).
 * Loads the full settings aggregate once, then feeds each sub-object into a
 * presentational section. Sidebar/tabbed navigation between sections.
 */
@Component({
  selector: 'app-company-settings',
  standalone: true,
  imports: [
    CommonModule,
    TranslateModule,
    OrgProfileSectionComponent,
    BrandingSectionComponent,
    LocalizationSectionComponent,
    SecuritySectionComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cs-page">
      <header class="cs-page-head">
        <h1 class="cs-page-title">{{ 'admin.companySettings.title' | translate }}</h1>
        <p class="cs-page-sub">{{ 'admin.companySettings.subtitle' | translate }}</p>
      </header>

      @if (loading()) {
        <div class="cs-loading">
          <span class="cs-spinner"></span>
          <p>{{ 'admin.companySettings.loading' | translate }}</p>
        </div>
      } @else if (loadError()) {
        <div class="cs-load-error">
          <p>{{ loadError() }}</p>
          <button type="button" class="cs-btn-secondary" (click)="load()">
            {{ 'admin.companySettings.retry' | translate }}
          </button>
        </div>
      } @else if (settings(); as s) {
        <div class="cs-layout">
          <!-- Sidebar tabs -->
          <nav class="cs-tabs" role="tablist" aria-label="Company settings sections">
            @for (tab of tabs; track tab.id) {
              <button
                type="button"
                role="tab"
                class="cs-tab"
                [class.active]="activeTab() === tab.id"
                [attr.aria-selected]="activeTab() === tab.id"
                (click)="activeTab.set(tab.id)"
              >
                {{ tab.label | translate }}
              </button>
            }
          </nav>

          <!-- Active panel -->
          <div class="cs-panel" role="tabpanel">
            @switch (activeTab()) {
              @case ('org') {
                <app-org-profile-section [value]="s.org" />
              }
              @case ('branding') {
                <app-branding-section [value]="s.branding" [plan]="s.plan ?? null" />
              }
              @case ('localization') {
                <app-localization-section [value]="s.localization" />
              }
              @case ('security') {
                <app-security-section
                  [passwordPolicy]="s.passwordPolicy"
                  [sessionPolicy]="s.sessionPolicy"
                />
              }
            }
          </div>
        </div>
      }
    </div>
  `,
  styleUrls: ['../../company-settings.styles.css'],
  styles: [`
    .cs-page {
      @apply mx-auto max-w-5xl px-4 py-6 sm:px-6;
    }
    .cs-page-head {
      @apply mb-5;
    }
    .cs-page-title {
      @apply text-xl font-semibold text-neutral-900 sm:text-2xl;
    }
    .cs-page-sub {
      @apply mt-1 text-sm text-neutral-500;
    }
    .cs-loading,
    .cs-load-error {
      @apply flex flex-col items-center justify-center gap-3 rounded-xl border border-neutral-100
        bg-white py-16 text-sm text-neutral-500 shadow-notion;
    }
    .cs-spinner {
      @apply h-7 w-7 rounded-full border-2 border-neutral-200 border-t-brand-600;
      animation: cs-spin 0.7s linear infinite;
    }
    .cs-layout {
      @apply flex flex-col gap-5 lg:flex-row;
    }
    /* Horizontal scrollable tabs on mobile; vertical sidebar on desktop. */
    .cs-tabs {
      @apply flex gap-1 overflow-x-auto rounded-xl border border-neutral-100 bg-white p-1
        shadow-sm lg:w-56 lg:flex-col lg:self-start;
    }
    .cs-tab {
      @apply whitespace-nowrap rounded-lg px-3 py-2 text-left text-sm font-medium
        text-neutral-600 transition-colors duration-200 hover:bg-neutral-50 hover:text-neutral-900
        lg:w-full;
    }
    .cs-tab.active {
      @apply bg-brand-50 text-brand-700;
    }
    .cs-panel {
      @apply min-w-0 flex-1;
    }
    @keyframes cs-spin { to { transform: rotate(360deg); } }
  `],
})
export class CompanySettingsComponent implements OnInit {
  private readonly service = inject(CompanySettingsService);

  readonly settings = signal<ICompanySettings | null>(null);
  readonly loading = signal(true);
  readonly loadError = signal('');
  readonly activeTab = signal<SettingsTab>('org');

  readonly tabs: { id: SettingsTab; label: string }[] = [
    { id: 'org', label: 'admin.companySettings.tabs.org' },
    { id: 'branding', label: 'admin.companySettings.tabs.branding' },
    { id: 'localization', label: 'admin.companySettings.tabs.localization' },
    { id: 'security', label: 'admin.companySettings.tabs.security' },
  ];

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.service.getSettings().subscribe({
      next: (s) => {
        this.settings.set(s);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.loadError.set(err.error?.message || 'Failed to load company settings.');
      },
    });
  }
}
