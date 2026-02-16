using System.Collections.Generic;
using UnityEngine;
using GrassSim.Enhancers;
using GrassSim.Combat;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SwordMeshTrail : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform trailBase;
    [SerializeField] private Transform trailTip;

    [Tooltip("Jeśli puste, weźmie z parenta.")]
    [SerializeField] private WeaponEnhancerSystem enhancerSystem;

    [Tooltip("Jeśli puste, weźmie z parenta.")]
    [SerializeField] private ICombatInput combatInput;

    [Header("Lifetime")]
    [SerializeField] private float lifeTime = 0.18f;          // jak długo segment żyje
    [SerializeField] private int maxSegments = 64;            // ograniczenie bufora

    [Header("Sampling")]
    [SerializeField] private float minDistance = 0.003f;      // minimalny ruch tipa/base, by dodać segment
    [SerializeField] private bool useUnscaledTime = false;

    [Header("Color")]
    [SerializeField] private float baseAlpha = 0.75f;         // alpha bez enhancerów (biała smuga)
    [SerializeField] private float enhancerAlphaMax = 0.9f;   // alpha przy enhancerach
    [SerializeField] private float intensityMultiplier = 1.2f;

    [Header("Debug")]
    [SerializeField] private bool debugLogs = false;

    private Mesh mesh;
    private MeshFilter mf;

    private readonly List<Segment> segments = new();
    private Vector3 lastBasePos;
    private Vector3 lastTipPos;
    private bool hasLast;

    // kolory docelowe
    private Color currentColor = Color.white;
    private float currentAlpha = 0f;

    private Vector3[] vertsBuffer = System.Array.Empty<Vector3>();
    private Vector2[] uvsBuffer = System.Array.Empty<Vector2>();
    private Color[] colorsBuffer = System.Array.Empty<Color>();
    private int[] trisBuffer = System.Array.Empty<int>();
    private float minDistanceSqr;

    private struct Segment
    {
        public Vector3 basePos;
        public Vector3 tipPos;
        public float time;
    }

    private void OnValidate()
    {
        lifeTime = Mathf.Max(0.01f, lifeTime);
        maxSegments = Mathf.Max(2, maxSegments);
        minDistance = Mathf.Max(0f, minDistance);
        minDistanceSqr = minDistance * minDistance;
    }

    private void Awake()
    {
        mf = GetComponent<MeshFilter>();

        mesh = new Mesh();
        mesh.name = "SwordTrailMesh";
        mf.sharedMesh = mesh;

        minDistanceSqr = Mathf.Max(0f, minDistance) * Mathf.Max(0f, minDistance);

        if (enhancerSystem == null)
            enhancerSystem = GetComponentInParent<WeaponEnhancerSystem>();

        if (combatInput == null)
            combatInput = GetComponentInParent<ICombatInput>();

        if (trailBase == null || trailTip == null)
        {
            Debug.LogError("[SwordMeshTrail] Missing trailBase/trailTip refs.", this);
            enabled = false;
            return;
        }

        if (enhancerSystem != null)
            enhancerSystem.OnChanged += RecalculateColorTarget;
        RecalculateColorTarget();
    }

    private void OnDestroy()
    {
        if (enhancerSystem != null)
            enhancerSystem.OnChanged -= RecalculateColorTarget;

        if (mesh != null)
            Destroy(mesh);
    }

    private void LateUpdate()
    {
        float now = useUnscaledTime ? Time.unscaledTime : Time.time;

        bool attacking = combatInput != null && combatInput.IsAttacking();

        // jeśli nie atakujemy — wygaszamy istniejącą smugę, ale nie dodajemy nowych segmentów
        if (attacking)
            TryAddSegment(now);

        PruneOldSegments(now);
        RebuildMesh(now);

        // jeśli nie ma segmentów, wyczyść mesh żeby nie wisiało
        if (segments.Count == 0)
            mesh.Clear();
    }

    private void TryAddSegment(float now)
    {
        Vector3 b = trailBase.position;
        Vector3 t = trailTip.position;

        if (!hasLast)
        {
            lastBasePos = b;
            lastTipPos = t;
            hasLast = true;
            AddSegment(b, t, now);
            return;
        }

        float moveBase = (b - lastBasePos).sqrMagnitude;
        float moveTip  = (t - lastTipPos).sqrMagnitude;

        if (moveBase < minDistanceSqr && moveTip < minDistanceSqr)
            return;

        lastBasePos = b;
        lastTipPos = t;

        AddSegment(b, t, now);
    }

    private void AddSegment(Vector3 b, Vector3 t, float now)
    {
        segments.Add(new Segment { basePos = b, tipPos = t, time = now });

        if (segments.Count > maxSegments)
            segments.RemoveAt(0);
    }

    private void PruneOldSegments(float now)
    {
        float cutoff = now - lifeTime;

        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (segments[i].time < cutoff)
                segments.RemoveAt(i);
        }

        if (segments.Count == 0)
            hasLast = false;
    }

    private void RebuildMesh(float now)
    {
        int count = segments.Count;
        if (count < 2)
        {
            // za mało do „wstęgi”
            mesh.Clear();
            return;
        }

        // Wstęga: dla każdego segmentu 2 wierzchołki (base, tip)
        // i między nimi quady -> trójkąty.
        int vertCount = count * 2;
        int triCount = (count - 1) * 2;   // quady
        int indexCount = triCount * 3;

        EnsureMeshBuffers(vertCount, indexCount);

        // kolor: biały bez enhancerów, albo mieszanka enhancerów
        Color col = currentColor;

        // alpha zależna od „strength” enhancerów
        float alpha = currentAlpha;

        for (int i = 0; i < count; i++)
        {
            float age01 = Mathf.InverseLerp(lifeTime, 0f, now - segments[i].time); // 1->0 w czasie
            float a = alpha * Mathf.Clamp01(age01);

            int vi = i * 2;

            vertsBuffer[vi + 0] = transform.InverseTransformPoint(segments[i].basePos);
            vertsBuffer[vi + 1] = transform.InverseTransformPoint(segments[i].tipPos);

            // UV: X wzdłuż czasu, Y = 0/1
            float x = (float)i / (count - 1);
            uvsBuffer[vi + 0] = new Vector2(x, 0f);
            uvsBuffer[vi + 1] = new Vector2(x, 1f);

            var c0 = col; c0.a = a;
            var c1 = col; c1.a = a;

            colorsBuffer[vi + 0] = c0;
            colorsBuffer[vi + 1] = c1;
        }

        int ti = 0;
        for (int i = 0; i < count - 1; i++)
        {
            int vi = i * 2;

            // quad: (vi, vi+1, vi+2, vi+3)
            trisBuffer[ti++] = vi + 0;
            trisBuffer[ti++] = vi + 1;
            trisBuffer[ti++] = vi + 2;

            trisBuffer[ti++] = vi + 2;
            trisBuffer[ti++] = vi + 1;
            trisBuffer[ti++] = vi + 3;
        }

        mesh.Clear(false);
        mesh.SetVertices(vertsBuffer, 0, vertCount);
        mesh.SetUVs(0, uvsBuffer, 0, vertCount);
        mesh.SetColors(colorsBuffer, 0, vertCount);
        mesh.SetTriangles(trisBuffer, 0, indexCount, 0, false);
        mesh.RecalculateBounds();
    }

    private void EnsureMeshBuffers(int vertCount, int indexCount)
    {
        if (vertsBuffer.Length < vertCount)
            System.Array.Resize(ref vertsBuffer, Mathf.NextPowerOfTwo(vertCount));

        if (uvsBuffer.Length < vertCount)
            System.Array.Resize(ref uvsBuffer, Mathf.NextPowerOfTwo(vertCount));

        if (colorsBuffer.Length < vertCount)
            System.Array.Resize(ref colorsBuffer, Mathf.NextPowerOfTwo(vertCount));

        if (trisBuffer.Length < indexCount)
            System.Array.Resize(ref trisBuffer, Mathf.NextPowerOfTwo(indexCount));
    }

    private void RecalculateColorTarget()
    {
        // domyślnie: biały trail zawsze
        Color mixed = Color.white;
        float strength = 0f;
        bool any = false;

        if (enhancerSystem != null)
        {
            mixed = Color.black;
            strength = 0f;

            foreach (var a in enhancerSystem.Active)
            {
                if (a == null || a.Definition == null)
                    continue;

                float s = a.GetStrength01();
                mixed += a.Definition.emissionColor * s;
                strength += s;
                any = true;
            }
        }

        if (!any)
        {
            currentColor = Color.white;
            currentAlpha = baseAlpha;
            return;
        }

        mixed /= Mathf.Max(1f, strength);
        currentColor = mixed;
        currentAlpha = Mathf.Clamp01(strength * intensityMultiplier) * enhancerAlphaMax;

        if (debugLogs)
            Debug.Log($"[SwordMeshTrail] mixed={currentColor} alpha={currentAlpha}");
    }
}
