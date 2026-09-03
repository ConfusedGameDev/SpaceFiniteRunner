using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track.Features
{
    /// <summary>
    /// A spawned jump ramp: where it sits on the track (start distance,
    /// length, lateral centre, half width) and what it does (its definition
    /// clone and the takeoff boost). <b>Detection is analytic, not physics</b>:
    /// the ship knows its distance and lateral exactly, so
    /// <see cref="Ship.ShipMotor"/> reads the live ramps off
    /// <see cref="Active"/> every frame and decides entry, side hit and
    /// takeoff from the numbers — a trigger box would be tunnelled by a ship
    /// moving 20 m per physics step. The visual (prefab or code-built slab
    /// and rails) is only ever a picture. Registered while enabled in play
    /// mode; the generator culls it with the rest of the spawned objects.
    /// </summary>
    public class JumpRamp : MonoBehaviour
    {
        /// <summary>Every ramp currently spawned, for the motor's per-frame scan.</summary>
        public static readonly List<JumpRamp> Active = new();

        public JumpDefinition Definition { get; private set; }
        public float StartDistance { get; private set; }
        public float Lateral { get; private set; }
        public float HalfWidth { get; private set; }

        /// <summary>Raw takeoff boost (m/s, before the ship's weight scaling).</summary>
        public float Boost { get; private set; }

        public float Length => Definition != null ? Definition.length : 0f;
        public float EndDistance => StartDistance + Length;

        public void Configure(JumpDefinition definition, float startDistance, float lateral, float halfWidth, float boost)
        {
            Definition = definition;
            StartDistance = startDistance;
            Lateral = lateral;
            HalfWidth = halfWidth;
            Boost = boost;
        }

        /// <summary>True while <paramref name="distance"/> lies on the run-up.</summary>
        public bool Spans(float distance) => distance >= StartDistance && distance < EndDistance;

        /// <summary>0 at the foot of the ramp, 1 at the lip.</summary>
        public float Progress(float distance) => Length > 0f ? Mathf.Clamp01((distance - StartDistance) / Length) : 1f;

        /// <summary>Height of the slope above the flight line at <paramref name="distance"/>.</summary>
        public float HeightAt(float distance) => Definition != null ? Progress(distance) * Definition.LipHeight : 0f;

        void OnEnable()
        {
            if (Application.isPlaying && !Active.Contains(this)) Active.Add(this);
        }

        void OnDisable() => Active.Remove(this);
    }
}
