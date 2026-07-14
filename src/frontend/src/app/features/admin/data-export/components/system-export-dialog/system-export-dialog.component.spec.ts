import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideToastr } from 'ngx-toastr';
import { provideTranslateService } from '@ngx-translate/core';
import { By } from '@angular/platform-browser';
import { SystemExportDialogComponent } from './system-export-dialog.component';
import { environment } from '../../../../../../environments/environment';
import { TrappedDialogDirective } from '../../../../../shared/directives';

/** Host that drives the dialog with bound inputs and captures (cancelled). */
@Component({
  standalone: true,
  imports: [SystemExportDialogComponent],
  template: `
    <app-system-export-dialog
      [tenantId]="'t-7'"
      [tenantName]="'Acme Corp'"
      (cancelled)="closed = true"
    />
  `,
})
class HostComponent {
  closed = false;
}

describe('SystemExportDialogComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  let httpMock: HttpTestingController;

  const systemBase = `${environment.apiBaseUrl}/system/tenants/t-7/data-exports`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideTranslateService(),
        provideToastr(),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
    // The embedded panel loads history from the SYSTEM base on init (AC-6).
    httpMock.expectOne(systemBase).flush([]);
    fixture.detectChanges();
  });

  afterEach(() => httpMock.verify());

  it('traps focus in the dialog (ISSUE-296)', () => {
    const dialog = fixture.debugElement.query(
      By.directive(TrappedDialogDirective),
    );
    expect(dialog).toBeTruthy();
  });

  it('renders the dialog targeting the tenant via the system path (AC-6)', () => {
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="system-export-dialog"]')).not.toBeNull();
    // The embedded export panel renders inside.
    expect(el.querySelector('[data-testid="start-export"]')).not.toBeNull();
  });

  it('emits cancelled when the close button is clicked', () => {
    const el: HTMLElement = fixture.nativeElement;
    (
      el.querySelector('[data-testid="system-export-close"]') as HTMLButtonElement
    ).click();
    expect(host.closed).toBeTrue();
  });
});
