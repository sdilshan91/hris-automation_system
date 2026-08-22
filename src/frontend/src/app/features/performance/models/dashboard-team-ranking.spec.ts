// ============================================================================
// ISSUE-379 — `teamRanking` was never a backend gap.
//
// The register recorded it as one of four fields the API "has never sent", rated HIGH, as part of
// "a whole feature surface" with no wire source. The API sends it. In Team scope the server's
// `topPerformers` IS the team ranking — BR-3 deliberately leaves `bottomPerformers` empty for a
// manager (`PerformanceDashboardService.cs:670-672`) — and this mapper hardcoded `teamRanking: []`,
// discarding it. The widget rendered blank because of a FRONTEND bug filed as a backend one.
//
// These arms exist so that cannot silently come back, and so the distinction the register got wrong
// (Team vs Organization) is the thing under test rather than an incidental detail.
// ============================================================================

import { mapDashboardOverview } from './dashboard.models';

function wire(scope: string, top: unknown[]) {
  return { scope, topPerformers: top, bottomPerformers: [] } as never;
}

const performer = {
  employeeId: 'e-1',
  employeeName: 'Alex Doe',
  departmentName: 'Eng',
  finalScore: 4.6,
};

describe('dashboard teamRanking mapping (ISSUE-379)', () => {
  it('uses the server topPerformers as the team ranking in Team scope', () => {
    const mapped = mapDashboardOverview(wire('Team', [performer]));

    expect(mapped.teamRanking.length)
      .withContext('the API sends this; discarding it is what blanked the widget')
      .toBe(1);
    expect(mapped.teamRanking[0].employeeName).toBe('Alex Doe');
  });

  /**
   * The other half of the distinction. In Organization scope `topPerformers` is a genuine org-wide
   * top-N list, NOT a team ranking — mapping it across would put org data in a manager-scoped widget.
   */
  it('leaves the team ranking empty in Organization scope', () => {
    const mapped = mapDashboardOverview(wire('Organization', [performer]));

    expect(mapped.teamRanking)
      .withContext('org-wide top performers are not a team ranking; the widget is manager-scoped')
      .toEqual([]);
    expect(mapped.topPerformers.length)
      .withContext('topPerformers itself must still map — only the ranking derivation is scoped')
      .toBe(1);
  });

  it('defaults to Organization when the wire omits scope', () => {
    const mapped = mapDashboardOverview({ topPerformers: [performer] } as never);
    expect(mapped.teamRanking).toEqual([]);
  });
});
