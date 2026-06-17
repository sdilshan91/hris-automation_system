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
