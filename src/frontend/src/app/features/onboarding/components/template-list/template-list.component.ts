import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { OnboardingTemplateService } from '../../services/onboarding-template.service';
import { IOnboardingTemplateSummary } from '../../models/onboarding-template.models';

/**
 * US-ONB-001: Onboarding checklist template list (AC-1 entry point + FR-6/FR-7).
 *
 * Notion-style card grid of existing templates with task-count + active badge.
 * Each card offers Clone (FR-6 → opens the builder pre-filled) and Activate/
 * Deactivate (FR-7 soft toggle); deactivated templates stay visible for
 * historical reference (BR-4). "Create Checklist Template" routes to the builder.
 */
@Component({
  selector: 'app-template-list',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '200ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-6xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div
        class="mb-6 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between"
      >
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Onboarding templates
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Reusable checklists assigned to new hires on joining.
          </p>
        </div>
        <a routerLink="/onboarding/templates/new" class="btn-primary self-start">
          + Create Checklist Template
        </a>
      </div>

      @if (loading()) {
        <div
          class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
          aria-busy="true"
        >
          @for (n of [1, 2, 3]; track n) {
            <div class="h-36 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (templates().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-white py-20 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">No templates yet</p>
          <p class="mt-1 text-sm text-neutral-500">
            Create your first onboarding checklist template to get started.
          </p>
          <a
            routerLink="/onboarding/templates/new"
            class="btn-primary mt-4 inline-block"
          >
            + Create Checklist Template
          </a>
        </div>
      } @else {
        <div
          @fadeIn
          class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
        >
          @for (t of templates(); track t.id) {
            <article class="card">
              <div class="flex items-start justify-between gap-2">
                <h2 class="truncate text-base font-semibold text-neutral-900">
                  {{ t.templateName }}
                </h2>
                <span
                  class="badge"
                  [class.badge-active]="t.isActive"
                  [class.badge-inactive]="!t.isActive"
                >
                  {{ t.isActive ? 'Active' : 'Inactive' }}
                </span>
              </div>

              @if (t.description) {
                <p class="mt-1 line-clamp-2 text-sm text-neutral-500">
                  {{ t.description }}
                </p>
              }

              <p class="mt-3 text-xs font-medium text-neutral-600">
                {{ t.taskCount }} task{{ t.taskCount === 1 ? '' : 's' }}
              </p>

              <div class="mt-4 flex flex-wrap items-center gap-2">
                <button
                  type="button"
                  class="action-btn"
                  (click)="clone(t)"
                  [attr.aria-label]="'Clone ' + t.templateName"
                >
                  Clone
                </button>
                @if (t.isActive) {
                  <button
                    type="button"
                    class="action-btn"
                    [disabled]="busyId() === t.id"
                    (click)="deactivate(t)"
                    [attr.aria-label]="'Deactivate ' + t.templateName"
                  >
                    Deactivate
                  </button>
                } @else {
                  <button
                    type="button"
                    class="action-btn action-accent"
                    [disabled]="busyId() === t.id"
                    (click)="activate(t)"
                    [attr.aria-label]="'Activate ' + t.templateName"
                  >
                    Activate
                  </button>
                }
              </div>
            </article>
          }
        </div>
      }
    </div>
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .card {
        border-radius: 0.75rem;
        border: 1px solid #f0f0f0;
        background: #fff;
        padding: 1.25rem;
        box-shadow: 0 1px 2px rgb(0 0 0 / 0.04);
        transition: box-shadow 200ms ease, transform 200ms ease;
      }
      .card:hover {
        box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
        transform: translateY(-1px);
      }
      .badge {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        padding: 0.125rem 0.625rem;
        font-size: 0.6875rem;
        font-weight: 600;
        white-space: nowrap;
      }
      .badge-active {
        background: #ecfdf5;
        color: #047857;
      }
      .badge-inactive {
        background: #f5f5f5;
        color: #737373;
      }
      .action-btn {
        border-radius: 0.5rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.375rem 0.875rem;
        font-size: 0.8125rem;
        font-weight: 500;
        color: #525252;
        transition: all 200ms ease;
      }
      .action-btn:hover:not(:disabled) {
        background: #f5f5f5;
      }
      .action-btn:disabled {
        opacity: 0.5;
      }
      .action-accent {
        border-color: #c7d2fe;
        color: #4338ca;
      }
      .action-accent:hover:not(:disabled) {
        background: #eef2ff;
      }
      .btn-primary {
        border-radius: 0.5rem;
        background: #4f46e5;
        padding: 0.5rem 1.25rem;
        font-size: 0.875rem;
        font-weight: 600;
        color: #fff;
        transition: background 200ms ease;
      }
      .btn-primary:hover {
        background: #4338ca;
      }
    `,
  ],
})
export class TemplateListComponent implements OnInit {
  private readonly service = inject(OnboardingTemplateService);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  readonly templates = signal<IOnboardingTemplateSummary[]>([]);
  readonly loading = signal(true);
  readonly busyId = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.service.list().subscribe({
      next: (list) => {
        this.templates.set(list);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.loading.set(false);
        this.toastr.error(OnboardingTemplateService.parseErrorMessage(err));
      },
    });
  }

  /** FR-6: open the builder with the source template pre-filled as a copy. */
  clone(t: IOnboardingTemplateSummary): void {
    this.router.navigate(['/onboarding/templates/new'], {
      queryParams: { cloneFrom: t.id },
    });
  }

  /** FR-7: reactivate a soft-deactivated template. */
  activate(t: IOnboardingTemplateSummary): void {
    this.busyId.set(t.id);
    this.service.activate(t.id).subscribe({
      next: (updated) => {
        this.applyStatus(t.id, updated.isActive);
        this.busyId.set(null);
        this.toastr.success('Template activated.');
      },
      error: (err: HttpErrorResponse) => {
        this.busyId.set(null);
        this.toastr.error(OnboardingTemplateService.parseErrorMessage(err));
      },
    });
  }

  /** FR-7 / BR-4: deactivate without deletion (stays visible). */
  deactivate(t: IOnboardingTemplateSummary): void {
    this.busyId.set(t.id);
    this.service.deactivate(t.id).subscribe({
      next: (updated) => {
        this.applyStatus(t.id, updated.isActive);
        this.busyId.set(null);
        this.toastr.success('Template deactivated.');
      },
      error: (err: HttpErrorResponse) => {
        this.busyId.set(null);
        this.toastr.error(OnboardingTemplateService.parseErrorMessage(err));
      },
    });
  }

  private applyStatus(id: string, isActive: boolean): void {
    this.templates.update((list) =>
      list.map((t) => (t.id === id ? { ...t, isActive } : t)),
    );
  }
}
