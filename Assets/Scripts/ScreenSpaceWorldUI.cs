using UnityEngine;
using UnityEngine.UI;

public class ScreenSpaceWorldUI : MonoBehaviour
{
    public Transform target;
    public Vector3 worldOffset = Vector3.up * 2f;

    [Header("Optimization")]
    [Min(1)] public int updateEveryNFrames = 2;
    public float maxVisibleDistance = 55f;
    public float distanceHysteresis = 8f;
    public bool scaleByDistance = true;

    private RectTransform rect;
    private CanvasGroup canvasGroup;
    private Camera mainCam;
    private int frameOffset;
    private bool isVisible;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        mainCam = Camera.main;
        frameOffset = Random.Range(0, Mathf.Max(1, updateEveryNFrames));
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            SetVisible(false);
            return;
        }

        if (mainCam == null)
        {
            mainCam = Camera.main;
            if (mainCam == null)
            {
                SetVisible(false);
                return;
            }
        }

        int step = Mathf.Max(1, updateEveryNFrames);
        if (((Time.frameCount + frameOffset) % step) != 0)
            return;

        Vector3 worldPos = target.position + worldOffset;
        Vector3 camPos = mainCam.transform.position;
        float sqrDistance = (worldPos - camPos).sqrMagnitude;

        float maxDist = Mathf.Max(0f, maxVisibleDistance);
        if (maxDist > 0f)
        {
            float testDistance = isVisible ? maxDist + Mathf.Max(0f, distanceHysteresis) : maxDist;
            if (sqrDistance > testDistance * testDistance)
            {
                SetVisible(false);
                return;
            }
        }

        Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);
        if (screenPos.z <= 0f)
        {
            SetVisible(false);
            return;
        }

        rect.position = screenPos;
        if (scaleByDistance)
            rect.localScale = GetScaleByDistance(Mathf.Sqrt(sqrDistance));

        SetVisible(true);
    }

    private Vector3 GetScaleByDistance(float distance)
    {
        float safeDistance = Mathf.Max(0.0001f, distance);
        float scale = Mathf.Clamp(1f / (safeDistance * 0.07f), 0.5f, 1.2f);
        return new Vector3(scale, scale, scale);
    }

    private void SetVisible(bool visible)
    {
        if (canvasGroup == null)
            return;

        if (isVisible == visible)
            return;

        isVisible = visible;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }
}
