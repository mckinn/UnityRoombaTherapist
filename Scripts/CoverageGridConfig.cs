using UnityEngine;

/// <summary>
/// Tunable configuration for the arena coverage grid - cell size, and the
/// heuristic that decides when a cell counts as "visited". Follows the same
/// pattern as EmotionMovementConfig: a ScriptableObject asset, so these
/// values are Inspector-tunable (including while the game is running)
/// without a code change.
///
/// The actual asset instance lives in the Configurators/ folder alongside
/// the project's other config assets - create it via
/// Assets > Create > RoombaTherapist > Coverage Grid Config once this
/// script has compiled, matching how EmotionMovementConfig.asset was set up.
///
/// See Planning.md, "Coverage Algorithm", for the reasoning behind the
/// default cellSize (matched to the Roomba's own footprint) and for why
/// VisitedHeuristic exists as a config choice rather than a single
/// hardcoded approach.
/// </summary>
[CreateAssetMenu(fileName = "CoverageGridConfig", menuName = "RoombaTherapist/Coverage Grid Config")]
public class CoverageGridConfig : ScriptableObject
{
    public enum VisitedHeuristic
    {
        // A cell is marked Visited only when the Roomba's center enters it.
        // Default for v1 - see cellSize below for why this alone should
        // give near-total physical coverage without needing the option
        // below.
        CenterContact,

        // A cell is marked Visited if any part of the Roomba's footprint
        // overlaps it. Not yet implemented by ArenaCoverageController -
        // reserved for a later pass if CenterContact leaves visible gaps
        // during playtesting.
        FootprintContact
    }

    [Header("Grid Geometry")]
    [Tooltip("Side length of one grid cell, in world units. Default (0.6239) matches the Roomba prefab's own footprint width (mesh bounds X and Z, as measured from the Inspector), so CenterContact heuristic alone gives near-total physical coverage without needing FootprintContact. Re-verify this value against the Roomba prefab's actual mesh bounds if that prefab ever changes.")]
    [SerializeField] private float cellSize = 0.6239f;

    [Header("Visited Heuristic")]
    [Tooltip("How a cell earns Visited status. Start with CenterContact - simplest, and sized correctly (see cellSize above) to give full coverage without it. Switch to FootprintContact only if playtesting shows real gaps.")]
    [SerializeField] private VisitedHeuristic visitedHeuristic = VisitedHeuristic.CenterContact;

    [Tooltip("How close (world units) the Roomba's center must be to a cell's center to count as having arrived there, for the purpose of requesting the next step. Should be meaningfully smaller than cellSize, or the Roomba could 'arrive' at a cell without having been meaningfully inside it.")]
    [SerializeField] private float arrivalTolerance = 0.1f;

    public float CellSize => cellSize;
    public VisitedHeuristic Heuristic => visitedHeuristic;
    public float ArrivalTolerance => arrivalTolerance;
}
