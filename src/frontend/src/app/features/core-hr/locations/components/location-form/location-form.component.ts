import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  input,
  output,
  computed,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ReactiveFormsModule,
  FormsModule,
  FormBuilder,
  FormGroup,
  Validators,
} from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { LocationService } from '../../services/location.service';
import {
  ILocation,
  ICreateLocationRequest,
  IUpdateLocationRequest,
  ILocationErrorResponse,
} from '../../models/location.models';
import {
  TIME_ZONES,
  COUNTRIES,
  ITimeZoneOption,
  ICountryOption,
} from '../../models/location-data.constants';

/**
 * US-CHR-007 AC-1: Location create/edit form as a slide-over panel.
 *
 * Fields: Location Name (required, max 150), Address section (collapsible),
 * Time Zone (required, searchable dropdown), Phone, Active toggle.
 */
@Component({
  selector: 'app-location-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="form-container">
      <!-- Header -->
      <div class="form-header">
        <h2 class="form-title">
          {{ location() ? 'Edit Location' : 'Add Location' }}
        </h2>
        <button
          type="button"
          class="close-btn"
          (click)="cancelled.emit()"
          aria-label="Close panel"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5" aria-hidden="true">
            <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
          </svg>
        </button>
      </div>

      <!-- Form body -->
      <form [formGroup]="form" (ngSubmit)="onSubmit()" class="form-body">
        <!-- Location Name (FR-2: unique within tenant) -->
        <div class="form-section">
          <label class="label-notion" for="loc-name">
            Location Name <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <input
            id="loc-name"
            type="text"
            formControlName="name"
            class="input-notion"
            placeholder="e.g. Headquarters, Branch Office"
            maxlength="150"
            autocomplete="off"
          />
          @if (form.get('name')?.invalid && form.get('name')?.touched) {
            <p class="field-error">
              @if (form.get('name')?.hasError('required')) {
                Location name is required.
              } @else if (form.get('name')?.hasError('maxlength')) {
                Location name cannot exceed 150 characters.
              }
            </p>
          }
          @if (duplicateNameError()) {
            <p class="field-error">{{ duplicateNameError() }}</p>
          }
        </div>

        <!-- Address section (collapsible group, AC-1 / FR-3) -->
        <div class="form-section">
          <button
            type="button"
            class="flex items-center gap-2 text-sm font-medium text-neutral-700 hover:text-neutral-900 transition-colors w-full text-left"
            (click)="addressExpanded.set(!addressExpanded())"
            [attr.aria-expanded]="addressExpanded()"
            aria-controls="address-section"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              viewBox="0 0 20 20"
              fill="currentColor"
              class="w-4 h-4 transition-transform duration-200"
              [class.rotate-90]="addressExpanded()"
              aria-hidden="true"
            >
              <path fill-rule="evenodd" d="M7.21 14.77a.75.75 0 0 1 .02-1.06L11.168 10 7.23 6.29a.75.75 0 1 1 1.04-1.08l4.5 4.25a.75.75 0 0 1 0 1.08l-4.5 4.25a.75.75 0 0 1-1.06-.02Z" clip-rule="evenodd" />
            </svg>
            Address
          </button>
          @if (addressExpanded()) {
            <div id="address-section" class="mt-3 space-y-3 pl-6 border-l-2 border-neutral-100">
              <!-- Address Line 1 -->
              <div>
                <label class="label-notion" for="loc-addr1">Street / Address Line 1</label>
                <input
                  id="loc-addr1"
                  type="text"
                  formControlName="addressLine1"
                  class="input-notion"
                  placeholder="123 Main St"
                  maxlength="250"
                />
              </div>
              <!-- Address Line 2 -->
              <div>
                <label class="label-notion" for="loc-addr2">Address Line 2</label>
                <input
                  id="loc-addr2"
                  type="text"
                  formControlName="addressLine2"
                  class="input-notion"
                  placeholder="Suite 100"
                  maxlength="250"
                />
              </div>
              <!-- City + State row -->
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div>
                  <label class="label-notion" for="loc-city">City</label>
                  <input
                    id="loc-city"
                    type="text"
                    formControlName="city"
                    class="input-notion"
                    placeholder="City"
                    maxlength="100"
                  />
                </div>
                <div>
                  <label class="label-notion" for="loc-state">State / Province</label>
                  <input
                    id="loc-state"
                    type="text"
                    formControlName="stateProvince"
                    class="input-notion"
                    placeholder="State"
                    maxlength="100"
                  />
                </div>
              </div>
              <!-- Country + Postal Code row -->
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div class="relative">
                  <label class="label-notion" for="loc-country">Country</label>
                  <input
                    id="loc-country"
                    type="text"
                    class="input-notion"
                    placeholder="Search country..."
                    [ngModel]="countrySearch()"
                    (ngModelChange)="onCountrySearch($event)"
                    [ngModelOptions]="{ standalone: true }"
                    (focus)="countryDropdownOpen.set(true)"
                    (blur)="onCountryBlur()"
                    autocomplete="off"
                    role="combobox"
                    aria-autocomplete="list"
                    [attr.aria-expanded]="countryDropdownOpen()"
                    aria-controls="country-listbox"
                  />
                  @if (countryDropdownOpen() && filteredCountries().length > 0) {
                    <ul
                      id="country-listbox"
                      role="listbox"
                      class="dropdown-list"
                    >
                      @for (c of filteredCountries(); track c.code) {
                        <li
                          role="option"
                          class="dropdown-item"
                          [class.dropdown-item-selected]="form.get('country')?.value === c.name"
                          (mousedown)="selectCountry(c)"
                        >
                          {{ c.name }}
                        </li>
                      }
                    </ul>
                  }
                </div>
                <div>
                  <label class="label-notion" for="loc-postal">Postal Code</label>
                  <input
                    id="loc-postal"
                    type="text"
                    formControlName="postalCode"
                    class="input-notion"
                    placeholder="Postal / ZIP"
                    maxlength="20"
                  />
                </div>
              </div>
            </div>
          }
        </div>

        <!-- Time Zone (required, FR-4, searchable dropdown) -->
        <div class="form-section">
          <label class="label-notion" for="loc-tz">
            Time Zone <span class="text-red-500" aria-hidden="true">*</span>
          </label>
          <div class="relative">
            <input
              id="loc-tz"
              type="text"
              class="input-notion"
              placeholder="Search time zone..."
              [ngModel]="tzSearch()"
              (ngModelChange)="onTzSearch($event)"
              [ngModelOptions]="{ standalone: true }"
              (focus)="tzDropdownOpen.set(true)"
              (blur)="onTzBlur()"
              autocomplete="off"
              role="combobox"
              aria-autocomplete="list"
              [attr.aria-expanded]="tzDropdownOpen()"
              aria-controls="tz-listbox"
            />
            @if (tzDropdownOpen() && filteredTimeZones().length > 0) {
              <ul
                id="tz-listbox"
                role="listbox"
                class="dropdown-list"
              >
                @if (commonTzFiltered().length > 0) {
                  <li class="dropdown-group-label">Common</li>
                  @for (tz of commonTzFiltered(); track tz.id) {
                    <li
                      role="option"
                      class="dropdown-item"
                      [class.dropdown-item-selected]="form.get('timeZone')?.value === tz.id"
                      (mousedown)="selectTimeZone(tz)"
                    >
                      <span class="font-medium">{{ tz.label }}</span>
                      <span class="text-neutral-400 text-xs ml-2">{{ tz.utcOffset }} &middot; {{ tz.id }}</span>
                    </li>
                  }
                }
                @if (otherTzFiltered().length > 0) {
                  <li class="dropdown-group-label">All Zones</li>
                  @for (tz of otherTzFiltered(); track tz.id) {
                    <li
                      role="option"
                      class="dropdown-item"
                      [class.dropdown-item-selected]="form.get('timeZone')?.value === tz.id"
                      (mousedown)="selectTimeZone(tz)"
                    >
                      <span class="font-medium">{{ tz.label }}</span>
                      <span class="text-neutral-400 text-xs ml-2">{{ tz.utcOffset }} &middot; {{ tz.id }}</span>
                    </li>
                  }
                }
              </ul>
            }
          </div>
          @if (form.get('timeZone')?.invalid && form.get('timeZone')?.touched) {
            <p class="field-error">Time zone is required.</p>
          }
        </div>

        <!-- Phone -->
        <div class="form-section">
          <label class="label-notion" for="loc-phone">Phone</label>
          <input
            id="loc-phone"
            type="tel"
            formControlName="phone"
            class="input-notion"
            placeholder="+1 555 123 4567"
            maxlength="20"
          />
        </div>

        <!-- Active Toggle -->
        <div class="form-section">
          <div class="toggle-row">
            <div class="toggle-label-block">
              <label class="label-notion mb-0" for="loc-active">
                Active
              </label>
              <p class="field-hint">
                Inactive locations cannot be assigned to new employees (BR-5).
              </p>
            </div>
            <label class="toggle-switch" for="loc-active">
              <input
                id="loc-active"
                type="checkbox"
                formControlName="isActive"
                class="toggle-input"
              />
              <span class="toggle-slider"></span>
            </label>
          </div>
        </div>

        <!-- Form actions -->
        <div class="form-actions">
          <button
            type="button"
            class="btn-secondary"
            (click)="cancelled.emit()"
          >
            Cancel
          </button>
          <button
            type="submit"
            class="btn-primary"
            [disabled]="isSaving() || form.invalid || form.pristine"
          >
            @if (isSaving()) {
              <span class="btn-spinner"></span>
              Saving...
            } @else {
              {{ location() ? 'Save Changes' : 'Create Location' }}
            }
          </button>
        </div>
      </form>
    </div>
  `,
  styles: [`
    :host {
      display: block;
      height: 100%;
    }

    .form-container {
      @apply flex flex-col h-full;
    }

    .form-header {
      @apply flex items-center justify-between px-6 py-4 border-b border-neutral-100;
    }

    .form-title {
      @apply text-lg font-semibold text-neutral-900;
    }

    .close-btn {
      @apply w-8 h-8 rounded-md flex items-center justify-center
        text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
        transition-colors duration-150;
    }

    .form-body {
      @apply flex-1 px-6 py-5 space-y-5 overflow-y-auto;
    }

    .form-section {
      @apply space-y-1.5;
    }

    .field-hint {
      @apply text-xs text-neutral-400;
    }

    .field-error {
      @apply text-xs text-red-600 mt-1;
    }

    /* --- Toggle switch ---------------------- */

    .toggle-row {
      @apply flex items-start justify-between gap-4;
    }

    .toggle-label-block {
      @apply flex-1;
    }

    .toggle-switch {
      @apply relative inline-flex h-6 w-11 flex-shrink-0 cursor-pointer
        rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out;
      background-color: theme('colors.neutral.200');
    }

    .toggle-input {
      @apply sr-only;
    }

    .toggle-input:checked + .toggle-slider {
      transform: translateX(1.25rem);
    }

    .toggle-switch:has(.toggle-input:checked) {
      background-color: theme('colors.brand.600');
    }

    .toggle-slider {
      @apply pointer-events-none inline-block h-5 w-5 transform rounded-full
        bg-white shadow ring-0 transition duration-200 ease-in-out;
    }

    /* --- Dropdown list ---------------------- */

    .dropdown-list {
      @apply absolute z-50 mt-1 max-h-60 w-full overflow-auto rounded-lg bg-white
        border border-neutral-200 shadow-notion-lg py-1 text-sm;
    }

    .dropdown-item {
      @apply px-3 py-2 cursor-pointer hover:bg-neutral-50 transition-colors
        duration-100 flex items-center;
    }

    .dropdown-item-selected {
      @apply bg-brand-50 text-brand-700;
    }

    .dropdown-group-label {
      @apply px-3 py-1.5 text-xs font-semibold uppercase tracking-wider
        text-neutral-400 select-none;
    }

    /* --- Buttons ----------------------------- */

    .form-actions {
      @apply flex justify-end gap-3 pt-4 border-t border-neutral-100 mt-auto;
    }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-5 py-2.5
        text-sm font-medium text-white shadow-sm transition-all duration-200
        hover:bg-brand-700 disabled:opacity-50 disabled:cursor-not-allowed;
    }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2.5
        text-sm font-medium text-neutral-700 shadow-sm ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }

    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }

    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `],
})
export class LocationFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly locationService = inject(LocationService);
  private readonly toastr = inject(ToastrService);

  /** Location to edit. null = create mode. */
  readonly location = input<ILocation | null>(null);

  /** Emitted on successful create/update */
  readonly saved = output<void>();

  /** Emitted when the user cancels */
  readonly cancelled = output<void>();

  readonly isSaving = signal(false);
  readonly duplicateNameError = signal('');
  readonly addressExpanded = signal(false);

  // --- Country searchable dropdown ---
  readonly countrySearch = signal('');
  readonly countryDropdownOpen = signal(false);

  readonly filteredCountries = computed(() => {
    const q = this.countrySearch().toLowerCase().trim();
    if (!q) return COUNTRIES;
    return COUNTRIES.filter(
      (c) =>
        c.name.toLowerCase().includes(q) ||
        c.code.toLowerCase().includes(q)
    );
  });

  // --- Time zone searchable dropdown ---
  readonly tzSearch = signal('');
  readonly tzDropdownOpen = signal(false);

  readonly filteredTimeZones = computed(() => {
    const q = this.tzSearch().toLowerCase().trim();
    if (!q) return TIME_ZONES;
    return TIME_ZONES.filter(
      (tz) =>
        tz.label.toLowerCase().includes(q) ||
        tz.id.toLowerCase().includes(q) ||
        tz.utcOffset.toLowerCase().includes(q)
    );
  });

  readonly commonTzFiltered = computed(() =>
    this.filteredTimeZones().filter((tz) => tz.isCommon)
  );

  readonly otherTzFiltered = computed(() =>
    this.filteredTimeZones().filter((tz) => !tz.isCommon)
  );

  form!: FormGroup;

  ngOnInit(): void {
    const loc = this.location();

    this.form = this.fb.group({
      name: [
        loc?.name ?? '',
        [Validators.required, Validators.maxLength(150)],
      ],
      addressLine1: [loc?.addressLine1 ?? ''],
      addressLine2: [loc?.addressLine2 ?? ''],
      city: [loc?.city ?? ''],
      stateProvince: [loc?.stateProvince ?? ''],
      country: [loc?.country ?? ''],
      postalCode: [loc?.postalCode ?? ''],
      timeZone: [loc?.timeZone ?? '', [Validators.required]],
      phone: [loc?.phone ?? ''],
      isActive: [loc?.isActive ?? true],
    });

    // If editing and there are address fields, expand the section
    if (loc) {
      if (loc.addressLine1 || loc.addressLine2 || loc.city ||
          loc.stateProvince || loc.country || loc.postalCode) {
        this.addressExpanded.set(true);
      }

      // Set the search display text for the time zone
      const tzOption = TIME_ZONES.find((tz) => tz.id === loc.timeZone);
      this.tzSearch.set(
        tzOption ? `${tzOption.label} (${tzOption.id})` : loc.timeZone
      );

      // Set the search display text for the country
      if (loc.country) {
        this.countrySearch.set(loc.country);
      }
    }
  }

  // --- Country dropdown handlers ---

  onCountrySearch(value: string): void {
    this.countrySearch.set(value);
    this.countryDropdownOpen.set(true);
    // If user clears the field, clear the form value
    if (!value.trim()) {
      this.form.get('country')?.setValue('');
      this.form.markAsDirty();
    }
  }

  selectCountry(country: ICountryOption): void {
    this.form.get('country')?.setValue(country.name);
    this.countrySearch.set(country.name);
    this.countryDropdownOpen.set(false);
    this.form.markAsDirty();
  }

  onCountryBlur(): void {
    // Delay to allow click events to fire on dropdown items
    setTimeout(() => this.countryDropdownOpen.set(false), 150);
  }

  // --- Time zone dropdown handlers ---

  onTzSearch(value: string): void {
    this.tzSearch.set(value);
    this.tzDropdownOpen.set(true);
    // If user clears the field, clear the form value
    if (!value.trim()) {
      this.form.get('timeZone')?.setValue('');
      this.form.get('timeZone')?.markAsTouched();
      this.form.markAsDirty();
    }
  }

  selectTimeZone(tz: ITimeZoneOption): void {
    this.form.get('timeZone')?.setValue(tz.id);
    this.tzSearch.set(`${tz.label} (${tz.id})`);
    this.tzDropdownOpen.set(false);
    this.form.markAsDirty();
  }

  onTzBlur(): void {
    setTimeout(() => this.tzDropdownOpen.set(false), 150);
  }

  onSubmit(): void {
    if (this.form.invalid || this.isSaving()) return;

    this.isSaving.set(true);
    this.duplicateNameError.set('');

    const fv = this.form.value;
    const loc = this.location();

    const payload = {
      name: fv.name.trim(),
      addressLine1: fv.addressLine1?.trim() || null,
      addressLine2: fv.addressLine2?.trim() || null,
      city: fv.city?.trim() || null,
      stateProvince: fv.stateProvince?.trim() || null,
      country: fv.country?.trim() || null,
      postalCode: fv.postalCode?.trim() || null,
      timeZone: fv.timeZone,
      phone: fv.phone?.trim() || null,
      isActive: fv.isActive,
    };

    if (loc) {
      // Edit mode
      this.locationService
        .updateLocation(loc.locationId, payload as IUpdateLocationRequest)
        .subscribe({
          next: () => {
            this.isSaving.set(false);
            this.toastr.success(`"${payload.name}" updated successfully.`);
            this.saved.emit();
          },
          error: (err: HttpErrorResponse) => {
            this.isSaving.set(false);
            this.handleError(err);
          },
        });
    } else {
      // Create mode
      this.locationService.createLocation(payload as ICreateLocationRequest).subscribe({
        next: () => {
          this.isSaving.set(false);
          this.toastr.success(`"${payload.name}" created successfully.`);
          this.saved.emit();
        },
        error: (err: HttpErrorResponse) => {
          this.isSaving.set(false);
          this.handleError(err);
        },
      });
    }
  }

  private handleError(err: HttpErrorResponse): void {
    const body = err.error as ILocationErrorResponse | undefined;

    if (body?.code === 'duplicate_name') {
      this.duplicateNameError.set(
        body.message || 'A location with this name already exists.'
      );
    } else {
      this.toastr.error(body?.message || 'Failed to save location.');
    }
  }
}
