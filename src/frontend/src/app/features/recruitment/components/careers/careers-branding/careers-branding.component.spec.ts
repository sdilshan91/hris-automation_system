import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CareersBrandingComponent } from './careers-branding.component';
import { TenantService } from '../../../../../core/tenant/tenant.service';

describe('CareersBrandingComponent', () => {
  let fixture: ComponentFixture<CareersBrandingComponent>;

  const build = async (name: string, logoUrl: string | null) => {
    await TestBed.configureTestingModule({
      imports: [CareersBrandingComponent],
      providers: [
        {
          provide: TenantService,
          useValue: {
            displayName: () => name,
            tenantContext: () => ({ logoUrl }),
          },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(CareersBrandingComponent);
    fixture.detectChanges();
    return fixture;
  };

  it('shows the workspace name and Careers label', async () => {
    await build('Acme Corp', null);
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Acme Corp');
    expect(text).toContain('Careers');
  });

  it('renders the logo image when a logo url is present', async () => {
    await build('Acme', 'https://cdn.example.com/logo.png');
    const img = (fixture.nativeElement as HTMLElement).querySelector('img');
    expect(img).toBeTruthy();
    expect(img?.getAttribute('src')).toBe('https://cdn.example.com/logo.png');
  });

  it('falls back to an initial avatar when no logo', async () => {
    await build('Beta', null);
    expect(fixture.componentInstance.initial()).toBe('B');
    expect((fixture.nativeElement as HTMLElement).querySelector('img')).toBeNull();
  });
});
