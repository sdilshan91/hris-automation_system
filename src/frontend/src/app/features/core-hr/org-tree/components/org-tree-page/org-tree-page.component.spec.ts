import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ToastrModule } from 'ngx-toastr';
import { OrgTreePageComponent } from './org-tree-page.component';
import { OrgTreeService } from '../../services/org-tree.service';
import {
  IOrgTreeNode,
  buildTreeFromFlat,
  createNodeState,
  findNodeInTree,
  findPathToNode,
} from '../../models/org-tree.models';
import { environment } from '../../../../../../environments/environment';

// @TC-CHR-336 (US-CHR-006) — org-tree page: nested-children consumption on load,
// zero-HTTP on in-depth expand, and truncated-node fallback fetch. FE-only binding
// (no xUnit Trait) — these Karma specs are TC-CHR-336's automated arms.
// See docs/QA/core-hr/TC-CHR-336.md.

// ─── Pure-function/utility tests (no httpMock.verify) ──────────

describe('OrgTree model utilities', () => {
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
      childrenCount: 0,
      parentId: 'dept-1',
    },
    {
      nodeId: 'dept-4',
      nodeType: 'department',
      name: 'Marketing',
      title: null,
      avatarUrl: null,
      employeeCount: 8,
      childrenCount: 0,
      parentId: null,
    },
  ];

  it('should build tree from flat node array', () => {
    const tree = buildTreeFromFlat(mockNodes);
    expect(tree.length).toBe(2);
    expect(tree[0].node.name).toBe('Engineering');
    expect(tree[1].node.name).toBe('Marketing');
  });

  it('should nest children under parent nodes', () => {
    const tree = buildTreeFromFlat(mockNodes);
    const eng = tree[0];
    expect(eng.children.length).toBe(2);
    expect(eng.children[0].node.name).toBe('Frontend');
    expect(eng.children[1].node.name).toBe('Backend');
  });

  it('should set correct levels on tree nodes', () => {
    const tree = buildTreeFromFlat(mockNodes);
    expect(tree[0].level).toBe(0);
    expect(tree[0].children[0].level).toBe(1);
    expect(tree[1].level).toBe(0);
  });

  it('should handle empty node array', () => {
    const tree = buildTreeFromFlat([]);
    expect(tree.length).toBe(0);
  });

  it('should mark leaf nodes as childrenLoaded', () => {
    const tree = buildTreeFromFlat(mockNodes);
    // Frontend has childrenCount=0, so childrenLoaded should be true
    expect(tree[0].children[0].childrenLoaded).toBeTrue();
  });

  it('should create a node state with defaults', () => {
    const state = createNodeState(mockNodes[0], 0);
    expect(state.expanded).toBeFalse();
    expect(state.childrenLoaded).toBeFalse();
    expect(state.loadingChildren).toBeFalse();
    expect(state.highlighted).toBeFalse();
    expect(state.level).toBe(0);
    expect(state.children).toEqual([]);
  });

  it('should find a node in tree by ID', () => {
    const tree = buildTreeFromFlat(mockNodes);
    const found = findNodeInTree(tree, 'dept-2');
    expect(found).toBeTruthy();
    expect(found!.node.name).toBe('Frontend');
  });

  it('should return null for non-existent node ID', () => {
    const tree = buildTreeFromFlat(mockNodes);
    expect(findNodeInTree(tree, 'non-existent')).toBeNull();
  });

  it('should find path to a node', () => {
    const tree = buildTreeFromFlat(mockNodes);
    const path = findPathToNode(tree, 'dept-2');
    expect(path).toEqual(['dept-1', 'dept-2']);
  });

  it('should return empty path for non-existent target', () => {
    const tree = buildTreeFromFlat(mockNodes);
    const path = findPathToNode(tree, 'non-existent');
    expect(path).toEqual([]);
  });
});

// ─── Component tests ───────────────────────────────────────────

describe('OrgTreePageComponent', () => {
  let component: OrgTreePageComponent;
  let fixture: ComponentFixture<OrgTreePageComponent>;
  let httpMock: HttpTestingController;

  const baseUrl = `${environment.apiBaseUrl}/tenant/org-tree`;

  // DF-17: the API delivers each ROOT with its `children` nested inline down to the
  // requested depth (2). Engineering(root) → [Frontend, Backend]; Frontend carries its own
  // children (delivered), while Backend advertises children (childrenCount=3) but none were
  // delivered inline — it was truncated at the depth limit and must be lazy-fetched.
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
          childrenCount: 2,
          parentId: 'dept-1',
          children: [
            {
              nodeId: 'dept-6',
              nodeType: 'department',
              name: 'Web',
              title: null,
              avatarUrl: null,
              employeeCount: 3,
              childrenCount: 0,
              parentId: 'dept-2',
            },
            {
              nodeId: 'dept-7',
              nodeType: 'department',
              name: 'Mobile',
              title: null,
              avatarUrl: null,
              employeeCount: 2,
              childrenCount: 0,
              parentId: 'dept-2',
            },
          ],
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

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [
        OrgTreePageComponent,
        ToastrModule.forRoot(),
      ],
      providers: [
        OrgTreeService,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideAnimationsAsync(),
        provideRouter([]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(OrgTreePageComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  // ISSUE-207: the org-tree GET endpoint returns { nodes, view, reportingViewAvailable },
  // not a bare array — the service projects `.nodes`. Flush the real envelope shape.
  function orgResult(nodes: IOrgTreeNode[]) {
    return { nodes, view: 'department', reportingViewAvailable: false };
  }

  function flushInitialLoad(nodes: IOrgTreeNode[] = mockNodes): void {
    const req = httpMock.expectOne(
      (r) => r.url === baseUrl && r.params.get('view') === 'department'
    );
    req.flush(orgResult(nodes));
    fixture.detectChanges();
  }

  it('should create', () => {
    fixture.detectChanges();
    flushInitialLoad();
    expect(component).toBeTruthy();
  });

  it('should load initial tree on init with department view and depth 2', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) =>
        r.url === baseUrl &&
        r.params.get('view') === 'department' &&
        r.params.get('depth') === '2'
    );
    expect(req.request.method).toBe('GET');
    req.flush(orgResult(mockNodes));
    fixture.detectChanges();

    expect(component.treeRoots().length).toBe(1);
    expect(component.treeRoots()[0].node.name).toBe('Engineering');
    expect(component.treeRoots()[0].children.length).toBe(2);
  });

  it('should build the whole delivered subtree from nested children (DF-17)', () => {
    // The depth-2 payload nests children inline; the page must build them up front so
    // no per-expand round-trip is needed within the delivered depth.
    fixture.detectChanges();
    flushInitialLoad();

    const eng = component.treeRoots()[0];
    const frontend = eng.children[0];
    // Depth-1 node (Frontend) already has its depth-2 children populated.
    expect(frontend.node.name).toBe('Frontend');
    expect(frontend.childrenLoaded).toBeTrue();
    expect(frontend.children.length).toBe(2);
    expect(frontend.children[0].node.name).toBe('Web');
    expect(frontend.children[1].node.name).toBe('Mobile');
    expect(frontend.children[0].level).toBe(2);
    // Leaf children (childrenCount=0) are marked loaded.
    expect(frontend.children[0].childrenLoaded).toBeTrue();

    // Backend was truncated at the depth limit: childrenCount>0 but no inline children.
    const backend = eng.children[1];
    expect(backend.node.childrenCount).toBe(3);
    expect(backend.children.length).toBe(0);
    expect(backend.childrenLoaded).toBeFalse();

    // Only the single initial GET should have been issued — no per-expand fetches.
    httpMock.verify();
  });

  it('should NOT issue any HTTP call when expanding a node whose children were delivered (DF-17)', () => {
    fixture.detectChanges();
    flushInitialLoad();

    // Frontend's children arrived inline with the initial depth-2 payload.
    const frontend = component.treeRoots()[0].children[0];
    expect(frontend.childrenLoaded).toBeTrue();
    expect(frontend.expanded).toBeFalse();

    const mockEvent = new MouseEvent('click');
    spyOn(mockEvent, 'stopPropagation');
    component.toggleNode(mockEvent, frontend);

    // Expanding it must be a pure in-place toggle — zero org-tree requests.
    httpMock.expectNone((r) => r.url === baseUrl);
    expect(component.treeRoots()[0].children[0].expanded).toBeTrue();
  });

  it('should show loading state initially', () => {
    fixture.detectChanges();
    expect(component.loading()).toBeTrue();

    flushInitialLoad();
    expect(component.loading()).toBeFalse();
  });

  it('should show empty state when no nodes returned', () => {
    fixture.detectChanges();
    flushInitialLoad([]);

    expect(component.treeRoots().length).toBe(0);
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('No departments found');
  });

  it('should show error state on API failure', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    expect(component.errorMessage()).toBeTruthy();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Failed to load organization chart');
  });

  it('should auto-expand root nodes on initial load', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.treeRoots()[0].expanded).toBeTrue();
  });

  it('should switch to reporting view and reload', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.switchView('reporting');
    expect(component.currentView()).toBe('reporting');

    const req = httpMock.expectOne(
      (r) =>
        r.url === baseUrl &&
        r.params.get('view') === 'reporting' &&
        r.params.get('depth') === '2'
    );
    req.flush(orgResult([]));
    fixture.detectChanges();
  });

  it('should not reload when switching to the same view', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.switchView('department');
    // No additional HTTP request should be made
    httpMock.expectNone((r) => r.url === baseUrl);
  });

  it('should show graceful empty state for reporting view', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.switchView('reporting');
    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush(orgResult([]));
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('No reporting structure available yet');
  });

  it('should fall back to a lazy fetch when expanding a truncated node (DF-17)', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad();

    // Backend was truncated at the depth limit (childrenCount=3 but no inline children),
    // so expanding it is the ONLY case that still fires the parentId&depth=1 round-trip.
    const backendNode = component.treeRoots()[0].children[1]; // Backend
    expect(backendNode.node.childrenCount).toBe(3);
    expect(backendNode.children.length).toBe(0);
    expect(backendNode.childrenLoaded).toBeFalse();

    // Simulate expand click
    const mockEvent = new MouseEvent('click');
    spyOn(mockEvent, 'stopPropagation');
    component.toggleNode(mockEvent, backendNode);

    // Expect lazy load request
    const req = httpMock.expectOne(
      (r) =>
        r.url === baseUrl &&
        r.params.get('parentId') === 'dept-3' &&
        r.params.get('depth') === '1'
    );
    req.flush(orgResult([
      {
        nodeId: 'dept-5',
        nodeType: 'department',
        name: 'DevOps',
        title: null,
        avatarUrl: null,
        employeeCount: 3,
        childrenCount: 0,
        parentId: 'dept-3',
      },
    ]));
    tick();
    fixture.detectChanges();

    // Find the updated backend node in the tree
    const updatedBackend = component.treeRoots()[0].children[1];
    expect(updatedBackend.expanded).toBeTrue();
    expect(updatedBackend.childrenLoaded).toBeTrue();
    expect(updatedBackend.children.length).toBe(1);
    expect(updatedBackend.children[0].node.name).toBe('DevOps');
  }));

  it('should collapse an expanded node', () => {
    fixture.detectChanges();
    flushInitialLoad();

    const root = component.treeRoots()[0];
    expect(root.expanded).toBeTrue();

    const mockEvent = new MouseEvent('click');
    spyOn(mockEvent, 'stopPropagation');
    component.toggleNode(mockEvent, root);

    expect(component.treeRoots()[0].expanded).toBeFalse();
  });

  it('should select a node and show detail panel', () => {
    fixture.detectChanges();
    flushInitialLoad();

    const root = component.treeRoots()[0];
    component.selectNode(root);

    expect(component.selectedNode()).toBeTruthy();
    expect(component.selectedNode()!.node.name).toBe('Engineering');
  });

  it('should close detail panel', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.selectNode(component.treeRoots()[0]);
    expect(component.selectedNode()).toBeTruthy();

    component.closeDetail();
    expect(component.selectedNode()).toBeNull();
  });

  it('should perform client-side search and highlight matching node', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad();

    component.onSearchInput('Frontend');
    tick(300); // debounce
    fixture.detectChanges();

    // The Frontend node should be highlighted
    const engNode = component.treeRoots()[0];
    const frontendNode = engNode.children[0];
    expect(frontendNode.highlighted).toBeTrue();
  }));

  it('should clear highlights when search is cleared', fakeAsync(() => {
    fixture.detectChanges();
    flushInitialLoad();

    component.onSearchInput('Frontend');
    tick(300);
    fixture.detectChanges();

    component.onSearchInput('');
    tick(300);
    fixture.detectChanges();

    const engNode = component.treeRoots()[0];
    const frontendNode = engNode.children[0];
    expect(frontendNode.highlighted).toBeFalse();
  }));

  it('should zoom in and out', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.zoom()).toBe(1);

    component.zoomIn();
    expect(component.zoom()).toBeCloseTo(1.1, 1);

    component.zoomOut();
    expect(component.zoom()).toBeCloseTo(1.0, 1);
  });

  it('should fit to screen (reset zoom and pan)', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.zoomIn();
    component.zoomIn();
    component.fitToScreen();

    expect(component.zoom()).toBe(1);
    expect(component.panX()).toBe(0);
    expect(component.panY()).toBe(0);
  });

  it('should not zoom beyond limits', () => {
    fixture.detectChanges();
    flushInitialLoad();

    for (let i = 0; i < 20; i++) component.zoomIn();
    expect(component.zoom()).toBeLessThanOrEqual(2);

    for (let i = 0; i < 30; i++) component.zoomOut();
    expect(component.zoom()).toBeGreaterThanOrEqual(0.3);
  });

  it('should compute zoom percent', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.zoomPercent()).toBe(100);
    component.zoomIn();
    expect(component.zoomPercent()).toBe(110);
  });

  it('should handle keyboard navigation: Enter selects node', () => {
    fixture.detectChanges();
    flushInitialLoad();

    const nodeState = component.treeRoots()[0];
    const event = new KeyboardEvent('keydown', { key: 'Enter' });
    spyOn(event, 'preventDefault');

    component.onNodeKeydown(event, nodeState);
    expect(event.preventDefault).toHaveBeenCalled();
    expect(component.selectedNode()).toBeTruthy();
  });

  it('should handle keyboard navigation: Escape closes detail', () => {
    fixture.detectChanges();
    flushInitialLoad();

    const nodeState = component.treeRoots()[0];
    component.selectNode(nodeState);

    const event = new KeyboardEvent('keydown', { key: 'Escape' });
    spyOn(event, 'preventDefault');
    component.onNodeKeydown(event, nodeState);

    expect(component.selectedNode()).toBeNull();
  });

  it('should get initials from name', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.getInitials('John Doe')).toBe('JD');
    expect(component.getInitials('Alice')).toBe('A');
    expect(component.getInitials('John Michael Smith')).toBe('JM');
  });

  it('should clear selectedNode when switching views', () => {
    fixture.detectChanges();
    flushInitialLoad();

    component.selectNode(component.treeRoots()[0]);
    expect(component.selectedNode()).toBeTruthy();

    component.switchView('reporting');
    expect(component.selectedNode()).toBeNull();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush(orgResult([]));
  });

  it('should show 404-specific message when endpoint not found', () => {
    fixture.detectChanges();

    const req = httpMock.expectOne((r) => r.url === baseUrl);
    req.flush('Not Found', { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(component.errorMessage()).toContain('endpoint not available');
  });

  it('should render department view by default', () => {
    fixture.detectChanges();
    flushInitialLoad();

    expect(component.currentView()).toBe('department');
  });
});
