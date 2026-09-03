using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.SaveData
{
    /// <summary>The letter a mission's payout earns. Order is the save format — append only (the profile stores the letter, never the number).</summary>
    public enum Rank { D = 0, C = 1, B = 2, A = 3, S = 4 }

    /// <summary>
    /// Money thresholds that turn a mission total into a <see cref="Rank"/>:
    /// at or above <c>sThreshold</c> is an S, and so on down to D below
    /// <c>cThreshold</c>. Lives here, in the leaf assembly, because both the
    /// city level asset (which authors it) and the runner's Mission Complete
    /// panel (which applies it, after the profile carried it across the
    /// scene handoff) need the same type. Thresholds are compared from the
    /// top down, so a table authored out of order still resolves.
    /// </summary>
    [Serializable]
    public class RankTable
    {
        [Tooltip("Mission total at or above this earns an S.")]
        [PropertyRange(0, 1000000), SuffixLabel("$", true)]
        public long sThreshold = 10000;

        [Tooltip("Mission total at or above this earns an A.")]
        [PropertyRange(0, 1000000), SuffixLabel("$", true)]
        public long aThreshold = 5000;

        [Tooltip("Mission total at or above this earns a B.")]
        [PropertyRange(0, 1000000), SuffixLabel("$", true)]
        public long bThreshold = 2500;

        [Tooltip("Mission total at or above this earns a C; anything below is a D.")]
        [PropertyRange(0, 1000000), SuffixLabel("$", true)]
        public long cThreshold = 1000;

        /// <summary>False for an all-zero table (a profile that never carried one) — callers fall back to their own.</summary>
        public bool IsSet => sThreshold > 0 || aThreshold > 0 || bThreshold > 0 || cThreshold > 0;

        /// <summary>The rank <paramref name="money"/> earns under this table.</summary>
        public Rank RankFor(long money)
        {
            if (money >= sThreshold) return Rank.S;
            if (money >= aThreshold) return Rank.A;
            if (money >= bThreshold) return Rank.B;
            if (money >= cThreshold) return Rank.C;
            return Rank.D;
        }

        /// <summary>The letter as the profile and the panel print it.</summary>
        public static string Letter(Rank rank) => rank.ToString();

        public RankTable Clone() => new()
        {
            sThreshold = sThreshold, aThreshold = aThreshold, bThreshold = bThreshold, cThreshold = cThreshold
        };
    }

    /// <summary>One completed objective as the Mission Complete panel lists it: its summary line and the money it paid.</summary>
    [Serializable]
    public class ObjectiveResult
    {
        public string label = "";
        public long reward;
        public bool done;

        public ObjectiveResult() { }
        public ObjectiveResult(string label, long reward, bool done) { this.label = label ?? ""; this.reward = reward; this.done = done; }
    }

    /// <summary>One accepted optional challenge as the panel lists it: its summary, the multiplier it offered and whether it landed.</summary>
    [Serializable]
    public class ChallengeResult
    {
        public string label = "";
        public int multiplier = 1;
        public bool done;

        public ChallengeResult() { }
        public ChallengeResult(string label, int multiplier, bool done) { this.label = label ?? ""; this.multiplier = multiplier; this.done = done; }
    }

    /// <summary>
    /// The one payout formula, shared by the city's brief (the offer), its
    /// manager (the running payout) and the runner's Mission Complete panel:
    /// (flat bonus + every objective's reward) × the multiplier of every
    /// completed challenge. Multipliers below 1 count as 1.
    /// </summary>
    public static class MissionPayout
    {
        public static long Total(long baseReward, IEnumerable<ObjectiveResult> objectives, IEnumerable<ChallengeResult> challenges)
        {
            long sum = baseReward;
            if (objectives != null)
                foreach (var o in objectives) if (o != null && o.done) sum += o.reward;
            long total = sum;
            if (challenges != null)
                foreach (var c in challenges) if (c != null && c.done) total *= Math.Max(1, c.multiplier);
            return total;
        }
    }
}
