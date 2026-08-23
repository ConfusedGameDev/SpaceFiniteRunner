using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.HUD
{
    /// <summary>
    /// Juice: a short-lived world-space text that rises, fades out and always
    /// faces the camera. Built entirely from code so no prefab or scene wiring
    /// is needed. Prefer going through <see cref="FloatingTextSystem"/> for
    /// gameplay messages — it handles placement relative to the ship.
    /// </summary>
    public class FloatingWorldText : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] float lifetime = 1.1f;
        [SerializeField] float riseSpeed = 7f;

        TextMesh textMesh;
        float age;
        Color baseColor;

        public static FloatingWorldText Spawn(Vector3 position, string text, Color color,
                                              float characterSize = 1.2f, float duration = 1.1f)
        {
            var go = new GameObject("FloatingText");
            go.transform.position = position;

            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.color = color;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.fontSize = 48;
            tm.characterSize = characterSize;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }

            var floating = go.AddComponent<FloatingWorldText>();
            floating.lifetime = Mathf.Max(0.1f, duration);
            return floating;
        }

        void Awake() => textMesh = GetComponent<TextMesh>();

        void Start() => baseColor = textMesh != null ? textMesh.color : Color.white;

        void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

            // Billboard toward the camera.
            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);

            if (textMesh != null)
            {
                float alpha = 1f - Mathf.SmoothStep(0.35f, 1f, age / lifetime);
                textMesh.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
            }
        }
    }
}
