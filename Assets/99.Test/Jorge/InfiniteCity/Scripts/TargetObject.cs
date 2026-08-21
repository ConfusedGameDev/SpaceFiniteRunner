using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.PoliceEscape.City;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// A named point a "go to" objective can send the player to. Designers
    /// drop one in the scene, give it an id, and reference that id from a
    /// <see cref="LevelDefinition"/>; the LevelManager resolves the id at
    /// runtime through the static registry. Must be a ROOT scene object: the
    /// city is regenerated from a fresh seed every play and its chunks are
    /// streamed around the player, so nothing can be parented to it. Because
    /// of that, a hand-placed position may land inside a building — with
    /// <see cref="snapToRoad"/> on, the target slides onto the nearest road
    /// cell once its own chunk has been built (the nearest-cell query only
    /// knows built chunks, so a far result is rejected until then). Builds a
    /// tall beam so it can be spotted from the road.
    /// </summary>
    public class TargetObject : MonoBehaviour
    {
        static readonly Dictionary<string, TargetObject> registry = new();

        [Tooltip("The id a level's GO TO objective refers to. Case-sensitive; must be unique in the scene.")]
        [Required]
        [SerializeField] string id = "target";

        [Tooltip("Slide onto the nearest road cell once the city has built the chunk under this point, so the target never sits inside a building.")]
        [SerializeField] bool snapToRoad = true;

        [Tooltip("Build a tall glowing beam at the target so it can be seen from a distance.")]
        [SerializeField] bool showBeam = true;

        [SerializeField] Color beamColor = new(1f, 0.85f, 0.4f, 0.6f);

        CityManager city;
        bool snapped;
        float snapTimer;
        string registeredId;

        public string Id => id;
        public Vector3 Position => transform.position;
        public bool Snapped => snapped || !snapToRoad;

        /// <summary>The registered target with this id, if one is enabled in the scene.</summary>
        public static bool TryFind(string targetId, out TargetObject target)
        {
            target = null;
            if (string.IsNullOrEmpty(targetId)) return false;
            return registry.TryGetValue(targetId.Trim(), out target) && target != null;
        }

        void OnEnable()
        {
            registeredId = string.IsNullOrEmpty(id) ? null : id.Trim();
            if (registeredId == null)
            {
                Debug.LogWarning($"{nameof(TargetObject)} '{name}' has no id — no objective can find it.", this);
                return;
            }
            if (registry.TryGetValue(registeredId, out var other) && other != null && other != this)
                Debug.LogWarning($"Duplicate {nameof(TargetObject)} id '{registeredId}' on '{name}' and '{other.name}' — the newest wins.", this);
            registry[registeredId] = this;
        }

        void OnDisable()
        {
            if (registeredId != null && registry.TryGetValue(registeredId, out var current) && current == this)
                registry.Remove(registeredId);
        }

        void Start()
        {
            if (showBeam) BuildBeam();
        }

        void Update()
        {
            if (snapped || !snapToRoad) return;
            snapTimer -= Time.deltaTime;
            if (snapTimer > 0f) return;
            snapTimer = 1f;

            if (city == null) city = FindFirstObjectByType<CityManager>();
            if (city == null || city.settings == null) { snapped = true; return; } // no city: the authored spot is final

            if (!city.TryFindNearestRoadCell(transform.position, out Vector3 center, out _)) return;
            float accept = 2f * city.settings.cellSize;
            if (HorizontalDistance(center, transform.position) > accept) return; // our chunk isn't built yet

            transform.position = new Vector3(center.x, center.y + 0.5f, center.z);
            snapped = true;
        }

        /// <summary>Ground-plane distance — the target floats above the road, and so does the car's pivot.</summary>
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // A collider-free emissive column — visible from afar, drives through.
        void BuildBeam()
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(transform, false);
            beam.transform.localPosition = new Vector3(0f, 30f, 0f);
            beam.transform.localScale = new Vector3(2f, 30f, 2f);

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                var material = new Material(shader) { color = beamColor };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", beamColor);
                if (material.HasProperty("_Surface")) { material.SetFloat("_Surface", 1f); material.renderQueue = 3000; }
                beam.GetComponent<Renderer>().material = material;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 5f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 40f);
        }
    }
}
