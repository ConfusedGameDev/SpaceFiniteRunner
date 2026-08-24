using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>The things a level can ask of the player. Order is the save format — append only.</summary>
    public enum ObjectiveType { ReachSpeed = 0, EscapePolice = 1, GoToTarget = 2, SurviveTime = 3, ChaseCar = 4 }

    /// <summary>
    /// How the objective list completes. <see cref="Independent"/>: steps
    /// complete in order and a finished step stays finished.
    /// <see cref="AllMustHold"/>: steps still complete in order, but an earlier
    /// state step (speed, police) can regress — the level falls back to it and
    /// every later step's progress resets — so the level only completes when
    /// everything holds at once. Order is the save format — append only.
    /// </summary>
    public enum CompletionMode { Independent = 0, AllMustHold = 1 }

    /// <summary>
    /// One step of a level. A plain enum-typed entry rather than a polymorphic
    /// class: the designer picks the type and only that type's knobs show, the
    /// list drags and reorders with the stock drawer, and the data stays
    /// trivially serializable. Speed and time are the "adjustable" parameters
    /// the debug menu exposes; the target id is resolved at runtime against
    /// the scene's <see cref="TargetObject"/>s. Runtime progress never lives
    /// here — the asset is shared and read live by the LevelManager.
    /// </summary>
    [System.Serializable]
    public class LevelObjective
    {
        [EnumToggleButtons, HideLabel]
        public ObjectiveType type = ObjectiveType.ReachSpeed;

        [ShowIf("type", ObjectiveType.ReachSpeed)]
        [Tooltip("Speed the player must reach (and, in All-Must-Hold levels, keep).")]
        [PropertyRange(50f, 300f), SuffixLabel("km/h", true)]
        public float targetSpeedKmh = 130f;

        [ShowIf("type", ObjectiveType.SurviveTime)]
        [Tooltip("Seconds this step must stay active without the level resetting. The timer only runs while the step is current.")]
        [PropertyRange(5f, 300f), SuffixLabel("s", true)]
        public float surviveSeconds = 30f;

        [ShowIf("@this.type == ObjectiveType.GoToTarget || this.type == ObjectiveType.ChaseCar")]
        [Tooltip("Go To: id of the TargetObject placed in the scene (unknown ids never complete — the HUD flags them). Chase Car: the id given to the escaping car — the LevelManager promotes a nearby traffic car to it when the step activates.")]
        public string targetId = "";

        [ShowIf("type", ObjectiveType.GoToTarget)]
        [Tooltip("Horizontal distance to the target that counts as arriving.")]
        [PropertyRange(3f, 60f), SuffixLabel("m", true)]
        public float arriveRadius = 12f;

        [ShowIf("type", ObjectiveType.EscapePolice)]
        [Tooltip("Off: the step completes the moment no patrol is hunting (even if none ever was). On: a pursuit has to be seen first, then shaken.")]
        public bool mustBeHuntedFirst;

        [Tooltip("Dialogue line shown when the step becomes active. {0} = the speed / seconds / target id. Leave empty for the built-in line.")]
        [MultiLineProperty(2)]
        public string briefing = "";

        [Tooltip("Dialogue accent and HUD tint for this step. Fully transparent = the type's default color.")]
        public Color accent = new(0.45f, 0.9f, 1f);

        /// <summary>Speed and time are the parameters the debug menu can slide.</summary>
        public bool HasAdjustableValue => type == ObjectiveType.ReachSpeed || type == ObjectiveType.SurviveTime;

        /// <summary>The accent to draw with — the authored one, or the type default when it was left transparent.</summary>
        public Color Accent => accent.a > 0.001f ? accent : DefaultAccent(type);

        /// <summary>The value that fills {0} in the briefing, as text.</summary>
        public string ValueText => type switch
        {
            ObjectiveType.ReachSpeed => targetSpeedKmh.ToString("0"),
            ObjectiveType.SurviveTime => surviveSeconds.ToString("0"),
            ObjectiveType.GoToTarget => string.IsNullOrEmpty(targetId) ? "?" : targetId,
            ObjectiveType.ChaseCar => string.IsNullOrEmpty(targetId) ? "?" : targetId,
            _ => ""
        };

        /// <summary>List element label in the inspector.</summary>
        public string Summary => type switch
        {
            ObjectiveType.ReachSpeed => $"REACH {targetSpeedKmh:0} KM/H",
            ObjectiveType.EscapePolice => mustBeHuntedFirst ? "ESCAPE POLICE (after a chase)" : "ESCAPE POLICE",
            ObjectiveType.GoToTarget => $"GO TO {(string.IsNullOrEmpty(targetId) ? "?" : targetId)}",
            ObjectiveType.SurviveTime => $"SURVIVE {surviveSeconds:0} S",
            ObjectiveType.ChaseCar => $"CHASE {(string.IsNullOrEmpty(targetId) ? "?" : targetId)}",
            _ => type.ToString()
        };

        /// <summary>The dialogue line for this step — the authored one with {0} filled, or the built-in default.</summary>
        public string BriefingText
        {
            get
            {
                string template = string.IsNullOrWhiteSpace(briefing) ? DefaultBriefing(type) : briefing;
                try { return string.Format(template, ValueText); }
                catch (System.FormatException) { return template; } // a stray brace in authored text must not throw mid-run
            }
        }

        public static Color DefaultAccent(ObjectiveType type) => type switch
        {
            ObjectiveType.EscapePolice => new Color(1f, 0.4f, 0.35f),
            ObjectiveType.GoToTarget => new Color(1f, 0.85f, 0.4f),
            ObjectiveType.SurviveTime => new Color(0.75f, 0.6f, 1f),
            ObjectiveType.ChaseCar => new Color(1f, 0.9f, 0.2f),
            _ => new Color(0.45f, 0.9f, 1f)
        };

        public static string DefaultBriefing(ObjectiveType type) => type switch
        {
            ObjectiveType.ReachSpeed => "We need to get to {0} km/h!",
            ObjectiveType.EscapePolice => "We need to escape the police, NOW!",
            ObjectiveType.GoToTarget => "Get to {0} — I'll mark the distance.",
            ObjectiveType.SurviveTime => "Stay alive for {0} seconds!",
            ObjectiveType.ChaseCar => "Chase down {0} and take it out — don't let it get away!",
            _ => ""
        };
    }

    /// <summary>
    /// A city-chase level as data: the ordered objective list, how it
    /// completes, the dialogue framing and the scene handed over to at the
    /// end. The LevelManager reads this asset LIVE every frame (no runtime
    /// clone), which is what lets the debug menu's sliders apply instantly and
    /// persist straight into the asset, the same rule as the other city
    /// settings. Drag objectives to reorder them; each shows only its own
    /// type's knobs.
    /// </summary>
    [CreateAssetMenu(fileName = "LevelDefinition", menuName = "PoliceEscape/Level Definition")]
    public class LevelDefinition : ScriptableObject
    {
        [TitleGroup("Level")]
        [Tooltip("Display name, for the designers and the debug menu.")]
        public string levelName = "Level 1";

        [TitleGroup("Level")]
        [Tooltip("Scene loaded once every objective is done — must be listed in Build Settings.")]
        public string nextSceneName = "FiniteRunner_Test";

        [TitleGroup("Level")]
        [Tooltip("Independent: a finished step stays finished. All Must Hold: speed / police steps can regress and pull the level back to them, resetting later steps — everything has to hold at once.")]
        [EnumToggleButtons]
        public CompletionMode mode = CompletionMode.Independent;

        [TitleGroup("Messages")]
        [Tooltip("Name shown next to the dialogue portrait.")]
        public string speakerName = "OPERATOR";

        [TitleGroup("Messages")]
        [Tooltip("How long each objective message stays on screen after typing out.")]
        [PropertyRange(1f, 10f), SuffixLabel("s", true)]
        public float messageHoldSeconds = 4f;

        [TitleGroup("Messages")]
        [Tooltip("Line shown when the last objective completes. The glitch handoff starts only after it disappears.")]
        [MultiLineProperty(2)]
        public string completionMessage = "Hack complete. LFG!";

        [TitleGroup("Messages")]
        [Tooltip("How long the screen holds at full corruption after the completion line before the next scene loads.")]
        [PropertyRange(0.2f, 4f), SuffixLabel("s", true)]
        public float completionGlitchHoldSeconds = 1.2f;

        [TitleGroup("Objectives")]
        [Tooltip("Played top to bottom. Drag to reorder.")]
        [ListDrawerSettings(DraggableItems = true, ShowIndexLabels = true, ListElementLabelName = nameof(LevelObjective.Summary))]
        public List<LevelObjective> objectives = new();

        public int Count => objectives != null ? objectives.Count : 0;

        /// <summary>
        /// The pre-data flow (reach 130 km/h, then shake the police) as an
        /// in-memory level — the runtime fallback for a manager with no asset
        /// and the seed the scene builder writes into a fresh asset.
        /// </summary>
        public static LevelDefinition CreateDefault()
        {
            var level = CreateInstance<LevelDefinition>();
            level.name = "DefaultLevel";
            SeedDefaultObjectives(level);
            return level;
        }

        /// <summary>Fills an empty objective list with the default two steps; leaves an authored list alone.</summary>
        public static void SeedDefaultObjectives(LevelDefinition level)
        {
            if (level.objectives == null) level.objectives = new List<LevelObjective>();
            if (level.objectives.Count > 0) return;
            level.objectives.Add(new LevelObjective { type = ObjectiveType.ReachSpeed, targetSpeedKmh = 130f });
            level.objectives.Add(new LevelObjective { type = ObjectiveType.EscapePolice, accent = LevelObjective.DefaultAccent(ObjectiveType.EscapePolice) });
        }
    }
}
