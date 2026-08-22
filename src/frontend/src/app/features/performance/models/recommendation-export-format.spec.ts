// ============================================================================
// BUG-311 — the export-format union must match the wire, and the mapper must NARROW rather than cast.
//
// The union was declared 'Excel' | 'Pdf' while the API sends ["csv","xlsx"], and a blind
// `as RecommendationExportFormat[]` cast in the mapper hid it. Consequences that were live in
// production:
//   * every branch on 'Excel'/'Pdf' was unreachable — including a download-filename ternary that
//     therefore ALWAYS produced `recommendations.pdf`, whatever the file actually contained;
//   * the component and service specs both mocked 'Excel', so they were green by agreeing with the
//     wrong type instead of with the API.
//
// These arms sit on the MAPPER, because that is where the narrowing happens. An earlier draft put them
// on the component spec — which injects an already-mapped object and therefore bypasses the filter
// entirely. The test passed the wrong layer and said so.
// ============================================================================

import {
  isRecommendationExportFormat,
  mapRecommendationWorkspace,
  RECOMMENDATION_EXPORT_FORMATS,
} from './recommendation.models';

describe('recommendation export formats (BUG-311)', () => {
  it('declares exactly the tokens the API sends', () => {
    // RecommendationService.cs:45 — SupportedExportFormats = ["csv", "xlsx"].
    expect([...RECOMMENDATION_EXPORT_FORMATS]).toEqual(['csv', 'xlsx']);
  });

  it('rejects the tokens the old union claimed', () => {
    expect(isRecommendationExportFormat('Excel'))
      .withContext('the union used to declare this; the wire has never sent it')
      .toBeFalse();
    expect(isRecommendationExportFormat('Pdf')).toBeFalse();
  });

  /**
   * THE ARM THAT MATTERS. The mapper must DROP a token it does not understand, not pass it through.
   * A cast would let a format a newer backend advertises reach the UI and render a button whose
   * label and handler the frontend cannot supply.
   */
  it('drops an unrecognised wire token instead of passing it through', () => {
    const mapped = mapRecommendationWorkspace({
      cycleId: 'c-1',
      availableExportFormats: ['csv', 'parquet', 'xlsx'],
    } as never);

    expect(mapped.availableExportFormats).toEqual(['csv', 'xlsx']);
    expect(mapped.availableExportFormats as string[])
      .withContext('an unrecognised token must not survive mapping')
      .not.toContain('parquet');
  });

  it('tolerates the field being absent or null', () => {
    expect(mapRecommendationWorkspace({ cycleId: 'c-1' } as never).availableExportFormats).toEqual([]);
    expect(
      mapRecommendationWorkspace({ cycleId: 'c-1', availableExportFormats: null } as never)
        .availableExportFormats,
    ).toEqual([]);
  });
});
