using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Free planar (X/Z) movement. Move() takes a world-space direction directly -
/// no turn-then-thrust coupling - so player input and a future Journey/Behavior
/// system can drive the same Rigidbody through the same primitive.
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

    private void FixedUpdate()
    {
        Vector3 moveInput = Vector3.zero;

        if (Keyboard.current.upArrowKey.isPressed) moveInput.z += 1f;
        if (Keyboard.current.downArrowKey.isPressed) moveInput.z -= 1f;
        if (Keyboard.current.leftArrowKey.isPressed) moveInput.x -= 1f;
        if (Keyboard.current.rightArrowKey.isPressed) moveInput.x += 1f;

        if (moveInput.sqrMagnitude > 0f)
        {
            Move(moveInput);
        }
    }

    /// <summary>
    /// Moves directly along a world-space direction (X/Z plane only - the Y
    /// component is ignored; use Jump() for vertical motion). Direction does
    /// not need to be pre-normalized. Snaps facing to match.
    ///
    /// Callable by player input now, and by the Journey/Behavior system later
    /// once it exists - same primitive either way, no player-vs-NPC branching.
    /// </summary>
    public void Move(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }
        direction.Normalize();

        Vector3 movement = direction * speed * Time.fixedDeltaTime;
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
}