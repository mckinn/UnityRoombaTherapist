using UnityEngine;
using System.Collections.Generic;

public class EntityRegistry : MonoBehaviour
{
    private static EntityRegistry instance;
    public static EntityRegistry Instance => instance;

    private readonly Dictionary<string, int> nextIdByType = new Dictionary<string, int>();

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    public string AssignNextId(string entityType)
    {
        string key = entityType.ToLower();

        if (!nextIdByType.TryGetValue(key, out int nextNumber))
        {
            nextNumber = 1;
        }

        nextIdByType[key] = nextNumber + 1;
        return $"{key}_{nextNumber:D2}";
    }
}