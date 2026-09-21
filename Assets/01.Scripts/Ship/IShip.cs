using System;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// What the rest of the game reads off a ship, in WORLD terms only: its
    /// speed, state, dash and roll, and the events its feedback hangs on. Both
    /// ships implement it — the runner's track-space <c>ShipMotor</c> and the
    /// standalone <see cref="HoverShip"/> — so the HUD, audio, trails, camera
    /// effects and menus are written once against this and never learn which
    /// ship is flying. Nothing here mentions a track: distance along it,
    /// lateral offset, ramps and loops are the runner's own extension
    /// (<c>IRunnerShip</c>), which only a level with a track can answer.
    /// An interface and never a base class, on purpose: the city assembly
    /// binds to <c>ShipMotor</c> without referencing this assembly, and a
    /// base type from here would break its build.
    /// </summary>
    public interface IShip
    {
        /// <summary>The ship's root — the physical pose. Satisfied by <c>Component.transform</c>.</summary>
        Transform transform { get; }
        /// <summary>The model child that banks, rolls and bobs.</summary>
        Transform Visual { get; }
        /// <summary>The run's definition — a runtime clone, safe to edit live.</summary>
        ShipDefinition Definition { get; }

        /// <summary>m/s.</summary>
        float CurrentSpeed { get; }
        ShipState State { get; }
        float AirTime { get; }
        bool IsSliding { get; }
        /// <summary>Latched once the ship has sat at a standstill with the throttle released for the stall grace.</summary>
        bool HasStopped { get; }
        /// <summary>Freezes the simulation (menus, a game's countdown, the end of a run).</summary>
        bool Paused { get; set; }

        /// <summary>0..1.</summary>
        float DashMeter { get; }
        /// <summary>Inside a dash window — no new dash starts in it.</summary>
        bool IsDashing { get; }
        /// <summary>Length of the current dash window, seconds (the barrel roll's while airborne).</summary>
        float DashBurstDuration { get; }
        bool IsBarrelRolling { get; }
        /// <summary>−1 / +1 while rolling, else 0.</summary>
        int BarrelRollDirection { get; }

        /// <summary>A pad, orb or boost was applied: the RAW magnitude, before the ship's weight.</summary>
        event Action<float> PadImpulse;
        /// <summary>−1 left / +1 right.</summary>
        event Action<int> DashPerformed;
        event Action<int> BarrelRollStarted;
        event Action Launched;
        event Action MeterFilled;
        /// <summary>The speed it was hit at, m/s.</summary>
        event Action<float> WallHit;
        /// <summary>A slide began: the lateral acceleration beyond the grip, m/s².</summary>
        event Action<float> Sliding;
        event Action<ShipState> StateChanged;
        event Action TookOff;
        event Action Landed;
        event Action FellOff;
        /// <summary>The world-space jump of the respawn teleport, for whatever follows the ship.</summary>
        event Action<Vector3> RespawnStarted;
        event Action Respawned;

        void SetDefinition(ShipDefinition runDefinition);
        void Launch();
        void AddSpeedImpulse(float rawMagnitude);
    }
}
