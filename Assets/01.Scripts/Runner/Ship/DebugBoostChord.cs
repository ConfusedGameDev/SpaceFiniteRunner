using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

using ConfusedGameDev.FiniteRunner.GameFlow;
using ConfusedGameDev.FiniteRunner.UI;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Debug utility: a press of Y on the pad gives the ship a speed boost of
    /// <see cref="GameSettings.debugBoostKmh"/>, through the same
    /// <see cref="ShipMotor.AddSpeedImpulse"/> an orb uses — so the "+N", the
    /// rumble and the patrol's boost share come with it. Switched by
    /// <see cref="GameSettings.debugBoostChord"/>, read live.
    ///
    /// Hand-placed on <c>PF_Ship</c> like <see cref="LoopSlowMo"/>:
    /// <see cref="Ensure"/> + <see cref="Configure"/> from <c>GameManager.Awake</c>.
    /// The switch and the amount stay on the settings asset (tunables live in
    /// ScriptableObjects, and the pause menu's DEBUG TOOLS page edits them
    /// there); this inspector shows them through the asset the prefab
    /// references, so both views edit one value. The button is a developer
    /// tool, not a control: it reads the pad raw and is never bindable (Y is
    /// only the car's respawn, which the runner never reads).
    /// </summary>
    [RequireComponent(typeof(ShipMotor))]
    [DisallowMultipleComponent]
    public class DebugBoostChord : MonoBehaviour
    {
        [Tooltip("The settings asset holding the switch and the amount. The run's own settings replace it at Configure.")]
        [SerializeField] GameSettings settings;

        ShipMotor motor;

        /// <summary>The debug boost's switch, on the settings asset.</summary>
        [ShowInInspector, ShowIf(nameof(HasSettings)), LabelText("Enabled")]
        public bool Enabled
        {
            get => HasSettings && settings.debugBoostChord;
            set { if (!HasSettings) return; settings.debugBoostChord = value; DebugAssetEdits.Touch(settings); }
        }

        /// <summary>Speed added per press, on the settings asset (raw, before the ship's weight scales it).</summary>
        [ShowInInspector, ShowIf(nameof(HasSettings)), PropertyRange(50f, 5000f), SuffixLabel("km/h", true)]
        public float BoostKmh
        {
            get => HasSettings ? settings.debugBoostKmh : 0f;
            set { if (!HasSettings) return; settings.debugBoostKmh = value; DebugAssetEdits.Touch(settings); }
        }

        bool HasSettings => settings != null;

        /// <summary>Add the component to a ship that has none yet — the GameManager.Awake hook.</summary>
        public static DebugBoostChord Ensure(ShipMotor ship) =>
            ship.GetComponent<DebugBoostChord>() ?? ship.gameObject.AddComponent<DebugBoostChord>();

        /// <summary>The run's settings: the switch and the boost size, read live.</summary>
        public void Configure(GameSettings runSettings) => settings = runSettings;

        void Awake() => motor = GetComponent<ShipMotor>();

        void Update()
        {
            if (!Enabled || motor == null || motor.Paused || Time.timeScale <= 0f) return;
            var pad = Gamepad.current;
            if (pad != null && pad.buttonNorth.wasPressedThisFrame)
                motor.AddSpeedImpulse(settings.debugBoostKmh / settings.speedDisplayMultiplier);
        }
    }
}
