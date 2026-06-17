import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
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
import { IReportCatalogItem } from '../../models/reports.models';

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
      } @else {
        <div class="rpt-grid">
          @for (item of catalog(); track item.type) {
            <article class="rpt-card">
              <mat-icon class="rpt-card-icon" aria-hidden="true">{{
                item.icon
              }}</mat-icon>
              <h2 class="rpt-card-title">{{ item.titleKey | translate }}</h2>
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
          }
        </div>
      }
    </section>
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
}
