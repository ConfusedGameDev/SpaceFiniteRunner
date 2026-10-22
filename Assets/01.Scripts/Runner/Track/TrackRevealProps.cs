using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.Contracts;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// The track reveal for the PLACEMENTS — ramps, loop portals, laser gates,
    /// orbs, coins, end ramps, custom prefabs: the road's pieces rise in the
    /// shader (<c>TrackReveal.hlsl</c>), but a prop is many materials and some
    /// pose themselves every frame (the orb's bob, the gate's emitters), so
    /// this moves the prop's ROOT on the CPU with the same motion, timed by
    /// the same front, so a prop lands with the road under it. Only the props
    /// inside the band cost anything; a seated one is dropped from the list.
    /// <b>The offset never sticks</b>: it is put on as the very last
    /// <c>LateUpdate</c> of the frame and taken off again by
    /// <see cref="TrackReveal"/> at the very start of the next frame's
    /// <c>Update</c> (<see cref="Restore"/>), so every prop script still reads
    /// and writes its true pose. Nothing in the band is reachable by a body —
    /// the ship and the field are behind its seated edge — so the pickups'
    /// colliders being displaced for the fixed step in between is harmless.
    /// <b>Builds are heard statically</b> (<see cref="TrackGenerator.PlacementBuilt"/>,
    /// subscribed before any scene loads): the generator builds its first
    /// stretch in its own Awake, which may run before this one's.
    /// <b>Moving things ride it too</b> (<see cref="Follow"/>: the oncoming
    /// cars, handed over each time one is placed): timed by where they are NOW,
    /// with no stagger, so a car spawned out in the unbuilt road stays hidden
    /// and rises into its lane as it drives into the assembling road — the
    /// player never sees one driving out of the void. A pooled car placed
    /// again replaces its old entry; a parked one is dropped.
    /// Sits beside <see cref="TrackReveal"/> on <c>PF_TrackReveal</c>.
    /// </summary>
    [DefaultExecutionOrder(10000)] // LateUpdate after every prop has posed itself
    public class TrackRevealProps : MonoBehaviour
    {
        struct Prop
        {
            public Transform root;
            public float distance;   // the record's distance
            public ITrackPositioned mover; // a moving thing: its live distance instead, no stagger
            public float seed;       // 0..1: stagger and tilt side
            public bool applied;     // offset on this frame
            public Vector3 position; // the true pose under the offset
            public Quaternion rotation;
            public Vector3 scale;
            public int bornFrame;    // the frame it was built: the front may still be last run's (a retry)
        }

        // A collapsed prop is not scaled to 0: a zero scale trips collider warnings.
        const float CollapsedScale = 1e-4f;
        // A scene with no reveal still hears builds: past this many, the destroyed ones are swept.
        const int SweepAbove = 4096;

        static readonly List<Prop> props = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() // domain reload is off
        {
            props.Clear();
            TrackGenerator.PlacementBuilt -= OnPlacementBuilt;
            TrackGenerator.PlacementBuilt += OnPlacementBuilt;
        }

        static void OnPlacementBuilt(GameObject go, float distance, float lateral)
        {
            if (go == null) return;
            if (props.Count > SweepAbove) props.RemoveAll(p => p.root == null);
            props.Add(new Prop { root = go.transform, distance = distance, seed = TrackReveal.Seed(distance, lateral), bornFrame = Time.frameCount });
        }

        /// <summary>
        /// Hands the reveal something that moves along the track (an oncoming
        /// car, each time it is placed): hidden while its spot is not built,
        /// rising in as it drives into the assembling road. Placing a pooled
        /// object again replaces its entry.
        /// </summary>
        public static void Follow(Transform root, ITrackPositioned mover, float lateral)
        {
            if (root == null || mover == null) return;
            for (int i = props.Count - 1; i >= 0; i--)
                if (props[i].root == root) props.RemoveAt(i);
            props.Add(new Prop { root = root, mover = mover, seed = TrackReveal.Seed(mover.Distance, lateral), bornFrame = Time.frameCount });
        }

        TrackReveal reveal;

        /// <summary>Props not yet seated (debugging).</summary>
        public int Pending => props.Count;

        void Awake() => reveal = GetComponent<TrackReveal>();

        void OnDisable() => Restore();

        /// <summary>Takes this frame's offset off every prop (the reveal calls it first thing each frame).</summary>
        public void Restore()
        {
            for (int i = props.Count - 1; i >= 0; i--)
            {
                Prop p = props[i];
                if (p.root == null) { props.RemoveAt(i); continue; }
                if (!p.applied) continue;
                p.root.SetPositionAndRotation(p.position, p.rotation);
                p.root.localScale = p.scale;
                p.applied = false;
                props[i] = p;
            }
        }

        void LateUpdate()
        {
            if (reveal == null || !reveal.Active) return; // off (a traffic level, the switch): the props stand as built

            TrackRevealSettings s = reveal.Settings;
            float band = Mathf.Max(0.001f, s.band);
            for (int i = props.Count - 1; i >= 0; i--)
            {
                Prop p = props[i];
                if (p.root == null || !p.root.gameObject.activeInHierarchy) { props.RemoveAt(i); continue; } // gone, parked or used up

                float at = p.mover != null ? p.mover.Distance : p.distance + p.seed * s.stagger;
                float t = Mathf.Clamp01((reveal.Front - at) / band);
                if (t >= 1f)
                {
                    // A retry rebuilds the track mid-frame, after the reveal set
                    // this frame's front off LAST run's distance: judged by it, the
                    // new props would read seated and never rise. Wait a frame.
                    if (p.bornFrame != Time.frameCount) props.RemoveAt(i); // seated for good: the front never goes back
                    continue;
                }

                Transform root = p.root;
                p.position = root.position;
                p.rotation = root.rotation;
                p.scale = root.localScale;
                p.applied = true;
                props[i] = p;

                Vector3 up = p.rotation * Vector3.up;
                if (t <= 0f)
                {
                    // Not started: collapsed out of sight under its spot.
                    root.position = p.position - up * s.drop;
                    root.localScale = p.scale * CollapsedScale;
                    continue;
                }

                // The shader's motion (TrackReveal.hlsl), on the root.
                float left = 1f - TrackReveal.Ease(t, s.overshoot);
                float side = p.seed < 0.5f ? -1f : 1f;
                Quaternion tilt = Quaternion.AngleAxis(s.tiltDegrees * left * side, p.rotation * Vector3.right);
                root.SetPositionAndRotation(p.position - up * (s.drop * left), tilt * p.rotation);
                root.localScale = p.scale * Mathf.LerpUnclamped(1f, s.startScale, Mathf.Clamp01(left));
            }
        }
    }
}
