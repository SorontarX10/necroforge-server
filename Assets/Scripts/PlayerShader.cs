using UnityEngine;

public class PlayerShader : MonoBehaviour
{
    [SerializeField] public Renderer targetRenderer;
    [SerializeField, Min(0.05f)] private float cameraResolveInterval = 0.25f;

    private static readonly int FrontWsId = Shader.PropertyToID("_FrontWS");

    private MaterialPropertyBlock mpb;
    private Camera cachedCamera;
    private float nextCameraResolveAt;

    private void Awake()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<Renderer>();

        mpb = new MaterialPropertyBlock();
        cachedCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (targetRenderer == null)
            return;

        targetRenderer.GetPropertyBlock(mpb);

        if (cachedCamera == null && Time.unscaledTime >= nextCameraResolveAt)
        {
            nextCameraResolveAt = Time.unscaledTime + Mathf.Max(0.05f, cameraResolveInterval);
            cachedCamera = Camera.main;
        }

        Vector3 viewDirWs = cachedCamera != null
            ? (cachedCamera.transform.position - transform.position).normalized
            : transform.forward;

        mpb.SetVector(FrontWsId, viewDirWs);
        targetRenderer.SetPropertyBlock(mpb);
    }
}
