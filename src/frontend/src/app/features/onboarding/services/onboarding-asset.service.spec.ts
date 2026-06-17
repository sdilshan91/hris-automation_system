import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import {
  provideHttpClient,
  HttpErrorResponse,
  HttpEventType,
} from '@angular/common/http';

import { OnboardingAssetService } from './onboarding-asset.service';
import { environment } from '../../../../environments/environment';
import {
  IAsset,
  IAssignedAsset,
  IIssueAssetsRequest,
} from '../models/onboarding-asset.models';

describe('OnboardingAssetService', () => {
  let service: OnboardingAssetService;
  let httpMock: HttpTestingController;
  const base = `${environment.apiBaseUrl}/onboarding/assets`;

  const available = (over: Partial<IAsset> = {}): IAsset => ({
    assetId: 'a-1',
    assetType: 'Laptop',
    assetTag: 'LAP-0042',
    serialNumber: 'SN-123',
    brand: 'Dell',
    model: 'XPS 13',
    condition: 'New',
    status: 'available',
    ...over,
  });

  const assigned = (over: Partial<IAssignedAsset> = {}): IAssignedAsset => ({
    assetId: 'a-1',
    assetType: 'Laptop',
    assetTag: 'LAP-0042',
    serialNumber: 'SN-123',
    brand: 'Dell',
    model: 'XPS 13',
    condition: 'New',
    status: 'assigned',
    issueDate: '2026-06-17',
    notes: null,
    ...over,
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        OnboardingAssetService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(OnboardingAssetService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  // ─── getAvailableAssets (AC-1 / FR-3) ──────────────────────

  it('getAvailableAssets GETs /available with the assetType param', () => {
    let result: IAsset[] | undefined;
    service.getAvailableAssets('Laptop').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === `${base}/available` && r.params.get('assetType') === 'Laptop',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([available(), available({ assetId: 'a-2', assetTag: 'LAP-0043' })]);

    expect(result!.length).toBe(2);
    expect(result![0].status).toBe('available');
  });

  it('getAvailableAssets omits the assetType param when none is given', () => {
    service.getAvailableAssets().subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === `${base}/available` && !r.params.has('assetType'),
    );
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  // ─── getEmployeeAssets (HR view) ───────────────────────────

  it('getEmployeeAssets GETs the register filtered by employeeId', () => {
    let result: IAssignedAsset[] | undefined;
    service.getEmployeeAssets('emp-1').subscribe((r) => (result = r));

    const req = httpMock.expectOne(
      (r) => r.url === base && r.params.get('employeeId') === 'emp-1',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([assigned()]);

    expect(result!.length).toBe(1);
    expect(result![0].status).toBe('assigned');
  });

  // ─── getMyAssets (AC-4 / BR-6) ─────────────────────────────

  it('getMyAssets GETs /me with credentials and no employeeId', () => {
    let result: IAssignedAsset[] | undefined;
    service.getMyAssets().subscribe((r) => (result = r));

    const req = httpMock.expectOne(`${base}/me`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBeTrue();
    req.flush([assigned()]);

    expect(result!.length).toBe(1);
    expect(result![0].issueDate).toBe('2026-06-17');
  });

  // ─── issueAssets (FR-5 / FR-6 / AC-2) ──────────────────────

  it('issueAssets POSTs multipart with the JSON "request" field (no file)', () => {
    const request: IIssueAssetsRequest = {
      employeeId: 'emp-1',
      taskInstanceId: null,
      assets: [
        {
          assetId: 'a-1',
          assetType: 'Laptop',
          assetTag: 'LAP-0042',
          serialNumber: 'SN-123',
          brand: 'Dell',
          model: 'XPS 13',
          condition: 'New',
          issueDate: '2026-06-17',
          notes: null,
        },
      ],
    };

    let result: IAssignedAsset[] | undefined;
    service.issueAssets(request).subscribe((event) => {
      if (event.type === HttpEventType.Response) result = event.body!;
    });

    const req = httpMock.expectOne(`${base}/issue`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBeTrue();
    expect(req.request.reportProgress).toBeTrue();
    expect(req.request.body instanceof FormData).toBeTrue();
    const fd = req.request.body as FormData;
    expect(JSON.parse(fd.get('request') as string)).toEqual(request);
    expect(fd.has('acknowledgment')).toBeFalse();

    req.flush([assigned()]);
    expect(result!.length).toBe(1);
  });

  it('issueAssets attaches the acknowledgment file + reports upload progress (FR-6)', () => {
    const file = new File(['x'], 'receipt.pdf', { type: 'application/pdf' });
    Object.defineProperty(file, 'size', { value: 4096 });
    const request: IIssueAssetsRequest = {
      employeeId: 'emp-1',
      assets: [
        {
          assetType: 'Phone',
          assetTag: 'PH-1',
          condition: 'Good',
          issueDate: '2026-06-17',
        },
      ],
    };

    let pct = -1;
    let result: IAssignedAsset[] | undefined;
    service.issueAssets(request, file).subscribe((event) => {
      if (event.type === HttpEventType.UploadProgress) {
        pct = event.total ? Math.round((event.loaded / event.total) * 100) : 0;
      } else if (event.type === HttpEventType.Response) {
        result = event.body!;
      }
    });

    const req = httpMock.expectOne(`${base}/issue`);
    expect(req.request.body instanceof FormData).toBeTrue();
    const fd = req.request.body as FormData;
    const sent = fd.get('acknowledgment') as File;
    expect(sent.name).toBe('receipt.pdf');
    expect(sent.type).toBe('application/pdf');

    req.event({ type: HttpEventType.UploadProgress, loaded: 2048, total: 4096 });
    expect(pct).toBe(50);

    req.flush([assigned({ assetType: 'Phone', assetTag: 'PH-1', condition: 'Good' })]);
    expect(result![0].assetType).toBe('Phone');
  });

  it('issueAssets surfaces a double-assignment 4xx to the subscriber (AC-3)', () => {
    let errored: HttpErrorResponse | undefined;
    service
      .issueAssets({ employeeId: 'emp-1', assets: [] })
      .subscribe({ error: (e) => (errored = e) });

    const req = httpMock.expectOne(`${base}/issue`);
    req.flush(
      {
        message:
          'This asset (Serial: XYZ) is currently assigned to Jane Doe. Please return it first or select a different asset.',
      },
      { status: 409, statusText: 'Conflict' },
    );

    expect(errored).toBeTruthy();
    expect(OnboardingAssetService.parseErrorMessage(errored!)).toContain(
      'currently assigned to Jane Doe',
    );
  });

  it('parseErrorMessage extracts message or falls back', () => {
    const withMsg = new HttpErrorResponse({ error: { message: 'Boom' }, status: 400 });
    expect(OnboardingAssetService.parseErrorMessage(withMsg)).toBe('Boom');
    const plain = new HttpErrorResponse({ error: 'x', status: 500 });
    expect(OnboardingAssetService.parseErrorMessage(plain)).toContain('unexpected');
  });
});
