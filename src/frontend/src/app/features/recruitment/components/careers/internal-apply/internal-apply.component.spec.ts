import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { signal } from '@angular/core';

import { InternalApplyComponent } from './internal-apply.component';
import { CareersService } from '../../../services/careers.service';
import { AuthService } from '../../../../../core/auth/auth.service';
import { IApplicant } from '../../../models/applicant.models';
import { IUser } from '../../../../../core/auth/auth.models';

describe('InternalApplyComponent', () => {
  let component: InternalApplyComponent;
  let fixture: ComponentFixture<InternalApplyComponent>;
  const currentUser = signal<IUser | null>(null);

  const mockApplicant: IApplicant = {
    id: 'app-1',
    applicationReferenceNumber: 'APP-INT-0001',
    vacancyId: 'vac-1',
    firstName: 'Grace',
    lastName: 'Hopper',
    email: 'grace@example.com',
    phone: null,
    coverLetter: null,
    resumeFileName: 'cv.pdf',
    stage: 'Applied',
    source: 'Internal',
    isInternal: true,
    appliedAt: '2026-06-15',
  };

  beforeEach(async () => {
    currentUser.set({
      userId: 'u1',
      email: 'grace@example.com',
      displayName: 'Grace Hopper',
      mfaEnabled: false,
    });

    await TestBed.configureTestingModule({
      imports: [InternalApplyComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: AuthService, useValue: { currentUser } },
        {
          provide: CareersService,
          useValue: jasmine.createSpyObj('CareersService', ['applyInternal']),
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(InternalApplyComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('vacancyId', 'vac-1');
    fixture.componentRef.setInput('vacancyTitle', 'Senior Engineer');
    fixture.detectChanges();
  });

  it('pre-fills first/last name + email from the auth user (AC-4)', () => {
    const pre = component.prefill();
    expect(pre.firstName).toBe('Grace');
    expect(pre.lastName).toBe('Hopper');
    expect(pre.email).toBe('grace@example.com');
  });

  it('handles a single-word display name gracefully', () => {
    currentUser.set({ userId: 'u2', email: 'cher@example.com', displayName: 'Cher', mfaEnabled: false });
    expect(component.prefill().firstName).toBe('Cher');
    expect(component.prefill().lastName).toBe('');
  });

  it('emits closed when the close button is used', () => {
    let closed = false;
    component.closed.subscribe(() => (closed = true));
    component.close();
    expect(closed).toBeTrue();
  });

  it('shows the confirmation with the reference on success (AC-1)', () => {
    let emitted: IApplicant | undefined;
    component.submitted.subscribe((a) => (emitted = a));
    component.onSubmitted(mockApplicant);
    fixture.detectChanges();
    expect(component.confirmation()).toBe(mockApplicant);
    expect(emitted).toBe(mockApplicant);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('APP-INT-0001');
  });
});
