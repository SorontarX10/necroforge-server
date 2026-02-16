using UnityEngine;

public class CustomCursorController : MonoBehaviour
{
    public static CustomCursorController Instance;

    public Texture2D cursorTexture;
    public Vector2 hotspot = Vector2.zero;

    void Awake()
    {
        // 🔒 singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (cursorTexture == null)
        {
            Debug.LogError("CustomCursorController: brak cursorTexture!");
            return;
        }

        Cursor.SetCursor(
            cursorTexture,
            hotspot,
            CursorMode.Auto
        );
    }
}
