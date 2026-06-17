import {
  IOffboardingInstance,
  isPastDate,
  todayIso,
  clearanceChipClass,
  trafficLightClass,
  clearanceLabel,
  pendingMandatoryTitles,
  canComplete,
  assetReturnLines,
} from './offboarding.models';

describe('offboarding.models helpers', () => {
  const FIXED = new Date(2026, 5, 17); // 2026-06-17 local

  const inst = (over: Partial<IOffboardingInstance> = {}): IOffboardingInstance => ({
    offboardingId: 'off-1',
    employeeId: 'emp-1',
    lastWorkingDay: '2026-07-31',
    reason: 'Resignation',
    status: 'in_progress',
    overallClearance: 'pending',
    progressPercent: 0,
    departments: [
      {
        department: 'IT',
        clearanceStatus: 'pending',
        tasks: [
          {
            taskId: 't1',
            title: 'Return laptop',
            responsibleRole: 'IT',
            dueDate: '2026-07-30',
            status: 'pending',
            isMandatory: true,
            clearanceStatus: 'pending',
            linkedAssetId: 'a-1',
          },
          {
            taskId: 't2',
            title: 'Optional survey',
            responsibleRole: 'HR',
            dueDate: '2026-07-25',
            status: 'pending',
            isMandatory: false,
            clearanceStatus: 'pending',
            linkedAssetId: null,
          },
        ],
      },
    ],
    ...over,
  });

  it('todayIso formats local date as yyyy-MM-dd', () => {
    expect(todayIso(FIXED)).toBe('2026-06-17');
  });

  it('isPastDate flags dates strictly before today only', () => {
    expect(isPastDate('2026-06-16', FIXED)).toBeTrue();
    expect(isPastDate('2026-06-17', FIXED)).toBeFalse();
    expect(isPastDate('2026-06-18', FIXED)).toBeFalse();
    expect(isPastDate('', FIXED)).toBeFalse();
  });

  it('maps clearance status to chip / light / label tokens', () => {
    expect(clearanceChipClass('cleared')).toBe('chip-cleared');
    expect(clearanceChipClass('issues')).toBe('chip-issues');
    expect(clearanceChipClass('pending')).toBe('chip-pending');
    expect(trafficLightClass('cleared')).toBe('light-green');
    expect(trafficLightClass('issues')).toBe('light-yellow');
    expect(trafficLightClass('pending')).toBe('light-red');
    expect(clearanceLabel('issues')).toBe('Issues');
  });

  it('pendingMandatoryTitles lists only uncleared mandatory tasks', () => {
    expect(pendingMandatoryTitles(inst())).toEqual(['Return laptop']);
    expect(pendingMandatoryTitles(null)).toEqual([]);
  });

  it('canComplete is false while a mandatory task is pending and when completed', () => {
    expect(canComplete(inst())).toBeFalse();
    const allCleared = inst({
      departments: [
        {
          department: 'IT',
          clearanceStatus: 'cleared',
          tasks: [
            {
              taskId: 't1',
              title: 'Return laptop',
              responsibleRole: 'IT',
              dueDate: '2026-07-30',
              status: 'completed',
              isMandatory: true,
              clearanceStatus: 'cleared',
              linkedAssetId: 'a-1',
            },
          ],
        },
      ],
    });
    expect(canComplete(allCleared)).toBeTrue();
    expect(canComplete(inst({ ...allCleared, status: 'completed' }))).toBeFalse();
    expect(canComplete(null)).toBeFalse();
  });

  it('assetReturnLines extracts tasks carrying a linkedAssetId', () => {
    const lines = assetReturnLines(inst());
    expect(lines.length).toBe(1);
    expect(lines[0]).toEqual(
      jasmine.objectContaining({ taskId: 't1', assetId: 'a-1', status: 'pending' }),
    );
    expect(assetReturnLines(null)).toEqual([]);
  });
});
