using System.Collections.Generic;
using ConfusedGameDev.FiniteRunner.HUD;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape
{
    /// <summary>
    /// A hand-placed volume that hands the player an optional objective when
    /// they drive into it — a challenge found on the road rather than
    /// accepted at the brief. It carries a full <see cref="OptionalChallenge"/>
    /// (any type, any clock, its multiplier) and a description line: on
    /// entry the challenge is added to the <see cref="LevelManager"/>'s
    /// accepted list (<see cref="LevelManager.AcceptChallenge"/> — it starts
    /// counting at once, shows on the map screen and multiplies the payout
    /// like any accepted challenge) and the line is spoken through the
    /// <see cref="RpgMessageSystem"/>. A challenge is taken once, so the
    /// trigger always destroys itself after firing. Same player rule as the
    /// other volumes (<see cref="DialogueTrigger.IsPlayer"/>), collider forced
    /// to a trigger; the <see cref="Debugging.DialogueTriggerVisualizer"/>
    /// fills it in green next to the orange dialogue and blue cinema volumes.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ChallengeTrigger : MonoBehaviour
    {
        [TitleGroup("Optional objective")]
        [Tooltip("The challenge handed to the player on entry — a full objective plus its reward multiplier.")]
        [HideLabel, InlineProperty]
        [SerializeField] OptionalChallenge challenge = new();

        [TitleGroup("Description")]
        [Tooltip("Portrait shown in the dialogue box. Empty falls back to the message system's default, then to the speaker's initial.")]
        [PreviewField(60)]
        [SerializeField] Sprite characterSprite;

        [TitleGroup("Description")]
        [Tooltip("Speaker name shown above the line. Empty = the level's speaker.")]
        [SerializeField] string characterName = "";

        [TitleGroup("Description")]
        [Tooltip("The pages spoken as the trigger fires — what the challenge is; one entry each, Enter / A advances. {0} = the objective's value, {1} = its time-rule seconds, {2} = the Destroy Cars filter, {3} = the Collect Objects id. Empty = the objective's own briefing.")]
        [ListDrawerSettings(ShowFoldout = false)]
        [MultiLineProperty(4)]
        [SerializeField] string[] pages = System.Array.Empty<string>();

        // Pre-pages authoring, upgraded by OnValidate into pages[0].
        [SerializeField, HideInInspector, FormerlySerializedAs("text")]
        string legacyText = "";

        [TitleGroup("Description")]
        [Tooltip("Seconds the finished line holds on screen before hiding. 0 = the level's hold time.")]
        [PropertyRange(0f, 15f)]
        [SerializeField] float duration = 0f;

        [TitleGroup("Description")]
        [Tooltip("Tint for the speaker name and portrait frame. Fully transparent = the objective's accent.")]
        [SerializeField] Color accent = new(0f, 0f, 0f, 0f);

        static readonly List<ChallengeTrigger> active = new();

        bool fired;

        /// <summary>Every enabled trigger in the scene — what the debug overlay draws.</summary>
        public static IReadOnlyList<ChallengeTrigger> Active => active;

        /// <summary>The challenge this trigger offers (read-only view for tooling).</summary>
        public OptionalChallenge Challenge => challenge;

        void Awake()
        {
            foreach (var col in GetComponents<Collider>()) col.isTrigger = true;
        }

        // Upgrades the trigger's own line and the embedded challenge's lines
        // to pages the first time the editor validates it.
        void OnValidate()
        {
            bool moved = LevelObjective.MigrateLine(ref legacyText, ref pages);
            if (challenge != null) moved |= challenge.MigrateLegacyText();
#if UNITY_EDITOR
            if (moved) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        void OnEnable()
        {
            active.Add(this);
            Debugging.DialogueTriggerVisualizer.EnsureInstalled();
        }

        void OnDisable() => active.Remove(this);

        void OnTriggerEnter(Collider other)
        {
            if (fired || !DialogueTrigger.IsPlayer(other)) return;
            var manager = FindFirstObjectByType<LevelManager>();
            if (manager == null)
            {
                Debug.LogWarning($"{nameof(ChallengeTrigger)} '{name}' fired with no LevelManager in the scene — nothing to accept the challenge.", this);
                return;
            }
            if (!manager.AcceptChallenge(challenge)) return; // level ending, or already taken
            fired = true;

            LevelDefinition level = manager.Level;
            string speaker = string.IsNullOrWhiteSpace(characterName) ? level.speakerName : characterName;
            string[] lines = LevelObjective.HasText(pages) ? challenge.FormatAll(pages) : challenge.BriefingPages;
            float hold = duration > 0f ? duration : level.messageHoldSeconds;
            Color tint = accent.a > 0.001f ? accent : challenge.Accent;
            RpgMessageSystem.Instance.ShowMessage(speaker, lines, hold, tint, characterSprite);

            Destroy(gameObject);
        }

        // Edit-mode placement aid: a translucent green cube the size of the
        // collider — the visualizer's colours for this trigger kind.
        void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;
            var fill = new Color(0.3f, 1f, 0.5f, 0.25f);
            var edge = new Color(0.35f, 1f, 0.55f, 0.9f);
            if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = fill;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = edge;
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else
            {
                Bounds b = col.bounds;
                Gizmos.color = fill;
                Gizmos.DrawCube(b.center, b.size);
                Gizmos.color = edge;
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }
    }
}
