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
}

[System.Serializable]
public class EmotionState
{
    public string entity_id;
    public string entity_type;
    public string emotion;
    public float strength;
}

[System.Serializable]
public class ArenaEventRequest
{
    public string session_id;
    public string event_type;
    public List<EmotionState> emotion_states;
    public string direction;
}
