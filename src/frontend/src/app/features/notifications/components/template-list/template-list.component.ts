import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  inject,
} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { NotificationTemplateService } from '../../services/notification-template.service';
import {
  ITemplateListItem,
  templateBadge,
} from '../../models/notification-template.models';

/**
 * US-NTF-002 AC-1: the Tenant Admin template list. Renders one card per event
 * type with its status badge ("Default" / "Custom"), language and last-modified
 * date. Clicking a card opens the split-pane editor (AC-2).
 *
 * Card-grid layout (Notion-inspired) rather than a dense table — responsive from
 * 360px (single column) up. Tenant isolation is server-side (AC-5).
 */
@Component({
  selector: 'app-template-list',
  standalone: true,
  imports: [CommonModule, TranslateModule, DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="mx-auto max-w-5xl px-4 py-6 sm:px-6">
      <header class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900">
          {{ 'notificationTemplates.list.title' | translate }}
        </h1>
        <p class="mt-1 text-sm text-neutral-500">
          {{ 'notificationTemplates.list.subtitle' | translate }}
        </p>
      </header>

      @if (svc.loadingList()) {
        <!-- Skeleton placeholders (micro-interaction) -->
        <ul class="grid gap-3 sm:grid-cols-2" aria-hidden="true">
          @for (s of [1, 2, 3, 4]; track s) {
            <li
              class="h-24 animate-pulse rounded-xl border border-neutral-100 bg-neutral-50"
            ></li>
          }
        </ul>
      } @else if (svc.templates().length === 0) {
        <div
          class="rounded-xl border border-dashed border-neutral-200 bg-white py-16 text-center"
        >
          <p class="text-sm text-neutral-500">
            {{ 'notificationTemplates.list.empty' | translate }}
          </p>
        </div>
      } @else {
        <ul class="grid gap-3 sm:grid-cols-2" role="list">
          @for (t of svc.templates(); track t.eventKey + t.language) {
            <li role="listitem">
              <button
                type="button"
                class="group flex w-full flex-col gap-2 rounded-xl border border-neutral-200 bg-white p-4 text-left shadow-sm transition duration-200 hover:-translate-y-0.5 hover:shadow-md focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-300"
                (click)="open(t)"
                [attr.aria-label]="
                  ('notificationTemplates.list.editAria' | translate) +
                  ' ' +
                  t.eventName
                "
              >
                <div class="flex items-start justify-between gap-2">
                  <span class="font-medium text-neutral-900">{{
                    t.eventName
                  }}</span>
                  <span
                    class="shrink-0 rounded-full px-2 py-0.5 text-xs font-medium"
                    [class.bg-indigo-50]="t.isCustom"
                    [class.text-indigo-700]="t.isCustom"
                    [class.bg-neutral-100]="!t.isCustom"
                    [class.text-neutral-600]="!t.isCustom"
                  >
                    {{
                      'notificationTemplates.badge.' + badge(t).toLowerCase()
                        | translate
                    }}
                  </span>
                </div>
                <div
                  class="flex items-center gap-3 text-xs text-neutral-500"
                >
                  <span class="uppercase tracking-wide">{{ t.language }}</span>
                  <span aria-hidden="true">&middot;</span>
                  <span>
                    @if (t.lastModified) {
                      {{ 'notificationTemplates.list.modified' | translate }}
                      {{ t.lastModified | date: 'mediumDate' }}
                    } @else {
                      {{ 'notificationTemplates.list.neverModified' | translate }}
                    }
                  </span>
                </div>
              </button>
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class TemplateListComponent implements OnInit {
  protected readonly svc = inject(NotificationTemplateService);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.svc.loadTemplates('en');
  }

  protected badge(t: ITemplateListItem): 'Custom' | 'Default' {
    return templateBadge(t.isCustom);
  }

  protected open(t: ITemplateListItem): void {
    this.router.navigate(['/admin/notification-templates', t.eventKey], {
      queryParams: { language: t.language },
    });
  }
}
