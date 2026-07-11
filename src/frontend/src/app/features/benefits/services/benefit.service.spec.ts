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
  IEligibilityRule,
  ICreateEligibilityRule,
  IEligiblePlan,
  IEnrollRequest,
  IBenefitEnrollment,
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

  // ===============================================================
  // US-TRN-003: eligibility rules + enrollment
  // ===============================================================

  const mockRule: IEligibilityRule = {
    id: 'r-1',
    benefitPlanId: 'p-1',
    attribute: 'TenureDays',
    operator: '>=',
    value: '90',
  };

  const mockEligiblePlan: IEligiblePlan = {
    planId: 'p-1',
    name: 'Gold Health',
    type: 'Health',
    currency: 'USD',
    employeeCost: 100,
    employerCost: 400,
    effectiveFrom: '2026-08-01',
    effectiveTo: '2027-07-31',
    enrollmentOpen: true,
  };

  const mockEnrollment: IBenefitEnrollment = {
    id: 'e-1',
    planId: 'p-1',
    planName: 'Gold Health',
    employeeId: 'emp-1',
    employeeName: 'Ada Lovelace',
    status: 'Active',
    coverageLevel: 'EmployeeOnly',
    effectiveDate: '2026-08-01',
    endDate: null,
    electedAt: '2026-07-15T10:00:00Z',
  };

  describe('getEligibilityRules', () => {
    it('should GET the rules for a plan', () => {
      service.getEligibilityRules('p-1').subscribe((rules) => {
        expect(rules.length).toBe(1);
        expect(rules[0].attribute).toBe('TenureDays');
      });

      const req = httpMock.expectOne(`${baseUrl}/plans/p-1/eligibility-rules`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockRule]);
    });
  });

  describe('createEligibilityRule', () => {
    it('should POST the rule request', () => {
      const request: ICreateEligibilityRule = {
        attribute: 'EmploymentType',
        operator: '==',
        value: 'FullTime',
      };

      service.createEligibilityRule('p-1', request).subscribe((rule) => {
        expect(rule.id).toBe('r-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/plans/p-1/eligibility-rules`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockRule, attribute: 'EmploymentType', operator: '==', value: 'FullTime' });
    });
  });

  describe('deleteEligibilityRule', () => {
    it('should DELETE the rule by id', () => {
      service.deleteEligibilityRule('r-1').subscribe();

      const req = httpMock.expectOne(`${baseUrl}/eligibility-rules/r-1`);
      expect(req.request.method).toBe('DELETE');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(null);
    });
  });

  describe('getEligiblePlans', () => {
    it('should GET the current user eligible plans', () => {
      service.getEligiblePlans().subscribe((plans) => {
        expect(plans.length).toBe(1);
        expect(plans[0].planId).toBe('p-1');
        expect(plans[0].enrollmentOpen).toBeTrue();
      });

      const req = httpMock.expectOne(`${baseUrl}/eligible`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockEligiblePlan]);
    });
  });

  describe('enroll', () => {
    it('should POST a self enrollment (no employeeId)', () => {
      const request: IEnrollRequest = {
        planId: 'p-1',
        coverageLevel: 'EmployeeOnly',
      };

      service.enroll(request).subscribe((enrollment) => {
        expect(enrollment.status).toBe('Active');
      });

      const req = httpMock.expectOne(`${baseUrl}/enrollments`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(request);
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockEnrollment);
    });

    it('should POST an admin enrollment for another employee', () => {
      const request: IEnrollRequest = {
        planId: 'p-1',
        coverageLevel: 'Family',
        employeeId: 'emp-2',
      };

      service.enroll(request).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/enrollments`);
      expect(req.request.body).toEqual(request);
      req.flush(mockEnrollment);
    });
  });

  describe('terminate', () => {
    it('should POST with an empty body by default', () => {
      service.terminate('e-1').subscribe((enrollment) => {
        expect(enrollment.id).toBe('e-1');
      });

      const req = httpMock.expectOne(`${baseUrl}/enrollments/e-1/terminate`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({});
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ ...mockEnrollment, status: 'Terminated', endDate: '2026-09-01' });
    });

    it('should POST an explicit end date', () => {
      service.terminate('e-1', { endDate: '2026-10-01' }).subscribe();

      const req = httpMock.expectOne(`${baseUrl}/enrollments/e-1/terminate`);
      expect(req.request.body).toEqual({ endDate: '2026-10-01' });
      req.flush({ ...mockEnrollment, status: 'Terminated', endDate: '2026-10-01' });
    });
  });

  describe('getMyEnrollments', () => {
    it('should GET the current user enrollments', () => {
      service.getMyEnrollments().subscribe((list) => {
        expect(list.length).toBe(1);
        expect(list[0].planName).toBe('Gold Health');
      });

      const req = httpMock.expectOne(`${baseUrl}/me/enrollments`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockEnrollment]);
    });
  });

  describe('getEmployeeEnrollments', () => {
    it('should GET a specific employee enrollments', () => {
      service.getEmployeeEnrollments('emp-1').subscribe((list) => {
        expect(list.length).toBe(1);
      });

      const req = httpMock.expectOne(`${baseUrl}/employees/emp-1/enrollments`);
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([mockEnrollment]);
    });
  });
});
