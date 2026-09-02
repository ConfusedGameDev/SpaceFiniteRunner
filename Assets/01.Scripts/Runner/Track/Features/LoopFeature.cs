using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// A spawned loop: the <see cref="LoopSection"/> the track routes through,
    /// the definition clone and the entry speed the generator fixed for it
    /// (<c>GameSettings</c>' floor + ramp × this loop's distance, capped — a
    /// number, so the gate can never lie). Like <see cref="JumpRamp"/> it is
    /// read analytically by <see cref="Ship.ShipMotor"/> off <see cref="Active"/>
    /// rather than through a trigger; the road round the loop is stamped by
    /// the decorator like any other stretch of track. The gate at the mouth
    /// (a portal frame the generator builds under this object) is recoloured
    /// every frame against the ship's speed by <see cref="SetGateColor"/>, and
    /// the required speed stands above it as a fixed label (<see cref="BuildLabel"/>).
    /// </summary>
    public class LoopFeature : MonoBehaviour
    {
        /// <summary>Every loop currently spawned, for the motor's per-frame scan and the GameManager's alerts.</summary>
        public static readonly List<LoopFeature> Active = new();

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public LoopDefinition Definition { get; private set; }
        public LoopSection Section { get; private set; }

        /// <summary>Entry speed the loop demands, m/s.</summary>
        public float RequiredSpeed { get; private set; }

        public float StartDistance => Section != null ? Section.StartDistance : 0f;
        public float EndDistance => Section != null ? Section.EndDistance : 0f;

        /// <summary>Whether the required-speed label at the mouth is currently shown.</summary>
        public bool LabelVisible => label != null && label.gameObject.activeSelf;

        readonly List<Renderer> gateRenderers = new();
        MaterialPropertyBlock mpb;
        bool? gateState;
        TextMesh label;

        public void Configure(LoopDefinition definition, LoopSection section, float requiredSpeed)
        {
            Definition = definition;
            Section = section;
            RequiredSpeed = requiredSpeed;
        }

        /// <summary>Renderers the gate colour is pushed onto.</summary>
        public void AddGateRenderer(Renderer renderer)
        {
            if (renderer != null) gateRenderers.Add(renderer);
        }

        /// <summary>
        /// The number at the mouth: a world-space text FIXED above the gate
        /// (never a popup that rides ahead of the ship) reading the required
        /// entry speed in km/h and nothing else — the gate's colour already
        /// says pass or fail, the label says how much. Built once by the
        /// generator, hidden until the GameManager reveals it inside the
        /// alert lead, and tinted with the gate.
        /// </summary>
        public void BuildLabel(float height, float characterSize)
        {
            if (label != null) Destroy(label.gameObject);
            var go = new GameObject("RequiredSpeedLabel");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            // A TextMesh reads correctly to a viewer looking along its +Z; the
            // mouth's forward IS the track direction the ship looks along, so
            // the label keeps the gate's rotation (a 180° turn mirrors it).
            go.transform.localRotation = Quaternion.identity;

            label = go.AddComponent<TextMesh>();
            label.text = $"{RequiredSpeed * 3.6f:0}";
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.fontStyle = FontStyle.Bold;
            label.fontSize = 48;
            label.characterSize = characterSize;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                label.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            label.color = gateState == true ? Definition.passColor : Definition.failColor;
            go.SetActive(false);
        }

        /// <summary>Show or hide the label. Cheap every frame — it only toggles on a change.</summary>
        public void SetLabelVisible(bool visible)
        {
            if (label == null || label.gameObject.activeSelf == visible) return;
            label.gameObject.SetActive(visible);
        }

        /// <summary>Tint the gate (and its label) pass or fail. Cheap to call every frame — it only writes on a change.</summary>
        public void SetGateColor(bool pass)
        {
            if (gateState == pass || Definition == null) return;
            gateState = pass;
            Color color = pass ? Definition.passColor : Definition.failColor;
            if (label != null) label.color = color;
            mpb ??= new MaterialPropertyBlock();
            foreach (var r in gateRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, color);
                mpb.SetColor(EmissionColorId, color);
                r.SetPropertyBlock(mpb);
            }
        }

        void OnEnable()
        {
            if (Application.isPlaying && !Active.Contains(this)) Active.Add(this);
        }

        void OnDisable() => Active.Remove(this);
    }
}
