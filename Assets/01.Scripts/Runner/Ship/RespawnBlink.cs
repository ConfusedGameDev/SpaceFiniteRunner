using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The respawn blink: while the ship waits on the track after a fall
    /// (<see cref="ShipState.Respawning"/>) — and through the invulnerability
    /// after a hull hit (<see cref="ShipHealth.IsInvulnerable"/>) — its model flickers between its
    /// own materials and the dash's ghost material at
    /// <see cref="GameSettings.respawnBlinkRate"/>, the classic "you can't be
    /// touched yet" read. It SWAPS the renderers' materials — a
    /// MaterialPropertyBlock tint is ignored by the ship shader (and
    /// unreliable under the SRP Batcher) — and always hands the ship's own
    /// back: when the wait ends, on a restart, and on disable. Added to the
    /// ship by the GameManager (<see cref="Ensure"/>), reading the settings
    /// live like <see cref="LoopSlowMo"/>.
    /// </summary>
    public class RespawnBlink : MonoBehaviour
    {
        ShipMotor motor;
        ShipHealth health; // optional: found lazily, the GameManager adds it after this
        GameSettings settings;
        Material ghostMaterial;
        bool ownsGhostMaterial;

        readonly List<Renderer> renderers = new();
        readonly List<Material[]> ownMaterials = new();
        readonly List<Material[]> ghostMaterials = new();
        bool blinking;
        bool showingGhost;

        public static RespawnBlink Ensure(ShipMotor motor)
        {
            var blink = motor.GetComponent<RespawnBlink>();
            if (blink == null) blink = motor.gameObject.AddComponent<RespawnBlink>();
            blink.motor = motor;
            return blink;
        }

        public void Configure(GameSettings settings)
        {
            this.settings = settings;
            if (ownsGhostMaterial && ghostMaterial != null) Destroy(ghostMaterial);
            ownsGhostMaterial = settings == null || settings.dashGhostMaterial == null;
            ghostMaterial = ownsGhostMaterial ? DashGhostTrail.BuildFallbackMaterial() : settings.dashGhostMaterial;
        }

        void Update()
        {
            if (motor == null || settings == null) return;

            // The same "can't be touched" read covers the blink after a hull hit.
            if (health == null) health = motor.GetComponent<ShipHealth>();
            bool shouldBlink = motor.State == ShipState.Respawning || (health != null && health.IsInvulnerable);
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
