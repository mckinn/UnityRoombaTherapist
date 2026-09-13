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
/// Explicitly still deferred (see project history): decreasing-radius
/// stepping and true wall-following. A "give up entirely" fallback for
/// genuinely unescapable geometry - once explicitly deferred here too -
/// was added 2026-09-11: see GetNextRecoveryTarget's null return, and
/// Movement_Concurrency_Plan.md section 4 item 5 (universal
/// stuck-abandonment - the Roomba locking against a wall because the
/// emotion/LLM driving a journey has no way to know it's trapped).
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
    ///
    /// Returns null once a full circle of redirect angles (Ceil(360 /
    /// stepDegrees) steps) has been tried within this episode without the
    /// journey resolving - universal stuck-abandonment,
    /// Movement_Concurrency_Plan.md section 4 item 5: genuinely
    /// unescapable geometry must eventually give up rather than spin
    /// through the same ring forever. The caller
    /// (JourneyCalculator.CheckJourneyStuckAndMaybeRedirect) is
    /// responsible for resolving the journey when this returns null - this
    /// method only decides when to give up, not what giving up means for
    /// the journey.
    /// </summary>
    public Vector3? GetNextRecoveryTarget(ActiveJourney journey, Vector3 currentDirectionAway, Vector3 entityPos, float destinationDistance, float stepDegrees)
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

        // stepDegrees is a per-journey config value, not guaranteed to
        // divide 360 evenly, so the cap is computed fresh each call
        // (CeilToInt, so a partial final step still counts as a full
        // circle) rather than cached on the episode.
        int maxSteps = Mathf.CeilToInt(360f / stepDegrees);
        if (episode.StepsTaken >= maxSteps)
        {
            return null;
        }

        episode.StepsTaken++;
        float newAngleDegrees = episode.BaseAngleDegrees + episode.Handedness * stepDegrees * episode.StepsTaken;
        float newAngleRadians = newAngleDegrees * Mathf.Deg2Rad;

        Vector3 direction = new Vector3(Mathf.Cos(newAngleRadians), 0f, Mathf.Sin(newAngleRadians));
        return entityPos + direction * destinationDistance;
    }
}
