using UnityEngine;

public class GlobalLoadingCamera : MonoBehaviour
{
    public static GlobalLoadingCamera Instance { get; private set; }

    public Camera cam;
    public Canvas canvas;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (cam != null)
            cam.enabled = true;
    }

    public void Shutdown()
    {
        Destroy(gameObject);
    }
}
