import {
  Component,
  ChangeDetectionStrategy,
  input,
  output,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';
import { trigger, transition, style, animate } from '@angular/animations';
import { ExportPanelComponent } from '../export-panel/export-panel.component';

/**
 * US-ADM-010 AC-6: System-Admin "Export Data" dialog, opened from the
 * tenant-monitoring detail "Actions" area. A modal shell around the SAME shared
 * {@link ExportPanelComponent}, but with `tenantId` set — so the panel targets
 * the System-Admin path (`/api/v1/system/tenants/{id}/exports`) for that tenant.
 *
 * Purely presentational shell: it owns only open/close; the panel owns all the
 * export logic, identical to the Tenant-Admin page.
 */
@Component({
  selector: 'app-system-export-dialog',
  standalone: true,
  imports: [CommonModule, TranslateModule, ExportPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('backdrop', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('150ms ease-out', style({ opacity: 1 })),
      ]),
    ]),
    trigger('pop', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px) scale(0.98)' }),
        animate(
          '180ms cubic-bezier(0.16,1,0.3,1)',
          style({ opacity: 1, transform: 'translateY(0) scale(1)' })
        ),
      ]),
    ]),
  ],
  template: `
    <div
      @backdrop
      class="fixed inset-0 z-[60] flex items-start justify-center overflow-y-auto bg-neutral-900/40 p-4 sm:p-6"
      (click)="cancelled.emit()"
    >
      <div
        @pop
        class="my-auto w-full max-w-3xl rounded-xl bg-neutral-50 shadow-xl"
        role="dialog"
        aria-modal="true"
        aria-labelledby="system-export-title"
        (click)="$event.stopPropagation()"
        data-testid="system-export-dialog"
      >
        <header
          class="flex items-start justify-between gap-3 border-b border-neutral-200 bg-white px-6 py-4"
        >
          <div class="min-w-0">
            <h2
              id="system-export-title"
              class="text-base font-semibold text-neutral-900"
            >
              {{ 'dataExport.system.title' | translate }}
            </h2>
            <p class="mt-0.5 truncate text-sm text-neutral-500">
              {{ 'dataExport.system.subtitle' | translate }}
              <span class="font-medium text-neutral-700">{{ tenantName() }}</span>
            </p>
          </div>
          <button
            type="button"
            class="rounded-md p-1.5 text-neutral-400 transition-colors hover:bg-neutral-100 hover:text-neutral-600"
            [attr.aria-label]="'common.close' | translate"
            (click)="cancelled.emit()"
            data-testid="system-export-close"
          >
            <svg class="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
              <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
            </svg>
          </button>
        </header>

        <div class="max-h-[75vh] overflow-y-auto px-6 py-5">
          <app-export-panel [tenantId]="tenantId()" />
        </div>
      </div>
    </div>
  `,
})
export class SystemExportDialogComponent {
  /** Target tenant id — routes the panel to the System-Admin export path. */
  readonly tenantId = input.required<string>();
  /** Tenant display name, shown in the dialog header. */
  readonly tenantName = input.required<string>();

  readonly cancelled = output<void>();
}
