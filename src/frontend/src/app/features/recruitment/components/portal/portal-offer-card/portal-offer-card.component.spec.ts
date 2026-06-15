import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PortalOfferCardComponent } from './portal-offer-card.component';
import { IPortalOffer } from '../../../models/portal.models';

describe('PortalOfferCardComponent (US-REC-008)', () => {
  let fixture: ComponentFixture<PortalOfferCardComponent>;
  let component: PortalOfferCardComponent;

  const offer = (over: Partial<IPortalOffer> = {}): IPortalOffer => ({
    id: 'o1',
    offerReferenceNumber: 'OFF-1',
    status: 'Sent',
    offeredPosition: 'Engineer',
    departmentName: 'Engineering',
    salaryAmount: 120000,
    currency: 'USD',
    salaryFrequency: 'Annual',
    benefitsSummary: null,
    startDate: '2026-07-01',
    expiryDate: '2026-07-15',
    canRespond: true,
    hasDocument: true,
    ...over,
  });

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PortalOfferCardComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(PortalOfferCardComponent);
    component = fixture.componentInstance;
  });

  it('shows Accept/Decline for a Sent offer with no response', () => {
    fixture.componentRef.setInput('offer', offer());
    fixture.detectChanges();
    expect(component.canRespond()).toBeTrue();
    expect(fixture.nativeElement.textContent).toContain('Accept offer');
    expect(fixture.nativeElement.textContent).toContain('Decline');
  });

  it('hides the buttons and shows the recorded response once acted on (BR-2)', () => {
    fixture.componentRef.setInput('offer', offer({ status: 'Accepted', canRespond: false }));
    fixture.detectChanges();
    expect(component.canRespond()).toBeFalse();
    expect(fixture.nativeElement.textContent).toContain('You accepted this offer');
  });

  it('opens a confirmation modal before emitting respond (BR-2 one-time)', () => {
    const emit = jasmine.createSpy('respond');
    fixture.componentRef.setInput('offer', offer());
    fixture.detectChanges();
    component.respond.subscribe(emit);

    component.ask('Accepted');
    fixture.detectChanges();
    expect(component.pendingChoice()).toBe('Accepted');
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeTruthy();
    expect(emit).not.toHaveBeenCalled();

    component.confirm();
    expect(emit).toHaveBeenCalledWith('Accepted');
  });

  it('cancel closes the modal without emitting', () => {
    const emit = jasmine.createSpy('respond');
    fixture.componentRef.setInput('offer', offer());
    fixture.detectChanges();
    component.respond.subscribe(emit);

    component.ask('Declined');
    component.cancel();
    expect(component.pendingChoice()).toBeNull();
    expect(emit).not.toHaveBeenCalled();
  });

  it('emits download when the PDF button is used', () => {
    const emit = jasmine.createSpy('download');
    fixture.componentRef.setInput('offer', offer());
    fixture.detectChanges();
    component.download.subscribe(emit);
    const btn: HTMLButtonElement = fixture.nativeElement.querySelector('button');
    btn.click();
    expect(emit).toHaveBeenCalled();
  });
});
