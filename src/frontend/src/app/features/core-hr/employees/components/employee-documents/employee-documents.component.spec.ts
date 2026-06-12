import {
  TestBed,
  ComponentFixture,
  fakeAsync,
  tick,
} from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService, provideToastr } from 'ngx-toastr';
import { ComponentRef } from '@angular/core';
import { EmployeeDocumentsComponent } from './employee-documents.component';
import { AuthService } from '@core/auth/auth.service';
import { IEmployeeDocument } from '../../models/document.models';
import { environment } from '../../../../../../environments/environment';

describe('EmployeeDocumentsComponent', () => {
  let fixture: ComponentFixture<EmployeeDocumentsComponent>;
  let component: EmployeeDocumentsComponent;
  let componentRef: ComponentRef<EmployeeDocumentsComponent>;
  let httpMock: HttpTestingController;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const employeeId = 'emp-1';
  const docsUrl = `${environment.apiBaseUrl}/employees/${employeeId}/documents`;

  const mockDocs: IEmployeeDocument[] = [
    {
      documentId: 'doc-1',
      tenantId: 'tenant-1',
      employeeId: 'emp-1',
      fileName: 'contract.pdf',
      storageKey: 'tenant-1/core-hr/emp-1/2026/06/contract.pdf',
      fileSizeBytes: 2048000,
      mimeType: 'application/pdf',
      category: 'Contract',
      description: 'Employment contract',
      expiryDate: '2099-12-31',
      uploadedBy: 'user-1',
      uploadedByName: 'Admin',
      createdAt: '2026-06-01T00:00:00Z',
      updatedAt: '2026-06-01T00:00:00Z',
    },
    {
      documentId: 'doc-2',
      tenantId: 'tenant-1',
      employeeId: 'emp-1',
      fileName: 'passport.jpg',
      storageKey: 'tenant-1/core-hr/emp-1/2026/06/passport.jpg',
      fileSizeBytes: 512000,
      mimeType: 'image/jpeg',
      category: 'ID',
      description: null,
      expiryDate: '2026-06-20',
      uploadedBy: 'user-1',
      uploadedByName: 'Admin',
      createdAt: '2026-06-10T00:00:00Z',
      updatedAt: '2026-06-10T00:00:00Z',
    },
    {
      documentId: 'doc-3',
      tenantId: 'tenant-1',
      employeeId: 'emp-1',
      fileName: 'cert.png',
      storageKey: 'tenant-1/core-hr/emp-1/2026/06/cert.png',
      fileSizeBytes: 1024000,
      mimeType: 'image/png',
      category: 'Certificate',
      description: null,
      expiryDate: null,
      uploadedBy: 'user-2',
      uploadedByName: 'HR User',
      createdAt: '2026-06-11T00:00:00Z',
      updatedAt: '2026-06-11T00:00:00Z',
    },
  ];

  function createAuthMock(
    role: 'HR Officer' | 'Employee' | 'Manager'
  ): jasmine.SpyObj<AuthService> {
    const mock = jasmine.createSpyObj('AuthService', [
      'hasRole',
      'hasPermission',
      'hasAnyPermission',
    ], {
      isAuthenticated: jasmine.createSpy().and.returnValue(true),
      currentUser: jasmine.createSpy().and.returnValue({ userId: 'u-1', email: 'test@test.com', displayName: 'Test', mfaEnabled: false }),
      permissions: jasmine.createSpy().and.returnValue([]),
      roles: jasmine.createSpy().and.returnValue([role]),
    });
    mock.hasRole.and.callFake((r: string) => {
      if (role === 'HR Officer' && (r === 'HR Officer' || r === 'Tenant Admin')) return true;
      return r === role;
    });
    mock.hasPermission.and.returnValue(true);
    mock.hasAnyPermission.and.returnValue(true);
    return mock;
  }

  function setup(
    role: 'HR Officer' | 'Employee' | 'Manager' = 'HR Officer'
  ): void {
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'info',
      'warning',
    ]);
    const authMock = createAuthMock(role);

    TestBed.configureTestingModule({
      imports: [EmployeeDocumentsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideToastr(),
        { provide: AuthService, useValue: authMock },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    });

    fixture = TestBed.createComponent(EmployeeDocumentsComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
    httpMock = TestBed.inject(HttpTestingController);

    // Set required input
    componentRef.setInput('employeeId', employeeId);
  }

  afterEach(() => {
    httpMock.verify();
  });

  // ─── Document list rendering ──────────────────────────────

  describe('Document list rendering', () => {
    beforeEach(() => setup('HR Officer'));

    it('should create the component', () => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      expect(component).toBeTruthy();
    });

    it('should load documents on init', fakeAsync(() => {
      fixture.detectChanges();
      expect(component.isLoading()).toBeTrue();

      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      expect(component.isLoading()).toBeFalse();
      expect(component.documents().length).toBe(3);
    }));

    it('should show error state on load failure', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(null, { status: 500, statusText: 'Error' });
      tick();

      expect(component.loadError()).toBeTruthy();
      expect(component.isLoading()).toBeFalse();
    }));

    it('should show empty state when no documents', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();
      fixture.detectChanges();

      expect(component.filteredDocuments().length).toBe(0);
    }));
  });

  // ─── Category filter ──────────────────────────────────────

  describe('Category filter', () => {
    beforeEach(() => setup('HR Officer'));

    it('should show all documents when filter is All', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.activeFilter.set('All');
      expect(component.filteredDocuments().length).toBe(3);
    }));

    it('should filter by Contract category', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.activeFilter.set('Contract');
      expect(component.filteredDocuments().length).toBe(1);
      expect(component.filteredDocuments()[0].fileName).toBe('contract.pdf');
    }));

    it('should filter by ID category', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.activeFilter.set('ID');
      expect(component.filteredDocuments().length).toBe(1);
      expect(component.filteredDocuments()[0].fileName).toBe('passport.jpg');
    }));

    it('should filter by Certificate category', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.activeFilter.set('Certificate');
      expect(component.filteredDocuments().length).toBe(1);
      expect(component.filteredDocuments()[0].fileName).toBe('cert.png');
    }));

    it('should return correct category count', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      expect(component.getCategoryCount('Contract')).toBe(1);
      expect(component.getCategoryCount('ID')).toBe(1);
      expect(component.getCategoryCount('Certificate')).toBe(1);
      expect(component.getCategoryCount('Other')).toBe(0);
    }));
  });

  // ─── Upload client-side validation (AC-3) ─────────────────

  describe('Upload validation', () => {
    beforeEach(() => setup('HR Officer'));

    it('should reject file exceeding 10 MB with correct error message', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      component.showUploadForm.set(true);

      // Create a file > 10 MB
      const bigFile = new File([new ArrayBuffer(11 * 1024 * 1024)], 'big.pdf', {
        type: 'application/pdf',
      });

      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [bigFile] },
      } as unknown as DragEvent);

      expect(component.uploadError()).toBe('File exceeds the 10 MB limit.');
      expect(component.selectedFile()).toBeNull();
    }));

    it('should reject disallowed MIME type with correct error message', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      component.showUploadForm.set(true);

      const exeFile = new File(['data'], 'malware.exe', {
        type: 'application/x-msdownload',
      });

      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [exeFile] },
      } as unknown as DragEvent);

      expect(component.uploadError()).toBe(
        'File type not allowed. Supported: PDF, JPEG, PNG, DOCX, XLSX.'
      );
      expect(component.selectedFile()).toBeNull();
    }));

    it('should accept valid PDF file', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      component.showUploadForm.set(true);

      const validFile = new File(['content'], 'doc.pdf', {
        type: 'application/pdf',
      });

      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [validFile] },
      } as unknown as DragEvent);

      expect(component.uploadError()).toBeNull();
      expect(component.selectedFile()).toBeTruthy();
      expect(component.selectedFile()!.name).toBe('doc.pdf');
    }));

    it('should accept valid JPEG file', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      const validFile = new File(['data'], 'photo.jpg', { type: 'image/jpeg' });
      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [validFile] },
      } as unknown as DragEvent);

      expect(component.uploadError()).toBeNull();
      expect(component.selectedFile()!.name).toBe('photo.jpg');
    }));

    it('should accept valid DOCX file', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      const validFile = new File(['data'], 'doc.docx', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      });
      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [validFile] },
      } as unknown as DragEvent);

      expect(component.uploadError()).toBeNull();
      expect(component.selectedFile()!.name).toBe('doc.docx');
    }));

    it('should accept valid XLSX file', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      const validFile = new File(['data'], 'sheet.xlsx', {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });
      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [validFile] },
      } as unknown as DragEvent);

      expect(component.uploadError()).toBeNull();
      expect(component.selectedFile()!.name).toBe('sheet.xlsx');
    }));
  });

  // ─── Download flow (AC-4) ─────────────────────────────────

  describe('Download', () => {
    beforeEach(() => setup('HR Officer'));

    it('should request download URL and trigger download', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      // Return a real anchor whose click() is stubbed so the download link
      // does NOT actually navigate (a real a.click() reloads the Karma page
      // and disconnects the browser). Other tags fall through to the original.
      const originalCreate = document.createElement.bind(document);
      const fakeAnchor = originalCreate('a') as HTMLAnchorElement;
      const clickSpy = spyOn(fakeAnchor, 'click').and.stub();
      const createElementSpy = spyOn(document, 'createElement').and.callFake(
        (tag: string) => (tag === 'a' ? fakeAnchor : originalCreate(tag))
      );
      const appendChildSpy = spyOn(document.body, 'appendChild').and.callFake(
        (node: any) => node
      );
      const removeChildSpy = spyOn(document.body, 'removeChild').and.callFake(
        (node: any) => node
      );

      component.downloadDocument(mockDocs[0]);
      expect(component.downloadingId()).toBe('doc-1');

      const req = httpMock.expectOne(`${docsUrl}/doc-1/download`);
      expect(req.request.method).toBe('GET');
      req.flush({
        downloadUrl: 'https://storage.example.com/signed',
        expiresAt: '2026-06-12T10:05:00Z',
      });
      tick();

      expect(component.downloadingId()).toBeNull();
      expect(createElementSpy).toHaveBeenCalledWith('a');
      expect(clickSpy).toHaveBeenCalled();
      expect(appendChildSpy).toHaveBeenCalled();
      expect(removeChildSpy).toHaveBeenCalled();
    }));

    it('should show error toast on 403 download', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.downloadDocument(mockDocs[0]);
      const req = httpMock.expectOne(`${docsUrl}/doc-1/download`);
      req.flush(null, { status: 403, statusText: 'Forbidden' });
      tick();

      expect(toastrSpy.error).toHaveBeenCalledWith(
        'You do not have permission to download this document.'
      );
      expect(component.downloadingId()).toBeNull();
    }));
  });

  // ─── Delete confirmation (FR-7) ───────────────────────────

  describe('Delete confirmation', () => {
    beforeEach(() => setup('HR Officer'));

    it('should open delete confirmation modal', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.confirmDelete(mockDocs[0]);
      expect(component.deleteTarget()).toBeTruthy();
      expect(component.deleteTarget()!.documentId).toBe('doc-1');
    }));

    it('should cancel delete and close modal', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.confirmDelete(mockDocs[0]);
      component.cancelDelete();
      expect(component.deleteTarget()).toBeNull();
    }));

    it('should execute delete and remove document from list', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      expect(component.documents().length).toBe(3);
      component.confirmDelete(mockDocs[0]);
      component.executeDelete();

      const req = httpMock.expectOne(`${docsUrl}/doc-1`);
      expect(req.request.method).toBe('DELETE');
      req.flush(null);
      tick();

      expect(component.deleteTarget()).toBeNull();
      expect(component.documents().length).toBe(2);
      expect(toastrSpy.success).toHaveBeenCalledWith('Document deleted successfully.');
    }));

    it('should show error toast on delete failure', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      component.confirmDelete(mockDocs[0]);
      component.executeDelete();

      const req = httpMock.expectOne(`${docsUrl}/doc-1`);
      req.flush(null, { status: 500, statusText: 'Error' });
      tick();

      expect(toastrSpy.error).toHaveBeenCalledWith(
        'Failed to delete document. Please try again.'
      );
      expect(component.documents().length).toBe(3);
    }));
  });

  // ─── Role-gated affordances (FR-10, BR-1/2/3) ────────────

  describe('Role gating', () => {
    it('HR Officer can see upload and delete controls', fakeAsync(() => {
      setup('HR Officer');
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      expect(component.canUpload()).toBeTrue();
      expect(component.canDelete()).toBeTrue();
    }));

    it('Employee cannot see upload or delete controls', fakeAsync(() => {
      setup('Employee');
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      expect(component.canUpload()).toBeFalse();
      expect(component.canDelete()).toBeFalse();
    }));

    it('Manager cannot see upload or delete controls', fakeAsync(() => {
      setup('Manager');
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush(mockDocs);
      tick();

      expect(component.canUpload()).toBeFalse();
      expect(component.canDelete()).toBeFalse();
    }));
  });

  // ─── Upload form interactions ─────────────────────────────

  describe('Upload form', () => {
    beforeEach(() => setup('HR Officer'));

    it('should toggle upload form visibility', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      expect(component.showUploadForm()).toBeFalse();
      component.showUploadForm.set(true);
      expect(component.showUploadForm()).toBeTrue();
    }));

    it('should cancel upload and reset state', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      component.showUploadForm.set(true);
      const file = new File(['data'], 'test.pdf', { type: 'application/pdf' });
      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [file] },
      } as unknown as DragEvent);

      expect(component.selectedFile()).toBeTruthy();

      component.cancelUpload();
      expect(component.showUploadForm()).toBeFalse();
      expect(component.selectedFile()).toBeNull();
      expect(component.uploadError()).toBeNull();
      expect(component.uploadProgress()).toBeNull();
    }));

    it('should remove selected file', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      const file = new File(['data'], 'test.pdf', { type: 'application/pdf' });
      component.onDrop({
        preventDefault: () => {},
        stopPropagation: () => {},
        dataTransfer: { files: [file] },
      } as unknown as DragEvent);

      expect(component.selectedFile()).toBeTruthy();
      component.removeSelectedFile();
      expect(component.selectedFile()).toBeNull();
    }));
  });

  // ─── Template helpers ─────────────────────────────────────

  describe('Template helpers', () => {
    beforeEach(() => setup('HR Officer'));

    it('should format file size correctly', () => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);

      expect(component.formatSize(0)).toBe('0 B');
      expect(component.formatSize(500)).toBe('500 B');
      expect(component.formatSize(1024)).toBe('1.0 KB');
      expect(component.formatSize(2048000)).toBe('2.0 MB');
    });

    it('should return correct MIME type icon', () => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);

      expect(component.getMimeIcon('application/pdf')).toBe('pdf');
      expect(component.getMimeIcon('image/jpeg')).toBe('image');
      expect(component.getMimeIcon('image/png')).toBe('image');
      expect(component.getMimeIcon('application/vnd.openxmlformats-officedocument.wordprocessingml.document')).toBe('word');
      expect(component.getMimeIcon('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')).toBe('excel');
      expect(component.getMimeIcon('application/octet-stream')).toBe('file');
    });

    it('should return correct file icon label', () => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);

      expect(component.getFileIconLabel('application/pdf')).toBe('PDF');
      expect(component.getFileIconLabel('image/jpeg')).toBe('IMG');
      expect(component.getFileIconLabel('application/vnd.openxmlformats-officedocument.wordprocessingml.document')).toBe('DOC');
      expect(component.getFileIconLabel('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')).toBe('XLS');
    });
  });

  // ─── Drag and drop interactions ───────────────────────────

  describe('Drag and drop', () => {
    beforeEach(() => setup('HR Officer'));

    it('should set isDragOver on dragover and clear on dragleave', fakeAsync(() => {
      fixture.detectChanges();
      httpMock.expectOne(docsUrl).flush([]);
      tick();

      const mockEvent = {
        preventDefault: () => {},
        stopPropagation: () => {},
      } as DragEvent;

      component.onDragOver(mockEvent);
      expect(component.isDragOver()).toBeTrue();

      component.onDragLeave(mockEvent);
      expect(component.isDragOver()).toBeFalse();
    }));
  });
});

// ─── Pure utility function tests (separate describe, no httpMock.verify) ────

import {
  validateDocumentFile,
  formatFileSize,
  getExpiryBadgeStatus,
  getMimeTypeIcon,
  DOCUMENT_VALIDATION_ERRORS,
} from '../../models/document.models';

describe('Document model utility functions', () => {
  describe('validateDocumentFile', () => {
    it('should return null for valid PDF under 10 MB', () => {
      const file = new File(['x'], 'test.pdf', { type: 'application/pdf' });
      expect(validateDocumentFile(file)).toBeNull();
    });

    it('should return size error for file > 10 MB', () => {
      const file = new File([new ArrayBuffer(11 * 1024 * 1024)], 'big.pdf', {
        type: 'application/pdf',
      });
      expect(validateDocumentFile(file)).toBe(DOCUMENT_VALIDATION_ERRORS.FILE_TOO_LARGE);
    });

    it('should return type error for .exe file', () => {
      const file = new File(['x'], 'bad.exe', { type: 'application/x-msdownload' });
      expect(validateDocumentFile(file)).toBe(DOCUMENT_VALIDATION_ERRORS.FILE_TYPE_NOT_ALLOWED);
    });

    it('should accept JPEG', () => {
      const file = new File(['x'], 'photo.jpg', { type: 'image/jpeg' });
      expect(validateDocumentFile(file)).toBeNull();
    });

    it('should accept PNG', () => {
      const file = new File(['x'], 'img.png', { type: 'image/png' });
      expect(validateDocumentFile(file)).toBeNull();
    });

    it('should accept DOCX', () => {
      const file = new File(['x'], 'doc.docx', {
        type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
      });
      expect(validateDocumentFile(file)).toBeNull();
    });

    it('should accept XLSX', () => {
      const file = new File(['x'], 'sheet.xlsx', {
        type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      });
      expect(validateDocumentFile(file)).toBeNull();
    });

    it('should reject GIF', () => {
      const file = new File(['x'], 'anim.gif', { type: 'image/gif' });
      expect(validateDocumentFile(file)).toBe(DOCUMENT_VALIDATION_ERRORS.FILE_TYPE_NOT_ALLOWED);
    });
  });

  describe('formatFileSize', () => {
    it('should format 0 bytes', () => {
      expect(formatFileSize(0)).toBe('0 B');
    });

    it('should format bytes', () => {
      expect(formatFileSize(500)).toBe('500 B');
    });

    it('should format kilobytes', () => {
      expect(formatFileSize(1024)).toBe('1.0 KB');
    });

    it('should format megabytes', () => {
      expect(formatFileSize(5 * 1024 * 1024)).toBe('5.0 MB');
    });

    it('should format fractional megabytes', () => {
      expect(formatFileSize(2048000)).toBe('2.0 MB');
    });
  });

  describe('getExpiryBadgeStatus', () => {
    const today = new Date('2026-06-12');

    it('should return null for no expiry date', () => {
      expect(getExpiryBadgeStatus(null, today)).toBeNull();
    });

    it('should return green for expiry > 30 days away', () => {
      expect(getExpiryBadgeStatus('2099-12-31', today)).toBe('green');
    });

    it('should return amber for expiry 8-29 days away', () => {
      // 20 days from today = 2026-07-02
      expect(getExpiryBadgeStatus('2026-07-02', today)).toBe('amber');
    });

    it('should return red for expiry < 7 days away', () => {
      // 5 days from today = 2026-06-17
      expect(getExpiryBadgeStatus('2026-06-17', today)).toBe('red');
    });

    it('should return red for already expired date', () => {
      expect(getExpiryBadgeStatus('2026-06-01', today)).toBe('red');
    });

    it('should return amber for exactly 7 days away', () => {
      // 7 days from today = 2026-06-19
      expect(getExpiryBadgeStatus('2026-06-19', today)).toBe('amber');
    });

    it('should return green for exactly 30 days away', () => {
      // 30 days from today = 2026-07-12
      expect(getExpiryBadgeStatus('2026-07-12', today)).toBe('green');
    });
  });

  describe('getMimeTypeIcon', () => {
    it('should return pdf for PDF', () => {
      expect(getMimeTypeIcon('application/pdf')).toBe('pdf');
    });

    it('should return image for JPEG', () => {
      expect(getMimeTypeIcon('image/jpeg')).toBe('image');
    });

    it('should return image for PNG', () => {
      expect(getMimeTypeIcon('image/png')).toBe('image');
    });

    it('should return word for DOCX', () => {
      expect(getMimeTypeIcon('application/vnd.openxmlformats-officedocument.wordprocessingml.document')).toBe('word');
    });

    it('should return excel for XLSX', () => {
      expect(getMimeTypeIcon('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')).toBe('excel');
    });

    it('should return file for unknown type', () => {
      expect(getMimeTypeIcon('application/octet-stream')).toBe('file');
    });
  });
});
