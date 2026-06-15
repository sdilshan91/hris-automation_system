import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { ToastrService } from 'ngx-toastr';
import { PayrollService } from '../../services/payroll.service';
import { ISalaryStructure } from '../../models/payroll.models';

/**
 * US-PAY-001: Salary structures landing page (AC-3) — a clean, card-based grid
 * with Active/Inactive status badges (§8). Each card links into the structure;
 * a "Clone" action (FR-6) duplicates an existing structure. A prominent link
 * routes to the salary-components configuration screen.
 *
 * This is the default `/payroll` route. Tenant primary color on the header CTA.
 */
@Component({
  selector: 'app-salary-structures',
  standalone: true,
  imports: [CommonModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(6px)' }),
        animate(
          '220ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' }),
        ),
      ]),
    ]),
  ],
  template: `
    <div class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
      <!-- Header -->
      <div
        class="mb-6 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between"
      >
        <div>
          <h1 class="text-2xl font-semibold tracking-tight text-neutral-900">
            Salary structures
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Compensation frameworks made of earnings, deductions and statutory
            components.
          </p>
        </div>
        <div class="flex flex-wrap gap-2 self-start sm:self-auto">
          <a
            routerLink="/payroll/statutory"
            class="rounded-lg px-4 py-2 text-sm font-medium text-neutral-700 ring-1 ring-neutral-200 transition hover:bg-neutral-50"
          >
            Statutory config
          </a>
          <a
            routerLink="/payroll/components"
            class="rounded-lg px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:opacity-90"
            [style.background-color]="'var(--brand-primary)'"
          >
            Manage components
          </a>
        </div>
      </div>

      <!-- Loading -->
      @if (loading()) {
        <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          @for (i of [1, 2, 3]; track i) {
            <div class="h-36 animate-pulse rounded-xl bg-neutral-100"></div>
          }
        </div>
      } @else if (error()) {
        <div
          class="rounded-lg border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700"
          role="alert"
        >
          {{ error() }}
          <button class="ml-2 font-medium underline" (click)="load()">
            Retry
          </button>
        </div>
      } @else if (structures().length === 0) {
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-300 bg-white px-6 py-16 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">
            No salary structures yet
          </p>
          <p class="mt-1 text-sm text-neutral-500">
            Start by configuring your
            <a
              routerLink="/payroll/components"
              class="font-medium text-indigo-600 hover:underline"
              >salary components</a
            >, then build a structure from them.
          </p>
        </div>
      } @else {
        <div
          @fadeIn
          class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
        >
          @for (s of structures(); track s.id) {
            <div
              class="flex flex-col rounded-xl border border-neutral-200 bg-white p-5 shadow-sm transition hover:-translate-y-0.5 hover:shadow-md"
            >
              <div class="mb-3 flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <h2 class="truncate text-base font-semibold text-neutral-900">
                    {{ s.name }}
                  </h2>
                  <p class="truncate font-mono text-xs text-neutral-400">
                    {{ s.code }}
                  </p>
                </div>
                <span
                  class="shrink-0 inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset"
                  [class]="
                    s.isActive
                      ? 'bg-emerald-50 text-emerald-700 ring-emerald-600/20'
                      : 'bg-neutral-100 text-neutral-500 ring-neutral-400/20'
                  "
                >
                  {{ s.isActive ? 'Active' : 'Inactive' }}
                </span>
              </div>

              @if (s.description) {
                <p class="mb-3 line-clamp-2 text-sm text-neutral-600">
                  {{ s.description }}
                </p>
              }

              <div class="mt-auto flex items-center gap-3 text-xs text-neutral-500">
                @if (s.isDefault) {
                  <span
                    class="inline-flex items-center rounded-full bg-indigo-50 px-2 py-0.5 font-medium text-indigo-600"
                    >Default</span
                  >
                }
                @if (s.componentCount != null) {
                  <span>{{ s.componentCount }} components</span>
                }
                <span>Effective {{ s.effectiveFrom }}</span>
              </div>

              <div
                class="mt-4 flex items-center justify-end gap-2 border-t border-neutral-100 pt-3"
              >
                <button
                  type="button"
                  class="rounded-md px-2.5 py-1 text-xs font-medium text-neutral-600 transition hover:bg-neutral-100"
                  [disabled]="cloning() === s.id"
                  (click)="clone(s)"
                >
                  {{ cloning() === s.id ? 'Cloning…' : 'Clone' }}
                </button>
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class SalaryStructuresComponent implements OnInit {
  private readonly payroll = inject(PayrollService);
  private readonly toastr = inject(ToastrService);

  readonly structures = signal<ISalaryStructure[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly cloning = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.payroll.listStructures().subscribe({
      next: (list) => {
        this.structures.set(list);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load salary structures.');
        this.loading.set(false);
      },
    });
  }

  clone(s: ISalaryStructure): void {
    this.cloning.set(s.id);
    this.payroll.cloneStructure(s.id).subscribe({
      next: (created) => {
        this.cloning.set(null);
        this.toastr.success(`Cloned “${s.name}”.`);
        this.structures.set([created, ...this.structures()]);
      },
      error: () => {
        this.cloning.set(null);
        this.toastr.error('Could not clone the structure.');
      },
    });
  }
}
