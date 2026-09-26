using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Boost orbs: every step draws one tier (Green / Blue / Purple) by
    /// probability and floats it on the flight line, clear of claimed ground.
    /// Each tier is a <see cref="PadSpawnEntry"/>; its boost is
    /// GameSettings.powerUpSpeedBoost × the tier's multiplier.
    /// </summary>
    [CreateAssetMenu(fileName = "Spawner_SpeedOrbs", menuName = "FiniteRunner/Spawners/Speed Orbs")]
    public class SpeedOrbSpawner : TrackSpawner
    {
        [Tooltip("One entry per orb tier. Every step draws one by probability; the sliders auto-rebalance to always total 100%.")]
        [Sirenix.OdinInspector.OnValueChanged(nameof(NormalizeProbabilities), true)]
        [SerializeField]
        PadSpawnEntry[] tiers =
        {
            new() { name = "Green",  probability = 74f, multiplier = 1f,   color = new Color(0.1f, 1f, 0.3f) },
            new() { name = "Blue",   probability = 22f, multiplier = 2.5f, color = new Color(0.25f, 0.55f, 1f), swayAmplitude = 4f, swayFrequency = 0.45f },
            new() { name = "Purple", probability = 4f,  multiplier = 10f,  color = new Color(0.75f, 0.3f, 1f),  swayAmplitude = 8f, swayFrequency = 0.8f },
        };

        [System.NonSerialized] float[] lastProbabilities;
        [System.NonSerialized] Dictionary<PadSpawnEntry, Material> tierMaterials;

        /// <summary>The orb tiers (the runtime clone's in play — what the debug menu edits).</summary>
        public PadSpawnEntry[] Tiers => tiers;

        protected override float Step(TrackSpawnContext ctx, float distance, float limit)
        {
            float claimEnd = ctx.ClaimEnd(distance);
            if (claimEnd >= 0f) return claimEnd;

            var tier = WeightedTable.Pick(tiers, Rng.NextFloat()) as PadSpawnEntry;
            if (tier == null || tier.definition == null) return -1f;

            float lateral = ctx.RandomLateral(ref Rng, distance, ctx.PadMargin(tier.definition, tier.swayAmplitude));
            ctx.CreatePad(distance, lateral, tier, TierMaterial(ctx, tier));
            return -1f;
        }

        // One recolored boost-material instance per tier. Play mode only —
        // edit-mode previews would leak the instances into the scene.
        Material TierMaterial(TrackSpawnContext ctx, PadSpawnEntry tier)
        {
            if (!Application.isPlaying || ctx.BoostMaterial == null) return ctx.BoostMaterial;
            tierMaterials ??= new Dictionary<PadSpawnEntry, Material>();
            if (!tierMaterials.TryGetValue(tier, out var mat))
            {
                mat = TrackSpawnContext.TintedCopy(ctx.BoostMaterial, tier.color);
                tierMaterials.Add(tier, mat);
            }
            return mat;
        }

        public override void Cleanup()
        {
            if (tierMaterials == null) return;
            foreach (var mat in tierMaterials.Values) DestroyRuntime(mat);
            tierMaterials.Clear();
        }

        void NormalizeProbabilities() => WeightedTable.Normalize(tiers, ref lastProbabilities);

        void OnValidate() => NormalizeProbabilities();
    }
}
