import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideToastr } from 'ngx-toastr';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { LocalizationSectionComponent } from './localization-section.component';
import { ILocalization } from '../../models/company-settings.models';
import { environment } from '../../../../../../environments/environment';

describe('LocalizationSectionComponent', () => {
  let fixture: ComponentFixture<LocalizationSectionComponent>;
  let component: LocalizationSectionComponent;
  let httpMock: HttpTestingController;

  const locUrl = `${environment.apiBaseUrl}/tenant/settings/localization`;

  const initial: ILocalization = {
    defaultLanguage: 'en',
    dateFormat: 'dd MMM yyyy',
    numberFormat: '1,234.56',
    timeZone: 'UTC',
    currency: 'USD',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LocalizationSectionComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideToastr(),
        provideAnimationsAsync(),
        provideTranslateService(),
      ],
    });
    fixture = TestBed.createComponent(LocalizationSectionComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.componentRef.setInput('value', initial);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('shows the date-format preview for the initial token', () => {
    expect(component.datePreview()).toBe('11 May 2026');
  });

  it('updates the date-format preview when the selection changes', () => {
    component.form.get('dateFormat')?.setValue('MM/dd/yyyy');
    fixture.detectChanges();
    expect(component.datePreview()).toBe('05/11/2026');
  });

  it('reflects the chosen currency + number format in the currency preview', () => {
    component.form.patchValue({ currency: 'EUR', numberFormat: '1.234,56' });
    fixture.detectChanges();
    expect(component.currencyPreview()).toBe('EUR 1.234,56');
  });

  it('PUTs the localization body on save', () => {
    component.form.patchValue({ dateFormat: 'dd/MM/yyyy', timeZone: 'Asia/Colombo' });
    component.form.markAsDirty();
    component.onSave();

    const req = httpMock.expectOne(locUrl);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.dateFormat).toBe('dd/MM/yyyy');
    expect(req.request.body.timeZone).toBe('Asia/Colombo');
    req.flush(null);
    expect(component.form.dirty).toBeFalse();
  });
});
