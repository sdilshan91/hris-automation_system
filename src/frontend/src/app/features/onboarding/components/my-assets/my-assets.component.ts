import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
  OnInit,
  OnDestroy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { trigger, transition, style, animate } from '@angular/animations';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { OnboardingAssetService } from '../../services/onboarding-asset.service';
import {
  IAssignedAsset,
  conditionChipClass,
} from '../../models/onboarding-asset.models';

/**
 * US-ONB-004: "My Assets" employee self-service view (AC-4 / BR-6).
 *
 * The onboarding-side equivalent of the profile Assets tab — a READ-ONLY list of
 * the assets currently assigned to the logged-in employee: type, serial number,
 * issue date and a colored condition chip. Identity + tenant are server-resolved
 * from the session (GET /onboarding/assets/me), so no employeeId is sent and
 * there are no mutating actions (BR-6). Does NOT touch Core HR profile screens.
 *
 * Standalone, signals-first, OnPush. WCAG 2.1 AA: a semantic list, condition
 * conveyed by label text (not colour alone), live-region load status.
 */
@Component({
  selector: 'app-my-assets',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'translateY(8px)' }),
        animate('220ms ease-out', style({ opacity: 1, transform: 'translateY(0)' })),
      ]),
    ]),
  ],
  template: `
    <div class="page-container max-w-3xl">
      <div class="mb-6">
        <h1 class="text-2xl font-semibold text-neutral-900 tracking-tight">My Assets</h1>
        <p class="mt-1 text-sm text-neutral-500">
          Company assets currently issued to you. This list is read-only.
        </p>
      </div>

      <!-- Loading skeleton -->
      @if (loading()) {
        <div class="space-y-3" aria-busy="true" aria-label="Loading your assets">
          @for (_ of [1, 2, 3]; track $index) {
            <div class="skeleton-card">
              <div class="skeleton-line w-40 h-4 mb-3"></div>
              <div class="skeleton-line w-2/3 h-3"></div>
            </div>
          }
        </div>
      }

      <!-- Load error -->
      @if (loadError()) {
        <div class="text-center py-10" @fadeIn>
          <p class="text-sm text-red-600 mb-3">{{ loadError() }}</p>
          <button type="button" class="btn-secondary" (click)="load()">Retry</button>
        </div>
      }

      <!-- Empty -->
      @if (!loading() && !loadError() && assets().length === 0) {
        <div class="empty-state" @fadeIn>
          <p class="text-sm text-neutral-500">No company assets have been issued to you yet.</p>
        </div>
      }

      <!-- Asset cards (AC-4) -->
      @if (!loading() && !loadError() && assets().length > 0) {
        <ul class="space-y-3" @fadeIn>
          @for (asset of assets(); track asset.assetId) {
            <li class="asset-card">
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="text-sm font-semibold text-neutral-900">{{ asset.assetType }}</p>
                  <p class="text-xs text-neutral-500 mt-0.5 truncate">
                    Tag {{ asset.assetTag }}
                    @if (asset.serialNumber) {
                      <span> · S/N {{ asset.serialNumber }}</span>
                    }
                  </p>
                  @if (makeLabel(asset)) {
                    <p class="text-xs text-neutral-400 mt-0.5 truncate">{{ makeLabel(asset) }}</p>
                  }
                </div>
                <span class="condition-chip" [ngClass]="chipClass(asset)">{{ asset.condition }}</span>
              </div>
              <div class="flex flex-wrap items-center gap-2 mt-3">
                <span class="meta-badge">Issued {{ asset.issueDate | date:'mediumDate' }}</span>
                @if (asset.notes) {
                  <span class="meta-badge">{{ asset.notes }}</span>
                }
              </div>
            </li>
          }
        </ul>
      }

      <p class="sr-only" aria-live="polite">{{ liveMessage() }}</p>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .asset-card { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-4; }
    .meta-badge { @apply text-[11px] text-neutral-500 bg-neutral-50 rounded px-1.5 py-0.5; }

    .condition-chip {
      @apply text-[11px] font-medium px-2 py-0.5 rounded-full whitespace-nowrap flex-shrink-0
        ring-1 ring-inset;
    }
    .chip-condition-new { @apply bg-green-50 text-green-700 ring-green-200; }
    .chip-condition-good { @apply bg-blue-50 text-blue-700 ring-blue-200; }
    .chip-condition-fair { @apply bg-yellow-50 text-yellow-700 ring-yellow-200; }
    .chip-condition-poor { @apply bg-red-50 text-red-700 ring-red-200; }

    .empty-state { @apply rounded-xl bg-white border border-neutral-100 shadow-notion p-8 text-center; }
    .skeleton-card { @apply rounded-xl bg-white border border-neutral-100 p-4; }
    .skeleton-line { @apply rounded bg-neutral-200; animation: shimmer 1.5s ease-in-out infinite; }

    .btn-secondary {
      @apply inline-flex items-center justify-center rounded-lg bg-white px-4 py-2 text-sm
        font-medium text-neutral-700 ring-1 ring-inset ring-neutral-200
        transition-all duration-200 hover:bg-neutral-50;
    }
    @keyframes shimmer { 0%, 100% { opacity: 1; } 50% { opacity: 0.4; } }
  `],
})
export class MyAssetsComponent implements OnInit, OnDestroy {
  private readonly assetService = inject(OnboardingAssetService);
  private readonly destroy$ = new Subject<void>();

  readonly assets = signal<IAssignedAsset[]>([]);
  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly liveMessage = signal('');

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(null);
    this.assetService
      .getMyAssets()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.assets.set(data ?? []);
          this.loading.set(false);
          this.liveMessage.set(`${data?.length ?? 0} assets loaded.`);
        },
        error: (err: HttpErrorResponse) => {
          this.loading.set(false);
          if (err.status === 404) {
            this.assets.set([]);
          } else {
            this.loadError.set('Failed to load your assets. Please try again.');
          }
        },
      });
  }

  chipClass(asset: IAssignedAsset): string {
    return conditionChipClass(asset.condition);
  }

  /** Brand + model joined for display, or empty when neither is set. */
  makeLabel(asset: IAssignedAsset): string {
    return [asset.brand, asset.model].filter(Boolean).join(' ');
  }
}
