import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  computed,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  FormGroup,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject, forkJoin } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { HolidayService } from '../../services/holiday.service';
import { LocationService } from '../../../core-hr/locations/services/location.service';
import { ILocation } from '../../../core-hr/locations/models/location.models';
import {
  IHoliday,
  ICreateHolidayRequest,
  IHolidayImportPreview,
  HolidayType,
  HOLIDAY_TYPE_OPTIONS,
  getHolidayTypeColor,
  getHolidayTypeBadgeClasses,
  getHolidayTypeLabel,
  buildMonthGrid,
  groupByMonth,
  parseHolidayCsv,
  yearOf,
  MONTH_NAMES,
  WEEKDAY_LABELS,
  ICalendarCell,
} from '../../models/holiday.models';

type ViewMode = 'calendar' | 'list';

/**
 * US-LV-007 (AC-1..AC-4): Holiday Calendar Management per tenant.
 *
 * Dual view (§8):
 *   - Calendar view: interactive month grid with color-coded holiday markers
 *     (public=blue, restricted=orange, optional=green). Year navigation.
 *   - List view: Notion-like table with per-row edit/deactivate.
 * Add/edit holiday via a slide-over panel (name, date, type, location,
 * description, recurring). CSV import via drag-and-drop with a client-side
 * preview + validation + duplicate flagging before confirm (AC-3).
 *
 * On mobile the calendar collapses to a compact list-by-month view (NFR-4).
 *
 * Role-gated to Tenant Admin / HR Officer via the route guard.
 */
@Component({
  selector: 'app-holiday-calendar',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeSlideIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('250ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('slideOver', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateX(100%)' }),
        animate('300ms ease-out', style({ opacity: 1, transform: 'translateX(0)' })),
      ]),
      transition(':leave', [
        animate('200ms ease-in', style({ opacity: 0, transform: 'translateX(100%)' })),
      ]),
    ]),
    trigger('overlayFade', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 })),
      ]),
      transition(':leave', [animate('150ms ease-in', style({ opacity: 0 }))]),
    ]),
  ],
  templateUrl: './holiday-calendar.component.html',
  styleUrls: ['./holiday-calendar.component.css'],
})
export class HolidayCalendarComponent implements OnInit, OnDestroy {
  private readonly holidayService = inject(HolidayService);
  private readonly locationService = inject(LocationService);
  private readonly toastr = inject(ToastrService);
  private readonly fb = inject(FormBuilder);
  private readonly destroy$ = new Subject<void>();

  // --- Data state -------------------------------------------------
  readonly holidays = signal<IHoliday[]>([]);
  readonly locations = signal<ILocation[]>([]);
  readonly isLoading = signal(true);
  readonly loadError = signal('');

  // --- View state -------------------------------------------------
  readonly viewMode = signal<ViewMode>('calendar');
  readonly year = signal<number>(new Date().getFullYear());
  /** Month being shown in the desktop calendar grid (0-based). */
  readonly activeMonth = signal<number>(new Date().getMonth());
  /** Active location filter; '' = all locations. */
  readonly locationFilter = signal<string>('');

  // --- Form slide-over state -------------------------------------
  readonly formOpen = signal(false);
  readonly editingHoliday = signal<IHoliday | null>(null);
  readonly isSaving = signal(false);
  readonly togglingId = signal<string | null>(null);

  // --- CSV import state ------------------------------------------
  readonly importOpen = signal(false);
  readonly isDragOver = signal(false);
  readonly importFile = signal<File | null>(null);
  readonly importPreview = signal<IHolidayImportPreview | null>(null);
  readonly importError = signal<string | null>(null);
  readonly isImporting = signal(false);

  // --- Template constants ----------------------------------------
  readonly typeOptions = HOLIDAY_TYPE_OPTIONS;
  readonly monthNames = MONTH_NAMES;
  readonly weekdayLabels = WEEKDAY_LABELS;
  readonly skeletonItems = [1, 2, 3, 4, 5, 6];

  readonly form: FormGroup = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    date: ['', [Validators.required]],
    type: ['public' as HolidayType, [Validators.required]],
    locationId: [''],
    description: [''],
    isRecurring: [false],
  });

  // --- Derived: location-filtered holidays for the active year ----
  readonly filteredHolidays = computed(() => {
    const filter = this.locationFilter();
    const all = this.holidays();
    if (!filter) return all;
    // Show holidays scoped to the chosen location AND tenant-wide ones
    // (null locationId applies to everyone).
    return all.filter((h) => h.locationId === filter || h.locationId === null);
  });

  /** Active-only holidays, used for calendar markers (AC-4). */
  readonly activeFilteredHolidays = computed(() =>
    this.filteredHolidays().filter((h) => h.isActive)
  );

  /** Month grid for the desktop calendar (AC-4). */
  readonly monthGrid = computed<ICalendarCell[]>(() =>
    buildMonthGrid(this.year(), this.activeMonth(), this.activeFilteredHolidays())
  );

  /** Holidays grouped by month for the mobile / list-by-month view (NFR-4). */
  readonly holidaysByMonth = computed<IHoliday[][]>(() =>
    groupByMonth(this.activeFilteredHolidays())
  );

  /** List-view rows sorted by date (AC-4). */
  readonly sortedHolidays = computed(() =>
    [...this.filteredHolidays()].sort((a, b) => a.date.localeCompare(b.date))
  );

  readonly hasHolidays = computed(() => this.filteredHolidays().length > 0);

  /** Whether the staged CSV import has any importable rows (AC-3). */
  readonly canConfirmImport = computed(() => {
    const p = this.importPreview();
    return !!p && p.validCount > 0 && !this.isImporting();
  });

  ngOnInit(): void {
    this.loadAll();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // --- Loading ----------------------------------------------------

  loadAll(): void {
    this.isLoading.set(true);
    this.loadError.set('');

    forkJoin({
      holidays: this.holidayService.getHolidaysForYear(this.year()),
      locations: this.locationService.getLocations(true),
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ holidays, locations }) => {
          this.holidays.set(holidays);
          this.locations.set(locations);
          this.isLoading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          this.loadError.set(
            err.error?.message || 'Failed to load the holiday calendar. Please try again.'
          );
        },
      });
  }

  /** Reload only the holidays for the active year (AC-4 year navigation). */
  loadHolidays(): void {
    this.isLoading.set(true);
    this.loadError.set('');
    this.holidayService
      .getHolidaysForYear(this.year())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (holidays) => {
          this.holidays.set(holidays);
          this.isLoading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.isLoading.set(false);
          this.loadError.set(
            err.error?.message || 'Failed to load the holiday calendar. Please try again.'
          );
        },
      });
  }

  // --- View toggle + navigation ----------------------------------

  setView(mode: ViewMode): void {
    this.viewMode.set(mode);
  }

  changeYear(delta: number): void {
    this.year.update((y) => y + delta);
    this.loadHolidays();
  }

  setYear(value: number): void {
    if (!Number.isFinite(value) || value === this.year()) return;
    this.year.set(value);
    this.loadHolidays();
  }

  onYearInput(event: Event): void {
    const value = parseInt((event.target as HTMLInputElement).value, 10);
    this.setYear(value);
  }

  changeMonth(delta: number): void {
    let m = this.activeMonth() + delta;
    let y = this.year();
    if (m < 0) {
      m = 11;
      y -= 1;
    } else if (m > 11) {
      m = 0;
      y += 1;
    }
    this.activeMonth.set(m);
    if (y !== this.year()) {
      this.year.set(y);
      this.loadHolidays();
    }
  }

  setLocationFilter(value: string): void {
    this.locationFilter.set(value);
  }

  onLocationFilterChange(event: Event): void {
    this.setLocationFilter((event.target as HTMLSelectElement).value);
  }

  // --- Color / label helpers (template) --------------------------

  typeColor(type: HolidayType | string): string {
    return getHolidayTypeColor(type);
  }

  typeBadge(type: HolidayType | string): string {
    return getHolidayTypeBadgeClasses(type);
  }

  typeLabel(type: HolidayType | string): string {
    return getHolidayTypeLabel(type);
  }

  // --- Form slide-over -------------------------------------------

  openCreate(prefillDate?: string): void {
    this.editingHoliday.set(null);
    this.form.reset({
      name: '',
      date: prefillDate ?? '',
      type: 'public',
      locationId: this.locationFilter() || '',
      description: '',
      isRecurring: false,
    });
    this.formOpen.set(true);
  }

  /** Open the create form for a clicked calendar day cell (AC-1, AC-4). */
  openCreateForCell(cell: ICalendarCell): void {
    if (!cell.date) return;
    this.openCreate(cell.date);
  }

  openEdit(holiday: IHoliday): void {
    this.editingHoliday.set(holiday);
    this.form.reset({
      name: holiday.name,
      date: holiday.date,
      type: holiday.type,
      locationId: holiday.locationId ?? '',
      description: holiday.description ?? '',
      isRecurring: holiday.isRecurring,
    });
    this.formOpen.set(true);
  }

  closeForm(): void {
    if (this.isSaving()) return;
    this.formOpen.set(false);
    this.editingHoliday.set(null);
  }

  submitForm(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const raw = this.form.getRawValue();
    const payload: ICreateHolidayRequest = {
      name: (raw.name ?? '').trim(),
      date: raw.date,
      type: raw.type,
      locationId: raw.locationId ? raw.locationId : null,
      description: raw.description ? (raw.description as string).trim() : null,
      isRecurring: !!raw.isRecurring,
    };

    this.isSaving.set(true);
    const editing = this.editingHoliday();
    const request$ = editing
      ? this.holidayService.updateHoliday(editing.id, payload)
      : this.holidayService.createHoliday(payload);

    request$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (saved) => {
        this.isSaving.set(false);
        this.upsertHoliday(saved);
        this.toastr.success(
          editing ? `"${saved.name}" updated.` : `"${saved.name}" added to the calendar.`
        );
        this.formOpen.set(false);
        this.editingHoliday.set(null);
      },
      error: (err: HttpErrorResponse) => {
        this.isSaving.set(false);
        const body = HolidayService.parseError(err);
        this.toastr.error(body?.message || 'Failed to save the holiday.');
      },
    });
  }

  // --- Deactivate / reactivate (BR-4) ----------------------------

  toggleActive(holiday: IHoliday, event?: Event): void {
    event?.stopPropagation();
    this.togglingId.set(holiday.id);
    const action$ = holiday.isActive
      ? this.holidayService.deactivateHoliday(holiday.id)
      : this.holidayService.reactivateHoliday(holiday.id);

    action$.pipe(takeUntil(this.destroy$)).subscribe({
      next: (updated) => {
        this.togglingId.set(null);
        this.upsertHoliday(updated);
        this.toastr.success(
          updated.isActive
            ? `"${updated.name}" reactivated.`
            : `"${updated.name}" deactivated.`
        );
      },
      error: (err: HttpErrorResponse) => {
        this.togglingId.set(null);
        const body = HolidayService.parseError(err);
        // BR-4: deletion/deactivation blocked within a finalized payroll period.
        this.toastr.error(body?.message || 'Failed to update the holiday status.');
      },
    });
  }

  private upsertHoliday(saved: IHoliday): void {
    // If the saved holiday's year differs from the active year (e.g. date moved
    // to another year), drop it from the current view; else upsert in place.
    const list = this.holidays();
    const exists = list.some((h) => h.id === saved.id);
    let next: IHoliday[];
    if (exists) {
      next = list.map((h) => (h.id === saved.id ? saved : h));
    } else {
      next = [...list, saved];
    }
    next = next.filter((h) => yearOf(h.date) === this.year());
    this.holidays.set(next);
  }

  // --- CSV import (AC-3) -----------------------------------------

  openImport(): void {
    this.resetImport();
    this.importOpen.set(true);
  }

  closeImport(): void {
    if (this.isImporting()) return;
    this.importOpen.set(false);
    this.resetImport();
  }

  private resetImport(): void {
    this.importFile.set(null);
    this.importPreview.set(null);
    this.importError.set(null);
    this.isDragOver.set(false);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(true);
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
    const file = event.dataTransfer?.files[0] ?? null;
    this.handleImportFile(file);
  }

  onImportFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.handleImportFile(file);
    input.value = '';
  }

  clearImportFile(event?: Event): void {
    event?.stopPropagation();
    this.resetImport();
  }

  private handleImportFile(file: File | null): void {
    this.importError.set(null);
    this.importPreview.set(null);
    if (!file) {
      this.importFile.set(null);
      return;
    }
    const ext = file.name.substring(file.name.lastIndexOf('.')).toLowerCase();
    if (ext !== '.csv') {
      this.importError.set('Invalid file type. Only .csv files are accepted.');
      this.importFile.set(null);
      return;
    }
    if (file.size === 0) {
      this.importError.set('The selected file is empty.');
      this.importFile.set(null);
      return;
    }
    this.importFile.set(file);

    this.readFileText(file)
      .then((text) => {
        const preview = parseHolidayCsv(text, this.holidays());
        if (preview.rows.length === 0) {
          this.importError.set('No data rows found in the file.');
        }
        this.importPreview.set(preview);
      })
      .catch(() => {
        this.importError.set('Failed to read the file.');
      });
  }

  /**
   * Read a file's text content. Extracted as an overridable seam so unit tests
   * can resolve synchronously rather than depend on the async FileReader macrotask.
   */
  protected readFileText(file: File): Promise<string> {
    return file.text();
  }

  confirmImport(): void {
    const file = this.importFile();
    if (!file || !this.canConfirmImport()) return;
    this.isImporting.set(true);

    this.holidayService
      .importHolidays(file)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (result) => {
          this.isImporting.set(false);
          this.importOpen.set(false);
          this.resetImport();
          this.toastr.success(
            `Imported ${result.imported} of ${result.total} holidays.` +
              (result.skipped > 0 ? ` ${result.skipped} skipped.` : '')
          );
          this.loadHolidays();
        },
        error: (err: HttpErrorResponse) => {
          this.isImporting.set(false);
          const body = HolidayService.parseError(err);
          this.importError.set(body?.message || 'Import failed. Please try again.');
        },
      });
  }
}
