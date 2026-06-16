import {
  Component,
  ChangeDetectionStrategy,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { ToastrService } from 'ngx-toastr';
import { AuthService } from '../../../../../core/auth/auth.service';
import { ImpersonationService } from '../../services/impersonation.service';

/**
 * US-ADM-003 NFR-4: the persistent impersonation banner.
 *
 * Mounted GLOBALLY in the main layout as a fixed, high-contrast (orange) top bar
 * that is a SIBLING of the routed outlet — so no routed/tenant component or
 * tenant-level CSS can hide or override it. Visible whenever the active token is
 * an impersonation token (AuthService.isImpersonating).
 *
 * Text is i18n-driven (BR-6) via ngx-translate keys under IMPERSONATION.BANNER.
 * "End Session" (AC-3) POSTs the end endpoint, restores the admin session, and
 * navigates back to the admin console.
 */
@Component({
  selector: 'app-impersonation-banner',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './impersonation-banner.component.html',
})
export class ImpersonationBannerComponent {
  private readonly auth = inject(AuthService);
  private readonly service = inject(ImpersonationService);
  private readonly router = inject(Router);
  private readonly toastr = inject(ToastrService);

  /** NFR-4: banner visibility is bound to the active token's impersonation claim. */
  readonly isImpersonating = this.auth.isImpersonating;
  readonly sessionId = this.auth.impersonationSessionId;
  readonly readOnly = this.auth.impersonationReadOnly;

  readonly ending = signal(false);

  /** AC-3: end the session, restore the admin session, return to the console. */
  endSession(): void {
    const id = this.sessionId();
    if (!id || this.ending()) {
      return;
    }
    this.ending.set(true);

    this.service.end(id).subscribe({
      next: () => this.restoreAndExit(),
      // Even if the end call fails, drop the impersonation token locally so the
      // admin is never stuck inside an impersonated session (fail-closed).
      error: () => this.restoreAndExit(),
    });
  }

  private restoreAndExit(): void {
    this.ending.set(false);
    const restored = this.auth.endImpersonation();
    this.toastr.info('Impersonation session ended.');
    if (restored) {
      this.router.navigate(['/admin/monitoring']);
    } else {
      // No stashed admin token (e.g. page reloaded mid-session) — re-login.
      this.auth.logout();
    }
  }
}
