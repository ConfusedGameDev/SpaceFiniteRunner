using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.TimeTrial
{
    /// <summary>
    /// One Arcade checkpoint's gate: the picture across the road (two pylons
    /// and a beam, or the trial's <see cref="TimeTrialDefinition.gatePrefab"/>
    /// with its colliders stripped) and a floating label over it that says what
    /// crossing it pays — the time award and the clean bonus (or the dirty
    /// penalty). A picture only: a checkpoint is judged on track distance,
    /// never by a trigger.
    ///
    /// Built in two places, always under the track's
    /// <see cref="TrackGenerator.CheckpointParent"/> (the hand-placed
    /// TimeTrialCheckpoints object next to Pads): in play by the
    /// <see cref="TimeTrialSystem"/>, which tints it and keeps the label live;
    /// in edit mode by the editor's preview of a saved track, where
    /// <see cref="Owner"/> / <see cref="RecordIndex"/> name the record it shows,
    /// so selecting the gate edits that checkpoint in the track asset.
    /// </summary>
    public class CheckpointGate : MonoBehaviour
    {
        [Tooltip("Edit preview: the generator whose loaded saved track holds this checkpoint. Empty in play.")]
        [SerializeField] TrackGenerator owner;
        [Tooltip("Edit preview: the index of this checkpoint's record in the loaded track's placements. -1 in play.")]
        [SerializeField] int recordIndex = -1;
        [SerializeField] TextMesh label;
        [SerializeField] bool prefabLook;

        Material material;
        string shownText;

        public TrackGenerator Owner => owner;
        public int RecordIndex => recordIndex;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>A gate under <paramref name="parent"/>: the prefab's picture if given, else pylons and a beam.</summary>
        public static CheckpointGate Create(Transform parent, string name, GameObject prefab)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var gate = go.AddComponent<CheckpointGate>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            gate.material = shader != null ? new Material(shader) { name = "CheckpointGate (runtime)" } : null;

            if (prefab != null)
            {
                gate.prefabLook = true;
                var visual = Instantiate(prefab, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                foreach (var c in visual.GetComponentsInChildren<Collider>(true)) DestroySafe(c);
                if (gate.material != null)
                    foreach (var r in visual.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = gate.material;
            }
            else
            {
                // Unit cubes, scaled to the road in Place.
                for (int part = 0; part < 3; part++)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = part == 0 ? "PylonL" : part == 1 ? "PylonR" : "Beam";
                    cube.transform.SetParent(go.transform, false);
                    DestroySafe(cube.GetComponent<Collider>());
                    var r = cube.GetComponent<Renderer>();
                    if (gate.material != null) r.sharedMaterial = gate.material;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            gate.label = BuildLabel(go.transform);
            return gate;
        }

        /// <summary>Edit preview: the record this gate shows (selecting it edits that record).</summary>
        public void BindRecord(TrackGenerator generator, int index)
        {
            owner = generator;
            recordIndex = index;
        }

        /// <summary>
        /// Stands the gate across the road at <paramref name="distance"/>, the
        /// beam <paramref name="height"/> up, and the label <paramref name="labelGap"/>
        /// over the beam at <paramref name="labelSize"/>.
        /// </summary>
        public void Place(TrackManager track, float distance, float height, float thickness, float labelGap, float labelSize)
        {
            track.GetPoseAtDistance(distance, 0f, out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
            if (label != null)
            {
                label.transform.localPosition = new Vector3(0f, height + labelGap, 0f);
                label.characterSize = labelSize;
            }
            if (prefabLook) return;

            track.GetLateralBand(distance, out float min, out float max);
            float half = Mathf.Max(Mathf.Abs(min), Mathf.Abs(max)) + 2f;
            for (int i = 0; i < transform.childCount && i < 3; i++)
            {
                Transform part = transform.GetChild(i);
                if (i < 2)
                {
                    part.localPosition = new Vector3((i == 0 ? -1f : 1f) * half, height * 0.5f - 1f, 0f);
                    part.localScale = new Vector3(thickness, height, thickness);
                }
                else
                {
                    part.localPosition = new Vector3(0f, height - 1f, 0f);
                    part.localScale = new Vector3(half * 2f + thickness, thickness, thickness);
                }
            }
        }

        public void SetColor(Color color)
        {
            if (material != null) material.SetColor(BaseColorId, color);
        }

        /// <summary>The floating text (rich text: colour and size tags). Cheap to call every frame — it only writes on a change.</summary>
        public void SetLabel(string text)
        {
            if (label == null || text == shownText) return;
            shownText = text;
            label.text = text;
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>
        /// The label's text: the award on the first line in <paramref name="awardColor"/>,
        /// then an optional smaller second line (the clean bonus, the dirty penalty).
        /// </summary>
        public static string Compose(string award, Color awardColor, string extra, Color extraColor)
        {
            string text = $"<color=#{ColorUtility.ToHtmlStringRGB(awardColor)}>{award}</color>";
            if (!string.IsNullOrEmpty(extra))
                text += $"\n<size=30><color=#{ColorUtility.ToHtmlStringRGB(extraColor)}>{extra}</color></size>";
            return text;
        }

        /// <summary>Signed seconds the way the HUD floaters print them: +4.2S / -3.0S.</summary>
        public static string Seconds(float seconds) =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}{1:0.0}S", seconds >= 0f ? "+" : "-", Mathf.Abs(seconds));

        // A TextMesh reads correctly to a viewer looking along its +Z — the
        // track's direction, which the ship looks along — so it keeps the gate's rotation.
        static TextMesh BuildLabel(Transform parent)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMesh>();
            text.anchor = TextAnchor.LowerCenter;
            text.alignment = TextAlignment.Center;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 48;
            text.richText = true;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            go.SetActive(false);
            return text;
        }

        void OnDestroy()
        {
            if (material != null) DestroySafe(material);
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
