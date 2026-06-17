import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';

import { MyAssetsComponent } from './my-assets.component';
import { OnboardingAssetService } from '../../services/onboarding-asset.service';
import { IAssignedAsset } from '../../models/onboarding-asset.models';

describe('MyAssetsComponent', () => {
  let component: MyAssetsComponent;
  let fixture: ComponentFixture<MyAssetsComponent>;
  let serviceSpy: jasmine.SpyObj<OnboardingAssetService>;

  const assets: IAssignedAsset[] = [
    {
      assetId: 'a-1',
      assetType: 'Laptop',
      assetTag: 'LAP-0042',
      serialNumber: 'SN-123',
      brand: 'Dell',
      model: 'XPS 13',
      condition: 'Good',
      status: 'assigned',
      issueDate: '2026-06-17',
      notes: null,
    },
  ];

  async function setup(): Promise<void> {
    serviceSpy = jasmine.createSpyObj('OnboardingAssetService', ['getMyAssets']);
    serviceSpy.getMyAssets.and.returnValue(of(assets));

    await TestBed.configureTestingModule({
      imports: [MyAssetsComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: OnboardingAssetService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(MyAssetsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  it('loads the logged-in employee assets on init (AC-4)', async () => {
    await setup();
    expect(serviceSpy.getMyAssets).toHaveBeenCalled();
    expect(component.assets().length).toBe(1);
    expect(component.loading()).toBeFalse();
  });

  it('renders type, tag, serial and condition chip (AC-4 / BR-6 read-only)', async () => {
    await setup();
    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain('Laptop');
    expect(html).toContain('LAP-0042');
    expect(html).toContain('SN-123');
    expect(html).toContain('Good');
    // No mutating controls in a read-only view.
    const buttons = (fixture.nativeElement as HTMLElement).querySelectorAll('button');
    expect(buttons.length).toBe(0);
  });

  it('maps condition to the colour chip class', async () => {
    await setup();
    expect(component.chipClass(assets[0])).toBe('chip-condition-good');
  });

  it('joins brand + model into a make label', async () => {
    await setup();
    expect(component.makeLabel(assets[0])).toBe('Dell XPS 13');
    expect(component.makeLabel({ ...assets[0], brand: null, model: null })).toBe('');
  });

  it('shows the empty state when no assets are assigned', async () => {
    serviceSpy = jasmine.createSpyObj('OnboardingAssetService', ['getMyAssets']);
    serviceSpy.getMyAssets.and.returnValue(of([]));
    await TestBed.configureTestingModule({
      imports: [MyAssetsComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: OnboardingAssetService, useValue: serviceSpy },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(MyAssetsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.assets().length).toBe(0);
    const html = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(html).toContain('No company assets');
  });

  it('treats a 404 as empty, not an error', async () => {
    serviceSpy = jasmine.createSpyObj('OnboardingAssetService', ['getMyAssets']);
    serviceSpy.getMyAssets.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );
    await TestBed.configureTestingModule({
      imports: [MyAssetsComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: OnboardingAssetService, useValue: serviceSpy },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(MyAssetsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.loadError()).toBeNull();
    expect(component.assets().length).toBe(0);
  });

  it('shows a load error on a non-404 failure', async () => {
    serviceSpy = jasmine.createSpyObj('OnboardingAssetService', ['getMyAssets']);
    serviceSpy.getMyAssets.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    await TestBed.configureTestingModule({
      imports: [MyAssetsComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: OnboardingAssetService, useValue: serviceSpy },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(MyAssetsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    expect(component.loadError()).toContain('Failed to load');
  });
});
