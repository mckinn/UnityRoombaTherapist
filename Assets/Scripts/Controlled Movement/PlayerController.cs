using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Free planar (X/Z) movement. Move() takes a world-space direction directly -
/// no turn-then-thrust coupling - so player input and the Journey system can
/// drive the same Rigidbody through the same primitive.
///
/// This is a pure motor: it does not read input and does not decide when to
/// move. JourneyCalculator is the single authority that decides, each frame,
/// whether to call Move() with an autonomous Journey direction or a manual
/// fallback direction - never both, and never through two independent magic
/// methods competing for the same frame.
///
/// Orientation is a side effect of movement, not a control input: whenever the
/// Roomba moves, it snaps instantly to face the direction it's moving in.
///
/// Jump() is a separate, decoupled vertical impulse. It's callable by player
/// input (spacebar) or by other logic (e.g. a collision-triggered "jump for
/// joy" Behavior) - it has no relationship to planar movement.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Tooltip("Planar movement speed (units/sec).")]
    public float speed = 5.0f;

    [Tooltip("Upward velocity applied by a jump impulse.")]
    public float jumpForce = 5.0f;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) Debug.LogWarning("PlayerController needs a Rigidbody.");
    }

    private void Update()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Jump();
        }
    }

    /// <summary>
    /// Moves directly along a world-space direction (X/Z plane only - the Y
    /// component is ignored; use Jump() for vertical motion). Direction does
    /// not need to be pre-normalized. Snaps facing to match.
    ///
    /// Callable by player input or by the Journey/Behavior system - same
    /// primitive either way, no player-vs-NPC branching inside this method.
    /// </summary>
    public void Move(Vector3 direction, float? speedOverride = null)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }
        direction.Normalize();

        float effectiveSpeed = (speedOverride ?? 1f) * speed;
        Vector3 movement = direction * effectiveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);

        rb.MoveRotation(Quaternion.LookRotation(direction, Vector3.up));
    }

    /// <summary>
    /// Applies a vertical impulse. Callable by player input (spacebar) or by
    /// other game logic (e.g. collision/Behavior code triggering a reaction)
    /// with an explicit force; defaults to jumpForce if none is given.
    /// </summary>
    public void Jump(float force = -1f)
    {
        if (force < 0f) force = jumpForce;
        rb.AddForce(Vector3.up * force, ForceMode.VelocityChange);
    }

    /// <summary>
    /// Moves directly to an absolute world position (X/Z only - the Y
    /// component is ignored, current height is preserved; use Jump() for
    /// vertical motion). Unlike Move(), does not change facing - a caller
    /// driving a positional pattern is responsible for orientation if it
    /// wants any.
    ///
    /// Separate primitive from Move() on purpose: Move() is "go this
    /// direction at this speed" (Journey/keyboard); MoveTo() is "be at this
    /// exact place right now". No current caller uses MoveTo() - it was
    /// written for the since-removed BehaviorController pattern renderer
    /// (removed 2026-09-11, Movement_Concurrency_Plan.md section 4 item 4)
    /// - but it's kept as a general-purpose primitive on the single shared
    /// motor rather than deleted, in case a future feature needs the same
    /// "be at this exact place right now" shape.
    /// </summary>
    public void MoveTo(Vector3 worldPosition)
    {
        worldPosition.y = rb.position.y;
        rb.MovePosition(worldPosition);
    }
}