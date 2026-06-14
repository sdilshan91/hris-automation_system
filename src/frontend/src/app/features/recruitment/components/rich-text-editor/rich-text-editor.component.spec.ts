import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RichTextEditorComponent } from './rich-text-editor.component';

describe('RichTextEditorComponent', () => {
  let component: RichTextEditorComponent;
  let fixture: ComponentFixture<RichTextEditorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RichTextEditorComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(RichTextEditorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('renders an editable region with role textbox', () => {
    const el: HTMLElement = fixture.nativeElement;
    const editable = el.querySelector('[role="textbox"]') as HTMLElement;
    expect(editable).toBeTruthy();
    expect(editable.getAttribute('aria-multiline')).toBe('true');
  });

  it('writeValue populates the editable HTML', () => {
    component.writeValue('<p>Hello</p>');
    const editable = fixture.nativeElement.querySelector(
      '[role="textbox"]',
    ) as HTMLElement;
    expect(editable.innerHTML).toBe('<p>Hello</p>');
  });

  it('onInput propagates the current HTML through registerOnChange', () => {
    let captured = '';
    component.registerOnChange((v: string) => (captured = v));
    const editable = fixture.nativeElement.querySelector(
      '[role="textbox"]',
    ) as HTMLElement;
    editable.innerHTML = '<strong>Bold</strong>';
    component.onInput();
    expect(captured).toBe('<strong>Bold</strong>');
  });

  it('treats an empty <br> as an empty string (so required validation works)', () => {
    let captured = 'seed';
    component.registerOnChange((v: string) => (captured = v));
    const editable = fixture.nativeElement.querySelector(
      '[role="textbox"]',
    ) as HTMLElement;
    editable.innerHTML = '<br>';
    component.onInput();
    expect(captured).toBe('');
  });

  it('onBlur fires registerOnTouched', () => {
    let touched = false;
    component.registerOnTouched(() => (touched = true));
    component.onBlur();
    expect(touched).toBeTrue();
  });

  it('setDisabledState toggles the disabled signal and contenteditable', () => {
    component.setDisabledState(true);
    fixture.detectChanges();
    expect(component.disabled()).toBeTrue();
    const editable = fixture.nativeElement.querySelector(
      '[role="textbox"]',
    ) as HTMLElement;
    expect(editable.getAttribute('contenteditable')).toBe('false');
  });

  it('exec is a no-op when disabled', () => {
    const execSpy = spyOn(document, 'execCommand');
    component.setDisabledState(true);
    component.exec('bold');
    expect(execSpy).not.toHaveBeenCalled();
  });

  it('exec applies the command and notifies onChange when enabled', () => {
    const execSpy = spyOn(document, 'execCommand');
    let changed = false;
    component.registerOnChange(() => (changed = true));
    component.exec('italic');
    expect(execSpy).toHaveBeenCalledWith('italic', false);
    expect(changed).toBeTrue();
  });

  it('insertLink does nothing when the prompt is cancelled', () => {
    spyOn(window, 'prompt').and.returnValue(null);
    const execSpy = spyOn(document, 'execCommand');
    component.insertLink();
    expect(execSpy).not.toHaveBeenCalled();
  });

  it('insertLink creates a link when a URL is provided', () => {
    spyOn(window, 'prompt').and.returnValue('https://example.com');
    const execSpy = spyOn(document, 'execCommand');
    component.insertLink();
    expect(execSpy).toHaveBeenCalledWith('createLink', false, 'https://example.com');
  });

  it('refreshActive reads queryCommandState into the active signal', () => {
    spyOn(document, 'queryCommandState').and.returnValue(true);
    component.refreshActive();
    expect(component.active().bold).toBeTrue();
    expect(component.active().italic).toBeTrue();
  });
});
