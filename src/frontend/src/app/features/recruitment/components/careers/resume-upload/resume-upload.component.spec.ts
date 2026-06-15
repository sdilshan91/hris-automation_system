import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ResumeUploadComponent } from './resume-upload.component';
import { RESUME_MAX_BYTES } from '../../../models/applicant.models';

describe('ResumeUploadComponent', () => {
  let component: ResumeUploadComponent;
  let fixture: ComponentFixture<ResumeUploadComponent>;
  let changed: (File | null)[];

  const makeFile = (name: string, type: string, size: number): File => {
    const file = new File(['x'], name, { type });
    Object.defineProperty(file, 'size', { value: size });
    return file;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ResumeUploadComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(ResumeUploadComponent);
    component = fixture.componentInstance;
    changed = [];
    component.registerOnChange((v) => changed.push(v));
    fixture.detectChanges();
  });

  it('creates and renders the accepted-formats hint', () => {
    expect(component).toBeTruthy();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('PDF, DOCX or DOC');
    expect(text).toContain('25 MB');
  });

  it('accepts a valid PDF, sets the file and emits via onChange', () => {
    const file = makeFile('cv.pdf', 'application/pdf', 1024);
    component.onPicked({ target: { files: [file], value: '' } } as unknown as Event);
    fixture.detectChanges();

    expect(component.file()).toBe(file);
    expect(component.error()).toBeNull();
    expect(changed.at(-1)).toBe(file);
  });

  it('rejects an oversized file with an inline error and emits null', () => {
    const file = makeFile('big.pdf', 'application/pdf', RESUME_MAX_BYTES + 1);
    component.onPicked({ target: { files: [file], value: '' } } as unknown as Event);
    fixture.detectChanges();

    expect(component.file()).toBeNull();
    expect(component.error()).toContain('25 MB');
    expect(changed.at(-1)).toBeNull();
    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('25 MB');
  });

  it('rejects a disallowed type with an inline error', () => {
    const file = makeFile('cv.exe', 'application/octet-stream', 100);
    component.onPicked({ target: { files: [file], value: '' } } as unknown as Event);
    fixture.detectChanges();

    expect(component.file()).toBeNull();
    expect(component.error()).toContain('Unsupported file type');
  });

  it('writeValue sets the file (form -> view)', () => {
    const file = makeFile('cv.docx', 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 2048);
    component.writeValue(file);
    fixture.detectChanges();
    expect(component.file()).toBe(file);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('cv.docx');
  });

  it('remove clears the file and emits null', () => {
    component.writeValue(makeFile('cv.pdf', 'application/pdf', 100));
    component.remove(new Event('click'));
    fixture.detectChanges();
    expect(component.file()).toBeNull();
    expect(changed.at(-1)).toBeNull();
  });

  it('accepts a dropped valid file via the drop handler', () => {
    const file = makeFile('drop.pdf', 'application/pdf', 100);
    const event = {
      preventDefault: () => {},
      dataTransfer: { files: [file] },
    } as unknown as DragEvent;
    component.onDrop(event);
    fixture.detectChanges();
    expect(component.file()).toBe(file);
  });

  it('humanSize formats bytes, KB and MB', () => {
    expect(component.humanSize(512)).toBe('512 B');
    expect(component.humanSize(2048)).toBe('2 KB');
    expect(component.humanSize(5 * 1024 * 1024)).toBe('5.0 MB');
  });
});
