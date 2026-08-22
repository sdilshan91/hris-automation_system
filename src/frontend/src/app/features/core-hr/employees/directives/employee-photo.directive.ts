import {
  Directive,
  ElementRef,
  OnDestroy,
  DestroyRef,
  effect,
  inject,
  input,
} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../../environments/environment';

/**
 * GAP-027 — renders an employee's profile photo from the AUTHENTICATED endpoint.
 *
 * Avatars were broken images. The stored `ProfilePhotoUrl` was `/{tenantId}/{path}` and the value handed
 * back on upload was `/files/{tenantId}/{path}`; no route serves either, so every `<img [src]>` bound to
 * them 404'd.
 *
 * **Why a directive and not three copies of a fetch.** Three components render avatars
 * (`employee-list`, `employee-profile`, `my-team`). Each would need to fetch the bytes, create an object
 * URL, and revoke it on destroy — and a missed `revokeObjectURL` leaks one blob per rendered photo for the
 * life of the page, which is invisible in review and cumulative in a directory view. Writing that lifecycle
 * three times is how one of them ends up wrong. This is the same S-1 reasoning that produced BUG-307 (ten
 * hand-written copies of one plan lookup) and BUG-311 (a second description of one wire contract).
 *
 * **Why a blob and not a URL.** `<img src>` cannot carry an Authorization header, and in this app the access
 * token IS a Bearer header — only the refresh token is a cookie. Fetching through `HttpClient` routes the
 * request via the auth interceptor, which is what makes the photo both visible and access-controlled.
 *
 * ```html
 * <img [appEmployeePhoto]="employee.employeeId" [alt]="name" />
 * ```
 */
@Directive({
  selector: 'img[appEmployeePhoto]',
  standalone: true,
})
export class EmployeePhotoDirective implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly el = inject(ElementRef<HTMLImageElement>);
  private readonly destroyRef = inject(DestroyRef);

  /** The employee whose photo to load. Empty/null renders nothing and issues no request. */
  readonly appEmployeePhoto = input<string | null | undefined>();

  private objectUrl: string | null = null;

  constructor() {
    effect((onCleanup) => {
      const employeeId = this.appEmployeePhoto();

      // Release the PREVIOUS photo before loading another. Without this, a list that re-renders — sorting,
      // paging, filtering — leaks one blob per row per render.
      this.release();

      if (!employeeId) {
        return;
      }

      const sub = this.http
        .get(`${environment.apiBaseUrl}/tenant/employees/${employeeId}/photo`, {
          withCredentials: true,
          responseType: 'blob',
        })
        .subscribe({
          next: (blob) => {
            this.objectUrl = URL.createObjectURL(blob);
            (this.el.nativeElement as HTMLImageElement).src = this.objectUrl;
          },
          // A missing photo is an ordinary 404, not an error worth surfacing: the caller already renders
          // initials when there is no photo, and a toast per avatar would be intolerable in a list.
          error: () => this.release(),
        });

      onCleanup(() => sub.unsubscribe());
    });

    this.destroyRef.onDestroy(() => this.release());
  }

  ngOnDestroy(): void {
    this.release();
  }

  private release(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }
}
