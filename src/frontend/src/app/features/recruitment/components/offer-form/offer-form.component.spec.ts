import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { ToastrService } from 'ngx-toastr';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse, HttpHeaders, HttpResponse } from '@angular/common/http';

import { OfferFormComponent } from './offer-form.component';
import { OfferService } from '../../services/offer.service';
import { VacancyService } from '../../services/vacancy.service';
import { IOffer } from '../../models/offer.models';

describe('OfferFormComponent (US-REC-007)', () => {
  let fixture: ComponentFixture<OfferFormComponent>;
  let component: OfferFormComponent;
  let offerSpy: jasmine.SpyObj<OfferService>;
  let vacancySpy: jasmine.SpyObj<VacancyService>;
  let toastrSpy: jasmine.SpyObj<ToastrService>;

  const offer = (over: Partial<IOffer> = {}): IOffer => ({
    id: 'o1',
    offerReferenceNumber: 'OFF-001',
    applicantId: 'a1',
    vacancyId: 'v1',
    status: 'Draft',
    offeredPosition: 'Senior Engineer',
    salaryAmount: 120000,
    currency: 'USD',
    salaryFrequency: 'Annual',
    startDate: '2026-07-01',
    expiryDate: '2026-07-08',
    version: 1,
    createdAt: '2026-06-15T10:00:00Z',
    ...over,
  });

  function create(existing: IOffer | null = null): void {
    fixture = TestBed.createComponent(OfferFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('applicantId', 'a1');
    fixture.componentRef.setInput('applicantName', 'Jane Doe');
    fixture.componentRef.setInput('offer', existing);
    fixture.detectChanges();
  }

  /** Fill the create form with a valid offer payload. */
  function fillValid(): void {
    component.form.patchValue({
      offeredPosition: 'Senior Engineer',
      salaryAmount: 120000,
      currency: 'USD',
      salaryFrequency: 'Annual',
      startDate: '2026-07-01',
      expiryDate: '2026-07-08',
    });
  }

  beforeEach(async () => {
    offerSpy = jasmine.createSpyObj('OfferService', [
      'generate',
      'send',
      'respond',
      'withdraw',
      'downloadPdf',
      'listForApplicant',
      'getOffer',
    ]);
    vacancySpy = jasmine.createSpyObj('VacancyService', [
      'getDepartments',
      'searchEmployees',
    ]);
    toastrSpy = jasmine.createSpyObj('ToastrService', [
      'success',
      'error',
      'warning',
    ]);

    vacancySpy.getDepartments.and.returnValue(of([]));
    vacancySpy.searchEmployees.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [OfferFormComponent],
      providers: [
        provideAnimationsAsync(),
        { provide: OfferService, useValue: offerSpy },
        { provide: VacancyService, useValue: vacancySpy },
        { provide: ToastrService, useValue: toastrSpy },
      ],
    }).compileComponents();
  });

  // ─── Mode / step ──────────────────────────────────────────

  it('opens in the create step when no offer is passed', () => {
    create(null);
    expect(component.step()).toBe('create');
  });

  it('opens straight in the preview step for an existing offer (FR-9)', () => {
    create(offer({ status: 'Sent' }));
    expect(component.step()).toBe('preview');
  });

  it('defaults the expiry date to +7 days on the create form (BR-6)', () => {
    create(null);
    const expiry = component.form.get('expiryDate')?.value as string;
    expect(expiry).toBeTruthy();
    const start = new Date();
    const due = new Date(expiry + 'T00:00:00');
    const diffDays = Math.round(
      (due.getTime() - new Date(start.toDateString()).getTime()) / 86400000,
    );
    expect(diffDays).toBe(7);
  });

  it('loads departments on init', () => {
    create(null);
    expect(vacancySpy.getDepartments).toHaveBeenCalled();
  });

  // ─── Validation / Generate (AC-1 / FR-1) ──────────────────

  it('does not generate while the form is invalid', () => {
    create(null);
    component.generate();
    expect(offerSpy.generate).not.toHaveBeenCalled();
    expect(component.form.touched).toBeTrue();
  });

  it('flags expiry-before-start as a cross-field error (BR-6)', () => {
    create(null);
    component.form.patchValue({
      startDate: '2026-07-10',
      expiryDate: '2026-07-01',
    });
    expect(component.form.errors?.['expiryBeforeStart']).toBeTrue();
  });

  it('generate POSTs the offer, switches to preview, and emits changed (AC-1)', () => {
    create(null);
    fillValid();
    offerSpy.generate.and.returnValue(of(offer()));
    const changedSpy = jasmine.createSpy('changed');
    component.changed.subscribe(changedSpy);

    component.generate();

    expect(offerSpy.generate).toHaveBeenCalledWith(
      'a1',
      jasmine.objectContaining({
        offeredPosition: 'Senior Engineer',
        salaryAmount: 120000,
        currency: 'USD',
        salaryFrequency: 'Annual',
      }),
    );
    expect(component.step()).toBe('preview');
    expect(component.currentOffer()?.id).toBe('o1');
    expect(toastrSpy.success).toHaveBeenCalled();
    expect(changedSpy).toHaveBeenCalled();
  });

  it('generate sends the selected reporting manager id', () => {
    create(null);
    fillValid();
    component.selectManager({ id: 'm9', label: 'Boss', sublabel: null });
    offerSpy.generate.and.returnValue(of(offer()));
    component.generate();
    expect(offerSpy.generate).toHaveBeenCalledWith(
      'a1',
      jasmine.objectContaining({ reportingManagerId: 'm9' }),
    );
  });

  it('generate surfaces an error toast on failure', () => {
    create(null);
    fillValid();
    offerSpy.generate.and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { message: 'boom' }, status: 400 })),
    );
    component.generate();
    expect(toastrSpy.error).toHaveBeenCalledWith('boom');
    expect(component.generating()).toBeFalse();
  });

  // ─── Lifecycle gates (FR-5 / AC-3 / FR-8) ─────────────────

  it('shows Send for a Draft, hides respond/withdraw-only actions appropriately', () => {
    create(offer({ status: 'Draft' }));
    const o = component.currentOffer()!;
    expect(component.canSend(o)).toBeTrue();
    expect(component.canRespond(o)).toBeFalse();
    expect(component.canWithdraw(o)).toBeTrue();
  });

  it('shows Respond + Withdraw for a Sent offer (AC-3 / FR-8)', () => {
    create(offer({ status: 'Sent' }));
    const o = component.currentOffer()!;
    expect(component.canSend(o)).toBeFalse();
    expect(component.canRespond(o)).toBeTrue();
    expect(component.canWithdraw(o)).toBeTrue();
  });

  it('shows no lifecycle actions for an Accepted offer', () => {
    create(offer({ status: 'Accepted' }));
    const o = component.currentOffer()!;
    expect(component.canSend(o)).toBeFalse();
    expect(component.canRespond(o)).toBeFalse();
    expect(component.canWithdraw(o)).toBeFalse();
  });

  // ─── Send (AC-2) ──────────────────────────────────────────

  it('send calls the service and emits changed with the Sent offer', () => {
    create(offer({ status: 'Draft' }));
    offerSpy.send.and.returnValue(of(offer({ status: 'Sent' })));
    const changedSpy = jasmine.createSpy('changed');
    component.changed.subscribe(changedSpy);

    component.send();

    expect(offerSpy.send).toHaveBeenCalledWith('o1');
    expect(component.currentOffer()?.status).toBe('Sent');
    expect(changedSpy).toHaveBeenCalled();
  });

  // ─── Respond (AC-3 / BR-3) ────────────────────────────────

  it('accept records Accepted and emits both changed and accepted (BR-3)', () => {
    create(offer({ status: 'Sent' }));
    offerSpy.respond.and.returnValue(of(offer({ status: 'Accepted', response: 'Accepted' })));
    const acceptedSpy = jasmine.createSpy('accepted');
    component.accepted.subscribe(acceptedSpy);

    component.accept();

    expect(offerSpy.respond).toHaveBeenCalledWith('o1', 'Accepted');
    expect(acceptedSpy).toHaveBeenCalled();
    expect(component.currentOffer()?.status).toBe('Accepted');
  });

  it('decline records Declined and does NOT emit accepted', () => {
    create(offer({ status: 'Sent' }));
    offerSpy.respond.and.returnValue(of(offer({ status: 'Declined', response: 'Declined' })));
    const acceptedSpy = jasmine.createSpy('accepted');
    component.accepted.subscribe(acceptedSpy);

    component.decline();

    expect(offerSpy.respond).toHaveBeenCalledWith('o1', 'Declined');
    expect(acceptedSpy).not.toHaveBeenCalled();
    expect(component.currentOffer()?.status).toBe('Declined');
  });

  // ─── Withdraw (FR-8) ──────────────────────────────────────

  it('withdraw calls the service and reflects the Withdrawn status', () => {
    create(offer({ status: 'Sent' }));
    offerSpy.withdraw.and.returnValue(of(offer({ status: 'Withdrawn' })));
    component.withdraw();
    expect(offerSpy.withdraw).toHaveBeenCalledWith('o1');
    expect(component.currentOffer()?.status).toBe('Withdrawn');
  });

  // ─── PDF download (FR-4) ──────────────────────────────────

  it('downloadPdf triggers a blob download', () => {
    create(offer());
    const blob = new Blob(['pdf'], { type: 'application/pdf' });
    const res = new HttpResponse<Blob>({
      body: blob,
      headers: new HttpHeaders({
        'content-disposition': 'attachment; filename="offer-OFF-001.pdf"',
      }),
    });
    offerSpy.downloadPdf.and.returnValue(of(res));
    const clickSpy = spyOn(HTMLAnchorElement.prototype, 'click');
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake');
    spyOn(URL, 'revokeObjectURL');

    component.downloadPdf();

    expect(offerSpy.downloadPdf).toHaveBeenCalledWith('o1');
    expect(clickSpy).toHaveBeenCalled();
    expect(component.downloading()).toBeFalse();
  });

  it('downloadPdf surfaces an error toast on failure', () => {
    create(offer());
    offerSpy.downloadPdf.and.returnValue(
      throwError(() => new HttpErrorResponse({ error: { message: 'no file' }, status: 404 })),
    );
    component.downloadPdf();
    expect(toastrSpy.error).toHaveBeenCalledWith('no file');
  });

  // ─── Cancel ───────────────────────────────────────────────

  it('cancel emits cancelled', () => {
    create(null);
    const cancelSpy = jasmine.createSpy('cancelled');
    component.cancelled.subscribe(cancelSpy);
    component.cancel();
    expect(cancelSpy).toHaveBeenCalled();
  });
});
