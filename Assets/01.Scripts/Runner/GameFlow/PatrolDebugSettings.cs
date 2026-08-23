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
        public float startGap = 250f;
        public float catchDistance = 10f;
        public float warnDistance = 130f;
        public float alertLead = 180f;

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
            startGap = definition.startGap;
            catchDistance = definition.catchDistance;
            warnDistance = definition.warnDistance;
            alertLead = definition.alertLead;

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
            definition.startGap = startGap;
            definition.catchDistance = catchDistance;
            definition.warnDistance = warnDistance;
            definition.alertLead = alertLead;
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
