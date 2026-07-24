import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { initSentry } from './app/core/monitoring/sentry';

// US-PLT-006 AC-6: initialise client-side error tracking BEFORE bootstrap so
// early errors are captured. Inert when environment.sentryDsn is blank (default).
initSentry();

bootstrapApplication(AppComponent, appConfig).catch((err) =>
  console.error(err)
);
