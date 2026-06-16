import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<router-outlet />`,
})
export class AppComponent {
  private readonly translate = inject(TranslateService);

  constructor() {
    // Activate the default language so translation keys resolve app-wide
    // (ngx-translate's defaultLanguage config sets the fallback but does not
    // call use()). Single-locale for now; a language switcher can swap this.
    this.translate.setDefaultLang('en');
    this.translate.use('en');
  }
}
