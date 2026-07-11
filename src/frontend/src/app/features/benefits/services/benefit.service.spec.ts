import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { BenefitService } from './benefit.service';
import {
  IBenefitPlan,
  ICreateBenefitPlan,
  IUpdateBenefitPlan,
} from '../models/benefit.models';
import { environment } from '../../../../environments/environment';

describe('BenefitService', () => {
  let service: BenefitService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/benefits`;

  const mockPlan: IBenefitPlan = {
    id: 'p-1',
    name: 'Gold Health',
    type: 'Health',
    description: 'Comprehensive medical cover',
    coverageDetails: 'In & out-patient, dental add-on',
    employerCost: 400,
    employeeCost: 100,
    currency: 'USD',
    effectiveFrom: '2026-08-01',
    effectiveTo: '2027-07-31',
    enrollmentOpensAt: null,
    enrollmentClosesAt: null,
    status: 'Active',
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        BenefitService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(BenefitService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getPlans', () => {
    it('should return all plans for the tenant', () => {
      service.getPlans().subscribe((plans) => {
        expect(plans.length).toBe(1);
        expect(plans[0].name).toBe('Gold Health');
      });

      const req = httpMock.expectOne(`${baseUrl}/plans`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockPlan]);
    });

    it('should return an empty array when no plans exist', () => {
      service.getPlans().subscribe((plans) => {
        expect(plans.length).toBe(0);
      });

      const req = httpMock.expectOne(`${baseUrl}/plans`);
      req.flush([]);
    });
  });

  describe('getPlan', () => {
    it('should return a single plan by ID', () => {
      service.getPlan('p-1').subscribe((plan) => {
        expect(plan.id).toBe('p-1');
        expect(plan.status).toBe('Active');
      });

      const req = httpMock.expectOne(`${baseUrl}/plans/p-1`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockPlan);
    });
  });

  describe('createPlan', () => {
    it('should post the create request and return the Draft plan', () => {
      const request: ICreateBenefitPlan = {
        name: 'Basic Dental',
        type: 'Dental',
        description: 'Routine dental',
        coverageDetails: null,
        employerCost: 50,
        employeeCost: 20,
        currency: null,
        effectiveFrom: '2026-09-01',
        effectiveTo: null,
        enrollmentOpensAt: null,
        enrollmentClosesAt: null,
      };

      service.createPlan(request).subscribe((plan) => {
        expect(plan.status).toBe('Draft');
      });

      const req = httpMock.expectOne(`${baseUrl}/plans`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockPlan, name: 'Basic Dental', status: 'Draft' });
    });

    it('should send a null currency so the backend applies the tenant default', () => {
      const request: ICreateBenefitPlan = {
        name: 'Vision Care',
        type: 'Vision',
        currency: null,
        effectiveFrom: '2026-09-01',
      };

      service.createPlan(request).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/plans`);
      expect(req.request.body.currency).toBeNull();
      req.flush({ ...mockPlan, name: 'Vision Care', type: 'Vision', status: 'Draft' });
    });
  });

  describe('updatePlan', () => {
    it('should put the update request', () => {
      const request: IUpdateBenefitPlan = {
        name: 'Gold Health Plus',
        type: 'Health',
        employerCost: 450,
        employeeCost: 110,
        effectiveFrom: '2026-08-01',
      };

      service.updatePlan('p-1', request).subscribe((plan) => {
        expect(plan.name).toBe('Gold Health Plus');
      });

      const req = httpMock.expectOne(`${baseUrl}/plans/p-1`);
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockPlan, name: 'Gold Health Plus' });
    });
  });

  describe('changePlanStatus', () => {
    it('should post the target status', () => {
      service.changePlanStatus('p-1', { status: 'Active' }).subscribe((plan) => {
        expect(plan.status).toBe('Active');
      });

      const req = httpMock.expectOne(`${baseUrl}/plans/p-1/status`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ status: 'Active' });
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockPlan);
    });
  });
});
