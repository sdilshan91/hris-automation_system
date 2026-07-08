import { bootstrapApplication } from '@angular/platform-browser';
import * as Sentry from '@sentry/angular';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { environment } from './environments/environment';

// Error tracking → self-hosted GlitchTip (Sentry-compatible). PII-scrubbed: we host
// customer HR data, so nothing identifying leaves the browser. Empty DSN => disabled.
if (environment.sentryDsn) {
  Sentry.init({
    dsn: environment.sentryDsn,
    environment: environment.production ? 'production' : 'development',
    sendDefaultPii: false,
    tracesSampleRate: 0.1,
    beforeSend(event) {
      if (event.request) {
        delete event.request.data;
        delete (event.request as Record<string, unknown>)['cookies'];
      }
      delete event.user;
      return event;
    },
  });
}

bootstrapApplication(AppComponent, appConfig).catch((err) =>
  console.error(err)
);
