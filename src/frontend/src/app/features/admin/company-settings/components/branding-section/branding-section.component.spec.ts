import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideToastr, ToastrService } from 'ngx-toastr';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { BrandingSectionComponent } from './branding-section.component';
import { IBranding, deriveColorTheme } from '../../models/company-settings.models';
import { environment } from '../../../../../../environments/environment';

describe('BrandingSectionComponent', () => {
  let fixture: ComponentFixture<BrandingSectionComponent>;
  let component: BrandingSectionComponent;
  let httpMock: HttpTestingController;
  let toastr: ToastrService;

  const uploadUrl = `${environment.apiBaseUrl}/tenant/settings/branding/upload`;
  const colorUrl = `${environment.apiBaseUrl}/tenant/settings/primary-color`;

  const initial: IBranding = {
    logoUrl: 'https://cdn/logo.png',
    emailLogoUrl: null,
    faviconUrl: null,
    primaryColor: '#4f46e5',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BrandingSectionComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        provideAnimationsAsync(),
        provideTranslateService(),
      ],
    });
    fixture = TestBed.createComponent(BrandingSectionComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    toastr = TestBed.inject(ToastrService);
    fixture.componentRef.setInput('value', initial);
    fixture.componentRef.setInput('plan', null);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('derives the colour theme from the current colour input', () => {
    // theme() must equal the pure helper output for the same primary.
    expect(component.theme()).toEqual(deriveColorTheme('#4f46e5'));

    component.onColorChange('#ffe066');
    expect(component.theme()).toEqual(deriveColorTheme('#ffe066'));
    expect(component.theme().contrastText).toBe('#111827');
  });

  it('marks the colour dirty when changed and clean again after save', () => {
    expect(component.colorDirty()).toBeFalse();
    component.onColorChange('#112233');
    expect(component.colorDirty()).toBeTrue();

    component.onSaveColor();
    const req = httpMock.expectOne(colorUrl);
    expect(req.request.body).toEqual({ primaryColor: '#112233' });
    req.flush(null);
    expect(component.colorDirty()).toBeFalse();
  });

  it('flags an invalid hex and does not POST', () => {
    component.onColorChange('nope');
    expect(component.colorInvalid()).toBeTrue();
    component.onSaveColor();
    httpMock.expectNone(colorUrl);
  });

  /**
   * ISSUE-212 regression (US-ADM-006 / TC-ADM-006-22, WCAG 2.1 AA — 3.3.1 / 4.1.2).
   *
   * The invalid-hex error must be programmatically associated with the field so
   * a screen reader announces it: aria-invalid="true" on the input and
   * aria-describedby pointing at the error element's id. Pre-fix the error <p>
   * had no id and neither aria attribute existed on the input.
   */
  it('brandingHex_invalid_setsAriaInvalidAndDescribedby_ISSUE212', () => {
    // Valid baseline: field is not flagged invalid and no error element renders.
    component.onColorChange('#123456');
    fixture.detectChanges();
    const hexBefore = fixture.nativeElement.querySelector(
      'input.color-hex'
    ) as HTMLInputElement;
    expect(hexBefore).toBeTruthy();
    expect(hexBefore.getAttribute('aria-invalid')).not.toBe('true');
    expect(fixture.nativeElement.querySelector('p.cs-error')).toBeNull();

    // Enter an invalid hex -> the error message renders and must be wired up.
    component.onColorChange('nope');
    fixture.detectChanges();
    expect(component.colorInvalid()).toBeTrue();

    const hex = fixture.nativeElement.querySelector(
      'input.color-hex'
    ) as HTMLInputElement;
    const error = fixture.nativeElement.querySelector('p.cs-error') as HTMLElement;

    expect(error)
      .withContext('invalid-hex error message should render')
      .toBeTruthy();
    expect(error.id)
      .withContext('error element must expose an id for aria-describedby')
      .toBeTruthy();
    expect(hex.getAttribute('aria-invalid')).toBe('true');
    expect(hex.getAttribute('aria-describedby')).toBe(error.id);
  });

  it('uploads to the right slot and stores the returned url', () => {
    const file = new File(['x'], 'email.png', { type: 'image/png' });
    component.onFilePicked(
      { target: { files: [file], value: '' } } as unknown as Event,
      'emailLogo'
    );

    const req = httpMock.expectOne(uploadUrl);
    const body = req.request.body as FormData;
    expect(body.get('slot')).toBe('emailLogo');
    req.flush({ url: 'https://cdn/email.png' });

    expect(component.urlFor('emailLogo')).toBe('https://cdn/email.png');
  });

  it('surfaces a server 400 validation message via toastr', () => {
    const errorSpy = spyOn(toastr, 'error');
    const file = new File(['x'], 'bad.gif', { type: 'image/gif' });
    component.onFilePicked(
      { target: { files: [file], value: '' } } as unknown as Event,
      'logo'
    );

    const req = httpMock.expectOne(uploadUrl);
    req.flush(
      { message: 'Unsupported file type. Use PNG or SVG.' },
      { status: 400, statusText: 'Bad Request' }
    );

    expect(errorSpy).toHaveBeenCalledWith('Unsupported file type. Use PNG or SVG.');
  });

  // ── ISSUE-358: the asset uploads are plan-gated too, not just the colour ──

  function lockAllBranding(): void {
    fixture.componentRef.setInput('plan', {
      tier: 'starter',
      lockedFeatures: [
        'branding.customColor',
        'branding.logo',
        'branding.emailLogo',
        'branding.favicon',
      ],
    });
    fixture.detectChanges();
  }

  it('reports each branding slot as locked when the plan says so (ISSUE-358)', () => {
    lockAllBranding();

    expect(component.slotLocked('logo')).toBeTrue();
    expect(component.slotLocked('emailLogo')).toBeTrue();
    expect(component.slotLocked('favicon')).toBeTrue();
  });

  it('leaves every slot unlocked when the plan reports nothing locked', () => {
    fixture.componentRef.setInput('plan', { tier: 'enterprise', lockedFeatures: [] });
    fixture.detectChanges();

    expect(component.slotLocked('logo')).toBeFalse();
    expect(component.slotLocked('favicon')).toBeFalse();
  });

  it('treats an absent plan block as unlocked — fail open (ISSUE-358)', () => {
    // The backend omits `plan` when it cannot resolve one. The UI must not gate on a signal it never got.
    fixture.componentRef.setInput('plan', null);
    fixture.detectChanges();

    expect(component.slotLocked('logo')).toBeFalse();
  });

  it('refuses a locked upload arriving via DRAG-AND-DROP, not just via the click guard (ISSUE-358)', () => {
    // The template disables the click and keyboard paths, but onDrop reaches the upload directly — so a
    // locked slot would otherwise be one drag away from a 403 the user never asked for.
    lockAllBranding();
    const errorSpy = spyOn(toastr, 'error');
    const file = new File(['x'], 'logo.png', { type: 'image/png' });

    component.onDrop(
      {
        preventDefault: () => {},
        dataTransfer: { files: [file] },
      } as unknown as DragEvent,
      'logo'
    );

    httpMock.expectNone(uploadUrl);
    expect(errorSpy).toHaveBeenCalled();
  });

  it('refuses a locked upload arriving via the file picker (ISSUE-358)', () => {
    lockAllBranding();
    const errorSpy = spyOn(toastr, 'error');
    const file = new File(['x'], 'logo.png', { type: 'image/png' });

    component.onFilePicked(
      { target: { files: [file], value: '' } } as unknown as Event,
      'favicon'
    );

    httpMock.expectNone(uploadUrl);
    expect(errorSpy).toHaveBeenCalled();
  });

  it('rejects an oversized file client-side without uploading', () => {
    const errorSpy = spyOn(toastr, 'error');
    const big = new File(['x'], 'huge.png', { type: 'image/png' });
    Object.defineProperty(big, 'size', { value: 3 * 1024 * 1024 });
    component.onFilePicked(
      { target: { files: [big], value: '' } } as unknown as Event,
      'logo'
    );
    httpMock.expectNone(uploadUrl);
    expect(errorSpy).toHaveBeenCalled();
  });
});
