import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse, HttpResponse, HttpHeaders } from '@angular/common/http';
import { ToastrService } from 'ngx-toastr';

import { CandidatePortalComponent } from './candidate-portal.component';
import { PortalService } from '../../../services/portal.service';
import { TenantService } from '../../../../../core/tenant/tenant.service';
import { IPortalDashboard } from '../../../models/portal.models';

describe('CandidatePortalComponent (US-REC-008)', () => {
  let component: CandidatePortalComponent;
  let fixture: ComponentFixture<CandidatePortalComponent>;
  let portalSpy: jasmine.SpyObj<PortalService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const dashboard = (over: Partial<IPortalDashboard> = {}): IPortalDashboard => ({
    applicantName: 'Jane Doe',
    applicantEmail: 'jane@example.com',
    applications: [
      {
        id: 'app1',
        applicationReferenceNumber: 'APP-001',
        vacancyId: 'v1',
        vacancyTitle: 'Backend Engineer',
        departmentName: 'Engineering',
        appliedAt: '2026-06-01',
        stage: 'Offer',
        interview: null,
        offer: {
          id: 'off1',
          offerReferenceNumber: 'OFF-1',
          status: 'Sent',
          offeredPosition: 'Backend Engineer',
          departmentName: 'Engineering',
          salaryAmount: 120000,
          currency: 'USD',
          salaryFrequency: 'Annual',
          benefitsSummary: null,
          startDate: '2026-07-01',
          expiryDate: '2026-07-15',
          canRespond: true,
          hasDocument: true,
        },
        timeline: [{ at: '2026-06-01T09:00:00Z', label: 'Application received' }],
      },
    ],
    ...over,
  });

  beforeEach(() => {
    portalSpy = jasmine.createSpyObj('PortalService', [
      'getDashboard',
      'respondToOffer',
      'downloadOfferDocument',
      'requestLink',
    ]);
    toastrSpy = jasmine.createSpyObj('ToastrService', ['success', 'error']);
  });

  /** Configure the TestBed with a given `?token=` value and create the component. */
  const setup = async (token: string | null) => {
    await TestBed.configureTestingModule({
      imports: [CandidatePortalComponent],
      providers: [
        provideRouter([]),
        { provide: PortalService, useValue: portalSpy },
        { provide: ToastrService, useValue: toastrSpy },
        {
          provide: TenantService,
          useValue: {
            displayName: () => 'Acme',
            tenantContext: () => ({ logoUrl: null }),
          },
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap(token ? { token } : {}),
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CandidatePortalComponent);
    component = fixture.componentInstance;
  };

  // ─── AC-1: dashboard loads with token from URL ────────────

  it('loads the dashboard with the token from the query param', async () => {
    portalSpy.getDashboard.and.returnValue(of(dashboard()));
    await setup('tok-1');
    fixture.detectChanges();
    expect(portalSpy.getDashboard).toHaveBeenCalledWith('tok-1');
    expect(component.loading()).toBeFalse();
    expect(component.applications().length).toBe(1);
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Backend Engineer');
    expect(text).toContain('Welcome back, Jane Doe');
  });

  // ─── empty state ──────────────────────────────────────────

  it('shows the empty state with a careers link when no applications', async () => {
    portalSpy.getDashboard.and.returnValue(of(dashboard({ applications: [] })));
    await setup('tok-1');
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('No applications found');
  });

  // ─── FR-8 / BR-5: no token → request-a-new-link prompt ────

  it('shows the request-new-link prompt when no token is present', async () => {
    await setup(null);
    fixture.detectChanges();
    expect(portalSpy.getDashboard).not.toHaveBeenCalled();
    expect(component.tokenError()).toBeTrue();
    const text = fixture.nativeElement.textContent as string;
    expect(text).toContain('Your link has expired');
  });

  it('shows the request-new-link prompt when the token is expired (410)', async () => {
    portalSpy.getDashboard.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 410 })),
    );
    await setup('expired');
    fixture.detectChanges();
    expect(component.tokenError()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Your link has expired');
  });

  it('shows a generic error (not the link prompt) for a non-token failure', async () => {
    portalSpy.getDashboard.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({ status: 500, error: { message: 'Boom' } }),
      ),
    );
    await setup('tok-1');
    fixture.detectChanges();
    expect(component.tokenError()).toBeFalse();
    expect(component.error()).toBe('Boom');
  });

  it('requestLink shows the confirmation regardless of backend outcome (no enumeration)', async () => {
    portalSpy.requestLink.and.returnValue(of(undefined));
    await setup(null);
    fixture.detectChanges();
    component.email.set('jane@example.com');
    component.requestLink();
    expect(portalSpy.requestLink).toHaveBeenCalledWith('jane@example.com');
    expect(component.linkSent()).toBeTrue();
  });

  it('requestLink still confirms on an error (anti-enumeration, NFR-6)', async () => {
    portalSpy.requestLink.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 429 })),
    );
    await setup(null);
    fixture.detectChanges();
    component.email.set('x@example.com');
    component.requestLink();
    expect(component.linkSent()).toBeTrue();
  });

  // ─── AC-3 / BR-3: accept offer reloads to reflect Hired ───

  it('accepting an offer reloads the dashboard so the stage advances to Hired', async () => {
    const hired = dashboard({
      applications: [
        {
          ...dashboard().applications[0],
          stage: 'Hired',
          offer: {
            ...dashboard().applications[0].offer!,
            status: 'Accepted',
            canRespond: false,
          },
        },
      ],
    });
    portalSpy.getDashboard.and.returnValues(of(dashboard()), of(hired));
    portalSpy.respondToOffer.and.returnValue(
      of({
        ...dashboard().applications[0].offer!,
        status: 'Accepted',
        canRespond: false,
      }),
    );
    await setup('tok-1');
    fixture.detectChanges();

    component.onRespond(component.applications()[0], 'Accepted');
    expect(portalSpy.respondToOffer).toHaveBeenCalledWith('off1', 'tok-1', 'Accepted');
    expect(toastrSpy.success).toHaveBeenCalled();
    // Reloaded: getDashboard called twice (initial + after respond).
    expect(portalSpy.getDashboard).toHaveBeenCalledTimes(2);
    expect(component.applications()[0].stage).toBe('Hired');
  });

  it('an expired token during respond surfaces the link prompt', async () => {
    portalSpy.getDashboard.and.returnValue(of(dashboard()));
    portalSpy.respondToOffer.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 410 })),
    );
    await setup('tok-1');
    fixture.detectChanges();
    component.onRespond(component.applications()[0], 'Declined');
    expect(component.tokenError()).toBeTrue();
  });

  // ─── FR-4: download triggers a blob download ──────────────

  it('downloading an offer fetches the blob and clears the downloading flag', async () => {
    portalSpy.getDashboard.and.returnValue(of(dashboard()));
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    const res = new HttpResponse<Blob>({
      body: blob,
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="offer-OFF-1.pdf"',
      }),
    });
    portalSpy.downloadOfferDocument.and.returnValue(of(res));
    spyOn(URL, 'createObjectURL').and.returnValue('blob:url');
    spyOn(URL, 'revokeObjectURL');
    await setup('tok-1');
    fixture.detectChanges();

    component.onDownload(component.applications()[0]);
    expect(portalSpy.downloadOfferDocument).toHaveBeenCalledWith('off1', 'tok-1');
    expect(component.downloadingId()).toBeNull();
  });
});
