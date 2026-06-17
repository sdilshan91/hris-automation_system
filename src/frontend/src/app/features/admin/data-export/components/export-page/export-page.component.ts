import { Component, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { ExportPanelComponent } from '../export-panel/export-panel.component';

/**
 * US-ADM-010 AC-1: Tenant-Admin data-export page (Data Management > Export).
 *
 * Rendered inside the normal authenticated MainLayout at `/admin/data-export`,
 * gated by roleGuard(['Tenant Admin', 'Tenant Owner']). A thin page shell around
 * the shared {@link ExportPanelComponent} with NO `tenantId` — so the panel hits
 * the implicit Tenant-Admin path (`/api/v1/tenant/exports`). The System-Admin
 * persona reuses the same panel WITH a tenantId from the tenant-detail dialog.
 */
@Component({
  selector: 'app-export-page',
  standalone: true,
  imports: [CommonModule, TranslateModule, ExportPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page-container max-w-4xl">
      <header class="mb-6">
        <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
          {{ 'dataExport.page.title' | translate }}
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          {{ 'dataExport.page.subtitle' | translate }}
        </p>
      </header>

      <app-export-panel />
    </div>
  `,
})
export class ExportPageComponent {}
