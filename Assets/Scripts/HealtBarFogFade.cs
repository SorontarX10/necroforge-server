using UnityEngine;
using GrassSim.Core;

[RequireComponent(typeof(CanvasGroup))]
public class HealthBarFogFade : MonoBehaviour
{
    [Header("Fade distances")]
    [SerializeField] private float visibleDistance = 20f;
    [SerializeField] private float fadeOutDistance = 40f;

    private CanvasGroup canvasGroup;
    private Transform player;
    private WorldHealthBar worldBar;
    private float nextPlayerResolveAt;
    private const float PlayerResolveInterval = 0.4f;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        worldBar = GetComponent<WorldHealthBar>();

        // startowo widoczne, żebyś od razu widział czy w ogóle działa
        canvasGroup.alpha = 1f;
    }

    void Start()
    {
        if (worldBar == null)
        {
            enabled = false;
            return;
        }
        
        player = PlayerLocator.GetTransform();
        nextPlayerResolveAt = Time.time + PlayerResolveInterval;
    }

    void LateUpdate()
    {
        if (worldBar == null || worldBar.target == null)
            return;

        if (player == null && Time.time >= nextPlayerResolveAt)
        {
            nextPlayerResolveAt = Time.time + PlayerResolveInterval;
            player = PlayerLocator.GetTransform();
        }

        if (player == null)
            return;

        // Player healthbar zawsze 1
        if (worldBar.target.CompareTag("Player"))
        {
            canvasGroup.alpha = 1f;
            return;
        }

        Vector3 a = worldBar.target.position;
        a.y = 0f;
        Vector3 b = player.position;
        b.y = 0f;
        float distSqr = (a - b).sqrMagnitude;
        float visibleSqr = visibleDistance * visibleDistance;
        float fadeSqr = fadeOutDistance * fadeOutDistance;

        float alpha;
        if (distSqr <= visibleSqr)
            alpha = 1f;
        else if (distSqr >= fadeSqr)
            alpha = 0f;
        else
        {
            float dist = Mathf.Sqrt(distSqr);
            float t = Mathf.InverseLerp(visibleDistance, fadeOutDistance, dist);
            alpha = 1f - t;
        }

        canvasGroup.alpha = alpha;
    }
}
