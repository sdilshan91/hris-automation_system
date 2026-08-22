// ============================================================================
// GAP-027 — avatars are fetched as authenticated blobs, and the object URLs are released.
//
// The stored `ProfilePhotoUrl` was `/{tenantId}/{path}` and the upload handed back
// `/files/{tenantId}/{path}`. No route served either, so every avatar was a broken image.
//
// The fix cannot be "point <img src> at the API": in this app the access token is a BEARER HEADER (only the
// refresh token is a cookie), and a plain image request carries no Authorization header. So the bytes are
// fetched through HttpClient — which the auth interceptor decorates — and bound as an object URL.
//
// The arm that matters here is the RELEASE. A missed revokeObjectURL leaks one blob per rendered photo for
// the life of the page: invisible in review, invisible in QA, and cumulative in a directory of 200 people.
// ============================================================================

import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';

import { EmployeePhotoDirective } from './employee-photo.directive';
import { environment } from '../../../../../environments/environment';

@Component({
  standalone: true,
  imports: [EmployeePhotoDirective],
  template: `<img [appEmployeePhoto]="employeeId" alt="avatar" />`,
})
class HostComponent {
  employeeId: string | null = 'emp-1';
}

describe('EmployeePhotoDirective (GAP-027)', () => {
  let fixture: ComponentFixture<HostComponent>;
  let httpMock: HttpTestingController;

  const photoUrl = (id: string) => `${environment.apiBaseUrl}/tenant/employees/${id}/photo`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(HostComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches the photo as an authenticated blob, not a plain image URL', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne(photoUrl('emp-1'));
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType)
      .withContext('the endpoint streams bytes; <img src> could not authenticate')
      .toBe('blob');

    const createSpy = spyOn(URL, 'createObjectURL').and.returnValue('blob:one');
    req.flush(new Blob(['x'], { type: 'image/png' }));
    fixture.detectChanges();

    expect(createSpy).toHaveBeenCalled();
    const img = fixture.nativeElement.querySelector('img') as HTMLImageElement;
    expect(img.src).toContain('blob:');
  });

  /**
   * THE ARM THAT MATTERS. Re-rendering with a different employee — sorting, paging, filtering a directory —
   * must release the previous blob. Without it, a list leaks one blob per row per render.
   */
  it('revokes the previous object URL when the employee changes', () => {
    spyOn(URL, 'createObjectURL').and.returnValues('blob:one', 'blob:two');
    const revokeSpy = spyOn(URL, 'revokeObjectURL').and.stub();

    fixture.detectChanges();
    httpMock.expectOne(photoUrl('emp-1')).flush(new Blob(['a'], { type: 'image/png' }));
    fixture.detectChanges();

    fixture.componentInstance.employeeId = 'emp-2';
    fixture.detectChanges();

    expect(revokeSpy)
      .withContext('the first blob must be released before the second is fetched')
      .toHaveBeenCalledWith('blob:one');

    httpMock.expectOne(photoUrl('emp-2')).flush(new Blob(['b'], { type: 'image/png' }));
    fixture.detectChanges();
  });

  it('revokes the object URL on destroy', () => {
    spyOn(URL, 'createObjectURL').and.returnValue('blob:one');
    const revokeSpy = spyOn(URL, 'revokeObjectURL').and.stub();

    fixture.detectChanges();
    httpMock.expectOne(photoUrl('emp-1')).flush(new Blob(['a'], { type: 'image/png' }));
    fixture.detectChanges();

    fixture.destroy();

    expect(revokeSpy).toHaveBeenCalledWith('blob:one');
  });

  it('issues no request when there is no employee id', () => {
    fixture.componentInstance.employeeId = null;
    fixture.detectChanges();

    httpMock.expectNone(() => true);
  });

  /**
   * A missing photo is an ordinary 404 — the callers already render initials instead. It must not surface an
   * error: a toast per avatar would be intolerable in a directory view.
   */
  it('treats a missing photo as unremarkable', () => {
    fixture.detectChanges();

    expect(() => {
      httpMock
        .expectOne(photoUrl('emp-1'))
        .flush(null, { status: 404, statusText: 'Not Found' });
      fixture.detectChanges();
    }).not.toThrow();
  });
});
