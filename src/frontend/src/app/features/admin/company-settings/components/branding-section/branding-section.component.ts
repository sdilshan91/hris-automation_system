import {
  Component,
  ChangeDetectionStrategy,
  inject,
  input,
  signal,
  computed,
  effect,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { CompanySettingsService } from '../../services/company-settings.service';
import {
  IBranding,
  IPlanInfo,
  BrandingSlot,
  IColorTheme,
  deriveColorTheme,
  normalizeHex,
} from '../../models/company-settings.models';

/** Client-side hints (server is authoritative on the hard reject — NFR-2). */
const LOGO_MAX_BYTES = 2 * 1024 * 1024; // 2MB
const FAVICON_MAX_BYTES = 500 * 1024; // 500KB

@Component({
  selector: 'app-branding-section',
  standalone: true,
  imports: [CommonModule, FormsModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="cs-section">
      <header class="cs-section-head">
        <h2 class="cs-section-title">{{ 'admin.companySettings.branding.title' | translate }}</h2>
        <p class="cs-section-sub">{{ 'admin.companySettings.branding.subtitle' | translate }}</p>
      </header>

      <div class="cs-form">
        <!-- ── Upload zones ───────────────────────────── -->
        <div class="upload-grid">
          @for (zone of zones; track zone.slot) {
            <div class="upload-block">
              <span class="cs-label">{{ zone.label | translate }}</span>
              <p class="cs-hint">{{ zone.hint | translate }}</p>
              @if (slotLocked(zone.slot)) {
                <span class="cs-upgrade-badge">{{
                  'admin.companySettings.upgradePlan' | translate
                }}</span>
              }
              <div
                class="dropzone"
                [class.dragging]="draggingSlot() === zone.slot"
                [class.uploading]="uploadingSlot() === zone.slot"
                [class.cs-locked]="slotLocked(zone.slot)"
                role="button"
                [attr.tabindex]="slotLocked(zone.slot) ? -1 : 0"
                [attr.aria-disabled]="slotLocked(zone.slot)"
                [attr.aria-label]="(zone.label | translate)"
                (click)="slotLocked(zone.slot) || picker.click()"
                (keydown.enter)="slotLocked(zone.slot) || picker.click()"
                (keydown.space)="slotLocked(zone.slot) || picker.click(); $event.preventDefault()"
                (dragover)="onDragOver($event, zone.slot)"
                (dragleave)="onDragLeave(zone.slot)"
                (drop)="onDrop($event, zone.slot)"
              >
                @if (urlFor(zone.slot); as url) {
                  <img [src]="url" [alt]="(zone.label | translate)" class="dropzone-preview" />
                } @else {
                  <span class="dropzone-empty">
                    {{ 'admin.companySettings.branding.dropHere' | translate }}
                  </span>
                }
                @if (uploadingSlot() === zone.slot) {
                  <span class="cs-btn-spinner dropzone-spinner"></span>
                }
              </div>
              <input
                #picker
                type="file"
                class="sr-only"
                [accept]="zone.accept"
                (change)="onFilePicked($event, zone.slot)"
              />
            </div>
          }
        </div>

        <hr class="cs-divider" />

        <!-- ── Live brand preview (sidebar / login / email) ── -->
        <div class="preview-block">
          <span class="cs-label">{{ 'admin.companySettings.branding.previewTitle' | translate }}</span>
          <div class="preview-grid" [style.--cs-preview-primary]="theme().primary"
               [style.--cs-preview-dark]="theme().dark"
               [style.--cs-preview-light]="theme().light"
               [style.--cs-preview-contrast]="theme().contrastText">
            <!-- Mock sidebar -->
            <div class="mock mock-sidebar">
              <div class="mock-sidebar-top">
                @if (urlFor('logo'); as logo) {
                  <img [src]="logo" alt="" class="mock-logo" />
                } @else {
                  <span class="mock-logo-fallback">A</span>
                }
                <span class="mock-sidebar-name">Acme HR</span>
              </div>
              <div class="mock-nav-item active">Dashboard</div>
              <div class="mock-nav-item">Employees</div>
            </div>
            <!-- Mock login -->
            <div class="mock mock-login">
              @if (urlFor('logo'); as logo) {
                <img [src]="logo" alt="" class="mock-login-logo" />
              }
              <button type="button" class="mock-cta">Sign in</button>
              <a class="mock-link">Forgot password?</a>
            </div>
            <!-- Mock email header -->
            <div class="mock mock-email">
              <div class="mock-email-header">
                @if (urlFor('emailLogo') || urlFor('logo'); as elogo) {
                  <img [src]="elogo" alt="" class="mock-email-logo" />
                }
              </div>
              <p class="mock-email-body">Welcome to the team!</p>
              <button type="button" class="mock-cta sm">Get started</button>
            </div>
          </div>
        </div>

        <hr class="cs-divider" />

        <!-- ── Color picker ──────────────────────────── -->
        <div class="color-block" [class.cs-locked]="colorLocked()">
          <div class="color-head">
            <span class="cs-label">
              {{ 'admin.companySettings.branding.primaryColor' | translate }}
            </span>
            @if (colorLocked()) {
              <span class="cs-upgrade-badge">
                {{ 'admin.companySettings.upgradePlan' | translate }}
              </span>
            }
          </div>
          <div class="color-row">
            <input
              type="color"
              class="color-swatch-input"
              [ngModel]="colorInput()"
              (ngModelChange)="onColorChange($event)"
              [disabled]="colorLocked()"
              aria-label="Primary colour swatch"
            />
            <input
              type="text"
              class="cs-input color-hex"
              [ngModel]="colorInput()"
              (ngModelChange)="onColorChange($event)"
              [disabled]="colorLocked()"
              placeholder="#4f46e5"
              aria-label="Primary colour hex"
              [attr.aria-invalid]="colorInvalid()"
              [attr.aria-describedby]="colorInvalid() ? 'branding-color-error' : null"
            />
            <div class="shade-row" aria-hidden="true">
              <span class="shade" [style.background]="theme().dark" title="Dark"></span>
              <span class="shade" [style.background]="theme().primary" title="Primary"></span>
              <span class="shade" [style.background]="theme().light" title="Light"></span>
            </div>
          </div>
          @if (colorInvalid()) {
            <p id="branding-color-error" class="cs-error" role="alert">
              {{ 'admin.companySettings.branding.invalidColor' | translate }}
            </p>
          }
        </div>

        <div class="cs-actions">
          <button
            type="button"
            class="cs-btn-primary"
            [disabled]="savingColor() || colorLocked() || !colorDirty() || colorInvalid()"
            (click)="onSaveColor()"
          >
            @if (savingColor()) {
              <span class="cs-btn-spinner"></span>
            }
            {{ 'admin.companySettings.branding.saveColor' | translate }}
          </button>
        </div>
      </div>
    </section>
  `,
  styleUrls: ['../../company-settings.styles.css', './branding-section.styles.css'],
})
export class BrandingSectionComponent {
  private readonly service = inject(CompanySettingsService);
  private readonly toastr = inject(ToastrService);

  readonly value = input<IBranding | null>(null);
  readonly plan = input<IPlanInfo | null>(null);

  readonly zones: {
    slot: BrandingSlot;
    label: string;
    hint: string;
    accept: string;
  }[] = [
    {
      slot: 'logo',
      label: 'admin.companySettings.branding.logo',
      hint: 'admin.companySettings.branding.logoHint',
      accept: 'image/png,image/svg+xml',
    },
    {
      slot: 'emailLogo',
      label: 'admin.companySettings.branding.emailLogo',
      hint: 'admin.companySettings.branding.logoHint',
      accept: 'image/png,image/svg+xml',
    },
    {
      slot: 'favicon',
      label: 'admin.companySettings.branding.favicon',
      hint: 'admin.companySettings.branding.faviconHint',
      accept: 'image/png,image/x-icon,image/vnd.microsoft.icon',
    },
  ];

  // Stored URLs (updated live after a successful upload).
  readonly logoUrl = signal<string | null>(null);
  readonly emailLogoUrl = signal<string | null>(null);
  readonly faviconUrl = signal<string | null>(null);

  readonly draggingSlot = signal<BrandingSlot | null>(null);
  readonly uploadingSlot = signal<BrandingSlot | null>(null);

  // Colour state.
  readonly colorInput = signal('#4f46e5');
  private readonly savedColor = signal('#4f46e5');
  readonly savingColor = signal(false);

  readonly theme = computed<IColorTheme>(() => deriveColorTheme(this.colorInput()));
  readonly colorInvalid = computed(() => normalizeHex(this.colorInput()) === null);
  readonly colorDirty = computed(
    () => normalizeHex(this.colorInput()) !== this.savedColor()
  );

  /** BR-3: custom brand colour is plan-gated when the backend flags it. */
  readonly colorLocked = computed(() =>
    (this.plan()?.lockedFeatures ?? []).includes('branding.customColor')
  );

  /**
   * ISSUE-358: the asset uploads are plan-gated too, not just the colour.
   *
   * The backend refuses an unentitled upload with 403 `plan_feature_locked`, so without this the dropzone
   * would happily accept a file and then surface a server error — the UI promising something the API will
   * refuse. Keyed per slot because the backend reports them individually.
   */
  readonly lockedSlots = computed(() => {
    const locked = this.plan()?.lockedFeatures ?? [];
    return {
      logo: locked.includes('branding.logo'),
      emailLogo: locked.includes('branding.emailLogo'),
      favicon: locked.includes('branding.favicon'),
    } as Record<BrandingSlot, boolean>;
  });

  slotLocked(slot: BrandingSlot): boolean {
    return this.lockedSlots()[slot] ?? false;
  }

  constructor() {
    effect(() => {
      const v = this.value();
      if (v) {
        this.logoUrl.set(v.logoUrl ?? null);
        this.emailLogoUrl.set(v.emailLogoUrl ?? null);
        this.faviconUrl.set(v.faviconUrl ?? null);
        const normalized = normalizeHex(v.primaryColor) ?? '#4f46e5';
        this.colorInput.set(normalized);
        this.savedColor.set(normalized);
      }
    });
  }

  urlFor(slot: BrandingSlot): string | null {
    return slot === 'logo'
      ? this.logoUrl()
      : slot === 'emailLogo'
        ? this.emailLogoUrl()
        : this.faviconUrl();
  }

  private setUrl(slot: BrandingSlot, url: string): void {
    if (slot === 'logo') {
      this.logoUrl.set(url);
    } else if (slot === 'emailLogo') {
      this.emailLogoUrl.set(url);
    } else {
      this.faviconUrl.set(url);
    }
  }

  // ── Drag & drop ────────────────────────────────────────────

  onDragOver(event: DragEvent, slot: BrandingSlot): void {
    event.preventDefault();
    this.draggingSlot.set(slot);
  }

  onDragLeave(slot: BrandingSlot): void {
    if (this.draggingSlot() === slot) {
      this.draggingSlot.set(null);
    }
  }

  onDrop(event: DragEvent, slot: BrandingSlot): void {
    event.preventDefault();
    this.draggingSlot.set(null);
    const file = event.dataTransfer?.files?.[0];
    if (file) {
      this.upload(file, slot);
    }
  }

  onFilePicked(event: Event, slot: BrandingSlot): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) {
      this.upload(file, slot);
    }
    input.value = '';
  }

  /**
   * Soft client-side size hint, then upload. The server does the hard reject
   * (magic-byte + size, NFR-2); we surface its 400 message.
   */
  private upload(file: File, slot: BrandingSlot): void {
    // ISSUE-358: the single choke point for every upload route. The template guards the click and the
    // keyboard activation, but DRAG-AND-DROP reaches onDrop directly — so the entitlement check has to live
    // here or a locked slot is one drag away from a 403 the user never asked for.
    if (this.slotLocked(slot)) {
      this.toastr.error(
        'Custom branding requires a plan that includes white-labelling.'
      );
      return;
    }

    const limit = slot === 'favicon' ? FAVICON_MAX_BYTES : LOGO_MAX_BYTES;
    if (file.size > limit) {
      const mb = slot === 'favicon' ? '500KB' : '2MB';
      this.toastr.error(`File is too large. Maximum size is ${mb}.`);
      return;
    }
    this.uploadingSlot.set(slot);
    this.service.uploadBranding(file, slot).subscribe({
      next: (res) => {
        this.uploadingSlot.set(null);
        this.setUrl(slot, res.url);
        this.toastr.success('Branding image uploaded.');
      },
      error: (err: HttpErrorResponse) => {
        this.uploadingSlot.set(null);
        // Surface the server's clear validation message (AC-2 / NFR-2).
        this.toastr.error(err.error?.message || 'Upload failed. Please try again.');
      },
    });
  }

  // ── Colour ─────────────────────────────────────────────────

  onColorChange(value: string): void {
    this.colorInput.set(value);
  }

  onSaveColor(): void {
    const normalized = normalizeHex(this.colorInput());
    if (this.savingColor() || this.colorLocked() || !normalized || !this.colorDirty()) {
      return;
    }
    this.savingColor.set(true);
    this.service.updatePrimaryColor(normalized).subscribe({
      next: () => {
        this.savingColor.set(false);
        this.savedColor.set(normalized);
        this.applyTheme();
        this.toastr.success('Brand colour saved.');
      },
      error: (err: HttpErrorResponse) => {
        this.savingColor.set(false);
        this.toastr.error(err.error?.message || 'Failed to save brand colour.');
      },
    });
  }

  /** FR-3: apply the derived shades app-wide via CSS custom properties. */
  private applyTheme(): void {
    const theme = this.theme();
    const root = document.documentElement;
    root.style.setProperty('--cs-brand-primary', theme.primary);
    root.style.setProperty('--cs-brand-dark', theme.dark);
    root.style.setProperty('--cs-brand-light', theme.light);
    root.style.setProperty('--cs-brand-contrast', theme.contrastText);
  }
}
