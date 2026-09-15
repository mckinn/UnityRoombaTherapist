using System.Collections.Generic;
using UnityEngine.Rendering;

[System.Serializable]
public class SessionStartRequest
{
    public string roomba_id;
}

[System.Serializable]
public class PADState
{
    public float pleasure;
    public float arousal;
    public float dominance;
}

[System.Serializable]
public class EntitySensitivity
{
    public string entity_type;
    public string emotion;
    public float strength;
}

[System.Serializable]
public class SessionStartResponse
{
    public string session_id;
    public string roomba_name;
    public string roomba_description;
    public PADState initial_pad;
    public List<EntitySensitivity> entity_sensitivities;
}

[System.Serializable]
public class TherapyMessageRequest
{
    public string session_id;
    public string message;
}

[System.Serializable]
public class OrchestratorResponse
{
    public string dialog;
    public PADState pad;
    public List<EntitySensitivity> entity_sensitivities;
    public bool should_pause;
    public MovementDirective movement_directive;
}

/// <summary>
/// A resolved LLM movement instruction - see Movement_Concurrency_Plan.md
/// section 4, items 1 and 3. target_entity_id is already a resolved
/// entity_id, not a name - resolution (roster name -> entity_id) happens
/// entirely in the Orchestrator, by design (item 3: Unity never sees
/// names). Newtonsoft leaves this null when the JSON key is absent
/// (movement_directive is optional - most turns won't include one), which
/// is exactly the "nothing to do" signal callers should check for.
/// </summary>
[System.Serializable]
public class MovementDirective
{
    public string target_entity_id;
    public string direction; // "closer" or "further"
    public float percent;    // 0-100, clamped server-side (llm.py)
}

[System.Serializable]
public class EmotionState
{
    public string entity_id;
    public string entity_type;
    public string emotion;
    public float strength;

    // Phase 2 (Narrative_Log_Stream_Plan.md section 6): populated by
    // CollisionController for a "collision" report only when
    // JourneyCalculator actually created/refreshed a Journey for this
    // entity. journey_distance is nullable because it only has a value
    // when journey_started is true - leaving it unassigned (default 0)
    // would otherwise be indistinguishable from a genuine zero distance.
    public bool journey_started;
    public float? journey_distance;
}

[System.Serializable]
public class ArenaEventRequest
{
    public string session_id;
    public string event_type;
    public List<EmotionState> emotion_states;
    public string direction;
    public float? percent_complete; // dirt_progress only
    public bool? is_complete;       // dirt_progress only
}
