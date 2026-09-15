using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// UI Canvas widget: a per-entity_type table of the Roomba's current
/// emotional state - one row per entity_type, sorted by intensity
/// (descending, most significant first), colored via EmotionColorConfig so
/// it stays visually consistent with EmotionIntensityRing. Deliberately
/// runs ALONGSIDE the ring, not as its replacement (Steve's call,
/// 2026-09-15) - organized by entity_type rather than by emotion, so it
/// answers a different question ("what does the Roomba feel about THIS
/// thing") than the ring does ("how strongly does the Roomba feel THIS
/// emotion, about whatever's currently triggering it most").
///
/// Filtering: entity_types with emotion == "none" (or empty) are skipped
/// entirely (Steve's call) rather than shown as a placeholder row. "none"
/// isn't actually produced by the Orchestrator today -
/// merge_entity_sensitivities's seed_new_at_zero flag, which would create
/// it, is currently off (see session_state.py; a JIRA backlog item covers
/// turning that back on) - so in practice nothing is filtered out yet. The
/// filter is here now so this widget needs no further changes whenever
/// that Orchestrator-side work happens.
///
/// Rebuild-on-update, not diffed/pooled: every OnStateUpdated, all row
/// children are destroyed and rebuilt from the current filtered/sorted
/// list. Simplest correct approach for what's realistically a handful of
/// entity_types in this game - revisit only if this ever shows up as an
/// actual performance problem, which is unlikely at this scale.
/// </summary>
public class EmotionProfileTable : MonoBehaviour
{
    [Tooltip("Row template - must have an EmotionProfileRow component. Instantiated once per visible entity_type, parented under rowContainer.")]
    [SerializeField] private EmotionProfileRow rowPrefab;

    [Tooltip("Parent all rows are instantiated under - give it a Vertical Layout Group (+ Content Size Fitter if the row count should resize the panel) so rows stack automatically without this script doing any layout math.")]
    [SerializeField] private RectTransform rowContainer;

    [Tooltip("Same color asset EmotionIntensityRing uses - keeps this table and the ring visually consistent by construction, rather than duplicating a second palette.")]
    [SerializeField] private EmotionColorConfig colorConfig;

    private readonly List<EmotionProfileRow> activeRows = new List<EmotionProfileRow>();

    private void OnEnable()
    {
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.OnStateUpdated += HandleStateUpdated;
            HandleStateUpdated(); // pick up whatever state already exists, don't wait for the next change
        }
    }

    private void OnDisable()
    {
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.OnStateUpdated -= HandleStateUpdated;
        }
    }

    private void HandleStateUpdated()
    {
        List<EntitySensitivity> sensitivities = SessionManager.Instance != null ? SessionManager.Instance.EntitySensitivities : null;
        Rebuild(sensitivities);
    }

    private void Rebuild(List<EntitySensitivity> sensitivities)
    {
        ClearRows();

        if (sensitivities == null || rowPrefab == null || rowContainer == null)
        {
            return;
        }

        List<EntitySensitivity> visible = sensitivities
            .Where(s => !string.IsNullOrEmpty(s.emotion) && s.emotion != "none")
            .OrderByDescending(s => s.strength)
            .ToList();

        foreach (EntitySensitivity sensitivity in visible)
        {
            EmotionProfileRow row = Instantiate(rowPrefab, rowContainer);
            Color color = colorConfig != null ? colorConfig.GetColor(sensitivity.emotion) : Color.white;
            row.Set(sensitivity.entity_type, sensitivity.emotion, sensitivity.strength, color);
            activeRows.Add(row);
        }
    }

    private void ClearRows()
    {
        foreach (EmotionProfileRow row in activeRows)
        {
            if (row != null)
            {
                Destroy(row.gameObject);
            }
        }
        activeRows.Clear();
    }
}
