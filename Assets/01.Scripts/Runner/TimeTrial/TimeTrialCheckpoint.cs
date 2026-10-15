using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.TimeTrial
{
    /// <summary>
    /// One Arcade checkpoint as a track records it: where it stands and the
    /// values it overrides on the trial's defaults (TimeTrialPRD D7, D15).
    /// Every override is optional — unset, the <see cref="TimeTrialDefinition"/>'s
    /// value applies — so an auto-laid checkpoint and an authored one read the
    /// same way.
    ///
    /// <b>The record layout</b> is packed into a <see cref="TrackPlacement"/>
    /// of kind <see cref="TrackPlacementKind.Checkpoint"/>, so a saved track
    /// needs no new fields: <c>variant</c> = which overrides are set (bits of
    /// <see cref="Overrides"/>), <c>data</c> = (time award, clean bonus, dirty
    /// penalty, patrol kill bonus), <c>height</c> = life penalty. A checkpoint
    /// spans the whole road, so its lateral and height mean nothing else.
    /// Kill and life overrides cover the STRETCH up to the checkpoint: they
    /// apply while it is the next one ahead.
    /// </summary>
    public struct TimeTrialCheckpoint
    {
        [System.Flags]
        public enum Overrides
        {
            None = 0,
            Award = 1 << 0,
            CleanBonus = 1 << 1,
            DirtyPenalty = 1 << 2,
            KillBonus = 1 << 3,
            LifePenalty = 1 << 4,
        }

        public float distance;
        public Overrides set;
        /// <summary>A fixed award, seconds, in place of the pace-sized one.</summary>
        public float award;
        public float cleanBonus;
        /// <summary>Seconds taken when the checkpoint is crossed after a hit.</summary>
        public float dirtyPenalty;
        public float killBonus;
        public float lifePenalty;

        public bool Has(Overrides flag) => (set & flag) != 0;

        public float CleanBonusOr(TimeTrialDefinition def) => Has(Overrides.CleanBonus) ? cleanBonus : def.cleanCheckpointBonus;
        public float DirtyPenaltyOr(TimeTrialDefinition def) => Has(Overrides.DirtyPenalty) ? dirtyPenalty : def.dirtyCheckpointPenalty;
        public float KillBonusOr(TimeTrialDefinition def) => Has(Overrides.KillBonus) ? killBonus : def.patrolKillBonus;
        public float LifePenaltyOr(TimeTrialDefinition def) => Has(Overrides.LifePenalty) ? lifePenalty : def.lifePenaltySeconds;

        public static TimeTrialCheckpoint At(float distance) => new() { distance = distance };

        public static TimeTrialCheckpoint FromPlacement(TrackPlacement p) => new()
        {
            distance = p.distance,
            set = (Overrides)p.variant,
            award = p.data.x,
            cleanBonus = p.data.y,
            dirtyPenalty = p.data.z,
            killBonus = p.data.w,
            lifePenalty = p.height,
        };

        public TrackPlacement ToPlacement() =>
            new(TrackPlacementKind.Checkpoint, distance, 0f, (int)set,
                new Vector4(award, cleanBonus, dirtyPenalty, killBonus), lifePenalty);

        // ---- auto layout, shared by the run and the editor's Lay Auto Checkpoints ----

        /// <summary>
        /// The auto checkpoints of a decided track (D7): <see cref="TimeTrialDefinition.autoCount"/>
        /// spread evenly from <see cref="TimeTrialDefinition.firstCheckpointMeters"/> to
        /// <paramref name="limit"/> (the final run-up), each slid forward off what the
        /// road claims, else back, else dropped. <paramref name="placements"/> are the
        /// track's records (laser gates and ramps are kept clear of).
        /// </summary>
        public static void LayAuto(TrackManager track, TrackGenerator generator, IReadOnlyList<TrackPlacement> placements,
                                   TimeTrialDefinition def, float limit, List<float> into)
        {
            var blocked = new List<(float start, float end)>();
            CollectBlocked(track, generator, placements, def.featureClearance, limit, blocked);
            float start = Mathf.Min(def.firstCheckpointMeters, limit * 0.5f);
            int count = Mathf.Max(1, def.autoCount);
            float span = limit - start;
            if (span <= 0f) return;
            float step = count > 1 ? span / count : span;
            float previous = 0f;
            for (int i = 0; i < count; i++)
            {
                float spot = start + step * i;
                float slideRoom = step * 0.45f;
                float placed = Clear(blocked, spot, spot + slideRoom, limit, forward: true);
                if (float.IsNaN(placed)) placed = Clear(blocked, spot, Mathf.Max(previous + 1f, spot - slideRoom), limit, forward: false);
                if (float.IsNaN(placed) || placed <= previous + 1f) continue;
                into.Add(placed);
                previous = placed;
            }
        }

        // The first free spot from `from` toward `to` (either direction), or NaN.
        static float Clear(List<(float start, float end)> blocked, float from, float to, float limit, bool forward)
        {
            float spot = from;
            for (int guard = 0; guard < 64; guard++)
            {
                if (forward ? spot > to : spot < to) return float.NaN;
                if (spot <= 0f || spot >= limit) return float.NaN;
                int hit = BlockedAt(blocked, spot);
                if (hit < 0) return spot;
                spot = forward ? blocked[hit].end + 1f : blocked[hit].start - 1f;
            }
            return float.NaN;
        }

        static int BlockedAt(List<(float start, float end)> blocked, float distance)
        {
            for (int i = 0; i < blocked.Count; i++)
                if (distance >= blocked[i].start && distance <= blocked[i].end) return i;
            return -1;
        }

        // Everything a checkpoint keeps off, widened by the clearance: the
        // generator's keep-outs (features, landings, the run-up) and sections,
        // read by sampling its own predicate, plus every laser gate and ramp.
        static void CollectBlocked(TrackManager track, TrackGenerator generator, IReadOnlyList<TrackPlacement> placements,
                                   float clearance, float limit, List<(float start, float end)> blocked)
        {
            const float Stride = 25f;
            float runStart = -1f;
            for (float d = 0f; d <= limit + Stride; d += Stride)
            {
                bool clear = TrackHazards.IsEncounterGroundClear(track, generator, d, d) || d >= limit;
                if (!clear && runStart < 0f) runStart = d;
                else if (clear && runStart >= 0f)
                {
                    blocked.Add((runStart - Stride - clearance, d + clearance));
                    runStart = -1f;
                }
            }
            if (placements != null)
                foreach (var placement in placements)
                    if (placement.kind == TrackPlacementKind.LaserGate || placement.kind == TrackPlacementKind.Ramp)
                        blocked.Add((placement.distance - clearance, placement.distance + clearance));
        }
    }
}
