using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Video;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Cinema
{
    /// <summary>
    /// A hand-placed volume that plays a cinema when the player drives (or
    /// flies) into it — the video twin of <see cref="DialogueTrigger"/>: the
    /// clip, its display format and the duration are authored on the trigger
    /// itself (the duration auto-fills from the clip's length and stays
    /// editable, with a Fetch Duration button to re-read it), and playback
    /// goes through the scene's <see cref="CinemaSystem"/>, so the freeze
    /// (<see cref="pauseGame"/>, on by default — off lets the player keep
    /// driving under the picture) and the long-press skip work exactly as
    /// for a level step. The same player rule as the dialogue trigger (the
    /// city car's CarInput, the runner ship's ShipMotor) keeps AI cars from
    /// tripping it.
    ///
    /// With <see cref="oneShot"/> on the object destroys itself as it fires.
    /// Otherwise it re-arms after <see cref="cooldownSeconds"/>, and the
    /// cooldown starts counting WHEN THE CINEMA CLEARS — after a skip, the
    /// duration running out, or another cinema taking the screen — not when
    /// it fired: the freeze takes an arbitrary real time, and a cooldown
    /// that ran under it would be half spent before the player could move.
    /// It counts in scaled time, like the dialogue trigger's, so a pause
    /// does not eat it. A ready trigger fires even while another cinema is
    /// up (the system ends that one at once and plays this — one cinema at
    /// a time, the newest wins), and a pre-empted cinema calls back the
    /// moment it is displaced, so the cooldown starts then. The collider is forced to a
    /// trigger so a misconfigured one can never block the car; in play the
    /// <see cref="Debugging.DialogueTriggerVisualizer"/> fills the volume
    /// (blue, against the dialogue triggers' orange) on the same debug
    /// channel, and the gizmo below covers edit-mode placement.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CinemaTrigger : MonoBehaviour
    {
        [TitleGroup("Cinema")]
        [Tooltip("The clip to play. Assigning one sets the duration to its length.")]
        [Required, OnValueChanged(nameof(FetchDuration))]
        [SerializeField] VideoClip clip;

        [Tooltip("Display format, one of the rows of the Cinema Format Library asset (Resources/PoliceEscape_CinemaFormats).")]
        [ValueDropdown(nameof(CinemaFormatIds))]
        [SerializeField] string format = CinemaFormatLibrary.FullScreenId;

        [Tooltip("How long the cinema stays up. Auto-filled from the clip; shorter cuts the clip, longer holds its last frame.")]
        [PropertyRange(0.5f, 300f), SuffixLabel("s", true)]
        [SerializeField] float duration = 5f;

        [Tooltip("Freeze the world under the video (the default). Off: the game keeps running while it plays — a pause menu opened over it pauses the clip too.")]
        [SerializeField] bool pauseGame = true;

        [TitleGroup("Re-trigger")]
        [Tooltip("Destroy this trigger after it fires once.")]
        [SerializeField] bool oneShot;

        [Tooltip("After the cinema clears (skipped or timed out), the trigger stays dead for this long. 0 = re-arms as soon as the cinema is gone.")]
        [HideIf(nameof(oneShot))]
        [PropertyRange(0f, 120f)]
        [SerializeField] float cooldownSeconds = 5f;

        static readonly List<CinemaTrigger> active = new();

        float readyTime = float.NegativeInfinity; // scaled time the cooldown ends; set when the cinema clears
        bool playing;

        /// <summary>Every enabled trigger in the scene — what the debug overlay draws.</summary>
        public static IReadOnlyList<CinemaTrigger> Active => active;

        /// <summary>True when this trigger's own cinema is not up and the cooldown has passed — the next entry will fire (displacing any other cinema).</summary>
        public bool IsReady => !playing && Time.time >= readyTime;

        /// <summary>Copies the clip's length into the duration — also run automatically whenever the clip field changes.</summary>
        [TitleGroup("Cinema")]
        [Button("Fetch Duration"), ShowIf("@clip != null")]
        public void FetchDuration()
        {
            if (clip == null || clip.length <= 0d) return;
            duration = Mathf.Clamp((float)clip.length, 0.5f, 300f);
        }

        static IEnumerable<string> CinemaFormatIds() => CinemaFormatLibrary.Ids();

        void Awake()
        {
            foreach (var col in GetComponents<Collider>()) col.isTrigger = true;
        }

        void OnEnable()
        {
            active.Add(this);
            Debugging.DialogueTriggerVisualizer.EnsureInstalled();
        }

        void OnDisable() => active.Remove(this);

        void OnTriggerEnter(Collider other)
        {
            if (!DialogueTrigger.IsPlayer(other)) return;
            if (!IsReady) return;
            if (clip == null)
            {
                Debug.LogWarning($"CinemaTrigger '{name}' has no clip — nothing to play.", this);
                return;
            }

            CinemaSystem cinema = CinemaSystem.Ensure(gameObject.scene);
            if (cinema == null) return; // a disabled system means cinemas are switched off

            playing = true;
            cinema.Play(clip, format, duration, pauseGame, OnCinemaCleared);
            if (oneShot) Destroy(gameObject);
        }

        // The cooldown starts here — when the screen is back (or handed to
        // another cinema) — never at fire time.
        void OnCinemaCleared()
        {
            playing = false;
            readyTime = Time.time + cooldownSeconds;
        }

        // Edit-mode placement aid: a semi-transparent orange cube the exact
        // size of the collider (oriented with the object for a box; the
        // world bounds for any other shape), the dialogue trigger's colours.
        void OnDrawGizmos()
        {
            var col = GetComponent<Collider>();
            if (col == null) return;
            var fill = new Color(1f, 0.55f, 0.1f, 0.25f);
            var edge = new Color(1f, 0.6f, 0.15f, 0.9f);
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
