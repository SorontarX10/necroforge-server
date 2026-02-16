using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime visual wrapper for imported skull model assets.
/// Lets us keep a stable GameObject prefab reference for relic skulls.
/// </summary>
public sealed class RelicSkullResourceVisual : MonoBehaviour
{
    [SerializeField] private string resourcePath = "FBX/21337_Skull_v1";
    [SerializeField] private bool removeColliders = true;

    private static readonly Dictionary<string, GameObject> CachedModels = new();
    private GameObject spawnedModel;

    private void Awake()
    {
        EnsureModel();
    }

    private void EnsureModel()
    {
        if (spawnedModel != null)
            return;

        if (string.IsNullOrWhiteSpace(resourcePath))
            return;

        if (!CachedModels.TryGetValue(resourcePath, out GameObject sourcePrefab) || sourcePrefab == null)
        {
            sourcePrefab = Resources.Load<GameObject>(resourcePath);
            CachedModels[resourcePath] = sourcePrefab;
        }

        if (sourcePrefab == null)
        {
            Debug.LogWarning($"[RelicSkullResourceVisual] Resource not found at '{resourcePath}'.", this);
            return;
        }

        spawnedModel = Instantiate(sourcePrefab, transform);
        spawnedModel.name = "SkullModel";
        spawnedModel.transform.localPosition = Vector3.zero;
        spawnedModel.transform.localRotation = Quaternion.identity;
        spawnedModel.transform.localScale = Vector3.one;

        if (removeColliders)
        {
            Collider[] colliders = spawnedModel.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    Destroy(colliders[i]);
            }
        }
    }
}
