using GrassSim.Core;
using UnityEngine;

public class DistanceCuller : MonoBehaviour
{
    [Tooltip("Fallback when generator cannot be resolved.")]
    [SerializeField] private float fallbackCullDistance = 50f;
    [SerializeField, Min(1)] private int updateEveryNFrames = 4;
    [SerializeField, Min(0.05f)] private float playerResolveInterval = 0.35f;

    private Transform player;
    private float sqrDist;
    private float nextPlayerResolveAt;
    private int frameOffset;

    private Renderer[] renderers;
    private Collider[] colliders;
    private bool lastVisible = true;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        frameOffset = Random.Range(0, Mathf.Max(1, updateEveryNFrames));
    }

    private void Start()
    {
        var gen = Object.FindAnyObjectByType<ChunkedProceduralLevelGenerator>();
        float dist = gen != null ? gen.treeCullDistance : fallbackCullDistance;
        sqrDist = dist * dist;

        ResolvePlayer(force: true);
    }

    private void Update()
    {
        int frameStep = Mathf.Max(1, updateEveryNFrames);
        if (((Time.frameCount + frameOffset) % frameStep) != 0)
            return;

        if (player == null)
        {
            ResolvePlayer(force: false);
            if (player == null)
                return;
        }

        bool visible = (transform.position - player.position).sqrMagnitude <= sqrDist;
        if (visible == lastVisible)
            return;

        lastVisible = visible;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = visible;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = visible;
        }
    }

    private void ResolvePlayer(bool force)
    {
        if (!force && Time.unscaledTime < nextPlayerResolveAt)
            return;

        nextPlayerResolveAt = Time.unscaledTime + Mathf.Max(0.05f, playerResolveInterval);
        player = PlayerLocator.GetTransform();
    }
}
