using UnityEngine;

/// <summary>
/// Minimal collision-listener component - see
/// Furniture_Displacement_Implementation_Plan.md section 4, step 1. Added
/// programmatically by FurnitureDisplacementTracker to each eligible
/// candidate at startup; never placed manually in the Editor, and there is
/// no reason to add it to anything by hand. Its only job is forwarding
/// this GameObject's own OnCollisionEnter back to the tracker - that's
/// what makes an indirect hit (Roomba shoves a table into a chair)
/// register correctly. The chair's own collider fires its own
/// OnCollisionEnter the instant anything touches it, regardless of what
/// did the pushing, so nothing in this class (or in
/// FurnitureDisplacementTracker) needs to know or care whether the Roomba
/// was one of the two colliding bodies.
/// </summary>
public class FurnitureCollisionListener : MonoBehaviour
{
    private FurnitureDisplacementTracker owner;

    /// <summary>
    /// Set immediately after AddComponent by FurnitureDisplacementTracker.
    /// Deliberately not a [SerializeField] - this component only ever
    /// exists via runtime AddComponent, never wired in the Editor, so
    /// there is no Inspector slot to leave unassigned by mistake.
    /// </summary>
    public void Initialize(FurnitureDisplacementTracker trackerOwner)
    {
        owner = trackerOwner;
    }

    private void OnCollisionEnter(Collision collision)
    {
        owner?.NotifyCollided(transform);
    }
}