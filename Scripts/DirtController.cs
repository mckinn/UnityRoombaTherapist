using UnityEngine;

/// <summary>
/// Trigger-based dirt patch: the Roomba drives over dirt and it's
/// removed, rather than the Roomba colliding with/bouncing off it. This is
/// deliberately IsTrigger, not a solid collider - see Planning.md,
/// "Resolved: dirt never produces a Journey", for why that specific choice
/// (not any special-casing in JourneyCalculator or CollisionController) is
/// what guarantees dirt contact never interrupts Clean/Map movement or
/// spawns a Journey, regardless of whatever emotion ends up associated
/// with entity_type "dirt".
///
/// Deliberately does NOT yet report to the Orchestrator/emotion pipeline -
/// that's step 4 (batched dirt-cleanup reporting), a distinct, later piece
/// of work. This class's only job right now is detection and removal.
///
/// Deliberately does NOT yet register itself into any coverage-grid
/// occupant list either, even though Planning.md's "Generalized Cell
/// Occupant Tracking" section names dirt as the first intended consumer of
/// that mechanism. CoverageGrid does not yet have an occupant list built
/// (only CellState exists so far) - wiring this in now would mean
/// building that extension as a side effect of dirt work rather than as
/// its own deliberate step. Flagged here for visibility, not silently
/// deferred or silently included - worth a conscious decision whenever
/// that extension is actually built.
/// </summary>
[RequireComponent(typeof(DirtVisual), typeof(BoxCollider))]
public class DirtController : MonoBehaviour
{
    [Tooltip("Tag expected on the Roomba GameObject. Assumed 'Player', matching the existing Collectible.cs convention - verify this is actually the Roomba's tag before relying on it.")]
    [SerializeField] private string roombaTag = "Player";

    private DirtVisual dirtVisual;
    private BoxCollider triggerCollider;

    private void Awake()
    {
        dirtVisual = GetComponent<DirtVisual>();
        triggerCollider = GetComponent<BoxCollider>();

        dirtVisual.Generate();
        SizeCollider();

        triggerCollider.isTrigger = true;
    }

    private void SizeCollider()
    {
        float footprint = dirtVisual.ColliderRadius;
        // A thin box just above/around the floor - doesn't need to match
        // the visual's irregular silhouette exactly, only needs to
        // reliably cover it.
        triggerCollider.size = new Vector3(footprint * 2f, 0.1f, footprint * 2f);
        triggerCollider.center = new Vector3(0f, 0.05f, 0f);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(roombaTag))
        {
            return;
        }

        // TODO (step 4): report this removal into the batched dirt-cleanup
        // reporting system once it exists, rather than just disappearing
        // silently. See Planning.md, "Dirt Cleanup and Reporting".
        Destroy(gameObject);
    }
}