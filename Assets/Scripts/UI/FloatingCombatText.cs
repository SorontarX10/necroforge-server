using UnityEngine;
using TMPro;

namespace GrassSim.UI
{
    public class FloatingCombatText : MonoBehaviour
    {
        public float lifetime = 1.2f;
        public float floatSpeed = 1.5f;
        public float fadeSpeed = 2f;

        private TextMeshProUGUI text;
        private float timer;
        private Color startColor;

        private void Awake()
        {
            text = GetComponentInChildren<TextMeshProUGUI>();
        }

        public void Init(string value, Color color, float scale = 1f)
        {
            text.text = value;
            text.color = color;
            startColor = color;
            transform.localScale *= scale;
        }

        private void Update()
        {
            timer += Time.deltaTime;

            // ruch w górę
            transform.position += Vector3.up * floatSpeed * Time.deltaTime;

            // fade out
            float t = timer / lifetime;
            Color c = startColor;
            c.a = Mathf.Lerp(1f, 0f, t);
            text.color = c;

            if (timer >= lifetime)
                Destroy(gameObject);
        }
    }
}
