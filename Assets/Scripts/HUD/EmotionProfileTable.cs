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
/// Rebuild-on-change, not diffed/pooled: whenever the underlying
/// EntitySensitivities list reference changes, all row children are
/// destroyed and rebuilt from the current filtered/sorted list. Simplest
/// correct approach for what's realistically a handful of entity_types in
/// this game - revisit only if this ever shows up as an actual performance
/// problem, which is unlikely at this scale.
///
/// Polls SessionManager.Instance.EntitySensitivities every Update() rather
/// than subscribing to SessionManager.OnStateUpdated - same reasoning
/// EmotionIntensityRing already uses (see its own doc comment): avoids
/// depending on cross-object Awake/OnEnable ordering. That ordering
/// dependency was a real bug here (found 2026-09-16): this component's
/// OnEnable ran before SessionManager.Awake() had set Instance, so it
/// silently never subscribed at all for the rest of the session - and
/// separately, SessionManager.StartSession()'s initial
/// entity_sensitivities never invokes OnStateUpdated in the first place
/// (only later SendArenaEvent/SendDirtProgress calls do, via UpdateState),
/// so even a correctly-timed subscription would have missed session-start
/// data. Polling sidesteps both problems at once.
///
/// The Update() check itself is cheap: a property read plus a reference
/// comparison against the last-seen list. SessionManager.EntitySensitivities
/// is always reassigned to a brand-new list object rather than mutated in
/// place (see StartSession/UpdateState), so reference inequality is a
/// reliable, near-zero-cost "did the server send something new" signal.
/// The actually expensive part - Rebuild(), which destroys and
/// re-instantiates row GameObjects - only runs on the frame where that
/// reference changes, not on every frame.
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
    private List<EntitySensitivity> lastSeenSensitivities;

    private void Update()
    {
        List<EntitySensitivity> sensitivities = SessionManager.Instance != null ? SessionManager.Instance.EntitySensitivities : null;

        if (ReferenceEquals(sensitivities, lastSeenSensitivities))
        {
            return;
        }

        lastSeenSensitivities = sensitivities;
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
