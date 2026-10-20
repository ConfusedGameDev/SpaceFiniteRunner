using ConfusedGameDev.FiniteRunner.Contracts;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The Bastion-style track reveal: the road ahead assembles itself, every
    /// stamped piece flying up into place as a front sweeps down the track.
    /// <b>Purely a picture.</b> The track is built (colliders, decoration,
    /// pickups) exactly as before, kilometres ahead; this only moves the
    /// pictures, in the vertex shader (<c>TrackReveal.hlsl</c>, read by the
    /// NeonRoad shader and the Road / laser graphs), from per-piece data the
    /// decorator bakes into the mesh (<see cref="TrackDecorator"/>). The CPU
    /// cost is one handful of global floats a frame, at any track density.
    /// <b>The front is one track distance and never moves back</b>, so a
    /// seated piece never comes apart: it is the reference's distance +
    /// <see cref="TrackRevealSettings.startAhead"/>, and below
    /// <see cref="TrackRevealSettings.creepSpeed"/> it creeps on to the
    /// visible road's end so nothing hangs mid-air at a standstill. A jump of
    /// the reference (a respawn down the track, the hyperspace end coming in)
    /// just seats everything it skipped; only a reference that goes BACK (a
    /// restart) starts it over, assembled as far as the eye reaches.
    /// <b>The reference</b> is the ship, or in a race the leader — whoever is
    /// furthest down the track, ghosts and finishers included — so a player
    /// behind the field sees road the leader already raised.
    /// <b>Off</b> (no binding, the asset's switch, a level with oncoming
    /// traffic — its cars drive out of the road ahead, which must already be
    /// there) the globals say so and the whole track stands assembled; a
    /// global never set reads 0 = off, so every scene without this system,
    /// the Scene view and the authoring tools are untouched.
    /// The placements (ramps, gates, orbs…) ride the same front on the CPU:
    /// <see cref="TrackRevealProps"/>, beside it, whose offset this takes off
    /// first thing every frame — hence the early execution order.
    /// Hand-placed under <c>===SYSTEMS===</c>; <c>GameManager</c> binds it.
    /// </summary>
    [DefaultExecutionOrder(-10000)] // undo the props' offset before anything reads them
    public class TrackReveal : MonoBehaviour
    {
        static readonly int OnId = Shader.PropertyToID("_TrackRevealOn");
        static readonly int FrontId = Shader.PropertyToID("_TrackRevealFront");
        static readonly int BandId = Shader.PropertyToID("_TrackRevealBand");
        static readonly int StaggerId = Shader.PropertyToID("_TrackRevealStagger");
        static readonly int DropId = Shader.PropertyToID("_TrackRevealDrop");
        static readonly int TiltId = Shader.PropertyToID("_TrackRevealTilt");
        static readonly int ScaleId = Shader.PropertyToID("_TrackRevealScale");
        static readonly int OvershootId = Shader.PropertyToID("_TrackRevealOvershoot");

        [InlineEditor, Required]
        [SerializeField] TrackRevealSettings settings;

        TrackRevealProps props;
        IStreamFocus focus;
        IRaceStandings standings;
        bool live;

        float front;
        float lastReference = float.NegativeInfinity;

        /// <summary>The reveal front, metres of track (pieces start rising here). Read-only, for debugging.</summary>
        [ShowInInspector, ReadOnly] public float Front => front;
        /// <summary>True while the reveal is running this frame.</summary>
        [ShowInInspector, ReadOnly] public bool Active { get; private set; }

        public TrackRevealSettings Settings => settings;

        /// <summary>
        /// Hands the reveal its run. <paramref name="raceStandings"/> = the
        /// leader is the reference (null = the ship alone); <paramref name="enabled"/>
        /// false keeps the track assembled for this run (a traffic level).
        /// </summary>
        public void Bind(IStreamFocus ship, IRaceStandings raceStandings, bool enabled)
        {
            focus = ship;
            standings = raceStandings;
            live = enabled;
            lastReference = float.NegativeInfinity; // starts assembled on the first frame
        }

        void Awake()
        {
            props = GetComponent<TrackRevealProps>();
            // Alone in a scene (a sandbox), follow whatever streams the track.
            if (focus == null)
            {
                focus = ContractLookup.Find<IStreamFocus>();
                live = focus != null;
            }
        }

        void OnEnable() => SetOff(); // domain reload is off: never inherit last session's globals
        void OnDisable() => SetOff();

        void Update()
        {
            if (props != null) props.Restore();
            if (!live || focus == null || settings == null || !settings.revealEnabled)
            {
                SetOff();
                return;
            }

            float reference = Reference();
            float seated = settings.startAhead + settings.band + settings.stagger; // the front where everything visible is down
            if (reference < lastReference - 1f || float.IsNegativeInfinity(lastReference))
                front = reference + seated; // a fresh run: assembled as far as the eye reaches
            lastReference = reference;

            front = Mathf.Max(front, reference + settings.startAhead);
            if (front < reference + seated)
                front = Mathf.Min(reference + seated, front + settings.creepSpeed * Time.deltaTime);

            Shader.SetGlobalFloat(OnId, 1f);
            Shader.SetGlobalFloat(FrontId, front);
            Shader.SetGlobalFloat(BandId, Mathf.Max(0.001f, settings.band));
            Shader.SetGlobalFloat(StaggerId, settings.stagger);
            Shader.SetGlobalFloat(DropId, settings.drop);
            Shader.SetGlobalFloat(TiltId, settings.tiltDegrees * Mathf.Deg2Rad);
            Shader.SetGlobalFloat(ScaleId, settings.startScale);
            Shader.SetGlobalFloat(OvershootId, settings.overshoot);
            Active = true;
        }

        // The ship, or the furthest racer in a race.
        float Reference()
        {
            float reference = focus.Distance;
            if (standings != null && standings.RaceActive)
                for (int i = 0; i < standings.RivalCount; i++)
                    reference = Mathf.Max(reference, standings.RivalDistance(i));
            return reference;
        }

        /// <summary>
        /// A 0..1 seed per piece (stagger and tilt side), hashed off its track
        /// distance and lateral: the same piece gets the same seed every time
        /// it is built. Shared by the decorator's bake and the props.
        /// </summary>
        public static float Seed(float distance, float lateral)
        {
            uint h = (uint)Mathf.RoundToInt(distance * 10f) * 2654435761u ^ (uint)Mathf.RoundToInt(lateral * 10f) * 2246822519u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        /// <summary>The landing ease, 0..1 → 0..1 (past 1 mid-way with an overshoot) — the shader's, line for line.</summary>
        public static float Ease(float t, float overshoot)
        {
            float u = t - 1f;
            return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
        }

        void SetOff()
        {
            Shader.SetGlobalFloat(OnId, 0f);
            Active = false;
        }
    }
}
