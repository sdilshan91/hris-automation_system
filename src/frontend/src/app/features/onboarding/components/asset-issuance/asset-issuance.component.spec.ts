import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { ToastrService } from 'ngx-toastr';
import { of, throwError, Subject } from 'rxjs';
import {
  HttpErrorResponse,
  HttpEvent,
  HttpEventType,
} from '@angular/common/http';

import { AssetIssuanceComponent } from './asset-issuance.component';
import { OnboardingAssetService } from '../../services/onboarding-asset.service';
import { IAsset, IAssignedAsset } from '../../models/onboarding-asset.models';

describe('AssetIssuanceComponent', () => {
  let component: AssetIssuanceComponent;
  let fixture: ComponentFixture<AssetIssuanceComponent>;
  let serviceSpy: jasmine.SpyObj<OnboardingAssetService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const available: IAsset[] = [
    {
      assetId: 'a-1',
      assetType: 'Laptop',
      assetTag: 'LAP-0042',
      serialNumber: 'SN-123',
      brand: 'Dell',
      model: 'XPS 13',
      condition: 'Good',
      status: 'available',
    },
    {
      assetId: 'a-2',
      assetType: 'Laptop',
      assetTag: 'LAP-0099',
      serialNumber: 'SN-999',
      brand: 'HP',
      model: 'EliteBook',
      condition: 'New',
      status: 'available',
    },
  ];

  const issued: IAssignedAsset[] = [
    { ...available[0], status: 'assigned', issueDate: '2026-06-17', notes: null },
  ];

  /** queryParams default empty; override per test. */
  async function setup(query: Record<string, string> = {}): Promise<void> {
    serviceSpy = jasmine.createSpyObj('OnboardingAssetService', [
      'getAvailableAssets',
      'getEmployeeAssets',
      'getMyAssets',
      'issueAssets',
    ]);
    serviceSpy.getAvailableAssets.and.returnValue(of(available));
    serviceSpy.issueAssets.and.returnValue(
      of({ type: HttpEventType.Response, body: issued } as HttpEvent<IAssignedAsset[]>),
    );

    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error', 'warning']);

    await TestBed.configureTestingModule({
      imports: [AssetIssuanceComponent],
      providers: [
        provideAnimationsAsync(),
        provideRouter([]),
        { provide: OnboardingAssetService, useValue: serviceSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: { get: () => 'emp-1' },
              queryParamMap: { get: (k: string) => query[k] ?? null },
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssetIssuanceComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('employeeId', 'emp-1');
    fixture.detectChanges();
  }

  it('starts with one asset line (AC-1)', async () => {
    await setup();
    expect(component.lines.length).toBe(1);
  });

  it('reads employeeName from the query param for the success toast', async () => {
    await setup({ employeeName: 'Grace Hopper' });
    expect(component.employeeName()).toBe('Grace Hopper');
  });

  it('Add Another Asset appends a removable line (FR-5)', async () => {
    await setup();
    component.addLine();
    expect(component.lines.length).toBe(2);
    component.removeLine(1);
    expect(component.lines.length).toBe(1);
  });

  it('sets the condition via the chip selector', async () => {
    await setup();
    component.setCondition(0, 'Poor');
    expect(component.lineControl(0, 'condition').value).toBe('Poor');
    expect(component.chipClass('Poor')).toBe('chip-condition-poor');
  });

  it('flags a future issue date as invalid (Data Req §7)', async () => {
    await setup();
    component.lineControl(0, 'issueDate').setValue('2999-01-01');
    expect(component.lineControl(0, 'issueDate').hasError('futureDate')).toBeTrue();
  });

  it('type-ahead loads + filters available assets by term (AC-1)', fakeAsync(async () => {
    await setup();
    component.lineControl(0, 'assetType').setValue('Laptop');
    component.onSearch(0, { target: { value: 'LAP-0099' } } as unknown as Event);
    tick(250);
    expect(serviceSpy.getAvailableAssets).toHaveBeenCalledWith('Laptop');
    expect(component.options().length).toBe(1);
    expect(component.options()[0].assetTag).toBe('LAP-0099');
  }));

  it('choosing an asset pre-fills the line and closes the dropdown', async () => {
    await setup();
    component.chooseAsset(0, available[0]);
    expect(component.lineControl(0, 'assetId').value).toBe('a-1');
    expect(component.lineControl(0, 'assetTag').value).toBe('LAP-0042');
    expect(component.lineControl(0, 'condition').value).toBe('Good');
    expect(component.activeSearch()).toBeNull();
  });

  it('validates a dropped acknowledgment file (FR-6)', async () => {
    await setup();
    const bad = new File(['x'], 'a.txt', { type: 'text/plain' });
    Object.defineProperty(bad, 'size', { value: 10 });
    const evt = { preventDefault() {}, stopPropagation() {}, dataTransfer: { files: [bad] } } as unknown as DragEvent;
    component.onDrop(evt);
    expect(component.ackError()).toContain('Unsupported');
    expect(component.ackFile()).toBeNull();

    const good = new File(['x'], 'r.pdf', { type: 'application/pdf' });
    Object.defineProperty(good, 'size', { value: 2048 });
    const evt2 = { preventDefault() {}, stopPropagation() {}, dataTransfer: { files: [good] } } as unknown as DragEvent;
    component.onDrop(evt2);
    expect(component.ackError()).toBeNull();
    expect(component.ackFile()!.name).toBe('r.pdf');
  });

  it('does not submit when the form is invalid (missing required fields)', async () => {
    await setup();
    component.submit();
    expect(serviceSpy.issueAssets).not.toHaveBeenCalled();
    expect(component.lineControl(0, 'assetType').touched).toBeTrue();
  });

  it('submits all lines in one issuance and shows a success toast (FR-5 / AC-2)', async () => {
    await setup({ employeeName: 'Grace Hopper' });
    component.lineControl(0, 'assetType').setValue('Laptop');
    component.lineControl(0, 'assetTag').setValue('LAP-0042');
    component.addLine();
    component.lineControl(1, 'assetType').setValue('Phone');
    component.lineControl(1, 'assetTag').setValue('PH-1');

    component.submit();

    expect(serviceSpy.issueAssets).toHaveBeenCalledTimes(1);
    const [req] = serviceSpy.issueAssets.calls.mostRecent().args;
    expect(req.employeeId).toBe('emp-1');
    expect(req.assets.length).toBe(2);
    expect(req.assets[1].assetTag).toBe('PH-1');
    expect(toastrSpy.success).toHaveBeenCalledWith('1 asset issued to Grace Hopper.');
    // form reset back to a single fresh line.
    expect(component.lines.length).toBe(1);
    expect(component.submitting()).toBeFalse();
  });

  it('passes the taskInstanceId query param into the request (FR-1)', async () => {
    await setup({ taskInstanceId: 'task-7' });
    component.lineControl(0, 'assetType').setValue('Laptop');
    component.lineControl(0, 'assetTag').setValue('LAP-0042');
    component.submit();
    const [req] = serviceSpy.issueAssets.calls.mostRecent().args;
    expect(req.taskInstanceId).toBe('task-7');
  });

  it('surfaces a double-assignment error inline (AC-3)', async () => {
    await setup();
    const msg = 'This asset (Serial: XYZ) is currently assigned to Jane Doe. Please return it first or select a different asset.';
    serviceSpy.issueAssets.and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { message: msg }, status: 409 })),
    );
    component.lineControl(0, 'assetType').setValue('Laptop');
    component.lineControl(0, 'assetTag').setValue('LAP-0042');
    component.submit();
    expect(component.submitError()).toBe(msg);
    expect(toastrSpy.success).not.toHaveBeenCalled();
    expect(component.submitting()).toBeFalse();
  });

  it('reports upload progress while issuing with an acknowledgment file (FR-6)', async () => {
    await setup();
    const events$ = new Subject<HttpEvent<IAssignedAsset[]>>();
    serviceSpy.issueAssets.and.returnValue(events$.asObservable());

    const good = new File(['x'], 'r.pdf', { type: 'application/pdf' });
    Object.defineProperty(good, 'size', { value: 2048 });
    component.onAckSelected({ target: { files: [good], value: '' } } as unknown as Event);

    component.lineControl(0, 'assetType').setValue('Laptop');
    component.lineControl(0, 'assetTag').setValue('LAP-0042');
    component.submit();

    events$.next({ type: HttpEventType.UploadProgress, loaded: 1024, total: 2048 } as HttpEvent<IAssignedAsset[]>);
    expect(component.uploadProgress()).toBe(50);

    events$.next({ type: HttpEventType.Response, body: issued } as HttpEvent<IAssignedAsset[]>);
    events$.complete();
    expect(component.uploadProgress()).toBeNull();
    expect(toastrSpy.success).toHaveBeenCalled();
  });
});
