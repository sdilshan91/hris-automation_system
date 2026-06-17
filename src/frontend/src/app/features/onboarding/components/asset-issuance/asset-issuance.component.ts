import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  input,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  FormArray,
  FormBuilder,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators,
  AbstractControl,
  ValidationErrors,
} from '@angular/forms';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpEventType, HttpErrorResponse } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';
import { Subject } from 'rxjs';
import { takeUntil, debounceTime } from 'rxjs/operators';
import { OnboardingAssetService } from '../../services/onboarding-asset.service';
import {
  ASSET_CONDITIONS,
  AssetCondition,
  IAsset,
  IIssueAssetLine,
  IIssueAssetsRequest,
  ACK_ACCEPT_ATTR,
  assetOptionLabel,
  conditionChipClass,
  formatAssetFileSize,
  isFutureDate,
  issuedToast,
  todayIso,
  validateAcknowledgmentFile,
} from '../../models/onboarding-asset.models';

/** Reactive group for one asset line in the bulk-issuance form (FR-5). */
type AssetLineGroup = FormGroup<{
  assetId: FormControl<string | null>;
  assetType: FormControl<string>;
  assetTag: FormControl<string>;
  serialNumber: FormControl<string | null>;
  brand: FormControl<string | null>;
  model: FormControl<string | null>;
  condition: FormControl<AssetCondition>;
  issueDate: FormControl<string>;
  notes: FormControl<string | null>;
}>;

/** Cross-field validator: issue date may not be in the future (Data Req §7). */
function notFutureDate(control: AbstractControl): ValidationErrors | null {
  const value = control.value as string;
  return value && isFutureDate(value) ? { futureDate: true } : null;
}

/**
 * US-ONB-004: HR asset-issuance form for a new hire (AC-1..AC-3, FR-1/FR-5/FR-6).
 *
 * Routed at `onboarding/assets/issue/:employeeId` (routed input() employeeId with
 * a route-snapshot fallback) plus an optional `?taskInstanceId=` to link the
 * issuance back to the originating onboarding checklist task (FR-1). Onboarding-
 * side only — never touches Core HR employee screens; keyed purely by employeeId.
 *
 * Each asset is a removable card (FR-5). Within a card the HR officer either picks
 * an available asset from a type-ahead dropdown (filtered by the typed asset type,
 * asset tag, or serial — AC-1) which pre-fills the line, or types an ad-hoc asset.
 * Condition is chosen via colored chips (green/blue/yellow/red). Issue date defaults
 * to today and is bounded so a future date can't be picked. An optional signed
 * acknowledgment receipt (PDF/JPEG/PNG, 10 MB) is attached via a drag-drop zone
 * (FR-6) reusing the US-ONB-003 upload pattern. On submit all lines are issued in
 * one multipart POST (NFR-5); a double-assignment 4xx (AC-3) is surfaced inline.
 *
 * Standalone, signals-first, OnPush. WCAG 2.1 AA: labelled controls, keyboard-
 * operable drop zone + chips, live-region status announcements.
 */
@Component({
  selector: 'app-asset-issuance',
  standalone: true,
  imports: [CommonModule, RouterLink, ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
    trigger('cardInOut', [
      transition(':enter', [
        style({ opacity: 0, height: '0', overflow: 'hidden' }),
        animate('200ms ease-out', style({ opacity: 1, height: '*' })),
      ]),
      transition(':leave', [
        animate('180ms ease-in', style({ opacity: 0, height: '0', overflow: 'hidden' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container max-w-3xl">
      <div class="mb-6">
        <a routerLink="/onboarding" class="text-xs font-medium text-brand-600 hover:text-brand-700">
          &larr; Back to onboarding
        </a>
        <h1 class="mt-2 text-2xl font-semibold text-neutral-900 tracking-tight">Issue Assets</h1>
        <p class="mt-1 text-sm text-neutral-500">
          Record company assets issued to this new hire. Add multiple assets to issue them together.
        </p>
      </div>

      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="space-y-4" formArrayName="assets">
          @for (line of lines.controls; track line; let i = $index) {
            <section class="asset-card" [formGroupName]="i" @cardInOut>
              <div class="flex items-center justify-between mb-3">
                <h2 class="text-sm font-semibold text-neutral-800">Asset {{ i + 1 }}</h2>
                @if (lines.length > 1) {
                  <button
                    type="button"
                    class="text-neutral-400 hover:text-red-600 transition-colors"
                    (click)="removeLine(i)"
                    [attr.aria-label]="'Remove asset ' + (i + 1)"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-5 h-5">
                      <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                    </svg>
                  </button>
                }
              </div>

              <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <!-- Asset type -->
                <div>
                  <label class="label-notion" [attr.for]="'type-' + i">Asset type *</label>
                  <input
                    [id]="'type-' + i"
                    type="text"
                    class="input-notion"
                    formControlName="assetType"
                    placeholder="Laptop, Phone, ID Card…"
                    (input)="onTypeChange(i)"
                    autocomplete="off"
                  />
                </div>

                <!-- Available-asset type-ahead -->
                <div class="relative">
                  <label class="label-notion" [attr.for]="'search-' + i">Find available asset</label>
                  <input
                    [id]="'search-' + i"
                    type="text"
                    class="input-notion"
                    [value]="termFor(i)"
                    (input)="onSearch(i, $event)"
                    (focus)="activeSearch.set(i)"
                    placeholder="Search tag / serial…"
                    role="combobox"
                    [attr.aria-expanded]="activeSearch() === i && options().length > 0"
                    aria-autocomplete="list"
                    autocomplete="off"
                  />
                  @if (activeSearch() === i && (searchLoading() || options().length > 0)) {
                    <ul class="typeahead" role="listbox" @fadeIn>
                      @if (searchLoading()) {
                        <li class="typeahead-empty">Searching…</li>
                      } @else {
                        @for (opt of options(); track opt.assetId) {
                          <li>
                            <button
                              type="button"
                              class="typeahead-option"
                              role="option"
                              (click)="chooseAsset(i, opt)"
                            >
                              <span class="font-medium text-neutral-800">{{ opt.assetType }}</span>
                              <span class="text-neutral-500"> — {{ optionLabel(opt) }}</span>
                            </button>
                          </li>
                        }
                      }
                    </ul>
                  }
                </div>

                <!-- Asset tag -->
                <div>
                  <label class="label-notion" [attr.for]="'tag-' + i">Asset tag *</label>
                  <input
                    [id]="'tag-' + i"
                    type="text"
                    class="input-notion"
                    formControlName="assetTag"
                    placeholder="e.g. LAP-0042"
                    autocomplete="off"
                  />
                </div>

                <!-- Serial number -->
                <div>
                  <label class="label-notion" [attr.for]="'serial-' + i">Serial number</label>
                  <input
                    [id]="'serial-' + i"
                    type="text"
                    class="input-notion"
                    formControlName="serialNumber"
                    autocomplete="off"
                  />
                </div>

                <!-- Brand -->
                <div>
                  <label class="label-notion" [attr.for]="'brand-' + i">Brand</label>
                  <input [id]="'brand-' + i" type="text" class="input-notion" formControlName="brand" autocomplete="off" />
                </div>

                <!-- Model -->
                <div>
                  <label class="label-notion" [attr.for]="'model-' + i">Model</label>
                  <input [id]="'model-' + i" type="text" class="input-notion" formControlName="model" autocomplete="off" />
                </div>

                <!-- Issue date -->
                <div>
                  <label class="label-notion" [attr.for]="'date-' + i">Issue date *</label>
                  <input
                    [id]="'date-' + i"
                    type="date"
                    class="input-notion"
                    formControlName="issueDate"
                    [max]="maxDate"
                  />
                  @if (lineControl(i, 'issueDate').touched && lineControl(i, 'issueDate').hasError('futureDate')) {
                    <p class="field-error" role="alert">Issue date cannot be in the future.</p>
                  }
                </div>

                <!-- Condition chips -->
                <div class="sm:col-span-2">
                  <span class="label-notion block mb-1">Condition *</span>
                  <div class="flex flex-wrap gap-2" role="radiogroup" [attr.aria-label]="'Condition for asset ' + (i + 1)">
                    @for (cond of conditions; track cond) {
                      <button
                        type="button"
                        class="condition-chip"
                        [ngClass]="chipClass(cond)"
                        [class.condition-chip-selected]="lineControl(i, 'condition').value === cond"
                        role="radio"
                        [attr.aria-checked]="lineControl(i, 'condition').value === cond"
                        (click)="setCondition(i, cond)"
                      >
                        {{ cond }}
                      </button>
                    }
                  </div>
                </div>

                <!-- Notes -->
                <div class="sm:col-span-2">
                  <label class="label-notion" [attr.for]="'notes-' + i">Notes</label>
                  <textarea
                    [id]="'notes-' + i"
                    class="input-notion"
                    formControlName="notes"
                    rows="2"
                    maxlength="500"
                    placeholder="Optional notes…"
                  ></textarea>
                </div>
              </div>
            </section>
          }
        </div>

        <button type="button" class="btn-add mt-4" (click)="addLine()">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4 mr-1.5" aria-hidden="true">
            <path d="M10.75 4.75a.75.75 0 0 0-1.5 0v4.5h-4.5a.75.75 0 0 0 0 1.5h4.5v4.5a.75.75 0 0 0 1.5 0v-4.5h4.5a.75.75 0 0 0 0-1.5h-4.5v-4.5Z"/>
          </svg>
          Add Another Asset
        </button>

        <!-- Acknowledgment upload (FR-6) -->
        <section class="ack-panel mt-6">
          <span class="label-notion block mb-1">Signed acknowledgment receipt (optional)</span>
          @if (!ackFile()) {
            <div
              class="drop-zone"
              [class.drop-zone-active]="isDragOver()"
              [class.drop-zone-error]="!!ackError()"
              (dragover)="onDragOver($event)"
              (dragleave)="onDragLeave($event)"
              (drop)="onDrop($event)"
              (click)="ackInput.click()"
              (keydown.enter)="ackInput.click()"
              (keydown.space)="$event.preventDefault(); ackInput.click()"
              tabindex="0"
              role="button"
              aria-label="Drop the signed receipt here or browse to upload"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" class="w-7 h-7 text-neutral-300 mb-1" aria-hidden="true">
                <path fill-rule="evenodd" d="M11.47 2.47a.75.75 0 0 1 1.06 0l4.5 4.5a.75.75 0 0 1-1.06 1.06l-3.22-3.22V16.5a.75.75 0 0 1-1.5 0V4.81L8.03 8.03a.75.75 0 0 1-1.06-1.06l4.5-4.5ZM3 15.75a.75.75 0 0 1 .75.75v2.25a1.5 1.5 0 0 0 1.5 1.5h13.5a1.5 1.5 0 0 0 1.5-1.5V16.5a.75.75 0 0 1 1.5 0v2.25a3 3 0 0 1-3 3H5.25a3 3 0 0 1-3-3V16.5a.75.75 0 0 1 .75-.75Z" clip-rule="evenodd"/>
              </svg>
              <p class="text-xs text-neutral-500">Drop a file or tap to browse</p>
              <p class="text-[11px] text-neutral-400 mt-0.5">PDF, JPG, PNG up to 10 MB</p>
            </div>
          } @else {
            <div class="selected-file-row">
              <span class="text-sm text-neutral-700 truncate">{{ ackFile()!.name }}</span>
              <span class="text-xs text-neutral-400 ml-2">{{ fileSize(ackFile()!) }}</span>
              <button type="button" class="ml-auto text-neutral-400 hover:text-red-600"
                (click)="clearAck()" aria-label="Remove acknowledgment file">
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" class="w-4 h-4">
                  <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z"/>
                </svg>
              </button>
            </div>
          }
          <input
            #ackInput
            type="file"
            class="sr-only"
            [accept]="ackAccept"
            (change)="onAckSelected($event)"
            aria-hidden="true"
          />
          @if (ackError()) {
            <p class="field-error" role="alert">{{ ackError() }}</p>
          }
        </section>

        <!-- Inline submit error (AC-3 double-assignment surfaced verbatim) -->
        @if (submitError()) {
          <div class="submit-error mt-5" role="alert" @fadeIn>{{ submitError() }}</div>
        }

        @if (uploadProgress() !== null) {
          <div class="progress-row mt-5" role="progressbar"
            [attr.aria-valuenow]="uploadProgress()" aria-valuemin="0" aria-valuemax="100">
            <div class="progress-track">
              <div class="progress-fill" [style.width.%]="uploadProgress()"></div>
            </div>
            <span class="text-xs text-neutral-500 w-9 text-right">{{ uploadProgress() }}%</span>
          </div>
        }

        <div class="flex items-center justify-end gap-2 mt-6">
          <a routerLink="/onboarding" class="btn-secondary">Cancel</a>
          <button type="submit" class="btn-primary" [disabled]="submitting() || form.invalid">
            @if (submitting()) {
              <span class="btn-spinner"></span> Issuing…
            } @else {
              Issue {{ lines.length }} {{ lines.length === 1 ? 'Asset' : 'Assets' }}
            }
          </button>
        </div>
      </form>

      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .asset-card { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-5; }

    .label-notion { @apply block text-xs font-medium text-neutral-600 mb-1; }
    .input-notion {
      @apply w-full rounded-lg border border-neutral-200 bg-white px-3 py-2 text-sm text-neutral-900
        transition-colors duration-150 focus:outline-none focus:ring-2 focus:ring-brand-200 focus:border-brand-400;
    }
    .field-error { @apply text-xs text-red-600 mt-1; }

    .typeahead {
      @apply absolute z-20 mt-1 w-full max-h-56 overflow-auto rounded-lg bg-white
        border border-neutral-200 shadow-md py-1;
    }
    .typeahead-option { @apply w-full text-left px-3 py-2 text-sm hover:bg-neutral-50 transition-colors; }
    .typeahead-empty { @apply px-3 py-2 text-sm text-neutral-400; }

    .condition-chip {
      @apply text-xs font-medium px-3 py-1.5 rounded-full ring-1 ring-inset transition-all duration-150;
    }
    .condition-chip-selected { @apply ring-2 shadow-sm; }
    .chip-condition-new { @apply bg-green-50 text-green-700 ring-green-200; }
    .chip-condition-good { @apply bg-blue-50 text-blue-700 ring-blue-200; }
    .chip-condition-fair { @apply bg-yellow-50 text-yellow-700 ring-yellow-200; }
    .chip-condition-poor { @apply bg-red-50 text-red-700 ring-red-200; }

    .btn-add {
      @apply inline-flex items-center rounded-lg bg-white px-3 py-2 text-sm font-medium
        text-brand-700 ring-1 ring-inset ring-brand-200 transition-colors duration-150 hover:bg-brand-50;
    }

    .ack-panel { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-5; }
    .drop-zone {
      @apply flex flex-col items-center justify-center rounded-lg border-2 border-dashed
        border-neutral-200 bg-white p-5 cursor-pointer transition-all duration-200 hover:border-brand-300;
    }
    .drop-zone-active { @apply border-brand-400 bg-brand-50/50; }
    .drop-zone-error { @apply border-red-300 bg-red-50/30; }
    .selected-file-row { @apply flex items-center rounded-lg bg-white border border-neutral-100 px-3 py-2; }

    .submit-error { @apply rounded-lg bg-red-50 border border-red-200 px-4 py-3 text-sm text-red-700; }

    .progress-row { @apply flex items-center gap-2; }
    .progress-track { @apply flex-1 h-2 rounded-full bg-neutral-100 overflow-hidden; }
    .progress-fill { @apply h-full rounded-full bg-brand-500 transition-all duration-300; }

    .btn-primary {
      @apply inline-flex items-center justify-center rounded-lg bg-brand-600 px-4 py-2 text-sm
        font-medium text-white transition-all duration-200 hover:bg-brand-700
        disabled:opacity-50 disabled:cursor-not-allowed;
    }
    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2 text-sm
        font-medium text-neutral-700 ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50 disabled:opacity-50;
    }
    .btn-spinner {
      @apply inline-block w-4 h-4 mr-2 border-2 border-white/30 border-t-white rounded-full;
      animation: spin 0.6s linear infinite;
    }
    @keyframes spin { to { transform: rotate(360deg); } }
  `],
})
export class AssetIssuanceComponent implements OnInit, OnDestroy {
  private readonly fb = inject(FormBuilder);
  private readonly toastr = inject(ToastrService);
  private readonly route = inject(ActivatedRoute);
  private readonly assetService = inject(OnboardingAssetService);
  private readonly destroy$ = new Subject<void>();
  private readonly search$ = new Subject<{ index: number; term: string; type: string | null }>();

  /** Routed input — employee the assets are issued to (AC-1, keyed by id). */
  readonly employeeId = input<string>('');

  readonly conditions = ASSET_CONDITIONS;
  readonly ackAccept = ACK_ACCEPT_ATTR;
  readonly maxDate = todayIso();

  /** Display name for the success toast (AC-2); resolved from the route if present. */
  readonly employeeName = signal<string>('the employee');

  // Type-ahead state.
  readonly searchTerm = signal<Record<number, string>>({});
  readonly activeSearch = signal<number | null>(null);
  readonly options = signal<IAsset[]>([]);
  readonly searchLoading = signal(false);

  // Acknowledgment upload state.
  readonly ackFile = signal<File | null>(null);
  readonly ackError = signal<string | null>(null);
  readonly isDragOver = signal(false);

  // Submit state.
  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly uploadProgress = signal<number | null>(null);
  readonly liveMessage = signal('');

  readonly form: FormGroup<{ assets: FormArray<AssetLineGroup> }> = this.fb.group({
    assets: this.fb.array<AssetLineGroup>([]),
  });

  get lines(): FormArray<AssetLineGroup> {
    return this.form.controls.assets;
  }

  /** Resolved employee id (input wins, else the route snapshot). */
  private resolvedEmployeeId(): string {
    return this.employeeId() || this.route.snapshot.paramMap.get('employeeId') || '';
  }

  ngOnInit(): void {
    const name = this.route.snapshot.queryParamMap.get('employeeName');
    if (name) this.employeeName.set(name);

    this.addLine();

    // Debounced type-ahead lookup of available assets (AC-1).
    this.search$
      .pipe(debounceTime(250), takeUntil(this.destroy$))
      .subscribe(({ index, term, type }) => {
        if (this.activeSearch() !== index) return;
        this.searchLoading.set(true);
        this.assetService
          .getAvailableAssets(type)
          .pipe(takeUntil(this.destroy$))
          .subscribe({
            next: (assets) => {
              this.searchLoading.set(false);
              this.options.set(this.filterOptions(assets, term));
            },
            error: () => {
              this.searchLoading.set(false);
              this.options.set([]);
            },
          });
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  // ─── Line management (FR-5) ─────────────────────────────────
  private newLine(): AssetLineGroup {
    return this.fb.group({
      assetId: this.fb.control<string | null>(null),
      assetType: this.fb.control('', {
        nonNullable: true,
        validators: [Validators.required, Validators.maxLength(50)],
      }),
      assetTag: this.fb.control('', {
        nonNullable: true,
        validators: [Validators.required, Validators.maxLength(100)],
      }),
      serialNumber: this.fb.control<string | null>(null, [Validators.maxLength(100)]),
      brand: this.fb.control<string | null>(null, [Validators.maxLength(100)]),
      model: this.fb.control<string | null>(null, [Validators.maxLength(100)]),
      condition: this.fb.control<AssetCondition>('New', { nonNullable: true }),
      issueDate: this.fb.control(this.maxDate, {
        nonNullable: true,
        validators: [Validators.required, notFutureDate],
      }),
      notes: this.fb.control<string | null>(null, [Validators.maxLength(500)]),
    }) as AssetLineGroup;
  }

  addLine(): void {
    this.lines.push(this.newLine());
  }

  removeLine(index: number): void {
    this.lines.removeAt(index);
    this.searchTerm.update((map) => {
      const next = { ...map };
      delete next[index];
      return next;
    });
    if (this.activeSearch() === index) this.activeSearch.set(null);
  }

  lineControl(index: number, name: keyof AssetLineGroup['controls']): AbstractControl {
    return this.lines.at(index).get(name)!;
  }

  // ─── Condition chips ────────────────────────────────────────
  setCondition(index: number, condition: AssetCondition): void {
    this.lines.at(index).controls.condition.setValue(condition);
  }

  chipClass(condition: AssetCondition): string {
    return conditionChipClass(condition);
  }

  // ─── Type-ahead ─────────────────────────────────────────────
  termFor(index: number): string {
    return this.searchTerm()[index] ?? '';
  }

  onTypeChange(index: number): void {
    // Refresh the option list against the new type if the dropdown is open.
    if (this.activeSearch() === index) {
      this.emitSearch(index, this.searchTerm()[index] ?? '');
    }
  }

  onSearch(index: number, event: Event): void {
    const term = (event.target as HTMLInputElement).value;
    this.searchTerm.update((map) => ({ ...map, [index]: term }));
    this.activeSearch.set(index);
    this.emitSearch(index, term);
  }

  private emitSearch(index: number, term: string): void {
    const type = (this.lines.at(index).controls.assetType.value || '').trim() || null;
    this.search$.next({ index, term, type });
  }

  private filterOptions(assets: IAsset[], term: string): IAsset[] {
    const q = term.trim().toLowerCase();
    if (!q) return assets.slice(0, 20);
    return assets
      .filter(
        (a) =>
          a.assetTag.toLowerCase().includes(q) ||
          (a.serialNumber?.toLowerCase().includes(q) ?? false) ||
          a.assetType.toLowerCase().includes(q),
      )
      .slice(0, 20);
  }

  chooseAsset(index: number, asset: IAsset): void {
    const group = this.lines.at(index);
    group.patchValue({
      assetId: asset.assetId,
      assetType: asset.assetType,
      assetTag: asset.assetTag,
      serialNumber: asset.serialNumber ?? null,
      brand: asset.brand ?? null,
      model: asset.model ?? null,
      condition: asset.condition,
    });
    this.searchTerm.update((map) => ({ ...map, [index]: '' }));
    this.activeSearch.set(null);
    this.options.set([]);
  }

  optionLabel(asset: IAsset): string {
    return assetOptionLabel(asset);
  }

  // ─── Acknowledgment upload (FR-6) ───────────────────────────
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
    const file = event.dataTransfer?.files?.[0];
    if (file) this.selectAck(file);
  }

  onAckSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.selectAck(file);
    input.value = '';
  }

  clearAck(): void {
    this.ackFile.set(null);
    this.ackError.set(null);
  }

  fileSize(file: File): string {
    return formatAssetFileSize(file.size);
  }

  private selectAck(file: File): void {
    const error = validateAcknowledgmentFile(file);
    if (error) {
      this.ackError.set(error);
      this.ackFile.set(null);
      return;
    }
    this.ackError.set(null);
    this.ackFile.set(file);
  }

  // ─── Submit (FR-5 / AC-2 / AC-3) ────────────────────────────
  submit(): void {
    this.submitError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const employeeId = this.resolvedEmployeeId();
    if (!employeeId) {
      this.submitError.set('No employee selected for this issuance.');
      return;
    }

    const taskInstanceId = this.route.snapshot.queryParamMap.get('taskInstanceId');
    const assets: IIssueAssetLine[] = this.lines.controls.map((g) => {
      const v = g.getRawValue();
      return {
        assetId: v.assetId,
        assetType: v.assetType.trim(),
        assetTag: v.assetTag.trim(),
        serialNumber: v.serialNumber?.trim() || null,
        brand: v.brand?.trim() || null,
        model: v.model?.trim() || null,
        condition: v.condition,
        issueDate: v.issueDate,
        notes: v.notes?.trim() || null,
      };
    });

    const request: IIssueAssetsRequest = {
      employeeId,
      taskInstanceId: taskInstanceId || null,
      assets,
    };

    this.submitting.set(true);
    this.uploadProgress.set(this.ackFile() ? 0 : null);

    this.assetService
      .issueAssets(request, this.ackFile())
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (event) => {
          if (event.type === HttpEventType.UploadProgress) {
            const pct = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
            this.uploadProgress.set(pct);
          } else if (event.type === HttpEventType.Response) {
            this.submitting.set(false);
            this.uploadProgress.set(null);
            const issued = event.body ?? [];
            const msg = issuedToast(issued.length || assets.length, this.employeeName());
            this.liveMessage.set(msg);
            this.toastr.success(msg);
            this.resetForm();
          }
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.uploadProgress.set(null);
          // AC-3 double-assignment + FR-3 unavailable surfaced verbatim inline.
          this.submitError.set(OnboardingAssetService.parseErrorMessage(err));
        },
      });
  }

  private resetForm(): void {
    this.lines.clear();
    this.searchTerm.set({});
    this.activeSearch.set(null);
    this.options.set([]);
    this.clearAck();
    this.addLine();
  }
}
