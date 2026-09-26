using System.Collections.Generic;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The respawn blink: while the ship waits on the track after a fall
    /// (<see cref="ShipState.Respawning"/>, or a rolling start's
    /// <see cref="ShipRecovery.RespawnShielded"/> window) — and through the invulnerability
    /// after a hull hit (<see cref="AlsoBlinkWhile"/>) — its model flickers between its
    /// own materials and the dash's ghost material at
    /// <see cref="ShipSettings.respawnBlinkRate"/>, the classic "you can't be
    /// touched yet" read. It SWAPS the renderers' materials — a
    /// MaterialPropertyBlock tint is ignored by the ship shader (and
    /// unreliable under the SRP Batcher) — and always hands the ship's own
    /// back: when the wait ends, on a restart, and on disable. It reads any
    /// <see cref="IShip"/> and the settings live: baked into the standalone
    /// prefab it wires itself to the ship beside it in <c>Start</c>; the
    /// runner's GameManager adds it to its ship with <see cref="Ensure"/>.
    /// </summary>
    public class RespawnBlink : MonoBehaviour
    {
        IShip motor;
        ShipSettings settings;
        ShipRecovery recovery;

        /// <summary>
        /// A game's own "can't be touched yet": the blink also runs while this answers true. The runner's hull sets
        /// it to its invulnerability after a hit — a type this assembly cannot see, hence a question and not a field.
        /// </summary>
        public System.Func<bool> AlsoBlinkWhile { get; set; }
        Material ghostMaterial;
        bool ownsGhostMaterial;

        readonly List<Renderer> renderers = new();
        readonly List<Material[]> ownMaterials = new();
        readonly List<Material[]> ghostMaterials = new();
        bool blinking;
        bool showingGhost;

        public static RespawnBlink Ensure(IShip motor)
        {
            GameObject host = motor.transform.gameObject;
            var blink = host.GetComponent<RespawnBlink>();
            if (blink == null) blink = host.AddComponent<RespawnBlink>();
            blink.motor = motor;
            return blink;
        }

        void Start()
        {
            // On the prefab nobody calls Ensure / Configure: the ship is the component beside this one.
            if (motor != null) return;
            if (!TryGetComponent(out HoverShip ship) || ship.Settings == null) return;
            motor = ship;
            Configure(ship.Settings);
        }

        public void Configure(ShipSettings settings)
        {
            this.settings = settings;
            if (ownsGhostMaterial && ghostMaterial != null) Destroy(ghostMaterial);
            ownsGhostMaterial = settings == null || settings.ghostMaterial == null;
            ghostMaterial = ownsGhostMaterial ? ShipGhostMaterial.BuildFallback() : settings.ghostMaterial;
        }

        void Update()
        {
            if (motor as Object == null || settings == null) return;

            // The same "can't be touched" read covers the blink after a hull hit.
            if (recovery == null) recovery = motor.transform.GetComponent<ShipRecovery>();
            bool shouldBlink = motor.State == ShipState.Respawning
                            || (recovery != null && recovery.RespawnShielded)
                            || (AlsoBlinkWhile != null && AlsoBlinkWhile());
            if (shouldBlink && !blinking) Begin();
            else if (!shouldBlink && blinking) End();
            if (!blinking) return;

            // Scaled time: a pause freezes the blink with everything else.
            float phase = Mathf.Repeat(Time.time * Mathf.Max(settings.respawnBlinkRate, 0.1f), 1f);
            Show(phase < 0.5f);
        }

        // The materials are read as the wait begins, not cached at startup:
        // anything that swapped a skin since then is what must come back.
        void Begin()
        {
            blinking = true;
            showingGhost = false;
            renderers.Clear();
            ownMaterials.Clear();
            ghostMaterials.Clear();

            Transform root = motor.Visual != null ? motor.Visual : motor.transform;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue; // trails and particles keep theirs
                Material[] own = renderer.sharedMaterials;
                var ghost = new Material[own.Length];
                for (int i = 0; i < ghost.Length; i++) ghost[i] = ghostMaterial;
                renderers.Add(renderer);
                ownMaterials.Add(own);
                ghostMaterials.Add(ghost);
            }
        }

        void End()
        {
            Show(false);
            blinking = false;
        }

        void Show(bool ghost)
        {
            if (ghost == showingGhost) return;
            showingGhost = ghost;
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i] != null)
                    renderers[i].sharedMaterials = ghost ? ghostMaterials[i] : ownMaterials[i];
        }

        void OnDisable()
        {
            if (blinking) End();
        }

        void OnDestroy()
        {
            if (ownsGhostMaterial && ghostMaterial != null) Destroy(ghostMaterial);
        }
    }
}
