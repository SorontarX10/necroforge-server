using UnityEngine;
using System.Collections.Generic;

public static class SimplePool
{
    public struct RuntimeSnapshot
    {
        public int frame;
        public int getCallsThisFrame;
        public int pooledHitsThisFrame;
        public int instantiatesThisFrame;
        public int returnCallsThisFrame;
        public int destroyFallbacksThisFrame;
        public int prefabBucketCount;
        public int pooledInactiveCount;
    }

    private static readonly Dictionary<GameObject, Queue<GameObject>> pool = new();
    private static int metricsFrame = -1;
    private static int getCallsThisFrame;
    private static int pooledHitsThisFrame;
    private static int instantiatesThisFrame;
    private static int returnCallsThisFrame;
    private static int destroyFallbacksThisFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlayModeStart()
    {
        pool.Clear();
        metricsFrame = -1;
        getCallsThisFrame = 0;
        pooledHitsThisFrame = 0;
        instantiatesThisFrame = 0;
        returnCallsThisFrame = 0;
        destroyFallbacksThisFrame = 0;
    }

    public static RuntimeSnapshot GetRuntimeSnapshot()
    {
        EnsureMetricsFrame();
        int pooledInactiveCount = 0;
        foreach (KeyValuePair<GameObject, Queue<GameObject>> kv in pool)
        {
            if (kv.Value != null)
                pooledInactiveCount += kv.Value.Count;
        }

        return new RuntimeSnapshot
        {
            frame = metricsFrame,
            getCallsThisFrame = getCallsThisFrame,
            pooledHitsThisFrame = pooledHitsThisFrame,
            instantiatesThisFrame = instantiatesThisFrame,
            returnCallsThisFrame = returnCallsThisFrame,
            destroyFallbacksThisFrame = destroyFallbacksThisFrame,
            prefabBucketCount = pool.Count,
            pooledInactiveCount = pooledInactiveCount
        };
    }

    private static void EnsureMetricsFrame()
    {
        int frame = Time.frameCount;
        if (metricsFrame == frame)
            return;

        metricsFrame = frame;
        getCallsThisFrame = 0;
        pooledHitsThisFrame = 0;
        instantiatesThisFrame = 0;
        returnCallsThisFrame = 0;
        destroyFallbacksThisFrame = 0;
    }

    public static GameObject Get(GameObject prefab)
    {
        EnsureMetricsFrame();
        getCallsThisFrame++;

        if (prefab == null)
            return null;

        if (!pool.TryGetValue(prefab, out var q))
            pool[prefab] = q = new Queue<GameObject>();

        while (q.Count > 0)
        {
            var go = q.Dequeue();
            if (go == null)
                continue;

            go.SetActive(true);
            pooledHitsThisFrame++;
            return go;
        }

        var inst = Object.Instantiate(prefab);
        instantiatesThisFrame++;
        var po = inst.GetComponent<PooledObject>();
        if (po == null)
            po = inst.AddComponent<PooledObject>();

        po.prefab = prefab;
        return inst;
    }

    public static void Return(GameObject go)
    {
        EnsureMetricsFrame();
        returnCallsThisFrame++;

        if (go == null)
            return;

        var po = go.GetComponent<PooledObject>();
        if (po == null || po.prefab == null)
        {
            destroyFallbacksThisFrame++;
            Object.Destroy(go);
            return;
        }

        go.SetActive(false);

        if (!pool.TryGetValue(po.prefab, out var q))
            pool[po.prefab] = q = new Queue<GameObject>();

        q.Enqueue(go);
    }
}
