using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.AI
{
    /// <summary>
    /// Alternating red/blue light bar: toggles the two renderers so no
    /// material instancing is needed. Cosmetic only — audio sirens are an
    /// M6 hook.
    /// </summary>
    public class PoliceLights : MonoBehaviour
    {
        [Tooltip("The red half of the light bar.")]
        public Renderer redLight;

        [Tooltip("The blue half of the light bar.")]
        public Renderer blueLight;

        [Tooltip("Seconds per half-cycle of the red/blue alternation.")]
        [PropertyRange(0.05f, 1f), SuffixLabel("s", true)]
        public float flashInterval = 0.22f;

        void Update()
        {
            bool redPhase = Mathf.FloorToInt(Time.time / flashInterval) % 2 == 0;
            if (redLight != null) redLight.enabled = redPhase;
            if (blueLight != null) blueLight.enabled = !redPhase;
        }

        void OnDisable()
        {
            if (redLight != null) redLight.enabled = true;
            if (blueLight != null) blueLight.enabled = true;
        }
    }
}
