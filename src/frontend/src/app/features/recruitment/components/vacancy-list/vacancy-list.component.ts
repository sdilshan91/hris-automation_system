import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { VacancyService } from '../../services/vacancy.service';
import { VacancyFormComponent } from '../vacancy-form/vacancy-form.component';
import {
  IVacancy,
  VacancyStatus,
  VACANCY_STATUS_OPTIONS,
  VACANCY_STATUS_BADGE,
} from '../../models/vacancy.models';

/**
 * US-REC-001: Recruiter-facing vacancy list.
 *
 * Table view with color-coded status badges (§8) and a status filter, plus an
 * inline status-change dropdown per row for quick actions. A card/grid toggle is
 * provided as a cheap nice-to-have. Create / edit open the right slide-over form.
 *
 * Role-gated to recruiters/HR via the route guard; the backend enforces the actual
 * Recruitment.Create.All / Manage.All permissions (BR-1).
 */
@Component({
  selector: 'app-vacancy-list',
  standalone: true,
  imports: [CommonModule, VacancyFormComponent],
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
            Vacancies
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Create, publish and manage open positions.
          </p>
        </div>
        <button
          type="button"
          class="self-start rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-indigo-700 sm:self-auto"
          (click)="openCreate()"
        >
          + New vacancy
        </button>
      </div>

      <!-- Toolbar: status filter + view toggle -->
      <div
        class="mb-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between"
      >
        <div class="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by status">
          <button
            type="button"
            class="filter-chip"
            [class.filter-active]="statusFilter() === null"
            (click)="setStatusFilter(null)"
          >
            All
          </button>
          @for (s of statuses; track s) {
            <button
              type="button"
              class="filter-chip"
              [class.filter-active]="statusFilter() === s"
              (click)="setStatusFilter(s)"
            >
              {{ s }}
            </button>
          }
        </div>

        <div class="flex items-center gap-1 rounded-lg bg-neutral-100 p-1" role="radiogroup" aria-label="View mode">
          <button
            type="button"
            class="view-btn"
            role="radio"
            [attr.aria-checked]="viewMode() === 'table'"
            [class.view-active]="viewMode() === 'table'"
            (click)="viewMode.set('table')"
          >
            Table
          </button>
          <button
            type="button"
            class="view-btn"
            role="radio"
            [attr.aria-checked]="viewMode() === 'grid'"
            [class.view-active]="viewMode() === 'grid'"
            (click)="viewMode.set('grid')"
          >
            Grid
          </button>
        </div>
      </div>

      <!-- Loading -->
      @if (loading()) {
        <div class="space-y-2">
          @for (n of [1, 2, 3]; track n) {
            <div class="h-14 animate-pulse rounded-lg bg-neutral-100"></div>
          }
        </div>
      } @else if (vacancies().length === 0) {
        <!-- Empty state -->
        <div
          @fadeIn
          class="rounded-xl border border-dashed border-neutral-200 bg-white py-16 text-center"
        >
          <p class="text-sm font-medium text-neutral-700">No vacancies yet</p>
          <p class="mt-1 text-sm text-neutral-500">
            Create your first vacancy to start hiring.
          </p>
        </div>
      } @else if (viewMode() === 'table') {
        <!-- Table view -->
        <div
          @fadeIn
          class="overflow-hidden rounded-xl border border-neutral-200 bg-white shadow-sm"
        >
          <table class="min-w-full divide-y divide-neutral-100">
            <thead class="bg-neutral-50">
              <tr>
                <th class="th">Reference</th>
                <th class="th">Title</th>
                <th class="th hidden md:table-cell">Department</th>
                <th class="th hidden lg:table-cell">Headcount</th>
                <th class="th">Status</th>
                <th class="th text-right">Actions</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-neutral-50">
              @for (v of vacancies(); track v.id) {
                <tr class="transition hover:bg-neutral-50/60">
                  <td class="td font-mono text-xs text-neutral-500">
                    {{ v.referenceNumber }}
                  </td>
                  <td class="td">
                    <button
                      type="button"
                      class="text-left font-medium text-neutral-900 hover:text-indigo-600"
                      (click)="openEdit(v)"
                    >
                      {{ v.title }}
                    </button>
                  </td>
                  <td class="td hidden md:table-cell text-neutral-600">
                    {{ v.departmentName || '—' }}
                  </td>
                  <td class="td hidden lg:table-cell text-neutral-600">
                    {{ v.filledCount }}/{{ v.headcount ?? '—' }}
                  </td>
                  <td class="td">
                    <span class="badge ring-1 ring-inset" [class]="badgeClass(v.status)">
                      {{ v.status }}
                    </span>
                  </td>
                  <td class="td">
                    <div class="flex items-center justify-end gap-2">
                      <select
                        class="status-select"
                        [attr.aria-label]="'Change status for ' + v.title"
                        [value]="v.status"
                        (change)="onInlineStatusChange(v, $event)"
                      >
                        @for (s of statuses; track s) {
                          <option [value]="s">{{ s }}</option>
                        }
                      </select>
                      <button
                        type="button"
                        class="rounded-md px-2 py-1 text-sm text-neutral-500 transition hover:bg-neutral-100 hover:text-neutral-800"
                        (click)="openEdit(v)"
                      >
                        Edit
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <!-- Grid view -->
        <div @fadeIn class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          @for (v of vacancies(); track v.id) {
            <div
              class="flex flex-col rounded-xl border border-neutral-200 bg-white p-4 shadow-sm transition hover:shadow-md"
            >
              <div class="flex items-start justify-between gap-2">
                <span class="font-mono text-xs text-neutral-400">
                  {{ v.referenceNumber }}
                </span>
                <span class="badge ring-1 ring-inset" [class]="badgeClass(v.status)">
                  {{ v.status }}
                </span>
              </div>
              <button
                type="button"
                class="mt-2 text-left text-base font-semibold text-neutral-900 hover:text-indigo-600"
                (click)="openEdit(v)"
              >
                {{ v.title }}
              </button>
              <p class="mt-1 text-sm text-neutral-500">
                {{ v.departmentName || 'No department' }}
              </p>
              <div class="mt-auto flex items-center justify-between pt-4">
                <span class="text-xs text-neutral-500">
                  {{ v.filledCount }}/{{ v.headcount ?? '—' }} filled
                </span>
                <button
                  type="button"
                  class="text-sm font-medium text-indigo-600 hover:text-indigo-700"
                  (click)="openEdit(v)"
                >
                  Edit
                </button>
              </div>
            </div>
          }
        </div>
      }
    </div>

    <!-- Slide-over create/edit form -->
    @if (showForm()) {
      <app-vacancy-form
        [vacancy]="editing()"
        (saved)="onSaved()"
        (closed)="closeForm()"
      />
    }
  `,
  styles: [
    `
      :host {
        display: block;
      }
      .th {
        padding: 0.75rem 1rem;
        text-align: left;
        font-size: 0.6875rem;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.05em;
        color: #737373;
      }
      .td {
        padding: 0.875rem 1rem;
        font-size: 0.875rem;
        vertical-align: middle;
      }
      .badge {
        display: inline-flex;
        align-items: center;
        border-radius: 9999px;
        padding: 0.125rem 0.625rem;
        font-size: 0.75rem;
        font-weight: 500;
      }
      .filter-chip {
        border-radius: 9999px;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.3125rem 0.875rem;
        font-size: 0.8125rem;
        color: #525252;
        transition: all 150ms ease;
      }
      .filter-chip:hover {
        background: #fafafa;
      }
      .filter-active {
        background: #eef2ff;
        border-color: #c7d2fe;
        color: #4338ca;
      }
      .view-btn {
        border-radius: 0.375rem;
        padding: 0.25rem 0.75rem;
        font-size: 0.8125rem;
        color: #525252;
        transition: all 150ms ease;
      }
      .view-active {
        background: #fff;
        color: #171717;
        box-shadow: 0 1px 2px rgb(0 0 0 / 0.06);
      }
      .status-select {
        border-radius: 0.375rem;
        border: 1px solid #e5e5e5;
        background: #fff;
        padding: 0.25rem 0.5rem;
        font-size: 0.8125rem;
        color: #404040;
      }
      .status-select:focus {
        outline: none;
        border-color: #818cf8;
        box-shadow: 0 0 0 2px #e0e7ff;
      }
    `,
  ],
})
export class VacancyListComponent implements OnInit {
  private readonly vacancyService = inject(VacancyService);
  private readonly toastr = inject(ToastrService);

  readonly statuses: VacancyStatus[] = VACANCY_STATUS_OPTIONS;

  readonly vacancies = signal<IVacancy[]>([]);
  readonly loading = signal(true);
  readonly statusFilter = signal<VacancyStatus | null>(null);
  readonly viewMode = signal<'table' | 'grid'>('table');

  readonly showForm = signal(false);
  readonly editing = signal<IVacancy | null>(null);

  ngOnInit(): void {
    this.load();
  }

  // ─── Data ────────────────────────────────────────────────

  load(): void {
    this.loading.set(true);
    const status = this.statusFilter();
    this.vacancyService
      .listVacancies(status ? { status } : {})
      .subscribe({
        next: (page) => {
          this.vacancies.set(page.data);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.loading.set(false);
          this.toastr.error(VacancyService.parseErrorMessage(err));
        },
      });
  }

  setStatusFilter(status: VacancyStatus | null): void {
    this.statusFilter.set(status);
    this.load();
  }

  // ─── Inline status change (quick action, §8) ─────────────

  onInlineStatusChange(vacancy: IVacancy, event: Event): void {
    const next = (event.target as HTMLSelectElement).value as VacancyStatus;
    if (next === vacancy.status) {
      return;
    }
    this.vacancyService.changeStatus(vacancy.id, next).subscribe({
      next: (updated) => {
        this.vacancies.update((list) =>
          list.map((v) => (v.id === updated.id ? updated : v)),
        );
        this.toastr.success(`Status changed to ${updated.status}.`);
        // If a status filter is active and the row no longer matches, refresh.
        if (this.statusFilter() && this.statusFilter() !== updated.status) {
          this.load();
        }
      },
      error: (err: HttpErrorResponse) => {
        // Revert the <select> by re-rendering the unchanged list.
        this.vacancies.update((list) => [...list]);
        this.toastr.error(VacancyService.parseErrorMessage(err));
      },
    });
  }

  // ─── Form drawer ─────────────────────────────────────────

  openCreate(): void {
    this.editing.set(null);
    this.showForm.set(true);
  }

  openEdit(vacancy: IVacancy): void {
    this.editing.set(vacancy);
    this.showForm.set(true);
  }

  closeForm(): void {
    this.showForm.set(false);
    this.editing.set(null);
  }

  onSaved(): void {
    this.closeForm();
    this.load();
  }

  // ─── View helpers ────────────────────────────────────────

  badgeClass(status: VacancyStatus): string {
    return VACANCY_STATUS_BADGE[status];
  }
}
