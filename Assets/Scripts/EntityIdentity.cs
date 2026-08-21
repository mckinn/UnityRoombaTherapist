using UnityEngine;

public class EntityIdentity : MonoBehaviour
{
    public string EntityId { get; private set; }

    public string GetOrAssignId()
    {
        if (string.IsNullOrEmpty(EntityId))
        {
            EntityId = EntityRegistry.Instance.AssignNextId(gameObject.tag);
        }
        return EntityId;
    }
}