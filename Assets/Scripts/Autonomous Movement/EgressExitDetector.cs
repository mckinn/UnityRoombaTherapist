using System;
using UnityEngine;

/// <summary>
/// Detects the Roomba successfully passing through the egress doorway -
/// see Egress_Door_Implementation_Plan.md section 3, item 7. Belongs on a
/// trigger volume positioned on the far/exterior side of the doorway (in
/// the hallway beyond WallDoor2m), physically reachable only by passing
/// through the doorway given the room's walls are otherwise sealed - so a
/// plain OnTriggerEnter here, with no sequencing against the door's own
/// trigger, is a valid "has exited" signal.
///
/// This story only detects the exit and exposes OnExitReached for the next
/// story (ROOMBA-64, "stopping the game") to subscribe to - it deliberately
/// does not itself stop the timer/score or freeze the Roomba. Fires at most
/// once (exitReported guard), since whatever ROOMBA-64 eventually hooks in
/// here will very likely have real side effects (stopping the clock,
/// sending a synthetic arena event, etc.) that should not repeat if the
/// Roomba lingers in or re-enters this volume.
/// </summary>
public class EgressExitDetector : MonoBehaviour
{
    /// <summary>
    /// Fired exactly once, the first time the Player reaches this trigger.
    /// No subscriber exists yet in this story - ROOMBA-64 will add one.
    /// </summary>
    public event Action OnExitReached;

    private bool exitReported;

    private void OnTriggerEnter(Collider other)
    {
        // Debug.Log($"EgressExitDetector: Collider Trigger Fired {exitReported}, {other.tag}, {other.name}");

        if (exitReported || !other.CompareTag("Player"))
        {
            return;
        }

        exitReported = true;
        Debug.Log("EgressExitDetector: Roomba has exited through the egress door.");
        OnExitReached?.Invoke();
    }
}
