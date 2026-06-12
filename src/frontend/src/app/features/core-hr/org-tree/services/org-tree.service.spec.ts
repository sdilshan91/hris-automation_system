import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { OrgTreeService } from './org-tree.service';
import { IOrgTreeNode } from '../models/org-tree.models';
import { environment } from '../../../../../environments/environment';

describe('OrgTreeService', () => {
  let service: OrgTreeService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/org-tree`;

  const mockNodes: IOrgTreeNode[] = [
    {
      nodeId: 'dept-1',
      nodeType: 'department',
      name: 'Engineering',
      title: null,
      avatarUrl: null,
      employeeCount: 15,
      childrenCount: 2,
      parentId: null,
    },
    {
      nodeId: 'dept-2',
      nodeType: 'department',
      name: 'Frontend',
      title: null,
      avatarUrl: null,
      employeeCount: 5,
      childrenCount: 0,
      parentId: 'dept-1',
    },
    {
      nodeId: 'dept-3',
      nodeType: 'department',
      name: 'Backend',
      title: null,
      avatarUrl: null,
      employeeCount: 10,
      childrenCount: 3,
      parentId: 'dept-1',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        OrgTreeService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(OrgTreeService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  describe('getOrgTree', () => {
    it('should send GET with view and depth params for initial load', () => {
      service
        .getOrgTree({ view: 'department', depth: 2 })
        .subscribe((nodes) => {
          expect(nodes.length).toBe(3);
          expect(nodes[0].name).toBe('Engineering');
        });

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('view') === 'department' &&
          r.params.get('depth') === '2'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush(mockNodes);
    });

    it('should send parentId param for lazy-loading children', () => {
      service
        .getOrgTree({ view: 'department', parentId: 'dept-3', depth: 1 })
        .subscribe((nodes) => {
          expect(nodes.length).toBe(2);
        });

      const req = httpMock.expectOne(
        (r) =>
          r.url === baseUrl &&
          r.params.get('view') === 'department' &&
          r.params.get('parentId') === 'dept-3' &&
          r.params.get('depth') === '1'
      );
      expect(req.request.method).toBe('GET');
      req.flush([
        { ...mockNodes[1], parentId: 'dept-3', nodeId: 'dept-4', name: 'DevOps' },
        { ...mockNodes[1], parentId: 'dept-3', nodeId: 'dept-5', name: 'QA' },
      ]);
    });

    it('should not send parentId when null', () => {
      service
        .getOrgTree({ view: 'reporting', depth: 2 })
        .subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('view') === 'reporting'
      );
      expect(req.request.params.has('parentId')).toBeFalse();
      req.flush([]);
    });

    it('should send reporting view param', () => {
      service
        .getOrgTree({ view: 'reporting', depth: 2 })
        .subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('view') === 'reporting'
      );
      expect(req.request.method).toBe('GET');
      req.flush([]);
    });

    it('should include withCredentials for tenant-scoped auth', () => {
      service.getOrgTree({ view: 'department', depth: 1 }).subscribe();

      const req = httpMock.expectOne((r) => r.url === baseUrl);
      expect(req.request.withCredentials).toBeTrue();
      req.flush([]);
    });
  });

  describe('searchNodes', () => {
    it('should send GET with query and view params', () => {
      service
        .searchNodes('John', 'department')
        .subscribe((results) => {
          expect(results.length).toBe(1);
        });

      const req = httpMock.expectOne(
        (r) =>
          r.url === `${baseUrl}/search` &&
          r.params.get('q') === 'John' &&
          r.params.get('view') === 'department'
      );
      expect(req.request.method).toBe('GET');
      expect(req.request.withCredentials).toBeTrue();
      req.flush([
        {
          node: {
            nodeId: 'emp-1',
            nodeType: 'employee',
            name: 'John Doe',
            title: 'Engineer',
            avatarUrl: null,
            employeeCount: 0,
            childrenCount: 0,
            parentId: 'dept-2',
          },
          ancestorPath: ['dept-1', 'dept-2', 'emp-1'],
        },
      ]);
    });
  });
});
