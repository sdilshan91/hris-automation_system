import type { ErrorEvent } from '@sentry/angular';
import { initSentry, scrubEvent, setSentryTenant, SentryApi } from './sentry';

/**
 * `@sentry/angular` ships as a frozen ESM namespace (non-writable exports), so
 * `spyOn(Sentry, 'init')` throws. The module exposes an injectable SentryApi seam;
 * tests pass this fake instead of spying the real SDK.
 */
function fakeSdk(): jasmine.SpyObj<SentryApi> {
  return jasmine.createSpyObj<SentryApi>('SentryApi', ['init', 'setTag']);
}

describe('sentry monitoring (US-PLT-006 AC-6)', () => {
  describe('initSentry — inert when DSN blank (AC-3 parity)', () => {
    it('does not initialise the SDK when the DSN is blank', () => {
      const sdk = fakeSdk();

      const enabled = initSentry('', sdk);

      expect(enabled).toBe(false);
      expect(sdk.init).not.toHaveBeenCalled();
    });

    it('leaves tenant tagging inert while the DSN is blank', () => {
      const sdk = fakeSdk();
      initSentry('', sdk);

      setSentryTenant('acme', 'tenant-123', sdk);

      expect(sdk.setTag).not.toHaveBeenCalled();
    });

    it('initialises the SDK with scrubbing enabled when a DSN is supplied', () => {
      const sdk = fakeSdk();

      const enabled = initSentry('https://public@glitchtip.internal/1', sdk);

      expect(enabled).toBe(true);
      expect(sdk.init).toHaveBeenCalledTimes(1);
      const options = sdk.init.calls.mostRecent().args[0]!;
      expect(options.dsn).toBe('https://public@glitchtip.internal/1');
      expect(options.sendDefaultPii).toBe(false);
      expect(typeof options.beforeSend).toBe('function');
    });

    it('tags the tenant once the SDK is initialised (AC-6)', () => {
      const sdk = fakeSdk();
      initSentry('https://public@glitchtip.internal/1', sdk);

      setSentryTenant('acme', 'tenant-123', sdk);

      expect(sdk.setTag).toHaveBeenCalledWith('tenant_subdomain', 'acme');
      expect(sdk.setTag).toHaveBeenCalledWith('tenant_id', 'tenant-123');
    });
  });

  describe('scrubEvent — mirrors the backend PII scrub (AC-2 parity)', () => {
    function buildEvent(): ErrorEvent {
      return {
        request: {
          data: { password: 'secret', email: 'user@example.com' },
          cookies: 'session=abc123',
          query_string: 'token=abc&q=1',
          headers: {
            Authorization: 'Bearer eyJ...',
            Cookie: 'session=abc123',
            'User-Agent': 'jasmine',
          },
        },
        user: { id: '42', email: 'user@example.com', ip_address: '203.0.113.9', username: 'jdoe' },
        extra: {
          email: 'user@example.com',
          nationalId: '199012345678',
          nested: { national_id: '99', keep: 'ok' },
        },
      } as unknown as ErrorEvent;
    }

    it('strips the request body, cookies and query string', () => {
      const out = scrubEvent(buildEvent());

      expect(out.request!.data).toBeUndefined();
      expect(out.request!.cookies).toBeUndefined();
      expect(out.request!.query_string).toBeUndefined();
    });

    it('strips Authorization and Cookie headers but keeps benign ones', () => {
      const out = scrubEvent(buildEvent());
      const headers = out.request!.headers as Record<string, string>;

      expect(headers['Authorization']).toBeUndefined();
      expect(headers['Cookie']).toBeUndefined();
      expect(headers['User-Agent']).toBe('jasmine');
    });

    it('drops default PII carried on the user context', () => {
      const out = scrubEvent(buildEvent());
      const user = out.user as Record<string, unknown>;

      expect(user['email']).toBeUndefined();
      expect(user['ip_address']).toBeUndefined();
      expect(user['username']).toBeUndefined();
      expect(user['id']).toBe('42');
    });

    it('redacts known PII fields anywhere in extra data', () => {
      const out = scrubEvent(buildEvent());
      const extra = out.extra as Record<string, unknown>;
      const nested = extra['nested'] as Record<string, unknown>;

      expect(extra['email']).toBe('[redacted]');
      expect(extra['nationalId']).toBe('[redacted]');
      expect(nested['national_id']).toBe('[redacted]');
      expect(nested['keep']).toBe('ok');
    });
  });
});
