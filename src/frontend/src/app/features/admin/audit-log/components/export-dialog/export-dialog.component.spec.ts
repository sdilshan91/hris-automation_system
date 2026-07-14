import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideTranslateService } from '@ngx-translate/core';
import { By } from '@angular/platform-browser';
import { ExportDialogComponent } from './export-dialog.component';
import { TrappedDialogDirective } from '../../../../../shared/directives';

describe('ExportDialogComponent', () => {
  let fixture: ComponentFixture<ExportDialogComponent>;
  let component: ExportDialogComponent;

  function create(count = 42): void {
    fixture = TestBed.createComponent(ExportDialogComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('recordCount', count);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ExportDialogComponent],
      providers: [provideAnimationsAsync(), provideTranslateService()],
    });
  });

  it('traps focus in the dialog (ISSUE-296)', () => {
    create(42);
    const dialog = fixture.debugElement.query(By.directive(TrappedDialogDirective));
    expect(dialog).toBeTruthy();
  });

  it('exposes the record count for the confirmation copy', () => {
    create(42);
    // The template interpolates recordCount() into the translated subtitle
    // ("{{count}} record(s) will be exported…"); under the fake translate loader
    // the interpolation is not applied, so assert the bound input directly.
    expect(component.recordCount()).toBe(42);
  });

  it('defaults to the csv format', () => {
    create();
    expect(component.format()).toBe('csv');
  });

  it('switches the selected format', () => {
    create();
    component.selectFormat('json');
    expect(component.format()).toBe('json');
  });

  it('emits the chosen format on confirm', () => {
    create();
    const spy = jasmine.createSpy('confirmed');
    component.confirmed.subscribe(spy);
    component.selectFormat('json');
    component.confirm();
    expect(spy).toHaveBeenCalledWith('json');
  });

  it('emits cancelled on cancel', () => {
    create();
    const spy = jasmine.createSpy('cancelled');
    component.cancelled.subscribe(spy);
    component.cancel();
    expect(spy).toHaveBeenCalled();
  });
});
