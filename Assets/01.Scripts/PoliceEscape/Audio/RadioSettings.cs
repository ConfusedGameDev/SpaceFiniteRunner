using System.Collections.Generic;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ConfusedGameDev.FiniteRunner.PoliceEscape.Audio
{
    /// <summary>
    /// The car radio's playlist and feel, one asset in Resources
    /// (<c>04.Data/Resources/PoliceEscape_Radio.asset</c>; <see cref="Load"/>
    /// falls back to an in-memory default so the radio never throws for want
    /// of wiring). Two song sources feed the playlist: the BUNDLED clips in
    /// <see cref="songs"/> — filled by <b>Fetch Songs</b> from
    /// <see cref="sourceFolder"/>, the audio folder that is the designer's
    /// hand-off — and, when <see cref="useStreamingAssets"/> is on, every
    /// audio file found at play time in <c>StreamingAssets/&lt;streamingFolder&gt;</c>,
    /// which is a plain folder next to the built executable, so players can
    /// drop their own songs in after the build (files sharing a bundled
    /// song's name are skipped, so <b>Copy Songs To StreamingAssets</b> can
    /// seed that folder without doubling the playlist). The buttons are
    /// editor-only bodies on a runtime asset: the asset must be able to fetch
    /// from its own inspector, and a build carries only the resulting list.
    /// </summary>
    [CreateAssetMenu(fileName = "PoliceEscape_Radio", menuName = "PoliceEscape/Radio Settings")]
    public class RadioSettings : ScriptableObject
    {
        /// <summary>Path inside any Resources folder. Keep in sync with the asset's file name.</summary>
        public const string ResourcePath = "PoliceEscape_Radio";

        /// <summary>Default hand-off folder for the bundled songs.</summary>
        public const string DefaultSourceFolder = "Assets/07.Audio/03.Music/InGame";

        /// <summary>Extensions the streaming loader accepts (matched case-insensitively).</summary>
        public static readonly string[] AudioExtensions = { ".mp3", ".ogg", ".wav" };

        // ------------------------------------------------------------- songs
        [TitleGroup("Songs")]
        [Tooltip("Project folder Fetch Songs scans for audio clips. The bundled playlist is whatever it finds, in name order.")]
        [FolderPath(ParentFolder = "", RequireExistingPath = true)]
        [SerializeField] string sourceFolder = DefaultSourceFolder;

        [TitleGroup("Songs")]
        [Tooltip("The bundled playlist — clips shipped inside the build. Filled by Fetch Songs; hand edits are fine too.")]
        [ListDrawerSettings(ShowFoldout = false)]
        public List<AudioClip> songs = new();

        // --------------------------------------------------------- streaming
        [ToggleGroup(nameof(useStreamingAssets), "Streaming assets (player-added songs)")]
        [Tooltip("Also play every audio file found in StreamingAssets/<folder> at play time — a folder players can add their own songs to after the build.")]
        public bool useStreamingAssets;

        [ToggleGroup(nameof(useStreamingAssets))]
        [Tooltip("Sub-folder of StreamingAssets scanned for .mp3 / .ogg / .wav files.")]
        public string streamingFolder = "Radio";

        // -------------------------------------------------------------- feel
        [TitleGroup("Playback")]
        [Tooltip("The radio is playing when the level starts.")]
        public bool startOn = true;

        [TitleGroup("Playback")]
        [PropertyRange(0f, 1f)]
        public float volume = 0.7f;

        [TitleGroup("Playback")]
        [Tooltip("How long left / 5 must be held to switch the radio OFF, or right / 6 to switch it ON. A shorter press skips a song.")]
        [PropertyRange(0.3f, 2f), SuffixLabel("s", true)]
        public float longPressSeconds = 0.6f;

        [TitleGroup("Playback")]
        [Tooltip("Volume fade on every transition: a song change fades the old song out and the new one in, the power switch fades out / in. 0 = hard cuts.")]
        [PropertyRange(0f, 3f), SuffixLabel("s", true)]
        public float fadeSeconds = 0.6f;

        // ---------------------------------------------------------- messages
        [TitleGroup("Messages")]
        [Tooltip("Speaker name on the RPG box for every radio line.")]
        public string speakerName = "RADIO";

        [TitleGroup("Messages")]
        [Tooltip("Shown when a song starts. {0} is the song's name.")]
        public string nowPlayingFormat = "Now Playing: {0}";

        [TitleGroup("Messages")]
        [Tooltip("Shown when the radio is switched off by the long press.")]
        public string radioOffText = "Radio OFF";

        [TitleGroup("Messages")]
        [Tooltip("Shown when the streaming folder is on but holds no playable song and the bundled list is empty too.")]
        public string noSongsText = "No songs found";

        [TitleGroup("Messages")]
        [PropertyRange(0.5f, 6f), SuffixLabel("s", true)]
        public float messageHoldSeconds = 2.5f;

        [TitleGroup("Messages")]
        public Color accent = new(0.35f, 0.9f, 1f, 1f);

        /// <summary>Absolute path of the streaming folder on this machine (the folder need not exist).</summary>
        public string StreamingPath => Path.Combine(Application.streamingAssetsPath, streamingFolder ?? string.Empty);

        /// <summary>True when the file's extension is one the loader accepts.</summary>
        public static bool IsAudioFile(string path)
        {
            string ext = Path.GetExtension(path);
            foreach (string accepted in AudioExtensions)
                if (string.Equals(ext, accepted, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The asset from Resources, or a throwaway default (no songs — the radio stays quiet).</summary>
        public static RadioSettings Load()
        {
            var asset = Resources.Load<RadioSettings>(ResourcePath);
            if (asset != null) return asset;
            var fallback = CreateInstance<RadioSettings>();
            fallback.name = "RadioSettings (default)";
            return fallback;
        }

#if UNITY_EDITOR
        /// <summary>Refills <see cref="songs"/> with every audio clip under <see cref="sourceFolder"/>, sorted by name.</summary>
        [TitleGroup("Songs")]
        [Button("Fetch Songs", ButtonSizes.Medium)]
        public void FetchSongs()
        {
            songs.Clear();
            if (string.IsNullOrEmpty(sourceFolder) || !UnityEditor.AssetDatabase.IsValidFolder(sourceFolder))
            {
                Debug.LogWarning($"RadioSettings: '{sourceFolder}' is not a project folder — nothing fetched.", this);
                return;
            }
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:AudioClip", new[] { sourceFolder }))
            {
                var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (clip != null) songs.Add(clip);
            }
            songs.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"RadioSettings: {songs.Count} song(s) fetched from {sourceFolder}.", this);
        }

        /// <summary>
        /// Copies the bundled songs' source files into the streaming folder so a
        /// build ships them as loose, replaceable files beside its own copy.
        /// Existing files are left alone.
        /// </summary>
        [ToggleGroup(nameof(useStreamingAssets))]
        [Button("Copy Songs To StreamingAssets", ButtonSizes.Medium)]
        public void CopySongsToStreamingAssets()
        {
            Directory.CreateDirectory(StreamingPath);
            int copied = 0;
            foreach (AudioClip clip in songs)
            {
                if (clip == null) continue;
                string assetPath = UnityEditor.AssetDatabase.GetAssetPath(clip);
                if (string.IsNullOrEmpty(assetPath)) continue;
                string target = Path.Combine(StreamingPath, Path.GetFileName(assetPath));
                if (File.Exists(target)) continue;
                File.Copy(Path.GetFullPath(assetPath), target);
                copied++;
            }
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"RadioSettings: {copied} song file(s) copied to {StreamingPath}.", this);
        }

        /// <summary>Opens the streaming folder in the OS file browser (creating it first) — where players drop their songs.</summary>
        [ToggleGroup(nameof(useStreamingAssets))]
        [Button("Open Streaming Folder")]
        public void OpenStreamingFolder()
        {
            Directory.CreateDirectory(StreamingPath);
            UnityEditor.EditorUtility.RevealInFinder(StreamingPath);
        }
#endif
    }
}
