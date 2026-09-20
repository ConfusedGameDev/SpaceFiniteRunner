using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.GameFlow
{
    /// <summary>
    /// Disk-backed home of the pause menu's patrol debug tweaks: every
    /// <see cref="PatrolDefinition"/> stat editable on the Patrol debug tab.
    /// Slider changes are captured here and the asset is flushed to disk at
    /// commit points (reload scene, resume), so tuned values survive scene
    /// reloads, play-mode exits and editor restarts. While
    /// <see cref="applyOnLoad"/> is on, the values are stamped onto the
    /// patrol's runtime clone right after it is made — same rule as
    /// <see cref="ShipDebugSettings"/>. Untick it on the asset to return to
    /// the authored definition.
    /// </summary>
    [CreateAssetMenu(fileName = "FiniteRunner_PatrolDebug", menuName = "FiniteRunner/Patrol Debug Settings")]
    public class PatrolDebugSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "FiniteRunner_PatrolDebug";

        [Tooltip("When on, these saved values override the patrol definition on every launch. Turned on the first time the debug menu saves; untick to return to the authored values.")]
        public bool applyOnLoad;

        public float baseSpeed = 97f;
        public float ramp = 0.8f;
        public float rubberBand = 1.05f;
        public float catchUpAccel = 16.7f;
        public float boostShare = 0.7f;
        public float startGap = 250f;
        public float catchDistance = 10f;
        public float warnDistance = 130f;

        // Handling, catch and driver knobs. -1 = never captured: leave the
        // definition's value alone (an asset saved before these keys existed
        // would otherwise stamp a made-up default over the authored one).
        public float lateralSpeed = -1f;
        public float handlingResponse = -1f;
        public float gripBase = -1f;
        public float gripPerSpeed = -1f;
        public float brakeDecel = -1f;
        public float catchLateral = -1f;
        public float sustainedCatchSeconds = -1f;
        public float curveLookaheadSeconds = -1f;
        public float orbLookaheadSeconds = -1f;
        public float orbSeekWeight = -1f;
        public float orbBoostShare = -1f;
        public float rampLookaheadSeconds = -1f;

        static PatrolDebugSettings cached;

        /// <summary>
        /// The asset, or a throwaway instance if none is in a Resources folder
        /// (the menu stays usable; tweaks just die with the session).
        /// </summary>
        public static PatrolDebugSettings Load()
        {
            if (cached != null) return cached;
            cached = Resources.Load<PatrolDebugSettings>(ResourcePath);
            if (cached == null)
            {
                Debug.LogWarning($"No {nameof(PatrolDebugSettings)} at Resources/{ResourcePath} — " +
                                 "patrol debug tweaks will not survive this session.");
                cached = CreateInstance<PatrolDebugSettings>();
            }
            return cached;
        }

        /// <summary>Snapshots the definition's current stats and arms the override.</summary>
        public void CaptureFrom(PatrolDefinition definition)
        {
            applyOnLoad = true;

            baseSpeed = definition.baseSpeed;
            ramp = definition.ramp;
            rubberBand = definition.rubberBand;
            catchUpAccel = definition.catchUpAccel;
            boostShare = definition.boostShare;
            startGap = definition.startGap;
            catchDistance = definition.catchDistance;
            warnDistance = definition.warnDistance;

            lateralSpeed = definition.lateralSpeed;
            handlingResponse = definition.handlingResponse;
            gripBase = definition.gripBase;
            gripPerSpeed = definition.gripPerSpeed;
            brakeDecel = definition.brakeDecel;
            catchLateral = definition.catchLateral;
            sustainedCatchSeconds = definition.sustainedCatchSeconds;
            curveLookaheadSeconds = definition.curveLookaheadSeconds;
            orbLookaheadSeconds = definition.orbLookaheadSeconds;
            orbSeekWeight = definition.orbSeekWeight;
            orbBoostShare = definition.orbBoostShare;
            rampLookaheadSeconds = definition.rampLookaheadSeconds;

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        /// <summary>
        /// Writes the saved stats onto a definition. Only ever hand this a
        /// runtime clone — never the ScriptableObject asset on disk.
        /// </summary>
        public void ApplyTo(PatrolDefinition definition)
        {
            if (!applyOnLoad) return;

            definition.baseSpeed = baseSpeed;
            definition.ramp = ramp;
            definition.rubberBand = rubberBand;
            definition.catchUpAccel = catchUpAccel;
            definition.boostShare = boostShare;
            definition.startGap = startGap;
            definition.catchDistance = catchDistance;
            definition.warnDistance = warnDistance;

            if (lateralSpeed >= 0f) definition.lateralSpeed = lateralSpeed;
            if (handlingResponse >= 0f) definition.handlingResponse = handlingResponse;
            if (gripBase >= 0f) definition.gripBase = gripBase;
            if (gripPerSpeed >= 0f) definition.gripPerSpeed = gripPerSpeed;
            if (brakeDecel >= 0f) definition.brakeDecel = brakeDecel;
            if (catchLateral >= 0f) definition.catchLateral = catchLateral;
            if (sustainedCatchSeconds >= 0f) definition.sustainedCatchSeconds = sustainedCatchSeconds;
            if (curveLookaheadSeconds >= 0f) definition.curveLookaheadSeconds = curveLookaheadSeconds;
            if (orbLookaheadSeconds >= 0f) definition.orbLookaheadSeconds = orbLookaheadSeconds;
            if (orbSeekWeight >= 0f) definition.orbSeekWeight = orbSeekWeight;
            if (orbBoostShare >= 0f) definition.orbBoostShare = orbBoostShare;
            if (rampLookaheadSeconds >= 0f) definition.rampLookaheadSeconds = rampLookaheadSeconds;
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
