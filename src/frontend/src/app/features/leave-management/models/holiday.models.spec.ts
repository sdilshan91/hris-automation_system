import {
  IHoliday,
  getHolidayTypeColor,
  getHolidayTypeBadgeClasses,
  getHolidayTypeLabel,
  toIsoDate,
  yearOf,
  buildMonthGrid,
  groupByMonth,
  parseHolidayCsv,
  splitCsvLine,
  HOLIDAY_TYPE_OPTIONS,
} from './holiday.models';

function makeHoliday(overrides: Partial<IHoliday> = {}): IHoliday {
  return {
    id: 'h-1',
    name: 'New Year',
    date: '2026-01-01',
    type: 'public',
    locationId: null,
    locationName: null,
    description: null,
    isRecurring: true,
    isActive: true,
    ...overrides,
  };
}

describe('holiday.models — type helpers', () => {
  it('maps each type to its documented color (§8)', () => {
    expect(getHolidayTypeColor('public')).toBe('#2563eb'); // blue
    expect(getHolidayTypeColor('restricted')).toBe('#ea580c'); // orange
    expect(getHolidayTypeColor('optional')).toBe('#16a34a'); // green
  });

  it('falls back to public color for an unknown type', () => {
    expect(getHolidayTypeColor('weird')).toBe('#2563eb');
  });

  it('maps each type to a distinct badge class set', () => {
    expect(getHolidayTypeBadgeClasses('public')).toContain('blue');
    expect(getHolidayTypeBadgeClasses('restricted')).toContain('orange');
    expect(getHolidayTypeBadgeClasses('optional')).toContain('green');
  });

  it('maps each type to a human label', () => {
    expect(getHolidayTypeLabel('public')).toBe('Public');
    expect(getHolidayTypeLabel('restricted')).toBe('Restricted');
    expect(getHolidayTypeLabel('optional')).toBe('Optional');
    expect(getHolidayTypeLabel('xyz')).toBe('xyz');
  });
});

describe('holiday.models — date helpers', () => {
  it('toIsoDate pads month and day', () => {
    expect(toIsoDate(2026, 0, 1)).toBe('2026-01-01');
    expect(toIsoDate(2026, 11, 25)).toBe('2026-12-25');
  });

  it('yearOf extracts the year', () => {
    expect(yearOf('2026-07-04')).toBe(2026);
    expect(yearOf('1999-12-31')).toBe(1999);
  });
});

describe('buildMonthGrid', () => {
  it('returns a fixed 42-cell grid (6 rows x 7 days)', () => {
    const grid = buildMonthGrid(2026, 0, []);
    expect(grid.length).toBe(42);
  });

  it('places leading padding cells before the first weekday', () => {
    // Jan 1 2026 is a Thursday (getDay() === 4), so 4 leading pad cells.
    const grid = buildMonthGrid(2026, 0, []);
    expect(grid[0].inMonth).toBeFalse();
    expect(grid[3].inMonth).toBeFalse();
    expect(grid[4].inMonth).toBeTrue();
    expect(grid[4].day).toBe(1);
    expect(grid[4].date).toBe('2026-01-01');
  });

  it('buckets a holiday onto its exact date cell', () => {
    const h = makeHoliday({ date: '2026-01-01' });
    const grid = buildMonthGrid(2026, 0, [h]);
    const cell = grid.find((c) => c.date === '2026-01-01');
    expect(cell!.holidays.length).toBe(1);
    expect(cell!.holidays[0].id).toBe('h-1');
  });

  it('does not place a holiday from another month', () => {
    const h = makeHoliday({ date: '2026-02-14' });
    const grid = buildMonthGrid(2026, 0, [h]);
    const withHoliday = grid.filter((c) => c.holidays.length > 0);
    expect(withHoliday.length).toBe(0);
  });
});

describe('groupByMonth', () => {
  it('buckets holidays into 12 month arrays', () => {
    const buckets = groupByMonth([
      makeHoliday({ id: 'a', date: '2026-01-01' }),
      makeHoliday({ id: 'b', date: '2026-01-15' }),
      makeHoliday({ id: 'c', date: '2026-12-25' }),
    ]);
    expect(buckets.length).toBe(12);
    expect(buckets[0].length).toBe(2);
    expect(buckets[11].length).toBe(1);
    expect(buckets[5].length).toBe(0);
  });

  it('sorts each month bucket by date ascending', () => {
    const buckets = groupByMonth([
      makeHoliday({ id: 'b', date: '2026-03-20' }),
      makeHoliday({ id: 'a', date: '2026-03-05' }),
    ]);
    expect(buckets[2].map((h) => h.id)).toEqual(['a', 'b']);
  });
});

describe('splitCsvLine', () => {
  it('splits plain comma-separated values', () => {
    expect(splitCsvLine('New Year,2026-01-01,public')).toEqual([
      'New Year',
      '2026-01-01',
      'public',
    ]);
  });

  it('respects quoted fields with embedded commas', () => {
    expect(splitCsvLine('"Day, of Rest",2026-05-01,public')).toEqual([
      'Day, of Rest',
      '2026-05-01',
      'public',
    ]);
  });

  it('handles escaped quotes inside a quoted field', () => {
    expect(splitCsvLine('"He said ""hi""",2026-01-01,public')[0]).toBe(
      'He said "hi"'
    );
  });
});

describe('parseHolidayCsv (AC-3)', () => {
  it('parses valid rows and counts them as ready', () => {
    const csv = [
      'name,date,type',
      'New Year,2026-01-01,public',
      'Diwali,2026-11-12,restricted',
    ].join('\n');
    const preview = parseHolidayCsv(csv, []);
    expect(preview.rows.length).toBe(2);
    expect(preview.validCount).toBe(2);
    expect(preview.invalidCount).toBe(0);
    expect(preview.duplicateCount).toBe(0);
  });

  it('skips the header row when present', () => {
    const csv = 'name,date,type\nNew Year,2026-01-01,public';
    const preview = parseHolidayCsv(csv, []);
    expect(preview.rows.length).toBe(1);
    expect(preview.rows[0].name).toBe('New Year');
    expect(preview.rows[0].rowNumber).toBe(1);
  });

  it('parses files without a header row', () => {
    const csv = 'New Year,2026-01-01,public';
    const preview = parseHolidayCsv(csv, []);
    expect(preview.rows.length).toBe(1);
    expect(preview.validCount).toBe(1);
  });

  it('flags an invalid date', () => {
    const csv = 'name,date,type\nBad,01/01/2026,public';
    const preview = parseHolidayCsv(csv, []);
    expect(preview.invalidCount).toBe(1);
    expect(preview.rows[0].errors.some((e) => e.includes('YYYY-MM-DD'))).toBeTrue();
  });

  it('flags an invalid type', () => {
    const csv = 'name,date,type\nBad,2026-01-01,bankholiday';
    const preview = parseHolidayCsv(csv, []);
    expect(preview.invalidCount).toBe(1);
    expect(preview.rows[0].errors.some((e) => e.includes('Type'))).toBeTrue();
  });

  it('flags a missing name', () => {
    const csv = 'name,date,type\n,2026-01-01,public';
    const preview = parseHolidayCsv(csv, []);
    expect(preview.invalidCount).toBe(1);
    expect(preview.rows[0].errors).toContain('Name is required.');
  });

  it('flags duplicate (date,type) rows within the file (BR-1)', () => {
    const csv = [
      'name,date,type',
      'New Year,2026-01-01,public',
      'NYD,2026-01-01,public',
    ].join('\n');
    const preview = parseHolidayCsv(csv, []);
    expect(preview.duplicateCount).toBe(1);
    expect(preview.rows[1].isDuplicate).toBeTrue();
    expect(preview.validCount).toBe(1);
  });

  it('flags rows duplicating an existing holiday (BR-1)', () => {
    const existing = [makeHoliday({ date: '2026-01-01', type: 'public' })];
    const csv = 'name,date,type\nNew Year,2026-01-01,public';
    const preview = parseHolidayCsv(csv, existing);
    expect(preview.duplicateCount).toBe(1);
    expect(preview.rows[0].isDuplicate).toBeTrue();
  });

  it('does not flag the same date with a different type as a duplicate', () => {
    const existing = [makeHoliday({ date: '2026-01-01', type: 'public' })];
    const csv = 'name,date,type\nOptional Day,2026-01-01,optional';
    const preview = parseHolidayCsv(csv, existing);
    expect(preview.duplicateCount).toBe(0);
    expect(preview.validCount).toBe(1);
  });

  it('returns an empty preview for empty input', () => {
    const preview = parseHolidayCsv('', []);
    expect(preview.rows.length).toBe(0);
    expect(preview.validCount).toBe(0);
  });

  it('exposes three holiday type options', () => {
    expect(HOLIDAY_TYPE_OPTIONS.length).toBe(3);
  });
});
