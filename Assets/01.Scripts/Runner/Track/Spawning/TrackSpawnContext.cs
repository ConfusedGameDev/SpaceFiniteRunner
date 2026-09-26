using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.Track
{
    /// <summary>
    /// What a <see cref="TrackSpawner"/> may touch of the generator: the track,
    /// the shared ground bookkeeping (claimed stretches, where pickups landed,
    /// the feature keep-outs) and the cull list. The generator owns the lists;
    /// this hands them out by reference, so every spawner plays by the same
    /// rules — a ground claimer claims, a pickup keeps off claimed ground and
    /// off other pickups, and everything is culled by where it ENDS.
    /// </summary>
    public sealed class TrackSpawnContext
    {
        readonly List<(float distance, GameObject go)> spawned;
        readonly List<(float start, float end)> claims;
        readonly List<float> pickupDistances;
        readonly List<(float start, float end)> featureKeepOuts;

        public TrackManager Track { get; }
        public GameManager GameManager { get; }
        /// <summary>Parent of everything spawned (null = the scene root).</summary>
        public Transform Parent { get; }
        /// <summary>Pad footprint (width, thickness, length): the unit orbs and pads scale from, and how far pickups keep apart.</summary>
        public Vector3 PadSize { get; }
        /// <summary>Base material code-built pickups tint; null in a scene that wires none.</summary>
        public Material BoostMaterial { get; }
        /// <summary>The layout's seed state: every spawner hashes its own stream off it.</summary>
        public uint LayoutSeed { get; }

        public TrackSpawnContext(TrackManager track, GameManager gameManager, Transform parent,
                                 Vector3 padSize, Material boostMaterial, uint layoutSeed,
                                 List<(float, GameObject)> spawned, List<(float, float)> claims,
                                 List<float> pickupDistances, List<(float, float)> featureKeepOuts)
        {
            Track = track;
            GameManager = gameManager;
            Parent = parent;
            PadSize = padSize;
            BoostMaterial = boostMaterial;
            LayoutSeed = layoutSeed;
            this.spawned = spawned;
            this.claims = claims;
            this.pickupDistances = pickupDistances;
            this.featureKeepOuts = featureKeepOuts;
        }

        /// <summary>Height of the air lane above the flight line, from GameSettings (30 m without a manager).</summary>
        public float AirLaneHeight => GameManager != null ? GameManager.AirLaneHeight : 30f;

        /// <summary>GameSettings.powerUpSpeedBoost — the base every boost tier multiplies (15 without a manager).</summary>
        public float BaseBoost => GameManager != null ? GameManager.PowerUpSpeedBoost : 15f;

        /// <summary>End of the claimed stretch covering <paramref name="distance"/> (widened by a pad length), or -1 when it is free.</summary>
        public float ClaimEnd(float distance)
        {
            foreach (var c in claims)
                if (distance >= c.start - PadSize.z && distance < c.end + PadSize.z) return c.end + PadSize.z;
            return -1f;
        }

        /// <summary>Claims a stretch of track: no pickup or coin lands on it.</summary>
        public void Claim(float start, float end) => claims.Add((start, end));

        /// <summary>True within a pad length of any pickup already placed.</summary>
        public bool NearPickup(float distance)
        {
            foreach (float d in pickupDistances)
                if (Mathf.Abs(d - distance) < PadSize.z) return true;
            return false;
        }

        /// <summary>Records a pickup's distance so later pickups and coins keep off it.</summary>
        public void RecordPickup(float distance) => pickupDistances.Add(distance);

        /// <summary>Hands a spawned object to the cull: it goes once the ship is <see cref="TrackGenerator"/>'s behind distance past <paramref name="endDistance"/>.</summary>
        public void Register(float endDistance, GameObject go) => spawned.Add((endDistance, go));

        /// <summary>
        /// Where something reaching <paramref name="reach"/> either side of
        /// <paramref name="distance"/> could go instead, or -1 when the spot is
        /// free: clear of every feature and what lies ahead of it (a ramp's
        /// landing zone), of any section, of the final run-up (+infinity —
        /// nothing fits after it) and, unless allowed, of flat sweeps and open
        /// edges. Every one of those is registered while its knot is still in
        /// the settle margin, so a reach shorter than that margin never lands
        /// something a feature is later decided on top of.
        /// </summary>
        public float KeepOutUntil(float distance, float reach, bool allowFlatSweeps, bool allowOpenEdges)
        {
            float start = distance - reach, end = distance + reach;

            foreach (var keepOut in featureKeepOuts)
                if (end > keepOut.start && start < keepOut.end)
                    return float.IsPositiveInfinity(keepOut.end) ? keepOut.end : keepOut.end + reach;

            if (!allowFlatSweeps)
            {
                TrackManager.FlatSweep sweep = Track.FlatSweepWithin(start, end - start);
                if (sweep != null) return sweep.End + reach;
            }

            // A loop or a tube is a keep-out already (belt and braces); an open edge is not.
            for (float d = start; d <= end; d += 25f)
                if (Track.SectionAt(d) != null
                    || (!allowOpenEdges && (Track.IsEdgeOpen(d, -1) || Track.IsEdgeOpen(d, 1))))
                    return d + reach + 25f;
            return -1f;
        }

        /// <summary>
        /// A lateral inside the lane at <paramref name="distance"/> keeping
        /// <paramref name="margin"/> off both edges — on a tube that is the arc
        /// round the pipe. The middle when the lane is narrower than that.
        /// </summary>
        public float RandomLateral(ref Unity.Mathematics.Random rng, float distance, float margin)
        {
            Track.GetLateralBand(distance, out float bandMin, out float bandMax);
            float lo = bandMin + margin, hi = bandMax - margin;
            return hi > lo ? rng.NextFloat(lo, hi) : (bandMin + bandMax) * 0.5f;
        }

        /// <summary>Half a pad's scaled width plus a margin — and the full sway arc of a moving orb — so the whole pad stays in the lane.</summary>
        public float PadMargin(PadDefinition def, float sway = 0f)
        {
            float width = PadSize.x * (def != null ? def.sizeMultiplier : 1f);
            return width * 0.5f + 2f + sway;
        }

        /// <summary>
        /// One <see cref="SpeedPad"/> from a spawn entry: the entry's prefab
        /// (colliders forced to triggers, one added if it has none) or a
        /// code-built orb / slab in <paramref name="material"/>. Boosts get
        /// the base boost × the entry's multiplier; a brake keeps its
        /// definition's own delta. Registered for the cull and as a pickup.
        /// </summary>
        public GameObject CreatePad(float distance, float lateral, PadSpawnEntry entry, Material material)
        {
            PadDefinition def = entry.definition;
            Track.GetPoseAtDistance(distance, lateral, out Vector3 pos, out Quaternion rot);
            // Orbs sit on the flight line; flat pads sink to road level. The
            // air lane rides the track's up so it stays overhead on a roll.
            Vector3 padPos = def.floatingOrb ? pos : pos + rot * new Vector3(0f, -0.9f, 0f);
            if (entry.lane == PadLane.Air) padPos += rot * (Vector3.up * AirLaneHeight);
            GameObject pad;

            if (entry.prefab != null)
            {
                // Designer-authored look: the prefab keeps its own materials,
                // only the definition's size multiplier scales it.
                pad = Object.Instantiate(entry.prefab, padPos, rot, Parent);
                pad.transform.localScale *= def.sizeMultiplier;
                if (!ForceTriggers(pad))
                {
                    if (def.floatingOrb)
                    {
                        var sphere = pad.AddComponent<SphereCollider>();
                        sphere.isTrigger = true;
                        sphere.radius = PadSize.x * 0.5f;
                    }
                    else
                    {
                        var box = pad.AddComponent<BoxCollider>();
                        box.isTrigger = true;
                        box.size = PadSize;
                    }
                }
            }
            else
            {
                pad = GameObject.CreatePrimitive(def.floatingOrb ? PrimitiveType.Sphere : PrimitiveType.Cube);
                pad.transform.SetParent(Parent, false);
                pad.transform.SetPositionAndRotation(padPos, rot);
                // Orb: small, on the flight line, must be aimed for. Slab: the whole pad footprint.
                pad.transform.localScale = def.floatingOrb ? Vector3.one * (PadSize.x * def.sizeMultiplier) : PadSize * def.sizeMultiplier;
                pad.GetComponent<Collider>().isTrigger = true;
                if (material != null) pad.GetComponent<Renderer>().sharedMaterial = material;
            }

            if (def.floatingOrb && Application.isPlaying)
                pad.AddComponent<OrbHover>().Configure(entry.swayAmplitude, entry.swayFrequency);

            pad.name = $"{entry.name}{def.displayName}Pad_{distance:00000}";
            // The prefabs carry a SpeedPad of their own (so they work dropped into any level); a second one would take twice.
            var speedPad = pad.GetComponent<SpeedPad>();
            if (speedPad == null) speedPad = pad.AddComponent<SpeedPad>();
            // Its pickup volume, in track space: an orb is a ball of its own
            // size, a flat pad the whole slab (and as tall as the ship, so a
            // grounded ship always reads as on it). The air lane lifts both.
            float padScale = def.sizeMultiplier;
            Vector3 halfExtents = def.floatingOrb
                ? Vector3.one * (PadSize.x * padScale * 0.5f)
                : new Vector3(PadSize.x * padScale * 0.5f, 1f, PadSize.z * padScale * 0.5f);
            speedPad.PlaceOnTrack(distance, lateral, entry.lane == PadLane.Air ? AirLaneHeight : 0f, halfExtents);
            // Boosts scale off the shared power-up base; brakes keep their
            // definition's own delta so dodging stays predictable.
            if (def.speedDelta >= 0f) speedPad.SetDefinition(def, BaseBoost * entry.multiplier, entry.color, entry.name);
            else speedPad.SetDefinition(def);

            Register(distance, pad);
            RecordPickup(distance);
            return pad;
        }

        /// <summary>Forces every collider under <paramref name="go"/> to a trigger — the ship's sweep finds pickups by collider, and a solid one would be flown into. False when it has none.</summary>
        public static bool ForceTriggers(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>();
            foreach (var c in colliders) c.isTrigger = true;
            return colliders.Length > 0;
        }

        /// <summary>A recolored copy of <paramref name="source"/> — the SRP Batcher ignores per-renderer tints, so every tint is its own instance. Glow scales the emission.</summary>
        public static Material TintedCopy(Material source, Color color, float glow = 1f)
        {
            var mat = new Material(source);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * glow);
            }
            return mat;
        }
    }
}
