using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// Brake pads: large flat slabs on the road that take speed (and, with the
    /// hull on, GameSettings.brakePadDamage) — to be dodged. Retired from the
    /// shipped spawn set now that laser gates are the slow-down hazard; the
    /// asset and the PowerUp_Brake prefab are kept so dropping this spawner
    /// back into a <see cref="TrackSpawnSet"/> brings them back as they were.
    /// </summary>
    [CreateAssetMenu(fileName = "Spawner_BrakePads", menuName = "FiniteRunner/Spawners/Brake Pads")]
    public class BrakePadSpawner : TrackSpawner
    {
        [Tooltip("The brake pad: its definition carries the (negative) speed delta; the multiplier is unused.")]
        [SerializeField] PadSpawnEntry pad = new() { name = "Brake", color = new Color(1f, 0.25f, 0.2f) };

        [Tooltip("Material of a code-built brake slab (a prefab keeps its own).")]
        [SerializeField] Material material;

        [Tooltip("Optional sign model placed at each pad, tinted with the pad's material.")]
        [SerializeField] GameObject signPrefab;

        [SerializeField, Min(0.1f)] float signScale = 8f;

        protected override float Step(TrackSpawnContext ctx, float distance, float limit)
        {
            float claimEnd = ctx.ClaimEnd(distance);
            if (claimEnd >= 0f) return claimEnd;
            if (pad.definition == null || ctx.NearPickup(distance)) return -1f;

            float lateral = ctx.RandomLateral(ref Rng, distance, ctx.PadMargin(pad.definition, pad.swayAmplitude));
            GameObject go = ctx.CreatePad(distance, lateral, pad, material);

            // Orbs are their own landmark; the gate-style sign only suits flat pads.
            if (!pad.definition.floatingOrb && signPrefab != null)
            {
                ctx.Track.GetPoseAtDistance(distance, lateral, out _, out Quaternion rot);
                var sign = Instantiate(signPrefab, go.transform.position, rot * Quaternion.Euler(0f, 90f, 0f), ctx.Parent);
                sign.name = go.name + "_Sign";
                sign.transform.localScale = Vector3.one * signScale;
                if (material != null) TrackDecorator.OverrideMaterials(sign, material);
                ctx.Register(distance, sign);
            }
            return -1f;
        }
    }
}
