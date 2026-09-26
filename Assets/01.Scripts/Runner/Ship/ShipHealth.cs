using System.Collections.Generic;
using UnityEngine;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.Track;
namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// The ship's hull: the HUD's life bar. Full at
    /// <see cref="ShipDefinition.maxHull"/> (read off the run's clone), it
    /// loses points to the three things that hurt — a brake pad
    /// (<see cref="SpeedPad.Collected"/> with a negative delta), a hard wall
    /// hit (<see cref="ShipMotor.WallHit"/>: a dash slam or a ramp's side) and
    /// plain wall contact (<see cref="ShipMotor.IsTouchingWall"/>, polled) —
    /// plus a fall off the track (<see cref="ShipMotor.FellOff"/>, the one hit
    /// that lands on an off-track ship and through the blink) —
    /// by the amounts in <see cref="GameSettings"/>' "Hull and lives" group.
    /// Every hit opens <see cref="GameSettings.hitInvulnerabilitySeconds"/> of
    /// invulnerability (<see cref="RespawnBlink"/> blinks the model through
    /// it), so a ship grinding along a wall is hurt once per window, never per
    /// frame. At 0 it raises <see cref="Destroyed"/> once; what that MEANS —
    /// the explosion, the lost life — is the GameManager's. Nothing hurts a
    /// ship that is off the track, waiting to respawn, frozen, or in a run
    /// that is already ending. It also owns hiding the model
    /// (<see cref="SetShipVisible"/>), and always hands it back: on
    /// <see cref="ResetForRun"/> and on disable. Added to the ship by the
    /// GameManager (<see cref="Ensure"/>), reading the settings live like
    /// <see cref="LoopSlowMo"/>. The one thing that gives points back is a
    /// <see cref="RepairOrb"/> (<see cref="Heal"/>), and it is a no-op at full hull.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipHealth : MonoBehaviour
    {
        ShipMotor motor;
        GameSettings settings;
        GameManager gameManager;
        float invulnerableLeft;

        readonly List<Renderer> hidden = new(); // what SetShipVisible(false) switched off

        // Every enabled hull, so a world pickup that only knows the IShip it hit can find the one to heal. Filled in OnEnable/OnDisable — domain reload is off.
        static readonly List<ShipHealth> active = new();

        /// <summary>Hull points left.</summary>
        public float Hull { get; private set; }

        /// <summary>What the bar is full at: the run definition's <see cref="ShipDefinition.maxHull"/>.</summary>
        public float MaxHull => motor != null && motor.Definition != null ? Mathf.Max(1f, motor.Definition.maxHull) : 1f;

        /// <summary>Hull left, 0..1 — what the HUD draws.</summary>
        public float Fraction => Mathf.Clamp01(Hull / MaxHull);

        /// <summary>True for the blink after a hit: nothing hurts.</summary>
        RespawnBlink blink;
        ShipRecovery recovery;

        public bool IsInvulnerable => invulnerableLeft > 0f;

        /// <summary>Latched at 0 hull until <see cref="ResetForRun"/>.</summary>
        public bool IsDestroyed { get; private set; }

        /// <summary>Raised on every hit that took points. Arguments: the points taken, and whether it was a HARD hit (brake pad, slam) rather than a scrape.</summary>
        public event System.Action<float, bool> Damaged;

        /// <summary>Raised once, the hit that takes the hull to 0.</summary>
        public event System.Action Destroyed;

        /// <summary>Raised on every <see cref="Heal"/> that gave points back. Argument: the points restored.</summary>
        public event System.Action<float> Healed;

        /// <summary>The enabled hull of <paramref name="ship"/>, or null (no hull on it — a patrol, the standalone ship).</summary>
        public static ShipHealth For(IShip ship)
        {
            if (ship == null) return null;
            foreach (var health in active)
                if (health.motor != null && health.motor.Is(ship)) return health;
            return null;
        }

        public static ShipHealth Ensure(ShipMotor motor)
        {
            var health = motor.GetComponent<ShipHealth>();
            if (health == null) health = motor.gameObject.AddComponent<ShipHealth>();
            health.Bind(motor);
            return health;
        }

        public void Configure(GameSettings settings, GameManager gameManager)
        {
            this.settings = settings;
            this.gameManager = gameManager;
            ResetForRun();
        }

        /// <summary>A fresh run: full hull, no blink, the model back on.</summary>
        public void ResetForRun()
        {
            Hull = MaxHull;
            IsDestroyed = false;
            invulnerableLeft = 0f;
            SetShipVisible(true);
        }

        /// <summary>Switches every renderer of the ship's model (mesh, trails, particles) off, or hands back exactly the ones it switched off.</summary>
        public void SetShipVisible(bool visible)
        {
            if (visible)
            {
                foreach (var renderer in hidden)
                    if (renderer != null) renderer.enabled = true;
                hidden.Clear();
                return;
            }

            if (hidden.Count > 0 || motor == null) return;
            Transform root = motor.Visual != null ? motor.Visual : motor.transform;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                renderer.enabled = false;
                hidden.Add(renderer);
            }
        }

        // Ensure runs after AddComponent's OnEnable, so the motor's event is
        // hooked here as well as in OnEnable (a re-enable).
        void Bind(ShipMotor motor)
        {
            if (this.motor == motor) return;
            if (this.motor != null) Unhook();
            this.motor = motor;
            if (isActiveAndEnabled) Hook();
        }

        void Hook()
        {
            motor.WallHit += OnWallHit;
            motor.FellOff += OnFellOff;
        }

        void Unhook()
        {
            motor.WallHit -= OnWallHit;
            motor.FellOff -= OnFellOff;
        }

        void OnEnable()
        {
            if (motor != null) Hook();
            SpeedPad.Collected += OnPadCollected; // static: paired below — domain reload is off
            if (!active.Contains(this)) active.Add(this);
        }

        void OnDisable()
        {
            if (motor != null) Unhook();
            SpeedPad.Collected -= OnPadCollected;
            active.Remove(this);
            SetShipVisible(true);
        }

        void Update()
        {
            if (motor == null || settings == null) return;

            // The blink lives in the Ship assembly and cannot see this type: it is handed the question instead (the GameManager adds it before or after us).
            if (blink == null)
            {
                blink = motor.GetComponent<RespawnBlink>();
                if (blink != null) blink.AlsoBlinkWhile = () => this != null && IsInvulnerable;
            }

            // Scaled time, and not while the sim is frozen: a pause or a
            // menu never runs the blink out.
            if (invulnerableLeft > 0f && !motor.Paused)
                invulnerableLeft = Mathf.Max(0f, invulnerableLeft - Time.deltaTime);

            if (motor.IsTouchingWall) ApplyDamage(settings.wallScrapeDamage, hard: false);
        }

        void OnWallHit(float impactSpeed)
        {
            if (settings != null) ApplyDamage(settings.wallSlamDamage, hard: true);
        }

        // Over an open edge: the ship is already OffTrack when this fires, and
        // a fall hurts through the blink too — hence forced.
        void OnFellOff()
        {
            if (settings != null) ApplyDamage(settings.fallDamage, hard: true, forced: true);
        }

        void OnPadCollected(SpeedPad pad, IShip collector)
        {
            if (motor == null || !motor.Is(collector) || settings == null || pad.SpeedDelta >= 0f) return;
            ApplyDamage(settings.brakePadDamage, hard: true);
        }

        /// <summary>
        /// The ship flew through a laser beam (<c>LaserGate.Hit</c>, routed by
        /// the GameManager so the rumble and the shake only play for a hit
        /// that landed). An ordinary hard hit: the blink shields it. Returns
        /// whether it took any hull.
        /// </summary>
        public bool ApplyLaserHit() =>
            settings != null && ApplyDamage(settings.laserDamage, hard: true);

        /// <summary>
        /// Gives back <paramref name="fraction"/> of <see cref="MaxHull"/>,
        /// clamped to full. Returns false — and changes nothing — when the hull
        /// is off, already full, destroyed, or the run is ending: the caller
        /// (a <see cref="RepairOrb"/>) is then left in place, not used up.
        /// </summary>
        public bool Heal(float fraction) => HealPoints(fraction) > 0f;

        /// <summary>A <see cref="RepairOrb"/> was flown through: <see cref="Heal"/> by <see cref="GameSettings.repairOrbHealFraction"/>. Returns the points restored, 0 when it did nothing (full hull).</summary>
        public float HealFromRepairOrb() => settings != null ? HealPoints(settings.repairOrbHealFraction) : 0f;

        float HealPoints(float fraction)
        {
            if (fraction <= 0f || settings == null || !settings.hullEnabled || IsDestroyed) return 0f;
            if (gameManager != null && (gameManager.IsEnding || gameManager.RunOver)) return 0f;
            if (Hull >= MaxHull) return 0f;

            float before = Hull;
            Hull = Mathf.Min(MaxHull, Hull + MaxHull * fraction);
            Healed?.Invoke(Hull - before);
            return Hull - before;
        }

        bool ApplyDamage(float amount, bool hard, bool forced = false)
        {
            if (amount <= 0f || !CanBeHurt(forced)) return false;

            Hull = Mathf.Max(0f, Hull - amount);
            invulnerableLeft = settings.hitInvulnerabilitySeconds;
            Damaged?.Invoke(amount, hard);

            if (Hull > 0f) return true;
            IsDestroyed = true;
            invulnerableLeft = 0f; // nothing left to blink
            Destroyed?.Invoke();
            return true;
        }

        // forced = the fall's own hit: the blink and the off-track state do not shield it.
        bool CanBeHurt(bool forced)
        {
            if (settings == null || !settings.hullEnabled) return false;
            if (IsDestroyed || motor.Paused) return false;
            if (gameManager != null && (gameManager.IsEnding || gameManager.RunOver)) return false;
            if (forced) return true;
            if (IsInvulnerable) return false;
            if (recovery == null) recovery = motor.GetComponent<ShipRecovery>();
            if (recovery != null && recovery.RespawnShielded) return false; // a rolling start flies its wait untouchable
            return motor.State != ShipState.OffTrack
                && motor.State != ShipState.Respawning
                && motor.State != ShipState.Falling;
        }
    }
}
