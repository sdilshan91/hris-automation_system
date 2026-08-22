// ============================================================================
// US-ONB-005 / B4 — the offboarding completion gate, and the two vocabularies that broke it.
//
// The old suite was green while the feature was broken, because its fixtures were hand-written guesses
// rather than the wire. Every task in them carried `clearanceStatus: 'pending'` — a value the API has
// never sent for a task — so the tests agreed with the same wrong union the production code used. The
// whole file typechecked, passed, and proved nothing about the real payload.
//
// It is rebuilt against the GENERATED contract type. The arms below fail if the wire changes.
// ============================================================================

import type { Schema } from '@core/api';
import {
  IOffboardingInstance,
  isPastDate,
  todayIso,
  clearanceChipClass,
  trafficLightClass,
  clearanceLabel,
  taskClearanceChipClass,
  taskClearanceLabel,
  toTaskClearanceStatus,
  toDepartmentClearanceStatus,
  toOffboardingStatus,
  toOffboardingReason,
  toPendingBlockReason,
  toTaskStatus,
  mapOffboardingInstance,
  pendingMandatoryTitles,
  canComplete,
  assetReturnLines,
  OFFBOARDING_REASON_LABEL,
  OFFBOARDING_REASONS,
} from './offboarding.models';

type InstanceWire = Schema<'OnboardingOffboardingInstanceDto'>;

/**
 * A REAL wire payload: flat PascalCase status tokens, `clearanceCategoryName` for the department, task
 * clearance in the `approved`/`pending_issues`/null vocabulary, and the server's own completion verdict.
 */
function wire(over: Partial<InstanceWire> = {}): InstanceWire {
  return {
    id: 'off-1',
    employeeId: 'emp-1',
    employeeName: 'Alex Doe',
    lastWorkingDay: '2026-07-31',
    reasonName: 'ContractEnd',
    statusName: 'InProgress',
    progressPercent: 0,
    clearanceSummary: {
      fullyCleared: false,
      totalDepartments: 1,
      clearedDepartments: 0,
      pendingDepartments: 1,
    },
    departments: [
      {
        clearanceCategoryName: 'IT',
        status: 'pending',
        tasks: [
          {
            id: 't1',
            title: 'Return laptop',
            responsibleRoleName: 'IT',
            dueDate: '2026-07-30',
            statusName: 'Pending',
            isMandatory: true,
            clearanceStatus: null,
            linkedAssetId: 'a-1',
          },
          {
            id: 't2',
            title: 'Optional survey',
            responsibleRoleName: 'HR',
            dueDate: '2026-07-25',
            statusName: 'Pending',
            isMandatory: false,
            clearanceStatus: null,
            linkedAssetId: null,
          },
        ],
      },
    ],
    pendingMandatoryItems: [
      {
        taskId: 't1',
        title: 'Return laptop',
        clearanceCategoryName: 'IT',
        reason: 'not_completed',
      },
    ],
    canComplete: false,
    ...over,
  };
}

const view = (over: Partial<InstanceWire> = {}): IOffboardingInstance =>
  mapOffboardingInstance(wire(over));

describe('offboarding.models helpers', () => {
  const FIXED = new Date(2026, 5, 17); // 2026-06-17 local

  it('todayIso formats local date as yyyy-MM-dd', () => {
    expect(todayIso(FIXED)).toBe('2026-06-17');
  });

  it('isPastDate flags dates strictly before today only', () => {
    expect(isPastDate('2026-06-16', FIXED)).toBeTrue();
    expect(isPastDate('2026-06-17', FIXED)).toBeFalse();
    expect(isPastDate('2026-06-18', FIXED)).toBeFalse();
    expect(isPastDate('', FIXED)).toBeFalse();
  });

  // ── the two vocabularies ───────────────────────────────────────────────────

  /**
   * THE ARM B4 EXISTS FOR. `'cleared'` is a DEPARTMENT token; a task never carries it. The production
   * gate used to compare a task's clearance against exactly this value, which is why it matched nothing
   * and every mandatory task blocked completion forever.
   *
   * Narrowing (not casting) is what makes that unrepresentable: an alien token becomes `null`
   * (undecided), so it can never masquerade as a verdict.
   */
  it('does not accept a department token as a task verdict', () => {
    expect(toTaskClearanceStatus('cleared'))
      .withContext("'cleared' is department vocabulary — a task is 'approved' or 'pending_issues'")
      .toBeNull();
    expect(toTaskClearanceStatus('issues')).toBeNull();
    expect(toTaskClearanceStatus(null)).toBeNull();
    expect(toTaskClearanceStatus('approved')).toBe('approved');
    expect(toTaskClearanceStatus('pending_issues')).toBe('pending_issues');
  });

  it('does not accept a task verdict as a department traffic light', () => {
    expect(toDepartmentClearanceStatus('approved'))
      .withContext("a task's 'approved' must not light a department green")
      .toBe('pending');
    expect(toDepartmentClearanceStatus('cleared')).toBe('cleared');
    expect(toDepartmentClearanceStatus('issues')).toBe('issues');
    expect(toDepartmentClearanceStatus(undefined)).toBe('pending');
  });

  it('maps DEPARTMENT clearance status to chip / light / label tokens', () => {
    expect(clearanceChipClass('cleared')).toBe('chip-cleared');
    expect(clearanceChipClass('issues')).toBe('chip-issues');
    expect(clearanceChipClass('pending')).toBe('chip-pending');
    expect(trafficLightClass('cleared')).toBe('light-green');
    expect(trafficLightClass('issues')).toBe('light-yellow');
    expect(trafficLightClass('pending')).toBe('light-red');
    expect(clearanceLabel('issues')).toBe('Issues');
  });

  it('labels a TASK verdict in its own vocabulary, including undecided', () => {
    expect(taskClearanceLabel('approved')).toBe('Approved');
    expect(taskClearanceLabel('pending_issues')).toBe('Issues');
    expect(taskClearanceLabel(null))
      .withContext('an undecided clearance is not the same thing as a refused one')
      .toBe('Awaiting clearance');
    expect(taskClearanceChipClass('approved')).toBe('chip-cleared');
    expect(taskClearanceChipClass(null)).toBe('chip-pending');
  });

  // ── the mapper ─────────────────────────────────────────────────────────────

  it('maps the wire payload onto the view-model', () => {
    const v = view();
    expect(v.status).toBe('InProgress');
    expect(v.reason).toBe('ContractEnd');
    expect(v.departments[0].department)
      .withContext('the wire names it clearanceCategoryName, not department')
      .toBe('IT');
    expect(v.departments[0].tasks[0].status).toBe('Pending');
    expect(v.departments[0].tasks[0].clearanceStatus).toBeNull();
    expect(v.pendingMandatory).toEqual([
      { taskId: 't1', title: 'Return laptop', department: 'IT', reason: 'not_completed' },
    ]);
  });

  /**
   * The clearance vocabularies were narrowed; these four fields were left as blind `as` casts in the first
   * pass, which is the same defect one layer over. An unknown token must not enter the view-model wearing a
   * type it does not have.
   */
  it('narrows the instance status, defaulting UNKNOWN to the actionable state', () => {
    expect(toOffboardingStatus('Completed')).toBe('Completed');
    expect(toOffboardingStatus('InProgress')).toBe('InProgress');
    expect(toOffboardingStatus('Cancelled'))
      .withContext(
        'Completed gates BR-6 and flips the button label — inferring "finished" from an unknown token is ' +
          'the dangerous direction, so an unrecognised status stays actionable',
      )
      .toBe('InProgress');
    expect(toOffboardingStatus(undefined)).toBe('InProgress');
  });

  it('narrows the leaving reason to null rather than guessing one', () => {
    expect(toOffboardingReason('ContractEnd')).toBe('ContractEnd');
    expect(toOffboardingReason('Redundancy'))
      .withContext('showing the wrong reason on a termination record is worse than showing none')
      .toBeNull();
    expect(toOffboardingReason(null)).toBeNull();
  });

  it('narrows the blocking reason to null rather than echoing an unknown token at the user', () => {
    expect(toPendingBlockReason('clearance_not_approved')).toBe('clearance_not_approved');
    expect(toPendingBlockReason('not_completed')).toBe('not_completed');
    expect(toPendingBlockReason('something_new')).toBeNull();
  });

  it('narrows the task status and keeps Skipped distinct from Completed', () => {
    expect(toTaskStatus('Skipped'))
      .withContext('"skipped" reads like a resolution but is NOT a completion — the gate still blocks on it')
      .toBe('Skipped');
    expect(toTaskStatus('Completed')).toBe('Completed');
    expect(toTaskStatus('nonsense')).toBe('Pending');
  });

  it('derives the overall traffic light from the clearance summary', () => {
    expect(view().overallClearance).toBe('pending');
    expect(
      view({
        clearanceSummary: {
          fullyCleared: true,
          totalDepartments: 1,
          clearedDepartments: 1,
          pendingDepartments: 0,
        },
      }).overallClearance,
    ).toBe('cleared');
  });

  /**
   * The `issues` branch had no arm, so deleting it survived: a department that flagged problems showed the
   * same neutral "pending" light as one nobody had looked at yet — the two situations FR-4's traffic light
   * exists to tell apart.
   */
  it('shows issues when any department flagged problems', () => {
    const wired = wire();
    wired.departments![0].status = 'issues';

    expect(mapOffboardingInstance(wired).overallClearance).toBe('issues');
  });

  // ── the completion gate is READ, not re-derived ────────────────────────────

  it('reports the blocking titles the server sent', () => {
    expect(pendingMandatoryTitles(view())).toEqual(['Return laptop']);
    expect(pendingMandatoryTitles(null)).toEqual([]);
  });

  /**
   * THE ARM THAT PINS THE FIX. The tasks here are deliberately left looking unfinished — mandatory,
   * `Pending`, no clearance decision — while the SERVER says completion is allowed and sends no blocking
   * items. Any client that still predicts the rule locally reports `false` and fails this arm.
   *
   * Its mirror below is what stops the helper being replaced with `return true`.
   */
  it('trusts the server when the tasks would suggest otherwise', () => {
    const v = view({ pendingMandatoryItems: [], canComplete: true });
    expect(v.departments[0].tasks[0].isMandatory).toBeTrue();
    expect(v.departments[0].tasks[0].status)
      .withContext('the fixture must genuinely look unfinished, or this arm proves nothing')
      .toBe('Pending');
    expect(canComplete(v)).toBeTrue();
  });

  it('refuses when the server says it is blocked, or already completed', () => {
    expect(canComplete(view())).toBeFalse();
    expect(
      canComplete(view({ statusName: 'Completed', pendingMandatoryItems: [], canComplete: false })),
    ).toBeFalse();
    expect(canComplete(null)).toBeFalse();
  });

  // ── the reason token vs its label ──────────────────────────────────────────

  /**
   * The dropdown used to render AND post `'Contract End'`. The API parses reasons with `Enum.TryParse`
   * after stripping underscores — not spaces — so that option always came back 400 `invalid_reason`.
   * The token must stay space-free; the label is where the space belongs.
   */
  it('keeps every reason token parseable by the API', () => {
    for (const token of OFFBOARDING_REASONS) {
      expect(token)
        .withContext(`"${token}" must contain no whitespace — Enum.TryParse rejects it`)
        .not.toMatch(/\s/);
    }
    expect(OFFBOARDING_REASONS).toContain('ContractEnd');
    expect(OFFBOARDING_REASON_LABEL.ContractEnd).toBe('Contract end');
  });

  it('assetReturnLines extracts tasks carrying a linkedAssetId', () => {
    const lines = assetReturnLines(view());
    expect(lines.length).toBe(1);
    expect(lines[0]).toEqual(
      jasmine.objectContaining({ taskId: 't1', assetId: 'a-1', status: 'Pending' }),
    );
    expect(assetReturnLines(null)).toEqual([]);
  });
});
