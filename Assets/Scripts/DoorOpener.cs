using UnityEngine;

/// <summary>
/// Opens the egress door once the configured dirt-collection threshold is
/// reached, and simultaneously registers the door as a valid LLM movement
/// destination - see Egress_Door_Implementation_Plan.md. Polls
/// DirtProgressReporter.PercentCollected01 against config.ExitDirtThreshold01
/// once per Update() rather than reacting to a dedicated event (plan doc
/// section 3, item 2 - a plain guarded poll was chosen over a new
/// DirtProgressReporter event since this component already needs a
/// GameScoreConfig reference, matching GameLevelTimer/GameScoreTable's
/// existing pattern). doorUnlocked is set exactly once, the first frame the
/// threshold is met; both the door-open gate below and the one-time
/// SeedLandmark call key off that single flag, not off repeated re-checks.
///
/// The Box Collider and this component both stay enabled at all times -
/// "locked" is purely the doorUnlocked bool, not a component/collider
/// enabled-state toggle. No feedback is given if the Player touches the
/// door before it unlocks - confirmed not needed (plan doc section 3, item
/// 4). This door's trigger collider already guarantees no
/// CollisionController reaction on contact regardless of lock state, since
/// only physical (non-trigger) collisions reach OnCollisionEnter there.
/// </summary>
public class DoorOpener : MonoBehaviour
{
    [Tooltip("Supplies ExitDirtThreshold01 - the dirt-collection fraction (0-1) at which this door unlocks.")]
    [SerializeField] private GameScoreConfig config;

    [Tooltip("This is the GameObject that will both act as the destination for leaving the room, and detect the departure from the room.  Imagine a turnstile at the other side of the door.  It is not the door, because the door has opened and is behind the wall.")]
    [SerializeField] private EntityIdentity entityIdentity;

    [Tooltip("entity_type reported to the Orchestrator when this door is seeded as a landmark - should match this GameObject's tag (door) case-insensitively, the same convention SessionManager's landmarkEntityType uses for the rug.")]
    [SerializeField] private string landmarkEntityType = "exit";

    private Animator doorAnimator;
    private bool doorUnlocked;

    private BoxCollider boxCollider;

    void Start()
    {
        // Get the Animator component attached to the same GameObject as this script
        doorAnimator = GetComponent<Animator>();

        if (config == null)
        {
            Debug.LogWarning("DoorOpener: no GameScoreConfig assigned - this door will never unlock.");
        }

        if (entityIdentity == null)
        {
            Debug.LogWarning("DoorOpener: no EntityIdentity assigned - this door will still unlock and open, but will never be seeded as a movement_directive destination.");
        }
    }

    void Update()
    {
        // Once-only latch: after doorUnlocked flips true there is nothing
        // left to check here every frame.
        if (doorUnlocked || config == null)
        {
            return;
        }

        if (DirtProgressReporter.Instance != null &&
            DirtProgressReporter.Instance.PercentCollected01 >= config.ExitDirtThreshold01)
        {
            doorUnlocked = true;
            Debug.Log($"DoorOpener: dirt threshold reached ({DirtProgressReporter.Instance.PercentCollected01:P0} >= {config.ExitDirtThreshold01:P0}) - door unlocked.");

            if (entityIdentity != null && SessionManager.Instance != null)
            {
                // Fire-and-forget, same convention used elsewhere in this
                // codebase (e.g. DirtProgressReporter.SendProgressReportAsync)
                // for an Awaitable call whose caller doesn't need to block on it.
                _ = SessionManager.Instance.SeedLandmark(entityIdentity, landmarkEntityType);
            }
            // Prior to being able to exit, the boxcollider that is used to trigger the door
            // opening gets in the way of the game play.   When the threshold is crossed one of the
            // things that needs to be done is the enabling of the boxcollider that triggers the
            // opening of the door
            boxCollider = GetComponent<BoxCollider>();
            boxCollider.enabled = true;
            boxCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Check if the object entering the trigger is the player (or another specified object)
        if (doorUnlocked && other.CompareTag("Player")) // Make sure the player GameObject has the tag "Player"
        {
            if (doorAnimator != null)
            {
                // Trigger the Door_Open animation
                doorAnimator.SetTrigger("Door_Open");
            }
        }
    }
}
