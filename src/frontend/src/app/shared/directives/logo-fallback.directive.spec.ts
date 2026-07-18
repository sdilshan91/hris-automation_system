import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LogoFallbackDirective } from './logo-fallback.directive';

@Component({
  standalone: true,
  imports: [LogoFallbackDirective],
  template: `
    <span class="wrap">
      <span class="initial">A</span>
      <img src="/broken/logo.png" alt="Acme" appLogoFallback />
    </span>
  `,
})
class HostComponent {}

describe('LogoFallbackDirective (ISSUE-204)', () => {
  let fixture: ComponentFixture<HostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
  });

  it('shows the image by default', () => {
    const img: HTMLImageElement = fixture.nativeElement.querySelector('img');
    expect(img.hidden).toBeFalse();
  });

  it('hides the image after a load error so the placeholder shows', () => {
    const img: HTMLImageElement = fixture.nativeElement.querySelector('img');
    img.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(img.hidden).toBeTrue();
  });
});
