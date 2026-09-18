using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.Ship
{
    /// <summary>
    /// Disk-backed home of the pause menu's ship debug tweaks: every
    /// <see cref="ShipDefinition"/> stat editable on the Ship Speed / Handling /
    /// Dash / Hover debug tabs. Slider changes are captured here and the asset
    /// is flushed to disk at commit points (reload scene, resume), so tuned
    /// values survive scene reloads, play-mode exits and editor restarts.
    /// While <see cref="applyOnLoad"/> is on, the values are stamped onto the
    /// tuning screen's runtime clone right after it is built — debug overrides
    /// win over point allocation, same rule as <see cref="TrackDebugSettings"/>.
    /// Untick it on the asset to return to the authored definition.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_ShipDebug", menuName = "FiniteRunner/Ship Debug Settings")]
    public class ShipDebugSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_ShipDebug";

        [Tooltip("When on, these saved values override the ship definition (and the tuning screen's point allocation) on every launch. Turned on the first time the debug menu saves; untick to return to the authored values.")]
        public bool applyOnLoad;

        [Header("Speed")]
        public float initialImpulse = 25f;
        public float passiveDeceleration = 3f;
        public float acceleration = 40f;
        public float weight = 1f;

        // Added after the debug asset was first saved with applyOnLoad ON: a
        // key the file lacks deserializes to the default here, so the default
        // is "never captured" (-1 = leave the definition alone) — otherwise
        // the armed asset would stamp a made-up number over the authored one.
        public float cruiseSpeed = -1f;
        public float thrust = -1f;
        public float brakeDecel = -1f;
        public float coastDrag = -1f;
        public float digitalThrottleRampSeconds = -1f;

        [Header("Handling")]
        public float gripBase = -1f;
        public float gripPerSpeed = -1f;
        public float slideThreshold = -1f;
        public float slideSpeedLoss = -1f;
        public float lateralSpeed = 8f;
        public float handlingResponse = 8f;
        public float maxBankAngle = 35f;
        public float bankResponse = 6f;

        [Header("Dash")]
        public float dashDistance = 12f;
        public float dashDuration = 0.25f;
        public float dashRechargeSeconds = 8f;
        public int dashGhostCount = 6;
        public float barrelRollSeconds = 0.5f;

        [Header("Hover")]
        public float hoverHeight = 2f;
        public float bobAmplitude = 0.35f;
        public float bobFrequency = 1.5f;
        public float hoverPitchDegrees = 2.5f;

        static ShipDebugSettings cached;

        /// <summary>
        /// The asset, or a throwaway instance if none is in a Resources folder
        /// (the menu stays usable; tweaks just die with the session).
        /// </summary>
        public static ShipDebugSettings Load()
        {
            if (cached != null) return cached;
            cached = Resources.Load<ShipDebugSettings>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(ShipDebugSettings)} at Resources/{ResourcePath} — " +
                                 "ship debug tweaks will not survive this session.");
                cached = CreateInstance<ShipDebugSettings>();
            }
            return cached;
        }

        /// <summary>Snapshots the definition's current stats and arms the override.</summary>
        public void CaptureFrom(ShipDefinition definition)
        {
            applyOnLoad = true;

            initialImpulse = definition.initialImpulse;
            passiveDeceleration = definition.passiveDeceleration;
            acceleration = definition.acceleration;
            weight = definition.weight;
            cruiseSpeed = definition.cruiseSpeed;
            thrust = definition.thrust;
            brakeDecel = definition.brakeDecel;
            coastDrag = definition.coastDrag;
            digitalThrottleRampSeconds = definition.digitalThrottleRampSeconds;

            gripBase = definition.gripBase;
            gripPerSpeed = definition.gripPerSpeed;
            slideThreshold = definition.slideThreshold;
            slideSpeedLoss = definition.slideSpeedLoss;
            lateralSpeed = definition.lateralSpeed;
            handlingResponse = definition.handlingResponse;
            maxBankAngle = definition.maxBankAngle;
            bankResponse = definition.bankResponse;

            dashDistance = definition.dashDistance;
            dashDuration = definition.dashDuration;
            dashRechargeSeconds = definition.dashRechargeSeconds;
            dashGhostCount = definition.dashGhostCount;
            barrelRollSeconds = definition.barrelRollSeconds;

            hoverHeight = definition.hoverHeight;
            bobAmplitude = definition.bobAmplitude;
            bobFrequency = definition.bobFrequency;
            hoverPitchDegrees = definition.hoverPitchDegrees;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Writes the saved stats onto a definition. Only ever hand this a
        /// runtime clone — never the ScriptableObject asset on disk.
        /// </summary>
        public void ApplyTo(ShipDefinition definition)
        {
            if (!applyOnLoad) return;

            definition.initialImpulse = initialImpulse;
            definition.passiveDeceleration = passiveDeceleration;
            definition.acceleration = acceleration;
            definition.weight = weight;
            if (cruiseSpeed >= 0f) definition.cruiseSpeed = cruiseSpeed;
            if (thrust >= 0f) definition.thrust = thrust;
            if (brakeDecel >= 0f) definition.brakeDecel = brakeDecel;
            if (coastDrag >= 0f) definition.coastDrag = coastDrag;
            if (digitalThrottleRampSeconds >= 0f) definition.digitalThrottleRampSeconds = digitalThrottleRampSeconds;

            if (gripBase >= 0f) definition.gripBase = gripBase;
            if (gripPerSpeed >= 0f) definition.gripPerSpeed = gripPerSpeed;
            if (slideThreshold >= 0f) definition.slideThreshold = slideThreshold;
            if (slideSpeedLoss >= 0f) definition.slideSpeedLoss = slideSpeedLoss;
            definition.lateralSpeed = lateralSpeed;
            definition.handlingResponse = handlingResponse;
            definition.maxBankAngle = maxBankAngle;
            definition.bankResponse = bankResponse;

            definition.dashDistance = dashDistance;
            definition.dashDuration = dashDuration;
            definition.dashRechargeSeconds = dashRechargeSeconds;
            definition.dashGhostCount = dashGhostCount;
            definition.barrelRollSeconds = barrelRollSeconds;

            definition.hoverHeight = hoverHeight;
            definition.bobAmplitude = bobAmplitude;
            definition.bobFrequency = bobFrequency;
            definition.hoverPitchDegrees = hoverPitchDegrees;
        }

        /// <summary>
        /// Writes the asset to disk (editor only — builds keep changes for the
        /// app session). Called at commit points, not on every slider tick.
        /// </summary>
        public void Flush()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
#endif
        }
    }
}
