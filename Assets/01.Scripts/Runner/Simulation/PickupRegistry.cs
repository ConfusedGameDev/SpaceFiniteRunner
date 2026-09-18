using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Simulation
{
    /// <summary>
    /// Something lying on the track a <see cref="TrackBody"/> can pick up,
    /// described in TRACK SPACE — the body's own coordinates — so detection is
    /// a comparison of numbers and never a physics overlap.
    /// </summary>
    public interface ITrackPickup
    {
        /// <summary>Metres from the track start, at the pickup's centre.</summary>
        float TrackDistance { get; }

        /// <summary>Metres across the track right now (a swaying orb moves it).</summary>
        float TrackLateral { get; }

        /// <summary>Height of its centre above the flight line (0 on the ground lane).</summary>
        float TrackHeight { get; }

        /// <summary>Half size of its volume: x across the track, y up, z along it.</summary>
        Vector3 TrackHalfExtents { get; }

        /// <summary>False once taken (or switched off): the sweep skips it.</summary>
        bool Available { get; }
    }

    /// <summary>
    /// Every pad, orb and coin currently on the track, registered with its
    /// track-space position when the generator places it and dropped when it
    /// is culled, consumed or disabled. <see cref="TrackBody"/> sweeps it each
    /// step over the distance it just covered, which is what makes pickups
    /// tunnel-proof at any speed (a ship covers ~36 m per physics step at
    /// Light Speed; a trigger volume had to be 20 m long to be caught at all)
    /// and lets the patrol see what lies ahead of it. A plain list scanned
    /// linearly: a streamed track holds a hundred or so live pickups.
    /// Static, and domain reload is off — so it is emptied on boot.
    /// </summary>
    public static class PickupRegistry
    {
        static readonly List<ITrackPickup> pickups = new();

        /// <summary>Everything registered, for look-ahead readers (the patrol's driver). Do not modify.</summary>
        public static IReadOnlyList<ITrackPickup> All => pickups;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Boot() => pickups.Clear();

        public static void Register(ITrackPickup pickup)
        {
            if (pickup != null && !pickups.Contains(pickup)) pickups.Add(pickup);
        }

        public static void Unregister(ITrackPickup pickup) => pickups.Remove(pickup);

        /// <summary>
        /// Collects into <paramref name="results"/> every available pickup whose
        /// volume the body touched while covering <paramref name="fromDistance"/>
        /// → <paramref name="toDistance"/> at the given lateral and height, with
        /// the body's own half width and half height added on.
        /// <paramref name="lateralWrap"/> &gt; 0 compares laterals round a full
        /// tube of that circumference (the ship's lateral grows a
        /// circumference per turn there; an orb's does not).
        /// </summary>
        public static void Sweep(float fromDistance, float toDistance, float lateral, float height,
                                 float reachLateral, float reachHeight, float lateralWrap,
                                 List<ITrackPickup> results)
        {
            for (int i = 0; i < pickups.Count; i++)
            {
                ITrackPickup pickup = pickups[i];
                if (pickup == null || !pickup.Available) continue;

                Vector3 half = pickup.TrackHalfExtents;
                float d = pickup.TrackDistance;
                if (d + half.z < fromDistance || d - half.z > toDistance) continue;

                float across = pickup.TrackLateral - lateral;
                if (lateralWrap > 0f) across = Mathf.Repeat(across + lateralWrap * 0.5f, lateralWrap) - lateralWrap * 0.5f;
                if (Mathf.Abs(across) > half.x + reachLateral) continue;
                if (Mathf.Abs(pickup.TrackHeight - height) > half.y + reachHeight) continue;

                results.Add(pickup);
            }
        }
    }
}
