using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class WorldStaminaBar : MonoBehaviour
{
    public Transform target;           // obiekt, nad którym pasek ma się pojawić
    public Canvas canvas;              // world-space canvas zawierający Slider
    public Slider slider;

    private Camera cam;

    void Awake()
    {
        // Nie logujemy błędu target tu — może zostać przypisany przez binder później.
        // Usuwamy ostrzeżenie z Awake.
    }

    void Start()
    {
        // Poczekaj aż kamera MainCamera będzie dostępna
        StartCoroutine(AssignCamera());
    }

    IEnumerator AssignCamera()
    {
        while (Camera.main == null)
            yield return null;

        cam = Camera.main;

        if (canvas != null)
            canvas.worldCamera = cam;
    }

    void LateUpdate()
    {
        // Nie rób nic jeśli nie mamy targeta lub kamery lub canvas
        if (target == null || cam == null || canvas == null)
            return;

        Vector3 worldPos = target.position + new Vector3(0, 1.4f, 0);
        canvas.transform.position = worldPos;
        canvas.transform.rotation = cam.transform.rotation;
    }

    public void SetStaminaFraction(float frac)
    {
        if (slider == null)
            return;

        slider.value = Mathf.Clamp01(frac);
    }
}
