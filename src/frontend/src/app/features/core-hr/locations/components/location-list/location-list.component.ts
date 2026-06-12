import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { LocationService } from '../../services/location.service';
import { ILocation, ILocationErrorResponse } from '../../models/location.models';
import { LocationFormComponent } from '../location-form/location-form.component';

/**
 * US-CHR-007: Locations list page.
 *
 * Displays locations as a card-based table with search.
 * Columns: Name, City, Country, Time Zone, Employee Count, Status, Actions.
 * Supports creating, editing, and deactivating locations via a slide-over
 * form panel.
 *
 * Role-gated to Tenant Admin / HR Officer via the route guard.
 */
@Component({
  selector: 'app-location-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    LocationFormComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate(
          '250ms ease-out',
          style({ opacity: 1, transform: 'translateY(0)' })
        ),
      ]),
    ]),
    trigger('slideOver', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(100%)' }),
        animate(
          '300ms ease-out',
          style({ opacity: 1, transform: 'translateX(0)' })
        ),
      ]),
      transition(':leave', [
        animate(
          '200ms ease-in',
          style({ opacity: 0, transform: 'translateX(100%)' })
        ),
      ]),
    ]),
    trigger('overlayFade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [
        animate('150ms ease-in', style({ opacity: 0 })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container">
      <!-- Header -->
      <div class="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6">
        <div>
          <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">
            Locations
          </h1>
          <p class="mt-1 text-sm text-neutral-500">
            Manage office locations and branches across your organization.
          </p>
        </div>
        <button
          type="button"
          class="btn-primary"
          (click)="openCreate()"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
          </svg>
          Add Location
        </button>
      </div>

      <!-- Search bar -->
      @if (!isLoading() && !loadError() && locations().length > 0) {
        <div class="mb-5">
          <div class="relative max-w-sm">
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-neutral-400 pointer-events-none"
              aria-hidden="true"
            >
              <path fill-rule="evenodd" d="M9 3.5a5.5 5.5 0 1 0 0 11 5.5 5.5 0 0 0 0-11ZM2 9a7 7 0 1 1 12.452 4.391l3.328 3.329a.75.75 0 1 1-1.06 1.06l-3.329-3.328A7 7 0 0 1 2 9Z" clip-rule="evenodd" />
            </svg>
            <input
              type="search"
              class="input-notion pl-9"
              placeholder="Search locations..."
              [ngModel]="searchQuery()"
              (ngModelChange)="searchQuery.set($event)"
              aria-label="Search locations"
            />
          </div>
        </div>
      }

      <!-- Loading skeleton -->
      @if (isLoading()) {
        <div class="card-notion overflow-hidden">
          <div class="animate-pulse space-y-4 p-2">
            <div class="h-5 bg-neutral-100 rounded w-1/3 mb-4"></div>
            @for (i of skeletonItems; track i) {
              <div class="flex items-center gap-4">
                <div class="h-4 bg-neutral-50 rounded w-1/4"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/6"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/6"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/5"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/12"></div>
                <div class="h-4 bg-neutral-50 rounded w-1/12"></div>
              </div>
            }
          </div>
        </div>
      }

      <!-- Error state -->
      @if (loadError()) {
        <div class="card-notion text-center py-12">
          <div class="w-12 h-12 rounded-full bg-red-50 flex items-center justify-center mx-auto mb-4">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-6 h-6 text-red-500" aria-hidden="true">
              <path fill-rule="evenodd" d="M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Zm-8-5a.75.75 0 0 1 .75.75v4.5a.75.75 0 0 1-1.5 0v-4.5A.75.75 0 0 1 10 5Zm0 10a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z" clip-rule="evenodd" />
            </svg>
          </div>
          <p class="text-sm text-neutral-600">{{ loadError() }}</p>
          <button class="btn-secondary mt-4" (click)="loadLocations()">
            Try Again
          </button>
        </div>
      }

      <!-- Content -->
      @if (!isLoading() && !loadError()) {
        @if (locations().length === 0) {
          <!-- Empty state -->
          <div class="card-notion text-center py-12">
            <div class="w-14 h-14 rounded-full bg-neutral-50 flex items-center justify-center mx-auto mb-4">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-7 h-7 text-neutral-400" aria-hidden="true">
                <path fill-rule="evenodd" d="m9.69 18.933.003.001C9.89 19.02 10 19 10 19s.11.02.308-.066l.002-.001.006-.003.018-.008a5.741 5.741 0 0 0 .281-.14c.186-.096.446-.24.757-.433.62-.384 1.445-.966 2.274-1.765C15.302 14.988 17 12.493 17 9A7 7 0 1 0 3 9c0 3.492 1.698 5.988 3.355 7.584a13.731 13.731 0 0 0 2.273 1.765 11.842 11.842 0 0 0 .976.544l.062.029.018.008.006.003ZM10 11.25a2.25 2.25 0 1 0 0-4.5 2.25 2.25 0 0 0 0 4.5Z" clip-rule="evenodd" />
              </svg>
            </div>
            <p class="text-sm font-medium text-neutral-700 mb-1">No locations yet</p>
            <p class="text-xs text-neutral-400 mb-4">
              Add your first office location to assign employees and apply location-specific policies.
            </p>
            <button type="button" class="btn-primary" (click)="openCreate()">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
                <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z" />
              </svg>
              Add Location
            </button>
          </div>
        } @else {
          <!-- Table card (AC-1, FR-7) -->
          <div class="card-notion overflow-hidden p-0" @fadeSlideIn>
            <!-- Desktop table -->
            <div class="hidden md:block overflow-x-auto">
              <table class="w-full" role="table">
                <thead>
                  <tr class="border-b border-neutral-100">
                    <th class="th-notion text-left">Name</th>
                    <th class="th-notion text-left">City</th>
                    <th class="th-notion text-left">Country</th>
                    <th class="th-notion text-left">Time Zone</th>
                    <th class="th-notion text-center">Employees</th>
                    <th class="th-notion text-center">Status</th>
                    <th class="th-notion text-right">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  @for (loc of filteredLocations(); track loc.locationId) {
                    <tr
                      class="table-row-notion group"
                      [class.opacity-60]="!loc.isActive"
                      (click)="openEdit(loc)"
                      (keydown.enter)="openEdit(loc)"
                      tabindex="0"
                      role="button"
                      [attr.aria-label]="'Edit location: ' + loc.name"
                    >
                      <td class="td-notion">
                        <span class="font-medium text-neutral-900">{{ loc.name }}</span>
                        @if (loc.addressLine1) {
                          <p class="text-xs text-neutral-400 mt-0.5 line-clamp-1">{{ loc.addressLine1 }}</p>
                        }
                      </td>
                      <td class="td-notion text-neutral-500">
                        {{ loc.city || '—' }}
                      </td>
                      <td class="td-notion text-neutral-500">
                        {{ loc.country || '—' }}
                      </td>
                      <td class="td-notion text-neutral-500">
                        <span class="text-xs">{{ loc.timeZone }}</span>
                      </td>
                      <td class="td-notion text-center">
                        <button
                          type="button"
                          class="inline-flex items-center justify-center px-2 py-0.5 rounded-full text-xs font-medium transition-colors duration-150"
                          [ngClass]="loc.employeeCount > 0 ? 'bg-brand-50 text-brand-700 hover:bg-brand-100 cursor-pointer' : 'bg-neutral-100 text-neutral-500 cursor-default'"
                          (click)="navigateToDirectory(loc, $event)"
                          [attr.aria-label]="loc.employeeCount + ' employees at ' + loc.name"
                          [attr.title]="loc.employeeCount > 0 ? 'View employees at this location' : ''"
                        >
                          {{ loc.employeeCount }}
                        </button>
                      </td>
                      <td class="td-notion text-center">
                        @if (loc.isActive) {
                          <span class="badge-active">Active</span>
                        } @else {
                          <span class="badge-inactive">Inactive</span>
                        }
                      </td>
                      <td class="td-notion text-right">
                        <div class="flex items-center justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                          <button
                            type="button"
                            class="action-btn"
                            (click)="openEdit(loc); $event.stopPropagation()"
                            [attr.aria-label]="'Edit location: ' + loc.name"
                            title="Edit"
                          >
                            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                              <path d="m5.433 13.917 1.262-3.155A4 4 0 0 1 7.58 9.42l6.92-6.918a2.121 2.121 0 0 1 3 3l-6.92 6.918c-.383.383-.84.685-1.343.886l-3.154 1.262a.5.5 0 0 1-.65-.65Z" />
                              <path d="M3.5 5.75c0-.69.56-1.25 1.25-1.25h5.5a.75.75 0 0 0 0-1.5h-5.5A2.75 2.75 0 0 0 2 5.75v8.5A2.75 2.75 0 0 0 4.75 17h8.5A2.75 2.75 0 0 0 16 14.25v-5.5a.75.75 0 0 0-1.5 0v5.5c0 .69-.56 1.25-1.25 1.25h-8.5c-.69 0-1.25-.56-1.25-1.25v-8.5Z" />
                            </svg>
                          </button>
                          @if (loc.isActive) {
                            <button
                              type="button"
                              class="action-btn action-btn-danger"
                              (click)="confirmDeactivate(loc, $event)"
                              [attr.aria-label]="'Deactivate location: ' + loc.name"
                              title="Deactivate"
                            >
                              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4" aria-hidden="true">
                                <path d="M2 3a1 1 0 0 0-1 1v1a1 1 0 0 0 1 1h16a1 1 0 0 0 1-1V4a1 1 0 0 0-1-1H2Z" />
                                <path fill-rule="evenodd" d="M2 7.5h16l-.811 7.71a2 2 0 0 1-1.99 1.79H4.802a2 2 0 0 1-1.99-1.79L2 7.5Zm5.22 1.72a.75.75 0 0 1 1.06 0L10 10.94l1.72-1.72a.75.75 0 1 1 1.06 1.06L11.06 12l1.72 1.72a.75.75 0 1 1-1.06 1.06L10 13.06l-1.72 1.72a.75.75 0 0 1-1.06-1.06L8.94 12l-1.72-1.72a.75.75 0 0 1 0-1.06Z" clip-rule="evenodd" />
                              </svg>
                            </button>
                          }
                        </div>
                      </td>
                    </tr>
                  } @empty {
                    <tr>
                      <td colspan="7" class="td-notion text-center text-neutral-400 py-8">
                        No locations match your search.
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <!-- Mobile card list -->
            <div class="md:hidden divide-y divide-neutral-100">
              @for (loc of filteredLocations(); track loc.locationId) {
                <div
                  class="p-4 hover:bg-neutral-50 transition-colors duration-150 cursor-pointer"
                  [class.opacity-60]="!loc.isActive"
                  (click)="openEdit(loc)"
                  (keydown.enter)="openEdit(loc)"
                  tabindex="0"
                  role="button"
                  [attr.aria-label]="'Edit location: ' + loc.name"
                >
                  <div class="flex items-start justify-between mb-1">
                    <h3 class="text-sm font-semibold text-neutral-900">
                      {{ loc.name }}
                    </h3>
                    @if (loc.isActive) {
                      <span class="badge-active">Active</span>
                    } @else {
                      <span class="badge-inactive">Inactive</span>
                    }
                  </div>
                  @if (loc.addressLine1 || loc.city || loc.country) {
                    <p class="text-xs text-neutral-400 mb-1 line-clamp-2">
                      {{ formatAddress(loc) }}
                    </p>
                  }
                  <div class="flex flex-wrap gap-x-4 gap-y-1 text-xs text-neutral-400 mt-2">
                    <span class="flex items-center gap-1" title="Time zone">
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                        <path fill-rule="evenodd" d="M1 8a7 7 0 1 1 14 0A7 7 0 0 1 1 8Zm7.75-4.25a.75.75 0 0 0-1.5 0V8c0 .414.336.75.75.75h3.25a.75.75 0 0 0 0-1.5h-2.5v-3.5Z" clip-rule="evenodd" />
                      </svg>
                      {{ loc.timeZone }}
                    </span>
                    <button
                      type="button"
                      class="flex items-center gap-1"
                      [ngClass]="loc.employeeCount > 0 ? 'text-brand-600 hover:text-brand-800' : 'text-neutral-400'"
                      (click)="navigateToDirectory(loc, $event)"
                    >
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" class="w-3.5 h-3.5" aria-hidden="true">
                        <path fill-rule="evenodd" d="M15 8A7 7 0 1 1 1 8a7 7 0 0 1 14 0Zm-5-2a2 2 0 1 1-4 0 2 2 0 0 1 4 0Zm-2 9c-2.841 0-4.263-.722-5.004-1.483-.173-.177-.18-.454-.023-.644A4.504 4.504 0 0 1 6.5 10.5h3a4.504 4.504 0 0 1 3.527 2.373c.157.19.15.467-.023.644C12.263 14.278 10.841 15 8 15Z" clip-rule="evenodd" />
                      </svg>
                      {{ loc.employeeCount }} employees
                    </button>
                  </div>
                  <div class="flex items-center gap-2 mt-3">
                    @if (loc.isActive) {
                      <button
                        type="button"
                        class="text-xs text-red-500 hover:text-red-700 transition-colors"
                        (click)="confirmDeactivate(loc, $event)"
                        [attr.aria-label]="'Deactivate location: ' + loc.name"
                      >
                        Deactivate
                      </button>
                    }
                  </div>
                </div>
              } @empty {
                <div class="p-6 text-center text-sm text-neutral-400">
                  No locations match your search.
                </div>
              }
            </div>
          </div>
        }
      }

      <!-- Slide-over form panel -->
      @if (formOpen()) {
        <div
          class="fixed inset-0 z-50"
          (keydown.escape)="closeForm()"
        >
          <!-- Overlay -->
          <div
            class="fixed inset-0 bg-black/20 backdrop-blur-sm"
            @overlayFade
            (click)="closeForm()"
            aria-hidden="true"
          ></div>

          <!-- Slide-over panel -->
          <div
            class="fixed inset-y-0 right-0 w-full sm:w-[30rem] bg-white shadow-notion-lg overflow-y-auto"
            @slideOver
            role="dialog"
            aria-modal="true"
            [attr.aria-label]="editingLocation() ? 'Edit location' : 'Create location'"
          >
            <app-location-form
              [location]="editingLocation()"
              (saved)="onFormSaved()"
              (cancelled)="closeForm()"
            />
          </div>
        </div>
      }

      <!-- Deactivate confirmation dialog (AC-3) -->
      @if (locationToDeactivate()) {
        <div
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/20 backdrop-blur-sm px-4"
          (click)="cancelDeactivate()"
          (keydown.escape)="cancelDeactivate()"
          role="dialog"
          aria-modal="true"
          aria-labelledby="deactivate-dialog-title"
        >
          <div
            class="w-full max-w-md rounded-xl bg-white shadow-notion-lg p-6"
            (click)="$event.stopPropagation()"
          >
            <h3
              id="deactivate-dialog-title"
              class="text-lg font-semibold text-neutral-900 mb-2"
            >
              Deactivate Location
            </h3>
            <p class="text-sm text-neutral-600 mb-1">
              Are you sure you want to deactivate
              <strong>{{ locationToDeactivate()!.name }}</strong>?
            </p>
            <p class="text-xs text-neutral-400 mt-2">
              Deactivated locations cannot be assigned to new employees but remain visible on existing records.
            </p>
            <div class="flex justify-end gap-3 mt-6">
              <button
                type="button"
                class="btn-secondary"
                (click)="cancelDeactivate()"
              >
                Cancel
              </button>
              <button
                type="button"
                class="btn-danger"
                (click)="deactivateLocation()"
                [disabled]="isDeactivating()"
              >
                @if (isDeactivating()) {
                  <svg class="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" aria-hidden="true">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                  </svg>
                  Deactivating...
                } @else {
                  Deactivate
                }
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
    }

    .line-clamp-1 {
      display: -webkit-box;
      -webkit-line-clamp: 1;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .line-clamp-2 {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    /* --- Table styles (Notion-inspired) --- */

    .th-notion {
      @apply px-4 py-3 text-xs font-semibold uppercase tracking-wider text-neutral-400
        whitespace-nowrap;
    }

    .td-notion {
      @apply px-4 py-3.5 text-sm;
    }

    .table-row-notion {
      @apply border-b border-neutral-50 hover:bg-neutral-50/50 transition-colors
        duration-150 cursor-pointer;
    }

    .table-row-notion:last-child {
      @apply border-b-0;
    }

    /* --- Badges --- */

    .badge-active {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium
        bg-green-50 text-green-700 whitespace-nowrap;
    }

    .badge-inactive {
      @apply inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium
        bg-neutral-100 text-neutral-500 whitespace-nowrap;
    }

    /* --- Action buttons --- */

    .action-btn {
      @apply w-7 h-7 rounded-md flex items-center justify-center
        text-neutral-400 transition-colors duration-150
        hover:text-neutral-600 hover:bg-neutral-100;
    }

    .action-btn-danger {
      @apply hover:text-red-500 hover:bg-red-50;
    }

    .btn-danger {
      @apply inline-flex items-center justify-center rounded-lg bg-red-600 px-4 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-red-700 focus-visible:outline-2 focus-visible:outline-offset-2
        focus-visible:outline-red-600 disabled:opacity-50 disabled:cursor-not-allowed;
    }
  `],
})
export class LocationListComponent implements OnInit {
  private readonly locationService = inject(LocationService);
  private readonly toastr = inject(ToastrService);
  private readonly router = inject(Router);

  readonly locations = signal<ILocation[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');
  readonly searchQuery = signal('');

  // Form slide-over state
  readonly formOpen = signal(false);
  readonly editingLocation = signal<ILocation | null>(null);

  // Deactivation dialog state
  readonly locationToDeactivate = signal<ILocation | null>(null);
  readonly isDeactivating = signal(false);

  /** Filter locations by search query */
  readonly filteredLocations = computed(() => {
    const query = this.searchQuery().toLowerCase().trim();
    const locs = this.locations();
    if (!query) return locs;
    return locs.filter(
      (loc) =>
        loc.name.toLowerCase().includes(query) ||
        (loc.city && loc.city.toLowerCase().includes(query)) ||
        (loc.country && loc.country.toLowerCase().includes(query)) ||
        loc.timeZone.toLowerCase().includes(query)
    );
  });

  /** Skeleton loading placeholder items */
  readonly skeletonItems = [1, 2, 3, 4, 5, 6];

  ngOnInit(): void {
    this.loadLocations();
  }

  loadLocations(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    this.locationService.getLocations().subscribe({
      next: (locations) => {
        this.locations.set(locations);
        this.isLoading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        this.isLoading.set(false);
        this.loadError.set(
          err.error?.message || 'Failed to load locations. Please try again.'
        );
      },
    });
  }

  // --- Form Slide-over ----------------------------------------

  openCreate(): void {
    this.editingLocation.set(null);
    this.formOpen.set(true);
  }

  openEdit(location: ILocation): void {
    this.editingLocation.set(location);
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editingLocation.set(null);
  }

  onFormSaved(): void {
    this.closeForm();
    this.loadLocations();
  }

  // --- Deactivation -------------------------------------------

  confirmDeactivate(location: ILocation, event?: Event): void {
    event?.stopPropagation();
    this.locationToDeactivate.set(location);
  }

  cancelDeactivate(): void {
    this.locationToDeactivate.set(null);
  }

  deactivateLocation(): void {
    const loc = this.locationToDeactivate();
    if (!loc) return;

    this.isDeactivating.set(true);

    this.locationService.deactivateLocation(loc.locationId).subscribe({
      next: () => {
        this.toastr.success(`"${loc.name}" has been deactivated.`);
        this.locationToDeactivate.set(null);
        this.isDeactivating.set(false);
        this.loadLocations();
      },
      error: (err: HttpErrorResponse) => {
        this.isDeactivating.set(false);
        const body = err.error as ILocationErrorResponse | undefined;
        if (body?.code === 'has_active_employees') {
          // AC-3: warn when location has active employees assigned
          this.toastr.warning(
            body.message ||
              `This location has ${body.employeeCount ?? 'some'} active employees. Reassign them before deactivating.`
          );
        } else {
          this.toastr.error(
            body?.message || 'Failed to deactivate location.'
          );
        }
      },
    });
  }

  // --- Navigation to Employee Directory (AC-2, FR-7) ----------

  /** Format address parts into a comma-separated string */
  formatAddress(loc: ILocation): string {
    return [loc.addressLine1, loc.city, loc.country]
      .filter((v) => !!v)
      .join(', ');
  }

  /**
   * Navigate to employee directory filtered by this location name.
   * Aligns with US-CHR-003's `location` query parameter.
   */
  navigateToDirectory(location: ILocation, event: Event): void {
    event.stopPropagation();
    if (location.employeeCount <= 0) return;
    this.router.navigate(['/employees'], {
      queryParams: { location: location.name },
    });
  }
}
