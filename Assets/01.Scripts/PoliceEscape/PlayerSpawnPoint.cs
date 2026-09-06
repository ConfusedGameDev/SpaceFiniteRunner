using System.Collections.Generic;
using System.Linq;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using ConfusedGameDev.FiniteRunner.PoliceEscape.Vehicles;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// A hand-placed pose the player car can start from. Designers drop
    /// these under the city prefab's <c>AdditionalItems</c> socket — the
    /// baker never touches it and <c>CityStreamer</c> never switches it off,
    /// so the points survive rebakes and are always registered. Every scene
    /// entry and every in-place retry (<see cref="Vehicles.PlayerCarSpawner"/>)
    /// spawns at a RANDOM registered point unless the level's
    /// <see cref="LevelDefinition.spawnPointId"/> names one, resolved through
    /// the static registry exactly like <see cref="TargetObject"/>.
    /// The pose is used AS AUTHORED: the position is the road surface
    /// (<see cref="Vehicles.CarFactory"/> lifts the car by the config's
    /// respawn height and it settles on its wheels) and the Y rotation is the
    /// heading — scale and tilt are ignored. An empty id is legal and means
    /// "random only". The Scene view draws the Quadron as a wire mesh at the
    /// pose so what you see is exactly where and which way the car appears.
    /// </summary>
    public class PlayerSpawnPoint : MonoBehaviour
    {
        static readonly Dictionary<string, PlayerSpawnPoint> byId = new();
        static readonly List<PlayerSpawnPoint> all = new();

        [Tooltip("Optional. A level's Spawn Point Id picks this point by name; empty = only ever picked at random. Case-sensitive; must be unique in the scene.")]
        [SerializeField] string id = "";

        [Tooltip("Slide onto the nearest road cell centre at spawn time (accepted within two cells) — for when a rebake moved the streets from under an authored point. Only X/Z move; height and heading stay as authored.")]
        [SerializeField] bool snapToRoad;

        string registeredId;

        public string Id => id;

        /// <summary>How many points are enabled in the scene right now.</summary>
        public static int Count => all.Count;

        /// <summary>Every non-empty id currently registered.</summary>
        public static IEnumerable<string> RegisteredIds => byId.Keys;

        /// <summary>The registered point with this id, if one is enabled in the scene.</summary>
        public static bool TryFind(string spawnPointId, out PlayerSpawnPoint point)
        {
            point = null;
            if (string.IsNullOrEmpty(spawnPointId)) return false;
            return byId.TryGetValue(spawnPointId.Trim(), out point) && point != null;
        }

        /// <summary>A uniformly random enabled point; false when the scene has none.</summary>
        public static bool TryPickRandom(out PlayerSpawnPoint point)
        {
            point = null;
            if (all.Count == 0) return false;
            point = all[Random.Range(0, all.Count)];
            return point != null;
        }

        /// <summary>
        /// The world spawn pose: the authored position (X/Z moved onto the
        /// nearest road cell when <see cref="snapToRoad"/> is on and the city
        /// knows a cell within two cells) and the authored heading in degrees
        /// (0 = +Z), which is what <see cref="Vehicles.CarFactory.Spawn"/> takes.
        /// </summary>
        public void GetPose(CityManager city, out Vector3 position, out float yaw)
        {
            position = transform.position;
            yaw = transform.eulerAngles.y;
            if (!snapToRoad || city == null || city.settings == null) return;

            if (!city.TryFindNearestRoadCell(position, out Vector3 center, out _))
                return;
            float accept = 2f * city.settings.cellSize;
            if (TargetObject.HorizontalDistance(center, position) > accept)
            {
                Debug.LogWarning($"{nameof(PlayerSpawnPoint)} '{name}': nearest road cell is {TargetObject.HorizontalDistance(center, position):0} m away — too far to snap, spawning at the authored pose.", this);
                return;
            }
            position = new Vector3(center.x, position.y, center.z);
        }

        void OnEnable()
        {
            all.Add(this);
            registeredId = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            if (registeredId == null) return; // random-only point
            if (byId.TryGetValue(registeredId, out var other) && other != null && other != this)
                Debug.LogWarning($"Duplicate {nameof(PlayerSpawnPoint)} id '{registeredId}' on '{name}' and '{other.name}' — the newest wins.", this);
            byId[registeredId] = this;
        }

        void OnDisable()
        {
            all.Remove(this);
            if (registeredId != null && byId.TryGetValue(registeredId, out var current) && current == this)
                byId.Remove(registeredId);
        }

        // ---- Scene view ghost -------------------------------------------------

        static readonly Color GizmoColor = new(0.4f, 0.9f, 1f, 0.9f);

        void OnDrawGizmos()
        {
            // The runtime pose only: position + heading, scale and tilt dropped.
            var heading = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            var pose = Matrix4x4.TRS(transform.position, heading, Vector3.one);
            Gizmos.color = GizmoColor;

            bool drewModel = false;
#if UNITY_EDITOR
            if (TryGetCarGizmo(out CarGizmoPart[] parts))
            {
                // The kit's bonnet points +X; ModelYaw turns it to the car's +Z, the same as the rig builders.
                var kitYaw = Matrix4x4.Rotate(Quaternion.Euler(0f, CyberpunkCarKit.ModelYaw, 0f));
                foreach (CarGizmoPart part in parts)
                {
                    Gizmos.matrix = pose * kitYaw * part.local;
                    Gizmos.DrawWireMesh(part.mesh, Vector3.zero, Quaternion.identity, Vector3.one);
                }
                drewModel = true;
            }
#endif
            if (!drewModel)
            {
                // No model to hand: a Quadron-sized box.
                Gizmos.matrix = pose;
                Gizmos.DrawWireCube(new Vector3(0f, 0.7f, 0f), new Vector3(2f, 1.4f, 4.8f));
            }
            Gizmos.matrix = Matrix4x4.identity;

            // Heading line from the pivot, so the facing reads even from far away.
            Gizmos.DrawLine(transform.position, transform.position + heading * Vector3.forward * 4f);
        }

#if UNITY_EDITOR
        // The ghost is the Quadron FBX's LOD0 parts (body + four wheels) with
        // each mesh's transform relative to the model root — the wheels sit
        // on their axles, so localPosition alone would not do. Loaded once
        // off the asset database (gizmos are editor-only) and rebuilt if a
        // reimport destroyed the meshes.
        struct CarGizmoPart
        {
            public Mesh mesh;
            public Matrix4x4 local;
        }

        const string CarModelGuid = "bf0fdb3730bee474aa7e802dd686df0c";
        const string CarModelPath = "Assets/Cyberpunk_Megapolis/Models/Car/CP_Quadron.fbx";
        static CarGizmoPart[] carGizmo;

        static bool TryGetCarGizmo(out CarGizmoPart[] parts)
        {
            if (carGizmo != null && (carGizmo.Length == 0 || carGizmo[0].mesh != null))
            {
                parts = carGizmo;
                return parts.Length > 0;
            }

            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(CarModelGuid);
            if (string.IsNullOrEmpty(path)) path = CarModelPath;
            var root = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);

            var list = new List<CarGizmoPart>();
            if (root != null)
            {
                IEnumerable<MeshFilter> filters = null;
                var lodGroup = root.GetComponentInChildren<LODGroup>();
                if (lodGroup != null && lodGroup.lodCount > 0)
                    filters = lodGroup.GetLODs()[0].renderers
                        .Where(r => r != null)
                        .Select(r => r.GetComponent<MeshFilter>())
                        .Where(f => f != null);
                if (filters == null || !filters.Any())
                    filters = root.GetComponentsInChildren<MeshFilter>(true)
                        .Where(f => !f.name.Contains("LOD1") && !f.name.Contains("LOD2"));

                Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
                foreach (MeshFilter filter in filters)
                    if (filter.sharedMesh != null)
                        list.Add(new CarGizmoPart { mesh = filter.sharedMesh, local = toRoot * filter.transform.localToWorldMatrix });
            }
            else
            {
                Debug.LogWarning($"{nameof(PlayerSpawnPoint)}: car model not found at '{path}' — drawing a box instead.");
            }

            carGizmo = list.ToArray();
            parts = carGizmo;
            return parts.Length > 0;
        }
#endif
    }
}
