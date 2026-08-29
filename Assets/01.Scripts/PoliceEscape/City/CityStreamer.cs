using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.City
{
    /// <summary>
    /// Activity culling inside the single baked city prefab. The whole city
    /// is instantiated with the scene (no per-block loading), but only the
    /// blocks within <see cref="CityRoot.streamEnterDistance"/> of the
    /// player keep their baked content ACTIVE: everything else has its
    /// content roots (Roads, Buildings, Decorations, Nature, Shoreline, the
    /// water quads — see <see cref="CityBlock.StreamedRoots"/>) switched off.
    /// The block objects themselves always stay alive: the road graph, the
    /// spawn walks, the map model, the ground / sea-floor colliders and the
    /// splash triggers all hang off them, so AI plans, the Tab map and a car
    /// that strays far out never notice. Membership is the TORUS rectangle
    /// distance from the player to each block (the diagonal neighbour
    /// qualifies near a corner, the pacman seam is one more route), with the
    /// same enter/exit hysteresis <see cref="CityBounds"/> uses so cruising
    /// along an edge cannot thrash. Toggles are time-sliced one root per
    /// frame, nearest first, because a Buildings root drops dozens of
    /// non-convex mesh colliders into PhysX in one go; the ring is meant to
    /// be wider than the fog (<c>DistanceFog</c>) so the pop-in lands behind
    /// it. Nothing is touched until the player exists, and disabling the
    /// component restores every block at once, so it can be toggled live and
    /// play-mode exit leaves the prefab instance intact. The invariant that
    /// keeps NPCs honest: the ENTER distance must cover the police and
    /// traffic despawn reach, or a cruiser far from the player drives on ramp
    /// colliders that are not there (spawns are already restricted to
    /// CityBounds' allowed set, a subset of the ring). <c>IsCellClear</c>
    /// reads "clear" in an unloaded block — only the debug Create Car button
    /// can land there, and the buildings arrive within a second.
    /// </summary>
    [DisallowMultipleComponent]
    public class CityStreamer : MonoBehaviour
    {
        struct Toggle
        {
            public GameObject Root;
            public bool Active;
            public float Distance;
        }

        [ShowInInspector, ReadOnly, Tooltip("Blocks whose content is currently kept active.")]
        public int LoadedBlocks => loaded.Count;

        [ShowInInspector, ReadOnly, Tooltip("Content roots still waiting for their SetActive slice.")]
        public int PendingToggles => Mathf.Max(0, pending.Count - drained);

        CityRoot root;
        CityBlock[] blocks;
        readonly HashSet<Vector2Int> loaded = new();
        readonly HashSet<Vector2Int> desired = new();
        readonly List<Toggle> pending = new();
        int drained;
        bool warned;

        // Activations nearest-first, then deactivations farthest-first: the
        // block the player is about to enter matters more than tidying up.
        static readonly System.Comparison<Toggle> Order = (a, b) =>
        {
            if (a.Active != b.Active) return a.Active ? -1 : 1;
            return a.Active ? a.Distance.CompareTo(b.Distance) : b.Distance.CompareTo(a.Distance);
        };

        void Awake()
        {
            root = GetComponent<CityRoot>();
            blocks = GetComponentsInChildren<CityBlock>();
        }

        void Update()
        {
            int budget = root != null ? Mathf.Max(1, root.activationsPerFrame) : 1;
            while (budget-- > 0 && drained < pending.Count)
            {
                Toggle toggle = pending[drained++];
                if (toggle.Root != null && toggle.Root.activeSelf != toggle.Active)
                    toggle.Root.SetActive(toggle.Active);
            }
            if (drained >= pending.Count)
            {
                pending.Clear();
                drained = 0;
            }
        }

        void OnDisable()
        {
            // Restore everything at once (no slicing): the component was
            // switched off by hand, or the scene is going away.
            pending.Clear();
            drained = 0;
            loaded.Clear();
            if (blocks == null || !gameObject.scene.isLoaded) return;
            foreach (CityBlock block in blocks)
            {
                if (block == null) continue;
                foreach (GameObject content in block.StreamedRoots)
                    if (content != null && !content.activeSelf) content.SetActive(true);
            }
        }

        /// <summary>
        /// Recompute the ring around the player and queue the toggles that get
        /// the blocks there. Called on <see cref="CityRoot"/>'s 1 s cadence
        /// right after CityBounds ticks; the queue is rebuilt from the blocks'
        /// actual state every time, so a block that changes its mind while
        /// its previous toggle is still queued simply gets the new one.
        /// </summary>
        public void Tick(Vector3 playerPosition)
        {
            if (root == null || blocks == null || !enabled) return;
            if (!warned) WarnAboutReach();

            Vector3 origin = root.transform.position;
            float px = Wrap01(playerPosition.x - origin.x, root.CitySizeX);
            float pz = Wrap01(playerPosition.z - origin.z, root.CitySizeZ);

            desired.Clear();
            pending.Clear();
            drained = 0;
            foreach (CityBlock block in blocks)
            {
                if (block == null) continue;
                float distance = RectangleDistance(block.coord, px, pz);
                bool wasLoaded = loaded.Contains(block.coord);
                float threshold = wasLoaded ? root.streamExitDistance : root.streamEnterDistance;
                bool keep = distance <= threshold;
                if (keep) desired.Add(block.coord);
                foreach (GameObject content in block.StreamedRoots)
                {
                    if (content == null || content.activeSelf == keep) continue;
                    pending.Add(new Toggle { Root = content, Active = keep, Distance = distance });
                }
            }
            pending.Sort(Order);

            loaded.Clear();
            loaded.UnionWith(desired);
        }

        /// <summary>
        /// Distance from the player (city-local, wrapped) to a block's
        /// rectangle on the torus: per axis, zero inside the interval, else
        /// the shorter of the direct gap and the gap the other way round the
        /// city (only when the city wraps); combined Euclidean, so a diagonal
        /// neighbour is as far as its corner.
        /// </summary>
        float RectangleDistance(Vector2Int coord, float px, float pz)
        {
            float block = root.BlockWorldSize;
            float dx = AxisDistance(px, coord.x * block, (coord.x + 1) * block, root.CitySizeX, root.pacmanWrap);
            float dz = AxisDistance(pz, coord.y * block, (coord.y + 1) * block, root.CitySizeZ, root.pacmanWrap);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static float AxisDistance(float p, float min, float max, float size, bool wrap)
        {
            if (p >= min && p <= max) return 0f;
            float direct = p < min ? min - p : p - max;
            if (!wrap) return direct;
            float around = p < min ? p + size - max : min + size - p;
            return Mathf.Min(direct, around);
        }

        static float Wrap01(float value, float size) =>
            size <= 0f ? 0f : value - Mathf.Floor(value / size) * size;

        /// <summary>
        /// One-time sanity check of the two invariants nobody can enforce
        /// across assets: the ring must reach further than any NPC is allowed
        /// to exist (PursuitSettings / TrafficSettings), and further than the
        /// fog needs to hide the pop-in (DistanceFogSettings.fogEnd).
        /// </summary>
        void WarnAboutReach()
        {
            warned = true;
            float reach = 0f;
            CityManager city = FindAnyObjectByType<CityManager>();
            if (city != null)
            {
                if (city.pursuitSettings != null)
                    reach = Mathf.Max(reach, Mathf.Max(city.pursuitSettings.despawnDistance, city.pursuitSettings.SpawnDistanceMax + 50f));
                if (city.trafficSettings != null)
                    reach = Mathf.Max(reach, city.trafficSettings.activeRadius + city.trafficSettings.despawnPadding);
            }
            if (reach > root.streamEnterDistance)
                Debug.LogWarning($"CityStreamer: streamEnterDistance ({root.streamEnterDistance:0} m) is shorter than the NPC reach ({reach:0} m): " +
                                 "police or traffic can drive on unloaded blocks whose ramp and deck colliders are switched off. Raise it on the City prefab's CityRoot.", this);

            var fog = FX.DistanceFog.Instance;
            if (fog != null && fog.settings != null && fog.settings.fogEnd > root.streamEnterDistance)
                Debug.LogWarning($"CityStreamer: DistanceFog fogEnd ({fog.settings.fogEnd:0} m) reaches beyond streamEnterDistance ({root.streamEnterDistance:0} m): " +
                                 "blocks will pop in ahead of the fog. Lower fogEnd or raise the enter distance.", this);
        }
    }
}
