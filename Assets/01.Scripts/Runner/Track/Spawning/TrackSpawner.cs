using Sirenix.OdinInspector;
using Unity.Mathematics;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// When a spawner runs in each streaming pass. Ground claimers (laser
    /// gates) go first and claim their stretch, so pickups keep off it.
    /// Serialized nowhere today, but append-only like every enum.
    /// </summary>
    public enum SpawnPhase { ClaimsGround, Pickup }

    /// <summary>
    /// One kind of thing the <see cref="TrackGenerator"/> streams onto the
    /// track — boost orbs, repair orbs, laser gates, brake pads, and whatever
    /// comes next. Each kind is an asset listed in the generator's
    /// <see cref="TrackSpawnSet"/>: add a kind by making an asset and dropping
    /// it into the set, remove it by taking it out (or unticking it).
    /// <para>
    /// Every spawner walks the track on a cursor and a random stream of its
    /// own (seeded off the layout's seed and its name), so adding or removing
    /// one never moves any other. Each step rolls <see cref="chance"/>, asks
    /// the subclass to <see cref="Step"/> — place one here, or say where the
    /// ground is free again — then moves on by <see cref="spacing"/> divided
    /// by the debug <see cref="Density"/>.
    /// </para>
    /// In play the generator runs a runtime CLONE of each asset (the debug
    /// menu edits the clone); the cursor, stream and density are runtime-only.
    /// </summary>
    public abstract class TrackSpawner : ScriptableObject
    {
        [Tooltip("Off = listed but never spawned.")]
        [SerializeField] bool active = true;

        [Tooltip("Name shown in the debug menu and used to seed this spawner's random stream (so renaming reshuffles its layout) and to match saved debug values.")]
        public string displayName = "Spawner";

        [Tooltip("Tint of this spawner's debug rows.")]
        public Color color = Color.white;

        [Tooltip("Metres of track between one spawn step and the next (min, max).")]
        [MinMaxSlider(10f, 5000f, true), SuffixLabel("m", true)]
        public Vector2 spacing = new(400f, 700f);

        [Tooltip("The first step lands between this distance and this plus the minimum spacing, so the launch stays clean.")]
        [PropertyRange(0f, 5000f), SuffixLabel("m", true)]
        public float startDistance = 120f;

        [Tooltip("Chance that a step spawns anything at all. 1 = every step.")]
        [PropertyRange(0f, 1f)]
        public float chance = 1f;

        /// <summary>Debug multiplier on the density (the debug menu's DENSITY row): 0 = none, 1 = the authored spacing, 2 = twice as many. Affects streaming immediately.</summary>
        public float Density { get => density; set => density = Mathf.Max(0f, value); }
        [System.NonSerialized] float density = 1f;

        /// <summary>This spawner's own random stream. Subclasses draw from it only, so a seed's layout of every other spawner never depends on this one.</summary>
        protected Unity.Mathematics.Random Rng;

        [System.NonSerialized] float cursor;

        /// <summary>The authored on/off toggle.</summary>
        public bool Active => active;

        /// <summary>When this spawner runs in a streaming pass.</summary>
        public virtual SpawnPhase Phase => SpawnPhase.Pickup;

        /// <summary>False skips this spawner (its cursor still walks on, so nothing lands behind the ship when it turns back on).</summary>
        public virtual bool IsActive(TrackSpawnContext ctx) => active;

        /// <summary>Resets the stream and the cursor for a fresh track. Called by every Generate.</summary>
        public void Begin(TrackSpawnContext ctx)
        {
            Rng = new Unity.Mathematics.Random(math.hash(new uint2(ctx.LayoutSeed, NameHash(displayName))) | 1u);
            cursor = startDistance + Rng.NextFloat(0f, spacing.x);
            OnBegin(ctx);
        }

        /// <summary>Places every step up to <paramref name="limit"/> (the settled track); the cursor resumes there next pass.</summary>
        public void PlaceUpTo(TrackSpawnContext ctx, float limit)
        {
            if (!IsActive(ctx) || density <= 0f)
            {
                cursor = Mathf.Max(cursor, limit);
                return;
            }

            while (cursor < limit)
            {
                if (chance < 1f && Rng.NextFloat() >= chance)
                {
                    Advance();
                    continue;
                }

                float resume = Step(ctx, cursor, limit);
                if (float.IsPositiveInfinity(resume)) { cursor = float.MaxValue; return; } // nothing ever fits again (the end zone)
                if (resume >= 0f) { cursor = Mathf.Max(resume, cursor + 1f); continue; }
                Advance();
            }
        }

        void Advance() => cursor += Rng.NextFloat(spacing.x, spacing.y) / density;

        /// <summary>Per-run setup after the stream is seeded: clone nested definitions, reset caches.</summary>
        protected virtual void OnBegin(TrackSpawnContext ctx) { }

        /// <summary>
        /// One step at <paramref name="distance"/>. Return -1 when the step is
        /// done (placed, or nothing fitted) and the cursor moves on by the
        /// spacing; otherwise the distance to try again from — the end of the
        /// claimed ground in the way — or +infinity for never again.
        /// </summary>
        protected abstract float Step(TrackSpawnContext ctx, float distance, float limit);

        /// <summary>Frees what the clone made for itself (nested definition clones, materials). Called before the clone is destroyed.</summary>
        public virtual void Cleanup() { }

        // FNV-1a: stable across runs and platforms (string.GetHashCode is not guaranteed to be).
        static uint NameHash(string name)
        {
            uint hash = 2166136261u;
            if (name != null)
                foreach (char c in name) hash = (hash ^ c) * 16777619u;
            return hash;
        }

        /// <summary>Destroys a runtime object in play, immediately in the editor.</summary>
        protected static void DestroyRuntime(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
