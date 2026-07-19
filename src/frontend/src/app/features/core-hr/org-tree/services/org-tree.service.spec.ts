import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { OrgTreeService } from './org-tree.service';
import { IOrgTreeNode } from '../models/org-tree.models';
import { environment } from '../../../../../environments/environment';

// @TC-CHR-336 (US-CHR-006) — nested-children org-tree consumption + lazy-expand.
// FE-only binding (no xUnit Trait): these Karma specs are the automated arms for
// TC-CHR-336 — nested children consumed on load, zero-HTTP on in-depth expand, and
// the truncated-node fallback fetch. See docs/QA/core-hr/TC-CHR-336.md.
describe('OrgTreeService', () => {
  let service: OrgTreeService;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/org-tree`;

  // DF-17: the API nests direct children inline inside each ROOT node down to the
  // requested depth. `mockNodes` is a single root (Engineering) carrying its children.
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
      children: [
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
      ],
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
          // DF-17: the service returns the ROOT nodes (one root here), NOT a flattened list.
          expect(nodes.length).toBe(1);
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
      // ISSUE-207: the real payload is an OBJECT { nodes, view, reportingViewAvailable },
      // not a bare array. The service must project `.nodes`; flushing the object here means
      // this test fails against the pre-fix bare-array typing (which yielded 0 nodes).
      req.flush({ nodes: mockNodes, view: 'department', reportingViewAvailable: false });
    });

    it('should preserve the nested children delivered inside each root (DF-17)', () => {
      service
        .getOrgTree({ view: 'department', depth: 2 })
        .subscribe((nodes) => {
          // The nested `children` the API delivered must survive intact — the service no
          // longer flattens the payload down to roots-only.
          expect(nodes[0].children?.length).toBe(2);
          expect(nodes[0].children?.[0].name).toBe('Frontend');
          expect(nodes[0].children?.[1].name).toBe('Backend');
          // Backend advertises children (childrenCount=3) but none were delivered inline —
          // it was truncated at the depth limit and must be lazy-fetched on expand.
          expect(nodes[0].children?.[1].childrenCount).toBe(3);
          expect(nodes[0].children?.[1].children).toBeUndefined();
        });

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('depth') === '2'
      );
      req.flush({ nodes: mockNodes, view: 'department', reportingViewAvailable: false });
    });

    it('should send parentId param for lazy-loading a truncated subtree (DF-17 fallback)', () => {
      // The lazy round-trip is still the fallback for subtrees truncated beyond the
      // delivered depth (e.g. expanding Backend/dept-3, which had no inline children).
      const leaf: IOrgTreeNode = {
        nodeId: 'dept-4',
        nodeType: 'department',
        name: 'DevOps',
        title: null,
        avatarUrl: null,
        employeeCount: 3,
        childrenCount: 0,
        parentId: 'dept-3',
      };
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
      req.flush({
        nodes: [
          { ...leaf, nodeId: 'dept-4', name: 'DevOps' },
          { ...leaf, nodeId: 'dept-5', name: 'QA' },
        ],
        view: 'department',
        reportingViewAvailable: false,
      });
    });

    it('should not send parentId when null', () => {
      service
        .getOrgTree({ view: 'reporting', depth: 2 })
        .subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('view') === 'reporting'
      );
      expect(req.request.params.has('parentId')).toBeFalse();
      req.flush({ nodes: [], view: 'department', reportingViewAvailable: false });
    });

    it('should send reporting view param', () => {
      service
        .getOrgTree({ view: 'reporting', depth: 2 })
        .subscribe();

      const req = httpMock.expectOne(
        (r) => r.url === baseUrl && r.params.get('view') === 'reporting'
      );
      expect(req.request.method).toBe('GET');
      req.flush({ nodes: [], view: 'department', reportingViewAvailable: false });
    });

    it('should include withCredentials for tenant-scoped auth', () => {
      service.getOrgTree({ view: 'department', depth: 1 }).subscribe();

      const req = httpMock.expectOne((r) => r.url === baseUrl);
      expect(req.request.withCredentials).toBeTrue();
      req.flush({ nodes: [], view: 'department', reportingViewAvailable: false });
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
