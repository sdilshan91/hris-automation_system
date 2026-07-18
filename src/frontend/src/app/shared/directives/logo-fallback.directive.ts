import { Directive } from '@angular/core';

/**
 * ISSUE-204: shared broken-logo fallback for tenant brand marks.
 *
 * Tenant `logoUrl`s can resolve to an asset that 404s (e.g. a relative
 * `/{tenantId}/branding/logo.png` served by nothing in the current environment).
 * A truthy-but-broken URL passes the `@if (logoUrl)` guard, so the browser paints
 * the broken-image glyph. This directive listens for the `<img>` `error` event and
 * hides the element, revealing whatever initial/placeholder is rendered beneath it
 * (each brand-mark container carries its `bg-brand-600` + tenant initial as the
 * base layer). Reused by the app shell (main-layout) and the auth-layout brand.
 *
 * Usage:
 *   <span class="tenant-logo">
 *     <span>{{ initial }}</span>
 *     <img [src]="logoUrl" [alt]="name" appLogoFallback />
 *   </span>
 */
@Directive({
  selector: 'img[appLogoFallback]',
  standalone: true,
  host: {
    '(error)': 'onError()',
    '[hidden]': 'failed',
  },
})
export class LogoFallbackDirective {
  /** Set once the image fails to load; hides the <img> so the placeholder shows. */
  failed = false;

  onError(): void {
    this.failed = true;
  }
}
