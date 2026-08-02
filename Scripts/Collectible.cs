using UnityEngine;

public class Collectible : MonoBehaviour
{
    [Tooltip("BaseLine rotation speed.")]
    public float rotationSpeed = 1.0f;

    public GameObject onCollectEffect;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.Rotate(0.5f * rotationSpeed,rotationSpeed,0.25f*rotationSpeed);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // this is an event 
            Debug.Log($"Object Deleted: {gameObject.name}, by {other.gameObject.name}");
            Destroy(gameObject);
            // instantiate the particle effect
            Instantiate(onCollectEffect, transform.position, transform.rotation);
        }
    }
}
