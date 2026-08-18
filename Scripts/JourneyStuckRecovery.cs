using UnityEngine;

/// <summary>
/// Recovery strategy for a Journey that's stuck (no progress toward its
/// target distance for too long) - see JourneyCalculator's per-journey
/// stuck detection, which calls this when a retry is needed.
///
/// Deliberately a single plain class, not an interface with one
/// implementation - "swappable" here just means this file gets replaced
/// and JourneyCalculator changes one instantiation line. No abstraction
/// layer is justified until there's a second real implementation to
/// abstract over.
///
/// Strategy: persistent-handedness angular stepping. The first time a
/// given journey gets stuck, a rotation direction (clockwise or
/// counter-clockwise, chosen randomly) is committed to for that stuck
/// episode. Each subsequent retry within the same episode steps further
/// in that SAME committed direction, rather than re-rolling a fresh random
/// angle each time - so a single episode naturally escalates (one step
/// might clear a narrow obstruction; several steps walks most of the way
/// around before that approach angle is effectively abandoned). An episode
/// ends (state cleared) whenever the journey resolves or is re-triggered
/// by a fresh collision - see JourneyCalculator's collision handling.
///
/// Explicitly deferred, not implemented here (see project history):
/// decreasing-radius stepping, true wall-following, and any "give up
/// entirely" fallback for genuinely unescapable geometry.
/// </summary>
public class JourneyStuckRecovery
{
    private class RecoveryEpisode
    {
        public float BaseAngleDegrees;
        public int Handedness; // +1 or -1, committed once per episode
        public int StepsTaken;
    }

    /// <summary>
    /// Call when a journey has just been detected as stuck. Returns the
    /// next world-space point to steer toward - on the same circle of
    /// radius destinationDistance around entityPos as the journey's
    /// original target, just at a different angle. Persists and advances
    /// state in journey.RecoveryState across repeated calls for the same
    /// journey, within the same stuck episode.
    /// </summary>
    public Vector3 GetNextRecoveryTarget(ActiveJourney journey, Vector3 currentDirectionAway, Vector3 entityPos, float destinationDistance, float stepDegrees)
    {
        RecoveryEpisode episode = journey.RecoveryState as RecoveryEpisode;

        if (episode == null)
        {
            episode = new RecoveryEpisode
            {
                BaseAngleDegrees = Mathf.Atan2(currentDirectionAway.z, currentDirectionAway.x) * Mathf.Rad2Deg,
                Handedness = Random.value < 0.5f ? 1 : -1,
                StepsTaken = 0
            };
            journey.RecoveryState = episode;
        }

        episode.StepsTaken++;
        float newAngleDegrees = episode.BaseAngleDegrees + episode.Handedness * stepDegrees * episode.StepsTaken;
        float newAngleRadians = newAngleDegrees * Mathf.Deg2Rad;

        Vector3 direction = new Vector3(Mathf.Cos(newAngleRadians), 0f, Mathf.Sin(newAngleRadians));
        return entityPos + direction * destinationDistance;
    }
}
