using UnityEngine;


/// <summary>
/// 
/// This class manages the changing mass of the Roomba - making it more massive as it gains in PAD 
/// dominance.  That enables it to push things around as it feels more dominant, and change the 
/// overall outcomes of the collisions.
/// 
/// one of the interesting settings that needs to be explored that will not be modified by this script 
/// is the "is Kinematic" setting that controls whether the change in mass impact how the roomba is 
/// impacted (Pun intended) by the mass that it carries.
///
/// </summary>
public class BodyMassController : MonoBehaviour

{
    [Header("The allowed mass range of the Roomba (or the floor will cave in) [0.5 to 10.0]")]
    [Tooltip("As the Roomba grows in a personal sense of Dominance, it's PhysX mass increases.  This value is the minimum value aligned with feelings of total inadequacy")]
    [Delayed]
    [SerializeField] private float minimumMassOfRoomba = 0.2f;
    [Tooltip("As the Roomba grows in a personal sense of Dominance, it's PhysX mass increases.  This value is the maximum value aligned with feelings of total world domination")]
    [Delayed]
    [SerializeField] private float maximumMassOfRoomba = 10.0f;

    private float currentMass = 7.0f;
    private Rigidbody rb;
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = currentMass;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Debug.Log("BodyMassController: in Start");
        if (SessionManager.Instance != null)
        {
            Debug.Log("BodyMassController: in Start - adding HandlePadUpdate");
            SessionManager.Instance.OnStateUpdated += HandlePadUpdate;
        }
    }

    private void OnValidate()
    {
        /// setting the current mass as a result of an edit change
        /// "[delayed]" is used to ensure that the desired value is completely entered before the mass is clamped between them
        float oldMass = currentMass;
        currentMass = Mathf.Clamp(currentMass, minimumMassOfRoomba, maximumMassOfRoomba);
        Debug.Log($"BodyMassController : OnValidate called for Roomba Mass: Minimum {minimumMassOfRoomba}, Maximum: {maximumMassOfRoomba}, Current: {currentMass}");
        if ( currentMass != oldMass )
        {
            // the currentMass value has fallen outside the new mass bounds and needs to be changed, regardless of the PAD value.
            // no attempt is made to recompute the PAD - driven value at this point, but this could be changed
            // [TODO] update the mass based on the PAD value if available.
            Debug.Log($"BodyMassController : changing the RigidBody mass because it now falls outside of the bounds.  Old: {oldMass}, Current: {currentMass}"); 
            rb.mass = currentMass;
        }
            
    }

    private void OnEnable()

    {
        Debug.Log("BodyMassController: in OnEnable");
        if (SessionManager.Instance != null)
        {
            Debug.Log("BodyMassController: in OnEnable - adding HandlePadUpdate");
            SessionManager.Instance.OnStateUpdated -= HandlePadUpdate;
            SessionManager.Instance.OnStateUpdated += HandlePadUpdate;
            HandlePadUpdate(); // pick up whatever state already exists
        }
    }

    private void OnDisable()
    {
        Debug.Log("BodyMassController: in OnDisable"); 
        if (SessionManager.Instance != null)
        {
            SessionManager.Instance.OnStateUpdated -= HandlePadUpdate;
        }
    }

    private void HandlePadUpdate()
    {
        Debug.Log("BodyMassController: in HandlePadUpdate");
        if (SessionManager.Instance != null )
        {
            float currentPadDominance = SessionManager.Instance.CurrentPad.dominance;
            float newMass = ComputeMass(currentPadDominance);
            Debug.Log($"BodyMassController: in HandlePadUpdate, Dominance: {currentPadDominance}, currentMass {currentMass}, newMass {newMass}");
            if (newMass != currentMass)
            {
                Debug.Log("BodyMassController: in HandlePadUpdate"); 
                rb.mass = currentMass = newMass;
            }
        }
        
    }

    /// <summary>
    /// This utility computes the new mass value from the current value, and the current PAD value.
    /// </summary>
    /// <param name="PadValue"></param>
    /// <returns> the new Mass Value </returns>
    private float ComputeMass ( float padDominanceValue )
    {
        float normalizedPad = Mathf.Clamp01((padDominanceValue + 1f) / 2f); // -1..1 -> 0..1, 1 = most pleasant
        float targetMass = Mathf.Lerp(minimumMassOfRoomba, maximumMassOfRoomba, normalizedPad);
        return targetMass;
    }

}
