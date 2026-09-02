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
    /// every frame against the ship's speed by <see cref="SetGateColor"/>.
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

        /// <summary>True once the GameManager has raised this loop's approach alert.</summary>
        public bool Alerted { get; set; }

        readonly List<Renderer> gateRenderers = new();
        MaterialPropertyBlock mpb;
        bool? gateState;

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

        /// <summary>Tint the gate pass or fail. Cheap to call every frame — it only writes on a change.</summary>
        public void SetGateColor(bool pass)
        {
            if (gateState == pass || Definition == null) return;
            gateState = pass;
            Color color = pass ? Definition.passColor : Definition.failColor;
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
