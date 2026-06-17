import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { DestroyRef } from '@angular/core';
import { ReportsService } from '../../services/reports.service';
import {
  IReportCatalogItem,
  ReportCategory,
} from '../../models/reports.models';

/** A catalog section: a (possibly categorized) bucket of report cards. */
interface ICatalogGroup {
  /** Stable category key (or '__none__' fallback). */
  key: string;
  /** i18n key for the section heading. */
  headingKey: string;
  items: IReportCatalogItem[];
}

/**
 * US-RPT-001 AC-1: report catalog. A card grid of the six pre-built report
 * types (icon, title, description, Generate button). "Generate" navigates to
 * the viewer keyed by report type.
 */
@Component({
  selector: 'app-report-catalog',
  standalone: true,
  imports: [CommonModule, TranslateModule, MatIconModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="rpt-page">
      <header class="rpt-head">
        <h1 class="rpt-title">{{ 'reports.catalog.title' | translate }}</h1>
        <p class="rpt-sub">{{ 'reports.catalog.subtitle' | translate }}</p>
      </header>

      @if (loading()) {
        <div class="rpt-grid" aria-hidden="true">
          @for (s of skeletons; track s) {
            <div class="rpt-card rpt-skeleton">
              <div class="rpt-skel-icon"></div>
              <div class="rpt-skel-line w-2/3"></div>
              <div class="rpt-skel-line w-full"></div>
              <div class="rpt-skel-line w-1/2"></div>
            </div>
          }
        </div>
      } @else if (loadError()) {
        <div class="rpt-error" role="alert">
          <p>{{ loadError() }}</p>
          <button type="button" class="rpt-btn-secondary" (click)="load()">
            {{ 'reports.catalog.retry' | translate }}
          </button>
        </div>
      } @else if (groups(); as gs) {
        <!-- Light grouping (US-RPT-002 section 8): one section per server
             category when any item carries one; else a single ungrouped grid. -->
        @if (grouped()) {
          @for (group of gs; track group.key) {
            <section class="rpt-group">
              <h2 class="rpt-group-title">
                {{ group.headingKey | translate }}
              </h2>
              <div class="rpt-grid">
                @for (item of group.items; track item.type) {
                  <ng-container
                    [ngTemplateOutlet]="cardTpl"
                    [ngTemplateOutletContext]="{ $implicit: item }"
                  />
                }
              </div>
            </section>
          }
        } @else {
          <div class="rpt-grid">
            @for (item of catalog(); track item.type) {
              <ng-container
                [ngTemplateOutlet]="cardTpl"
                [ngTemplateOutletContext]="{ $implicit: item }"
              />
            }
          </div>
        }
      }
    </section>

    <ng-template #cardTpl let-item>
      <article class="rpt-card">
        <mat-icon class="rpt-card-icon" aria-hidden="true">{{
          item.icon
        }}</mat-icon>
        <h3 class="rpt-card-title">{{ item.titleKey | translate }}</h3>
        <p class="rpt-card-desc">{{ item.descriptionKey | translate }}</p>
        <button
          type="button"
          class="rpt-btn-primary"
          (click)="generate(item)"
          [attr.aria-label]="
            ('reports.catalog.generateFor' | translate) +
            ' ' +
            (item.titleKey | translate)
          "
        >
          {{ 'reports.catalog.generate' | translate }}
        </button>
      </article>
    </ng-template>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .rpt-page {
        padding: 1.5rem;
        max-width: 80rem;
        margin: 0 auto;
      }
      .rpt-head {
        margin-bottom: 1.5rem;
      }
      .rpt-group {
        margin-bottom: 2rem;
      }
      .rpt-group-title {
        font-size: 1.125rem;
        font-weight: 600;
        color: #374151;
        margin-bottom: 0.875rem;
      }
      .rpt-title {
        font-size: 1.5rem;
        font-weight: 700;
        color: #111827;
      }
      .rpt-sub {
        color: #6b7280;
        margin-top: 0.25rem;
      }
      .rpt-grid {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(18rem, 1fr));
        gap: 1.25rem;
      }
      .rpt-card {
        background: #fff;
        border-radius: 0.875rem;
        padding: 1.5rem;
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
        border: 1px solid #f3f4f6;
        transition: box-shadow 0.2s ease, transform 0.2s ease;
        display: flex;
        flex-direction: column;
      }
      .rpt-card:hover {
        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
        transform: translateY(-2px);
      }
      .rpt-card-icon {
        width: 2.5rem;
        height: 2.5rem;
        font-size: 2.5rem;
        line-height: 2.5rem;
        color: #4f46e5;
        margin-bottom: 0.875rem;
      }
      .rpt-card-title {
        font-size: 1.05rem;
        font-weight: 600;
        color: #111827;
      }
      .rpt-card-desc {
        color: #6b7280;
        font-size: 0.875rem;
        margin: 0.5rem 0 1.25rem;
        flex: 1;
      }
      .rpt-btn-primary {
        align-self: flex-start;
        background: #4f46e5;
        color: #fff;
        border: none;
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        font-weight: 500;
        cursor: pointer;
        transition: background 0.2s ease;
      }
      .rpt-btn-primary:hover {
        background: #4338ca;
      }
      .rpt-btn-secondary {
        background: #f3f4f6;
        color: #374151;
        border: none;
        border-radius: 0.5rem;
        padding: 0.5rem 1rem;
        cursor: pointer;
      }
      .rpt-error {
        background: #fef2f2;
        border: 1px solid #fecaca;
        color: #b91c1c;
        padding: 1.25rem;
        border-radius: 0.75rem;
      }
      .rpt-skeleton {
        pointer-events: none;
      }
      .rpt-skel-icon {
        width: 2.5rem;
        height: 2.5rem;
        border-radius: 0.5rem;
        background: #e5e7eb;
        margin-bottom: 0.875rem;
      }
      .rpt-skel-line {
        height: 0.75rem;
        border-radius: 0.375rem;
        background: #e5e7eb;
        margin-bottom: 0.625rem;
      }
      .w-2\\/3 {
        width: 66%;
      }
      .w-full {
        width: 100%;
      }
      .w-1\\/2 {
        width: 50%;
      }
      @media (max-width: 768px) {
        .rpt-grid {
          grid-template-columns: 1fr;
        }
      }
    `,
  ],
})
export class ReportCatalogComponent implements OnInit {
  private readonly service = inject(ReportsService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  readonly catalog = signal<IReportCatalogItem[]>([]);
  readonly loading = signal<boolean>(false);
  readonly loadError = signal<string | null>(null);

  readonly skeletons = [0, 1, 2, 3, 4, 5];

  /**
   * US-RPT-002 §8: true when the server tagged any catalog item with a
   * `category`, so the catalog renders grouped sections; false → one flat grid.
   */
  readonly grouped = computed(() =>
    this.catalog().some((item) => !!item.category)
  );

  /**
   * Catalog items bucketed by `category`, preserving first-seen order. Items
   * without a category fall into an "other" group. Only consumed when
   * {@link grouped} is true.
   */
  readonly groups = computed<ICatalogGroup[]>(() => {
    const order: string[] = [];
    const buckets = new Map<string, IReportCatalogItem[]>();
    for (const item of this.catalog()) {
      const key = item.category ?? '__none__';
      if (!buckets.has(key)) {
        buckets.set(key, []);
        order.push(key);
      }
      buckets.get(key)!.push(item);
    }
    return order.map((key) => ({
      key,
      headingKey: this.headingKey(key),
      items: buckets.get(key)!,
    }));
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.service
      .getCatalog()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (items) => {
          this.catalog.set(items);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.loadError.set(
            err.error?.message ?? 'Could not load the report catalog.'
          );
          this.loading.set(false);
        },
      });
  }

  /** Navigate to the viewer for the chosen report type. */
  generate(item: IReportCatalogItem): void {
    this.router.navigate(['/reports', item.type]);
  }

  /** Map a server category key to its section-heading i18n key. */
  private headingKey(category: ReportCategory): string {
    if (category === '__none__') {
      return 'reports.catalog.groups.other';
    }
    return `reports.catalog.groups.${category}`;
  }
}
