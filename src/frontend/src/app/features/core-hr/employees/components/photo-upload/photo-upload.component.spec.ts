import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PhotoUploadComponent } from './photo-upload.component';

describe('PhotoUploadComponent', () => {
  let component: PhotoUploadComponent;
  let fixture: ComponentFixture<PhotoUploadComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PhotoUploadComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PhotoUploadComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create the component', () => {
    expect(component).toBeTruthy();
  });

  it('should start with no preview and no error', () => {
    expect(component.previewUrl()).toBeNull();
    expect(component.validationError()).toBeNull();
    expect(component.isDragOver()).toBeFalse();
  });

  describe('file validation', () => {
    it('should reject files with invalid MIME type', () => {
      const invalidFile = new File(['data'], 'doc.pdf', {
        type: 'application/pdf',
      });
      spyOn(component.photoSelected, 'emit');

      // Simulate file selection via the internal method
      component.onFileSelected({
        target: { files: [invalidFile], value: '' },
      } as unknown as Event);

      expect(component.validationError()).toBe(
        'Invalid file type. Only JPEG, PNG, and WebP are allowed.'
      );
      expect(component.photoSelected.emit).not.toHaveBeenCalled();
    });

    it('should reject files larger than 5 MB', () => {
      // Create a File object that claims to be > 5MB
      const largeData = new ArrayBuffer(6 * 1024 * 1024);
      const largeFile = new File([largeData], 'large.jpg', {
        type: 'image/jpeg',
      });
      spyOn(component.photoSelected, 'emit');

      component.onFileSelected({
        target: { files: [largeFile], value: '' },
      } as unknown as Event);

      expect(component.validationError()).toBe(
        'File is too large. Maximum size is 5 MB.'
      );
      expect(component.photoSelected.emit).not.toHaveBeenCalled();
    });

    it('should accept valid JPEG files', () => {
      const validFile = new File(['data'], 'photo.jpg', {
        type: 'image/jpeg',
      });
      spyOn(component.photoSelected, 'emit');

      component.onFileSelected({
        target: { files: [validFile], value: '' },
      } as unknown as Event);

      expect(component.validationError()).toBeNull();
      expect(component.photoSelected.emit).toHaveBeenCalledWith(validFile);
    });

    it('should accept valid PNG files', () => {
      const validFile = new File(['data'], 'photo.png', {
        type: 'image/png',
      });
      spyOn(component.photoSelected, 'emit');

      component.onFileSelected({
        target: { files: [validFile], value: '' },
      } as unknown as Event);

      expect(component.validationError()).toBeNull();
      expect(component.photoSelected.emit).toHaveBeenCalledWith(validFile);
    });

    it('should accept valid WebP files', () => {
      const validFile = new File(['data'], 'photo.webp', {
        type: 'image/webp',
      });
      spyOn(component.photoSelected, 'emit');

      component.onFileSelected({
        target: { files: [validFile], value: '' },
      } as unknown as Event);

      expect(component.validationError()).toBeNull();
      expect(component.photoSelected.emit).toHaveBeenCalledWith(validFile);
    });
  });

  describe('drag and drop', () => {
    it('should set isDragOver on dragover', () => {
      const event = new DragEvent('dragover');
      spyOn(event, 'preventDefault');
      spyOn(event, 'stopPropagation');

      component.onDragOver(event);

      expect(component.isDragOver()).toBeTrue();
      expect(event.preventDefault).toHaveBeenCalled();
    });

    it('should clear isDragOver on dragleave', () => {
      component.isDragOver.set(true);
      const event = new DragEvent('dragleave');
      spyOn(event, 'preventDefault');
      spyOn(event, 'stopPropagation');

      component.onDragLeave(event);

      expect(component.isDragOver()).toBeFalse();
    });

    it('should process dropped files on drop', () => {
      const validFile = new File(['data'], 'drop.jpg', {
        type: 'image/jpeg',
      });
      spyOn(component.photoSelected, 'emit');

      const dataTransfer = new DataTransfer();
      dataTransfer.items.add(validFile);
      const dropEvent = new DragEvent('drop', { dataTransfer });
      spyOn(dropEvent, 'preventDefault');
      spyOn(dropEvent, 'stopPropagation');

      component.onDrop(dropEvent);

      expect(component.isDragOver()).toBeFalse();
      expect(component.photoSelected.emit).toHaveBeenCalledWith(validFile);
    });
  });

  describe('photo removal', () => {
    it('should clear preview and emit photoRemoved', () => {
      // Set up initial state with a preview
      component.previewUrl.set('data:image/jpeg;base64,...');
      spyOn(component.photoRemoved, 'emit');

      component.removePhoto();

      expect(component.previewUrl()).toBeNull();
      expect(component.validationError()).toBeNull();
      expect(component.photoRemoved.emit).toHaveBeenCalled();
    });
  });
});
