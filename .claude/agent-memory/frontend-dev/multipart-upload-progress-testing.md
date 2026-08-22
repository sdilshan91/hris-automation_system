---
name: multipart-upload-progress-testing
description: How to test a service that POSTs multipart/form-data with reportProgress (observe:'events'), and assert FormData fields
metadata:
  type: feedback
---

When a service uploads a file via `http.post(url, formData, { reportProgress: true, observe: 'events' })` and maps `HttpEventType.UploadProgress`/`Response` into a small event union, test it with `HttpTestingController` like this:

- Drive a progress event: `req.event({ type: HttpEventType.UploadProgress, loaded: 50, total: 100 })`, then `req.flush(payload)` for the final Response. The mapped stream will emit your `{type:'progress'}` event(s) then the `{type:'done'}` one.
- Assert the request body is FormData: `const body = req.request.body as FormData; expect(body instanceof FormData).toBeTrue(); expect(body.get('firstName')).toBe(...)`.
- For File fixtures with a controlled size: `const f = new File(['x'],'cv.pdf',{type:'application/pdf'}); Object.defineProperty(f,'size',{value: N});` — `File` size is read-only, so `defineProperty` is the only way to fake an oversized file for client-side size-limit validation tests.

**Why:** US-REC-002 resume upload. The progress bar + duplicate(409)/file-rejection error paths are core ACs; this is the only reliable way to exercise them in Karma without a real upload.

**How to apply:** any multipart upload service spec (resume, document, avatar, import). Keep multipart field naming in ONE `buildFormData` helper so a backend key mismatch is a 1-line fix. Service specs still use plain `provideHttpClient()` + `provideHttpClientTesting()` and flush BARE payloads (the global [[apiEnvelopeInterceptor]] is not in the test chain). See also [[blob-export-download-pattern]] for the download counterpart.
